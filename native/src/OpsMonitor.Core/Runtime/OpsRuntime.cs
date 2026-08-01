using OpsMonitor.Core.Alerts;
using OpsMonitor.Core.History;
using OpsMonitor.Core.Metrics;
using OpsMonitor.Core.Providers;
using OpsMonitor.Core.Scheduling;
using OpsMonitor.Core.Settings;

namespace OpsMonitor.Core.Runtime;

public sealed record OpsRuntimeOptions
{
    public string? SettingsPath { get; init; }
    public bool IncludeWindowsNativeProvider { get; init; } = true;
    public bool IncludeConnectivityProvider { get; init; } = true;
    public bool IncludeCpuTemperatureBridgeProvider { get; init; } = true;
    public bool IncludeHardwareSensorBridgeProvider { get; init; } = true;
    public bool IncludeNvidiaProvider { get; init; } = true;
    public ConnectivityProviderOptions Connectivity { get; init; } = new();
    public CpuTemperatureBridgeOptions CpuTemperatureBridge { get; init; } = new();
    public HardwareSensorBridgeOptions HardwareSensorBridge { get; init; } = new();
    public NvidiaProviderOptions Nvidia { get; init; } = new();
    public IReadOnlyList<IMetricProvider> AdditionalProviders { get; init; } = [];
}

public sealed record OpsRuntimeSnapshot
{
    public required DateTimeOffset TimestampUtc { get; init; }
    public required OpsSettingsDocument Settings { get; init; }
    public required IReadOnlyDictionary<MetricId, MetricSample> Metrics { get; init; }
    public required IReadOnlyDictionary<string, ProviderHealth> ProviderHealth { get; init; }
    public required IReadOnlyDictionary<string, AlertStateSnapshot> AlertStates { get; init; }
}

public sealed class RuntimeSettingsChangedEventArgs : EventArgs
{
    public RuntimeSettingsChangedEventArgs(OpsSettingsDocument settings) =>
        Settings = settings;

    public OpsSettingsDocument Settings { get; }
}

