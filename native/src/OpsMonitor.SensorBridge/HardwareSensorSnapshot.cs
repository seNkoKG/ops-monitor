using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpsMonitor.SensorBridge;

internal sealed record HardwareSensorReading(
    string HardwareName,
    string HardwareIdentifier,
    string HardwareType,
    string SensorName,
    string SensorIdentifier,
    string SensorType,
    double Value);

internal sealed record HardwareProbeResult
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required DateTimeOffset TimestampUtc { get; init; }
    public required IReadOnlyList<HardwareSensorReading> Sensors { get; init; }
    [JsonIgnore]
    public CpuTemperatureProbeResult CpuTemperature { get; init; } = null!;
    public string Message { get; init; } = string.Empty;

    [JsonIgnore]
    public bool HasSensors => Sensors.Count > 0;

    internal string ToJson() => JsonSerializer.Serialize(
        this,
        HardwareSensorJsonContext.Default.HardwareProbeResult);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
[JsonSerializable(typeof(HardwareProbeResult))]
internal sealed partial class HardwareSensorJsonContext : JsonSerializerContext;
