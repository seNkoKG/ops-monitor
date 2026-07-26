namespace OpsMonitor.Widget.Models;

public enum SensorState
{
    Available,
    Stale,
    Unavailable,
    Warning,
    Critical
}

public sealed record CpuTelemetry(
    double? LoadPercent,
    double? TemperatureCelsius,
    double? ClockGhz,
    double? PackagePowerWatts,
    SensorState State = SensorState.Available);

public sealed record GpuTelemetry(
    double? LoadPercent,
    double? TemperatureCelsius,
    double? ClockGhz,
    double? UsedVramGigabytes,
    double? TotalVramGigabytes,
    SensorState State = SensorState.Available);

public sealed record MemoryTelemetry(
    double? UsedGigabytes,
    double? TotalGigabytes,
    double? CommitGigabytes,
    double? CachedGigabytes,
    SensorState State = SensorState.Available);

public sealed record NetworkTelemetry(
    double? DownloadBytesPerSecond,
    double? UploadBytesPerSecond,
    double? PingMilliseconds,
    double? JitterMilliseconds,
    double? PacketLossPercent,
    SensorState ThroughputState = SensorState.Available,
    SensorState ConnectivityState = SensorState.Available);

public sealed record StorageTelemetry(
    double UsedPercent,
    double ReadBytesPerSecond,
    double WriteBytesPerSecond,
    double? TemperatureCelsius,
    string Health,
    SensorState State = SensorState.Available);

public sealed record BatteryTelemetry(
    double? ChargePercent,
    string? PowerState,
    TimeSpan? Remaining,
    double? DrawWatts,
    SensorState State);

public sealed record TelemetrySnapshot(
    DateTimeOffset CapturedAt,
    CpuTelemetry Cpu,
    GpuTelemetry Gpu,
    MemoryTelemetry Memory,
    NetworkTelemetry Network,
    StorageTelemetry Storage,
    BatteryTelemetry Battery);
