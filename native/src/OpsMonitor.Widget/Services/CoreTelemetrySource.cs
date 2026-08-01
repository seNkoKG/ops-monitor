using System.Diagnostics;
using OpsMonitor.Core.Metrics;
using OpsMonitor.Core.Runtime;
using OpsMonitor.Widget.Models;
using Timer = System.Threading.Timer;

namespace OpsMonitor.Widget.Services;

internal sealed class CoreTelemetrySource : ITelemetrySource
{
    private readonly Lock _gate = new();
    private readonly OpsRuntime _runtime;
    private Timer? _publishTimer;
    private Task? _startTask;
    private Task? _reloadTask;
    private readonly CancellationTokenSource _shutdown = new();
    private TimeSpan _uiCadence = TimeSpan.FromSeconds(1);
    private int _isPublishing;
    private bool _reloadRequested;
    private volatile bool _disposed;

    public CoreTelemetrySource()
    {
        _runtime = OpsRuntime.CreateDefault();
    }

    public string Name => "Native sensor core";

    public bool IsDemo => false;

    public event EventHandler<TelemetrySnapshot>? SnapshotAvailable;

    public void SetUpdateCadence(TimeSpan cadence)
    {
        var normalized = NormalizeCadence(cadence);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _uiCadence = normalized;
            _publishTimer?.Change(normalized, normalized);
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _startTask ??= Task.Run(() => StartCoreAsync(_shutdown.Token));
        }
    }

    public void ReloadSettings()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _reloadRequested = true;
            _reloadTask ??= Task.Run(() => ProcessReloadRequestsAsync(_shutdown.Token));
        }
    }

    public void Dispose()
    {
        Task? startTask;
        Task? reloadTask;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _publishTimer?.Dispose();
            _publishTimer = null;
            startTask = _startTask;
            reloadTask = _reloadTask;
        }

        _shutdown.Cancel();
        WaitForShutdown(startTask);
        WaitForShutdown(reloadTask);
        try
        {
            _runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Application shutdown may cancel an in-flight bounded provider poll.
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "OPS Monitor telemetry runtime disposal failed: {0}",
                exception);
        }

        _shutdown.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _runtime.StartAsync(cancellationToken).ConfigureAwait(false);
            if (_disposed)
            {
                return;
            }

            PublishSnapshot();
            lock (_gate)
            {
                if (!_disposed)
                {
                    var cadence = _uiCadence;
                    _publishTimer = new Timer(
                        _ => PublishSnapshot(),
                        null,
                        cadence,
                        cadence);
                }
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
            // Normal shutdown while the runtime is starting.
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            // Normal shutdown while the runtime is starting.
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "OPS Monitor telemetry runtime startup failed: {0}",
                exception);
        }
    }

    private async Task ProcessReloadRequestsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_gate)
            {
                if (_disposed || !_reloadRequested)
                {
                    _reloadTask = null;
                    return;
                }

                _reloadRequested = false;
            }

            try
            {
                Task? startTask;
                lock (_gate)
                {
                    startTask = _startTask;
                }

                if (startTask is not null)
                {
                    await startTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                await _runtime.ReloadSettingsAsync(cancellationToken).ConfigureAwait(false);
                var cadence = GetUiCadence();
                lock (_gate)
                {
                    if (!_disposed)
                    {
                        _uiCadence = cadence;
                        _publishTimer?.Change(cadence, cadence);
                    }
                }
            }
            catch (Exception exception) when (
                cancellationToken.IsCancellationRequested &&
                exception is ObjectDisposedException or OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                Trace.TraceError(
                    "OPS Monitor telemetry settings reload failed: {0}",
                    exception);
            }
        }
    }

    private void PublishSnapshot()
    {
        if (_disposed || Interlocked.Exchange(ref _isPublishing, 1) != 0)
        {
            return;
        }

        try
        {
            var metrics = _runtime.Metrics.GetSnapshot();
            MetricId[] selectedDetails = _runtime.Settings.Widgets
                .Where(widget => widget.Enabled)
                .SelectMany(widget => widget.Modules)
                .SelectMany(module => module.AdditionalMetrics ?? [])
                .Distinct()
                .ToArray();
            PublishToObservers(CreateSnapshot(
                metrics,
                DateTimeOffset.Now,
                _runtime.Metrics.GetDescriptors(),
                selectedDetails));
        }
        finally
        {
            Interlocked.Exchange(ref _isPublishing, 0);
        }
    }

    internal static TelemetrySnapshot CreateSnapshot(
        IReadOnlyDictionary<MetricId, MetricSample> metrics,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<MetricDescriptor>? descriptors = null,
        IReadOnlyCollection<MetricId>? selectedDetails = null)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return new TelemetrySnapshot(
            capturedAt,
            new CpuTelemetry(
                OptionalValue(metrics, WellKnownMetrics.CpuTotalUtilization),
                OptionalValue(metrics, WellKnownMetrics.CpuTemperature),
                HertzToGigahertz(OptionalValue(metrics, WellKnownMetrics.CpuClock)),
                OptionalValue(metrics, WellKnownMetrics.CpuPackagePower),
                CombinedState(
                    metrics,
                    WellKnownMetrics.CpuTotalUtilization,
                    WellKnownMetrics.CpuTemperature)),
            new GpuTelemetry(
                OptionalValue(metrics, WellKnownMetrics.GpuUtilization),
                OptionalValue(metrics, WellKnownMetrics.GpuTemperature),
                HertzToGigahertz(OptionalValue(metrics, WellKnownMetrics.GpuClock)),
                BytesToGigabytes(OptionalValue(
                    metrics,
                    WellKnownMetrics.GpuMemoryUsedBytes)),
                BytesToGigabytes(OptionalValue(
                    metrics,
                    WellKnownMetrics.GpuMemoryTotalBytes)),
                CombinedState(
                    metrics,
                    WellKnownMetrics.GpuUtilization,
                    WellKnownMetrics.GpuTemperature,
                    WellKnownMetrics.GpuMemoryUsedBytes,
                    WellKnownMetrics.GpuMemoryTotalBytes)),
            new MemoryTelemetry(
                BytesToGigabytes(OptionalValue(
                    metrics,
                    WellKnownMetrics.MemoryUsedBytes)),
                BytesToGigabytes(OptionalValue(
                    metrics,
                    WellKnownMetrics.MemoryTotalBytes)),
                null,
                null,
                CombinedState(
                    metrics,
                    WellKnownMetrics.MemoryUsedBytes,
                    WellKnownMetrics.MemoryTotalBytes)),
            new NetworkTelemetry(
                OptionalValue(metrics, WellKnownMetrics.NetworkDownloadRate),
                OptionalValue(metrics, WellKnownMetrics.NetworkUploadRate),
                OptionalValue(metrics, WellKnownMetrics.NetworkPing),
                OptionalValue(metrics, WellKnownMetrics.NetworkJitter),
                OptionalValue(metrics, WellKnownMetrics.NetworkPacketLoss),
                CombinedState(
                    metrics,
                    WellKnownMetrics.NetworkDownloadRate,
                    WellKnownMetrics.NetworkUploadRate),
                CombinedState(
                    metrics,
                    WellKnownMetrics.NetworkPing,
                    WellKnownMetrics.NetworkJitter,
                    WellKnownMetrics.NetworkPacketLoss)),
            new StorageTelemetry(
                OptionalValue(metrics, WellKnownMetrics.StorageUsedPercent),
                OptionalValue(metrics, WellKnownMetrics.StorageReadRate),
                OptionalValue(metrics, WellKnownMetrics.StorageWriteRate),
                OptionalValue(metrics, WellKnownMetrics.StorageTemperature),
                StorageHealth(metrics),
                CombinedState(
                    metrics,
                    WellKnownMetrics.StorageUsedPercent,
                    WellKnownMetrics.StorageTemperature,
                    WellKnownMetrics.StorageHealthPercent)),
            BuildBattery(metrics),
            BuildGenericMetrics(metrics, descriptors, selectedDetails));
    }

    private static Dictionary<string, GenericMetricTelemetry> BuildGenericMetrics(
        IReadOnlyDictionary<MetricId, MetricSample> metrics,
        IReadOnlyCollection<MetricDescriptor>? descriptors,
        IReadOnlyCollection<MetricId>? selectedDetails)
    {
        if (selectedDetails is null || selectedDetails.Count == 0)
        {
            return new Dictionary<string, GenericMetricTelemetry>(StringComparer.Ordinal);
        }

        var selected = selectedDetails.ToHashSet();
        var descriptorIndex = (descriptors ?? [])
            .ToDictionary(item => item.Id, item => item);
        return metrics.Where(pair => selected.Contains(pair.Key)).ToDictionary(
            pair => pair.Key.Value,
            pair =>
            {
                descriptorIndex.TryGetValue(pair.Key, out MetricDescriptor? descriptor);
                return new GenericMetricTelemetry(
                    pair.Key.Value,
                    descriptor?.ShortName is { Length: > 0 } shortName
                        ? shortName
                        : descriptor?.DisplayName ?? pair.Key.Value,
                    pair.Value.HasUsableValue ? pair.Value.Value : null,
                    descriptor?.Unit ?? MetricUnit.None,
                    State(metrics, pair.Key));
            },
            StringComparer.Ordinal);
    }

    private static BatteryTelemetry BuildBattery(
        IReadOnlyDictionary<MetricId, MetricSample> metrics)
    {
        var charge = OptionalValue(metrics, WellKnownMetrics.BatteryCharge);
        var acOnline = OptionalValue(metrics, WellKnownMetrics.BatteryAcOnline);
        var remainingSeconds = OptionalValue(metrics, WellKnownMetrics.BatteryRemaining);
        return new BatteryTelemetry(
            charge,
            acOnline switch
            {
                > 0.5 => "AC connected",
                not null => "On battery",
                _ => null
            },
            remainingSeconds is > 0
                ? TimeSpan.FromSeconds(remainingSeconds.Value)
                : null,
            null,
            CombinedState(
                metrics,
                WellKnownMetrics.BatteryCharge,
                WellKnownMetrics.BatteryAcOnline));
    }

    private static double? OptionalValue(
        IReadOnlyDictionary<MetricId, MetricSample> metrics,
        MetricId id) =>
        metrics.TryGetValue(id, out var sample) &&
        sample.HasUsableValue &&
        sample.Value is { } value &&
        double.IsFinite(value)
            ? sample.Value
            : null;

    private static SensorState CombinedState(
        IReadOnlyDictionary<MetricId, MetricSample> metrics,
        params MetricId[] ids)
    {
        var states = ids.Select(id => State(metrics, id)).ToArray();
        if (states.Contains(SensorState.Critical))
        {
            return SensorState.Critical;
        }

        if (states.Contains(SensorState.Warning))
        {
            return SensorState.Warning;
        }

        if (states.Contains(SensorState.Stale))
        {
            return SensorState.Stale;
        }

        if (states.All(state => state == SensorState.Unavailable))
        {
            return SensorState.Unavailable;
        }

        return SensorState.Available;
    }

    private static SensorState State(
        IReadOnlyDictionary<MetricId, MetricSample> metrics,
        MetricId id)
    {
        if (!metrics.TryGetValue(id, out var sample))
        {
            return SensorState.Unavailable;
        }

        return sample.Availability switch
        {
            MetricAvailability.Available => SensorState.Available,
            MetricAvailability.Stale => SensorState.Stale,
            MetricAvailability.Initializing => SensorState.Stale,
            MetricAvailability.Error => SensorState.Warning,
            _ => SensorState.Unavailable
        };
    }

    private static double? BytesToGigabytes(double? bytes) =>
        bytes is { } value
            ? value / (1024d * 1024d * 1024d)
            : null;

    private static double? HertzToGigahertz(double? hertz) =>
        hertz is { } value ? value / 1_000_000_000d : null;

    private static string StorageHealth(
        IReadOnlyDictionary<MetricId, MetricSample> metrics)
    {
        double? health = OptionalValue(metrics, WellKnownMetrics.StorageHealthPercent);
        return health is { } value
            ? $"{Math.Clamp(value, 0, 100):0}% health"
            : "SMART unavailable";
    }

    private TimeSpan GetUiCadence()
    {
        var settings = _runtime.Settings;
        var defaultScene = settings.Scenes.FirstOrDefault(candidate =>
            candidate.Enabled && candidate.IsDefault);
        var profile = defaultScene is null
            ? null
            : settings.PerformanceProfiles.FirstOrDefault(candidate =>
                candidate.Enabled &&
                StringComparer.Ordinal.Equals(
                    candidate.Id,
                    defaultScene.PerformanceProfileId));
        profile ??= settings.PerformanceProfiles.FirstOrDefault(candidate =>
            candidate.Enabled);
        var requested = profile?.UiRefreshCadence ?? TimeSpan.FromSeconds(1);
        return NormalizeCadence(requested);
    }

    private static TimeSpan NormalizeCadence(TimeSpan cadence)
    {
        var milliseconds = double.IsFinite(cadence.TotalMilliseconds)
            ? cadence.TotalMilliseconds
            : 1_000;
        return TimeSpan.FromMilliseconds(
            Math.Clamp(milliseconds, 500, 10_000));
    }

    private void PublishToObservers(TelemetrySnapshot snapshot)
    {
        var handlers = SnapshotAvailable;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<TelemetrySnapshot> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, snapshot);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or
                ObjectDisposedException or
                TaskCanceledException)
            {
                // A closing UI observer must not terminate the timer thread.
            }
        }
    }

    private static void WaitForShutdown(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Expected when the widget closes during startup or a settings reload.
        }
        catch (ObjectDisposedException)
        {
            // Expected when runtime teardown wins a shutdown race.
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "OPS Monitor telemetry task shutdown failed: {0}",
                exception);
        }
    }
}
