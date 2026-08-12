using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpsMonitor.Core.Metrics;

[JsonConverter(typeof(MetricIdJsonConverter))]
public readonly record struct MetricId
{
    public MetricId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator MetricId(string value) => new(value);
}

public sealed class MetricIdJsonConverter : JsonConverter<MetricId>
{
    public override MetricId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        _ = typeToConvert;
        _ = options;

        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return FromNullableString(reader.GetString());
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A metric id must be a string or an object.");
        }

        string? value = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("The metric id object is malformed.");
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                throw new JsonException("The metric id object ended unexpectedly.");
            }

            if (propertyName?.Equals(
                    nameof(MetricId.Value),
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                value = reader.TokenType == JsonTokenType.String
                    ? reader.GetString()
                    : null;
            }
            else
            {
                reader.Skip();
            }
        }

        return FromNullableString(value);
    }

    public override void Write(
        Utf8JsonWriter writer,
        MetricId value,
        JsonSerializerOptions options)
    {
        _ = options;

        writer.WriteStartObject();
        if (string.IsNullOrWhiteSpace(value.Value))
        {
            writer.WriteNull("value");
        }
        else
        {
            writer.WriteString("value", value.Value);
        }

        writer.WriteEndObject();
    }

    private static MetricId FromNullableString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? default : new MetricId(value);
}

public enum MetricCategory
{
    Cpu,
    Gpu,
    Memory,
    Storage,
    Network,
    Battery,
    System,
    Process,
    Cooling,
    Custom
}

public enum MetricUnit
{
    None,
    Percent,
    Celsius,
    Bytes,
    BytesPerSecond,
    BitsPerSecond,
    Hertz,
    Watts,
    Volts,
    RevolutionsPerMinute,
    Milliseconds,
    Seconds,
    Count,
    OperationsPerSecond
}

public enum MetricAggregationKind
{
    Gauge,
    Counter,
    Rate,
    Duration,
    State
}

public sealed record MetricDescriptor
{
    public required MetricId Id { get; init; }
    public required string DisplayName { get; init; }
    public string ShortName { get; init; } = string.Empty;
    public MetricCategory Category { get; init; }
    public MetricUnit Unit { get; init; }
    public MetricAggregationKind Aggregation { get; init; } = MetricAggregationKind.Gauge;
    public double? ExpectedMinimum { get; init; }
    public double? ExpectedMaximum { get; init; }
    public int PreferredDecimals { get; init; }
    public bool HigherIsWorse { get; init; }
    public string Description { get; init; } = string.Empty;
}

public enum MetricAvailability
{
    Initializing,
    Available,
    Stale,
    Unavailable,
    Error
}

public enum MetricUnavailableReason
{
    None,
    FirstSamplePending,
    ProviderNotSupported,
    HardwareNotPresent,
    PermissionDenied,
    SourceMissing,
    SourceStale,
    Timeout,
    NetworkUnavailable,
    InvalidData,
    ProviderFaulted,
    Cancelled,
    Disabled
}

public enum MetricSourceKind
{
    WindowsNative,
    HardwareBridge,
    VendorApi,
    ExternalProcess,
    NetworkProbe,
    Derived,
    Custom
}

public sealed record MetricSource
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string ProviderId { get; init; }
    public MetricSourceKind Kind { get; init; }
    public bool RequiresElevation { get; init; }
    public string Detail { get; init; } = string.Empty;
}

public sealed record MetricSample
{
    public required MetricId MetricId { get; init; }
    public double? Value { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public MetricAvailability Availability { get; init; }
    public MetricUnavailableReason UnavailableReason { get; init; }
    public required MetricSource Source { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Tags { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;

    public bool HasUsableValue =>
        Value.HasValue &&
        Availability is MetricAvailability.Available or MetricAvailability.Stale;

    public static MetricSample Available(
        MetricId id,
        double value,
        DateTimeOffset timestampUtc,
        MetricSource source,
        IReadOnlyDictionary<string, string>? tags = null) =>
        new()
        {
            MetricId = id,
            Value = value,
            TimestampUtc = timestampUtc,
            Availability = MetricAvailability.Available,
            Source = source,
            Tags = tags ?? ReadOnlyDictionary<string, string>.Empty
        };

    public static MetricSample Missing(
        MetricId id,
        DateTimeOffset timestampUtc,
        MetricSource source,
        MetricAvailability availability,
        MetricUnavailableReason reason,
        string message = "") =>
        new()
        {
            MetricId = id,
            TimestampUtc = timestampUtc,
            Availability = availability,
            UnavailableReason = reason,
            Source = source,
            Message = message
        };
}

public static class WellKnownMetrics
{
    public static readonly MetricId CpuTotalUtilization = new("cpu.utilization.total");
    public static readonly MetricId CpuTemperature = new("cpu.temperature.package");
    public static readonly MetricId CpuClock = new("cpu.clock.effective");
    public static readonly MetricId CpuPackagePower = new("cpu.power.package");
    public static readonly MetricId MemoryUsedBytes = new("memory.physical.used");
    public static readonly MetricId MemoryAvailableBytes = new("memory.physical.available");
    public static readonly MetricId MemoryTotalBytes = new("memory.physical.total");
    public static readonly MetricId MemoryUtilization = new("memory.physical.utilization");
    public static readonly MetricId NetworkDownloadRate = new("network.throughput.download");
    public static readonly MetricId NetworkUploadRate = new("network.throughput.upload");
    public static readonly MetricId NetworkPing = new("network.connectivity.ping");
    public static readonly MetricId NetworkJitter = new("network.connectivity.jitter");
    public static readonly MetricId NetworkPacketLoss = new("network.connectivity.packet_loss");
    public static readonly MetricId SystemUptime = new("system.uptime");
    public static readonly MetricId BatteryCharge = new("battery.charge");
    public static readonly MetricId BatteryAcOnline = new("battery.ac_online");
    public static readonly MetricId BatteryRemaining = new("battery.remaining");
    public static readonly MetricId BatterySaver = new("battery.saver");
    public static readonly MetricId GpuUtilization = new("gpu.nvidia.utilization");
    public static readonly MetricId GpuTemperature = new("gpu.nvidia.temperature");
    public static readonly MetricId GpuMemoryUsedBytes = new("gpu.nvidia.memory.used");
    public static readonly MetricId GpuMemoryTotalBytes = new("gpu.nvidia.memory.total");
    public static readonly MetricId GpuPowerWatts = new("gpu.nvidia.power");
    public static readonly MetricId GpuFanPercent = new("gpu.nvidia.fan");
    public static readonly MetricId GpuClock = new("gpu.nvidia.clock.graphics");
    public static readonly MetricId GpuMemoryClock = new("gpu.nvidia.clock.memory");
    public static readonly MetricId GpuPrimaryUtilization = new("gpu.primary.utilization");
    public static readonly MetricId GpuPrimaryTemperature = new("gpu.primary.temperature");
    public static readonly MetricId GpuPrimaryMemoryUsedBytes = new("gpu.primary.memory.used");
    public static readonly MetricId GpuPrimaryMemoryTotalBytes = new("gpu.primary.memory.total");
    public static readonly MetricId GpuPrimaryClock = new("gpu.primary.clock");
    public static readonly MetricId StorageUsedPercent = new("storage.system.used");
    public static readonly MetricId StorageReadRate = new("storage.system.read");
    public static readonly MetricId StorageWriteRate = new("storage.system.write");
    public static readonly MetricId StorageTemperature = new("storage.system.temperature");
    public static readonly MetricId StorageHealthPercent = new("storage.system.health");
}
