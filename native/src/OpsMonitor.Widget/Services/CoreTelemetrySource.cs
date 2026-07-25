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
    private int _isPublishing;
    private bool _disposed;

    public CoreTelemetrySource()
    {
        _runtime = OpsRuntime.CreateDefault();
    }

    public string Name => "Native sensor core";

    public bool IsDemo => false;

    public event EventHandler<TelemetrySnapshot>? SnapshotAvailable;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            _startTask ??= Task.Run(StartCoreAsync);
        }
    }

    public void ReloadSettings()
    {
        if (!_disposed)
        {
            _ = ReloadSettingsCoreAsync();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _publishTimer?.Dispose();
            _publishTimer = null;
        }

        try
        {
            _runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Application shutdown may cancel an in-flight bounded provider poll.
        }

        GC.SuppressFinalize(this);
    }

    private async Task StartCoreAsync()
    {
        try
        {
            await _runtime.StartAsync().ConfigureAwait(false);
            if (_disposed)
            {
                return;
            }

            PublishSnapshot();
            lock (_gate)
            {
                if (!_disposed)
                {
                    var cadence = GetUiCadence();
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
    }

    private async Task ReloadSettingsCoreAsync()
    {
        try
        {
            await _runtime.ReloadSettingsAsync().ConfigureAwait(false);
            var cadence = GetUiCadence();
            lock (_gate)
            {
                _publishTimer?.Change(cadence, cadence);
            }
        }
        catch (Exception exception) when (
            _disposed &&
            exception is ObjectDisposedException or OperationCanceledException)
        {
            // Normal shutdown while settings are being reloaded.
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
            var cpuLoad = Value(metrics, WellKnownMetrics.CpuTotalUtilization);
            var cpuTemperature = OptionalValue(metrics, WellKnownMetrics.CpuTemperature);
            var gpuLoad = Value(metrics, WellKnownMetrics.GpuUtilization);
            var gpuTemperature = OptionalValue(metrics, WellKnownMetrics.GpuTemperature);
            var gpuMemoryUsed = BytesToGigabytes(Value(
                metrics,
                WellKnownMetrics.GpuMemoryUsedBytes));
            var gpuMemoryTotal = BytesToGigabytes(Value(
                metrics,
                WellKnownMetrics.GpuMemoryTotalBytes));
            var memoryUsed = BytesToGigabytes(Value(metrics, WellKnownMetrics.MemoryUsedBytes));
            var memoryTotal = BytesToGigabytes(Value(metrics, WellKnownMetrics.MemoryTotalBytes));

            var snapshot = new TelemetrySnapshot(
                DateTimeOffset.Now,
                new CpuTelemetry(
                    cpuLoad,
                    cpuTemperature,
                    0,
                    0,
                    CombinedState(
                        metrics,
                        WellKnownMetrics.CpuTotalUtilization,
                        WellKnownMetrics.CpuTemperature)),
                new GpuTelemetry(
                    gpuLoad,
                    gpuTemperature,
                    0,
                    gpuMemoryUsed,
                    gpuMemoryTotal,
                    CombinedState(
                        metrics,
                        WellKnownMetrics.GpuUtilization,
                        WellKnownMetrics.GpuTemperature)),
                new MemoryTelemetry(
                    memoryUsed,
                    memoryTotal,
                    0,
                    0,
                    State(metrics, WellKnownMetrics.MemoryTotalBytes)),
                new NetworkTelemetry(
                    Value(metrics, WellKnownMetrics.NetworkDownloadRate),
                    Value(metrics, WellKnownMetrics.NetworkUploadRate),
                    Value(metrics, WellKnownMetrics.NetworkPing),
                    Value(metrics, WellKnownMetrics.NetworkJitter),
                    Value(metrics, WellKnownMetrics.NetworkPacketLoss),
                    CombinedState(
                        metrics,
                        WellKnownMetrics.NetworkDownloadRate,
                        WellKnownMetrics.NetworkPing)),
                new StorageTelemetry(
                    0,
                    0,
                    0,
                    null,
                    "Provider not enabled",
                    SensorState.Unavailable),
                BuildBattery(metrics));

            SnapshotAvailable?.Invoke(this, snapshot);
        }
        finally
        {
            Interlocked.Exchange(ref _isPublishing, 0);
        }
    }

    private static BatteryTelemetry BuildBattery(
        IReadOnlyDictionary<MetricId, MetricSample> metrics)
    {
        var charge = OptionalValue(metrics, WellKnownMetrics.BatteryCharge);
        if (charge is null)
        {
            return new BatteryTelemetry(
                null,
                "Not present",
                null,
                null,
                SensorState.Unavailable);
        }

        var acOnline = Value(metrics, WellKnownMetrics.BatteryAcOnline) > 0.5;
        var remainingSeconds = OptionalValue(metrics, WellKnownMetrics.BatteryRemaining);
        return new BatteryTelemetry(
            charge,
            acOnline ? "AC connected" : "On battery",
            remainingSeconds is > 0
                ? TimeSpan.FromSeconds(remainingSeconds.Value)
                : null,
            null,
            State(metrics, WellKnownMetrics.BatteryCharge));
    }

    private static double Value(
        IReadOnlyDictionary<MetricId, MetricSample> metrics,
        MetricId id) =>
        OptionalValue(metrics, id) ?? 0;

    private static double? OptionalValue(
        IReadOnlyDictionary<MetricId, MetricSample> metrics,
        MetricId id) =>
        metrics.TryGetValue(id, out var sample) && sample.HasUsableValue
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

    private static double BytesToGigabytes(double bytes) =>
        bytes / (1024d * 1024d * 1024d);

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
        return TimeSpan.FromMilliseconds(
            Math.Clamp(requested.TotalMilliseconds, 500, 10_000));
    }
}
