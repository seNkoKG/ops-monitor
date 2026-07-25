using System.Windows.Forms;
using OpsMonitor.Widget.Models;

namespace OpsMonitor.Widget.Services;

internal sealed class DemoTelemetrySource : ITelemetrySource
{
    private readonly Lock _gate = new();
    private readonly Random _random = new(0x0F51);
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private System.Threading.Timer? _timer;
    private bool _disposed;

    public string Name => "Demo telemetry";

    public bool IsDemo => true;

    public event EventHandler<TelemetrySnapshot>? SnapshotAvailable;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            _timer ??= new System.Threading.Timer(
                PublishSnapshot,
                null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1));
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
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void PublishSnapshot(object? state)
    {
        _ = state;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var elapsed = (DateTimeOffset.UtcNow - _startedAt).TotalSeconds;
            var cpuLoad = Clamp(34 + (Math.Sin(elapsed / 3.7) * 19) + Noise(8), 4, 96);
            var gpuLoad = Clamp(20 + (Math.Sin(elapsed / 5.2) * 15) + Noise(9), 1, 93);
            var usedMemory = Clamp(15.2 + (Math.Sin(elapsed / 24) * 0.8) + Noise(0.12), 10, 29);
            var download = Math.Max(0, 720_000 + (Math.Sin(elapsed / 2.9) * 580_000) + Noise(180_000));
            var upload = Math.Max(0, 42_000 + (Math.Sin(elapsed / 4.3) * 31_000) + Noise(13_000));
            var ping = Clamp(16 + (Math.Sin(elapsed / 7.5) * 4) + Noise(1.8), 8, 60);
            var jitter = Clamp(1.8 + Math.Abs(Math.Sin(elapsed / 4.6) * 2.2) + Noise(0.35), 0.2, 12);
            var packetLoss = Clamp(Math.Max(0, Noise(0.18)), 0, 2.5);
            var storageIsStale = ((int)elapsed % 37) >= 33;

            var snapshot = new TelemetrySnapshot(
                DateTimeOffset.Now,
                new CpuTelemetry(
                    cpuLoad,
                    45 + (cpuLoad * 0.37),
                    4.25 + (cpuLoad / 100 * 0.7),
                    18 + (cpuLoad * 0.72)),
                new GpuTelemetry(
                    gpuLoad,
                    39 + (gpuLoad * 0.34),
                    1.74 + (gpuLoad / 100 * 0.65),
                    2.7 + (gpuLoad / 100 * 3.8),
                    12),
                new MemoryTelemetry(usedMemory, 30.9, usedMemory + 2.4, 5.1),
                new NetworkTelemetry(download, upload, ping, jitter, packetLoss),
                new StorageTelemetry(
                    68,
                    8_500_000 + Math.Max(0, Noise(5_500_000)),
                    2_100_000 + Math.Max(0, Noise(1_700_000)),
                    42,
                    "Healthy",
                    storageIsStale ? SensorState.Stale : SensorState.Available),
                ReadBattery());

            SnapshotAvailable?.Invoke(this, snapshot);
        }
    }

    private static BatteryTelemetry ReadBattery()
    {
        var power = SystemInformation.PowerStatus;
        if (power.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery))
        {
            return new BatteryTelemetry(
                null,
                "Not present",
                null,
                null,
                SensorState.Unavailable);
        }

        var percent = Math.Clamp(power.BatteryLifePercent * 100, 0, 100);
        TimeSpan? remaining = power.BatteryLifeRemaining > 0
            ? TimeSpan.FromSeconds(power.BatteryLifeRemaining)
            : null;
        var powerState = power.PowerLineStatus == PowerLineStatus.Online ? "Charging / AC" : "On battery";

        return new BatteryTelemetry(percent, powerState, remaining, null, SensorState.Available);
    }

    private double Noise(double amplitude) => ((_random.NextDouble() * 2) - 1) * amplitude;

    private static double Clamp(double value, double minimum, double maximum)
        => Math.Clamp(value, minimum, maximum);
}