public sealed class OpsRuntime : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly IReadOnlyList<IMetricProvider> _providers;
    private readonly bool _ownsSettingsRepository;
    private OpsSettingsDocument _settings = OpsSettingsDocument.CreateDefault();
    private bool _started;
    private bool _disposed;

    public OpsRuntime(
        IEnumerable<IMetricProvider> providers,
        ISettingsRepository settingsRepository,
        MetricStore? metricStore = null,
        MetricHistoryStore? history = null,
        AlertEngine? alerts = null,
        bool ownsSettingsRepository = false)
    {
        ArgumentNullException.ThrowIfNull(providers);
        SettingsRepository =
            settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _ownsSettingsRepository = ownsSettingsRepository;
        _providers = providers.ToArray();

        var duplicate = _providers
            .GroupBy(provider => provider.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Provider id '{duplicate.Key}' is registered more than once.",
                nameof(providers));
        }

        Metrics = metricStore ?? new MetricStore();
        History = history ?? new MetricHistoryStore();
        Alerts = alerts ?? new AlertEngine();
        Scheduler = new AdaptiveMetricScheduler(
            Metrics,
            _providers.Select(provider => new MetricProviderRegistration(provider)));
        Scheduler.BatchPolled += OnProviderBatchPolled;
    }

    public event EventHandler<RuntimeSettingsChangedEventArgs>? SettingsChanged;

    public MetricStore Metrics { get; }
    public MetricHistoryStore History { get; }
    public AlertEngine Alerts { get; }
    public AdaptiveMetricScheduler Scheduler { get; }
    public ISettingsRepository SettingsRepository { get; }
    public IReadOnlyList<IMetricProvider> Providers => _providers;
    public OpsSettingsDocument Settings => Volatile.Read(ref _settings);
    public bool IsRunning => _started && Scheduler.IsRunning;

    public static OpsRuntime CreateDefault(OpsRuntimeOptions? options = null)
    {
        options ??= new OpsRuntimeOptions();
        List<IMetricProvider> providers = [];
        if (options.IncludeWindowsNativeProvider)
        {
            providers.Add(new WindowsNativeProvider());
        }

        if (options.IncludeConnectivityProvider)
        {
            providers.Add(new ConnectivityProvider(options.Connectivity));
        }

        if (options.IncludeCpuTemperatureBridgeProvider)
        {
            providers.Add(new CpuTemperatureBridgeProvider(options.CpuTemperatureBridge));
        }

        if (options.IncludeHardwareSensorBridgeProvider)
        {
            providers.Add(new HardwareSensorBridgeProvider(options.HardwareSensorBridge));
        }

        if (options.IncludeNvidiaProvider)
        {
            providers.Add(new NvidiaProvider(options.Nvidia));
        }

        providers.AddRange(options.AdditionalProviders);
        var repository = new JsonSettingsRepository(options.SettingsPath);
        return new OpsRuntime(
            providers,
            repository,
            ownsSettingsRepository: true);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started)
            {
                return;
            }

            var settings = await SettingsRepository
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            ApplySettingsCore(settings);
            await Scheduler.StartAsync(cancellationToken).ConfigureAwait(false);
            _started = true;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_started)
            {
                return;
            }

            await Scheduler.StopAsync(cancellationToken).ConfigureAwait(false);
            _started = false;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task ReloadSettingsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var settings = await SettingsRepository
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        ApplySettingsCore(settings);
    }

    public async Task ApplySettingsAsync(
        OpsSettingsDocument settings,
        bool persist,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);

        // Alert validation is transactional: ReplaceRules validates the complete set
        // before changing active state.
        Alerts.ReplaceRules(settings.AlertRules);
        if (persist)
        {
            await SettingsRepository
                .SaveAsync(settings, cancellationToken)
                .ConfigureAwait(false);
        }

        ApplySettingsCore(settings, alertsAlreadyApplied: true);
    }

    public OpsRuntimeSnapshot GetSnapshot() =>
        new()
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Settings = Settings,
            Metrics = Metrics.GetSnapshot(),
            ProviderHealth = Scheduler.GetProviderHealth(),
            AlertStates = Alerts.GetStates()
        };

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Scheduler.BatchPolled -= OnProviderBatchPolled;
        await StopAsync().ConfigureAwait(false);
        await Scheduler.DisposeAsync().ConfigureAwait(false);

        foreach (var provider in _providers)
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }

        if (_ownsSettingsRepository)
        {
            switch (SettingsRepository)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }

        _lifecycle.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ApplySettingsCore(
        OpsSettingsDocument settings,
        bool alertsAlreadyApplied = false)
    {
        if (!alertsAlreadyApplied)
        {
            Alerts.ReplaceRules(settings.AlertRules);
        }

        History.Configure(
            settings.DataRetention.MaximumSamplesPerMetric,
            settings.DataRetention.Retention);

        var profile = ResolveActivePerformanceProfile(settings);
        if (profile is not null)
        {
            Scheduler.CadenceMultiplier = profile.Mode switch
            {
                PowerAwarenessMode.Performance => 0.75,
                PowerAwarenessMode.Efficiency => 2,
                _ => 1
            };

            foreach (var provider in _providers)
            {
                if (!Scheduler.TryGetRegistration(provider.Id, out var registration) ||
                    registration is null)
                {
                    continue;
                }

                registration.Enabled =
                    !profile.DisabledProviderIds.Contains(provider.Id);
                registration.Cadence = profile.ProviderCadences.TryGetValue(
                    provider.Id,
                    out var configured)
                    ? configured
                    : provider.DefaultCadence;
            }
        }

        Volatile.Write(ref _settings, settings);
        SettingsChanged?.Invoke(this, new RuntimeSettingsChangedEventArgs(settings));
    }

    private void OnProviderBatchPolled(
        object? sender,
        ProviderBatchPolledEventArgs eventArgs)
    {
        var settings = Settings;
        IEnumerable<MetricSample> historySamples = eventArgs.Samples.Where(sample =>
            ShouldRecordHistory(settings, sample.MetricId));
        if (settings.DataRetention.RecordUnavailableSamples)
        {
            History.AddRange(historySamples);
        }
        else
        {
            History.AddRange(historySamples.Where(sample => sample.HasUsableValue));
        }

        Alerts.Evaluate(eventArgs.Samples);
    }

    internal static bool ShouldRecordHistory(
        OpsSettingsDocument settings,
        MetricId metricId)
    {
        if (string.IsNullOrWhiteSpace(metricId.Value) ||
            !metricId.Value.StartsWith("hardware.", StringComparison.Ordinal))
        {
            return true;
        }

        if (settings.AlertRules.Any(rule => rule.MetricId == metricId))
        {
            return true;
        }

        return settings.Widgets
            .SelectMany(widget => widget.Modules)
            .Any(module =>
                module.PrimaryMetric == metricId ||
                module.SecondaryMetric == metricId ||
                (module.AdditionalMetrics ?? []).Contains(metricId));
    }

    private static PerformanceProfileSettings? ResolveActivePerformanceProfile(
        OpsSettingsDocument settings)
    {
        var defaultScene = settings.Scenes.FirstOrDefault(scene =>
            scene.Enabled && scene.IsDefault);
        if (defaultScene is not null)
        {
            var sceneProfile = settings.PerformanceProfiles.FirstOrDefault(profile =>
                profile.Enabled &&
                StringComparer.Ordinal.Equals(
                    profile.Id,
                    defaultScene.PerformanceProfileId));
            if (sceneProfile is not null)
            {
                return sceneProfile;
            }
        }

        return settings.PerformanceProfiles.FirstOrDefault(profile => profile.Enabled);
    }
}
