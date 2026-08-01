using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpsMonitor.Core.Metrics;

namespace OpsMonitor.Core.Providers;

public sealed record HardwareSensorBridgeOptions
{
    public string? SnapshotPath { get; init; }
    public TimeSpan MaximumAge { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan MaximumFutureSkew { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan Cadence { get; init; } = TimeSpan.FromSeconds(3);
    public long MaximumSnapshotBytes { get; init; } = 2 * 1024 * 1024;
}

public sealed class HardwareSensorBridgeProvider : MetricProviderBase
{
    private static readonly MetricDescriptor[] FixedDescriptors =
    [
        Descriptor(WellKnownMetrics.CpuClock, "CPU effective clock", "CPU clock", MetricCategory.Cpu, MetricUnit.Hertz, 0),
        Descriptor(WellKnownMetrics.CpuPackagePower, "CPU package power", "CPU power", MetricCategory.Cpu, MetricUnit.Watts, 1, true),
        Descriptor(WellKnownMetrics.StorageUsedPercent, "System drive used", "Drive used", MetricCategory.Storage, MetricUnit.Percent, 0, true, 0, 100),
        Descriptor(WellKnownMetrics.StorageReadRate, "System storage read rate", "Disk read", MetricCategory.Storage, MetricUnit.BytesPerSecond, 1),
        Descriptor(WellKnownMetrics.StorageWriteRate, "System storage write rate", "Disk write", MetricCategory.Storage, MetricUnit.BytesPerSecond, 1),
        Descriptor(WellKnownMetrics.StorageTemperature, "System storage temperature", "Disk temp", MetricCategory.Storage, MetricUnit.Celsius, 0, true, 0, 125),
        Descriptor(WellKnownMetrics.StorageHealthPercent, "System storage health", "Disk health", MetricCategory.Storage, MetricUnit.Percent, 0, false, 0, 100),
    ];

    private readonly HardwareSensorBridgeOptions _options;
    private readonly ConcurrentDictionary<MetricId, MetricDescriptor> _descriptors =
        new(FixedDescriptors.ToDictionary(item => item.Id));

    public HardwareSensorBridgeProvider(HardwareSensorBridgeOptions? options = null)
    {
        _options = options ?? new HardwareSensorBridgeOptions();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_options.MaximumAge, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaximumFutureSkew, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaximumSnapshotBytes, 4_096);
        SnapshotPath = _options.SnapshotPath ?? GetDefaultSnapshotPath();
    }

    public string SnapshotPath { get; }

    public override string Id => "hardware.sensor.bridge";
    public override string DisplayName => "Hardware sensor catalog";
    public override IReadOnlyCollection<MetricDescriptor> Descriptors => _descriptors.Values.ToArray();
    public override TimeSpan DefaultCadence => _options.Cadence;
    public override TimeSpan MinimumCadence => TimeSpan.FromSeconds(1);
    public override TimeSpan MaximumCadence => TimeSpan.FromMinutes(1);
    public override TimeSpan PollTimeout => TimeSpan.FromSeconds(2);

    public static string GetDefaultSnapshotPath()
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            string? userSid = identity.User?.Value;
            if (!string.IsNullOrWhiteSpace(userSid))
            {
                string programFiles =
                    Environment.GetEnvironmentVariable("ProgramW6432") ??
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return Path.Combine(
                    programFiles,
                    "OPS Monitor Sensor",
                    "Data",
                    userSid,
                    "hardware-sensors.json");
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PerformancePill",
            "hardware-sensors.json");
    }

    public static MetricId GetMetricId(string sensorIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sensorIdentifier);
        return new MetricId("hardware.lhm." + Slug(sensorIdentifier));
    }

    public override async ValueTask<ProviderPollResult> PollAsync(
        MetricProviderContext context,
        CancellationToken cancellationToken)
    {
        var source = new MetricSource
        {
            Id = "opsmonitor.hardware.sensor",
            DisplayName = "OPS Monitor hardware sensor",
            ProviderId = Id,
            Kind = MetricSourceKind.HardwareBridge,
            RequiresElevation = true,
            Detail = SnapshotPath
        };
        List<MetricSample> samples = [];
        AddSystemDriveCapacity(samples, context.TimestampUtc, source);

        if (!File.Exists(SnapshotPath))
        {
            AddMissingFixedHardware(samples, context.TimestampUtc, source, MetricUnavailableReason.SourceMissing, "The hardware sensor catalog has not published a snapshot.");
            return Degraded(samples, MetricUnavailableReason.SourceMissing, "Hardware catalog pending; system drive capacity remains available.");
        }

        try
        {
            var file = new FileInfo(SnapshotPath);
            if (file.Length <= 0 || file.Length > _options.MaximumSnapshotBytes)
            {
                throw new JsonException("The hardware sensor snapshot size is invalid.");
            }

            await using var stream = new FileStream(
                SnapshotPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                16_384,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            HardwareSnapshotDocument? document = await JsonSerializer.DeserializeAsync(
                stream,
                HardwareSensorDocumentJsonContext.Default.HardwareSnapshotDocument,
                cancellationToken).ConfigureAwait(false);
            if (document is null || document.SchemaVersion != 1 || document.Sensors is null)
            {
                throw new JsonException("The hardware sensor snapshot schema is unsupported.");
            }

            TimeSpan age = context.TimestampUtc - document.TimestampUtc;
            if (age < -_options.MaximumFutureSkew)
            {
                throw new JsonException("The hardware sensor snapshot timestamp is in the future.");
            }

            if (age > _options.MaximumAge)
            {
                AddMissingFixedHardware(samples, context.TimestampUtc, source, MetricUnavailableReason.SourceStale, $"The hardware sensor snapshot is {age.TotalSeconds:0} seconds old.", MetricAvailability.Stale);
                return Degraded(samples, MetricUnavailableReason.SourceStale, "Hardware sensor snapshot is stale.");
            }

            List<(HardwareSensorDocument Sensor, MetricDescriptor Descriptor, double? Value)> mapped = [];
            foreach (HardwareSensorDocument sensor in document.Sensors
                         .Where(IsValidSensor)
                         .DistinctBy(item => item.SensorIdentifier, StringComparer.OrdinalIgnoreCase))
            {
                MetricDescriptor descriptor = CreateDynamicDescriptor(sensor);
                _descriptors[descriptor.Id] = descriptor;
                mapped.Add((sensor, descriptor, ConvertValue(sensor.SensorType, sensor.Value)));
            }

            foreach (var item in mapped)
            {
                var tags = new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["hardware"] = item.Sensor.HardwareName,
                        ["hardwareType"] = item.Sensor.HardwareType,
                        ["sensor"] = item.Sensor.SensorName,
                        ["sensorType"] = item.Sensor.SensorType,
                        ["identifier"] = item.Sensor.SensorIdentifier,
                    });
                samples.Add(item.Value is { } value && double.IsFinite(value)
                    ? MetricSample.Available(item.Descriptor.Id, value, document.TimestampUtc, source, tags)
                    : MetricSample.Missing(item.Descriptor.Id, document.TimestampUtc, source, MetricAvailability.Unavailable, MetricUnavailableReason.InvalidData, "The sensor did not expose a live value."));
            }

            AddCuratedSamples(samples, mapped, document.TimestampUtc, source);
            bool anyLive = mapped.Any(item => item.Value.HasValue);
            return new ProviderPollResult
            {
                Samples = samples,
                Descriptors = mapped.Select(item => item.Descriptor).ToArray(),
                HealthState = anyLive ? ProviderHealthState.Healthy : ProviderHealthState.Degraded,
                Reason = anyLive ? MetricUnavailableReason.None : MetricUnavailableReason.InvalidData,
                Message = $"{mapped.Count(item => item.Value.HasValue)} of {mapped.Count} hardware sensors are live."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            AddMissingFixedHardware(samples, context.TimestampUtc, source, MetricUnavailableReason.PermissionDenied, exception.Message);
            return Degraded(samples, MetricUnavailableReason.PermissionDenied, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or JsonException or NotSupportedException)
        {
            AddMissingFixedHardware(samples, context.TimestampUtc, source, MetricUnavailableReason.InvalidData, exception.Message, MetricAvailability.Error);
            return Degraded(samples, MetricUnavailableReason.InvalidData, exception.Message);
        }
    }

    private static void AddCuratedSamples(
        List<MetricSample> samples,
        IReadOnlyList<(HardwareSensorDocument Sensor, MetricDescriptor Descriptor, double? Value)> mapped,
        DateTimeOffset timestampUtc,
        MetricSource source)
    {
        AddCpuClock(samples, mapped, timestampUtc, source);
        AddSelected(samples, mapped, WellKnownMetrics.CpuPackagePower, timestampUtc, source,
            item => IsCpu(item.Sensor) && IsType(item.Sensor, "Power") &&
                    item.Sensor.SensorName.Contains("Package", StringComparison.OrdinalIgnoreCase),
            values => values.Max());
        AddStorageTemperature(samples, mapped, timestampUtc, source);
        AddSelected(samples, mapped, WellKnownMetrics.StorageReadRate, timestampUtc, source,
            item => IsStorage(item.Sensor) && IsType(item.Sensor, "Throughput") &&
                    item.Sensor.SensorName.Contains("Read", StringComparison.OrdinalIgnoreCase), values => values.Sum());
        AddSelected(samples, mapped, WellKnownMetrics.StorageWriteRate, timestampUtc, source,
            item => IsStorage(item.Sensor) && IsType(item.Sensor, "Throughput") &&
                    item.Sensor.SensorName.Contains("Write", StringComparison.OrdinalIgnoreCase), values => values.Sum());
        AddSelected(samples, mapped, WellKnownMetrics.StorageHealthPercent, timestampUtc, source,
            item => IsStorage(item.Sensor) &&
                    (item.Sensor.SensorName.Contains("Life", StringComparison.OrdinalIgnoreCase) ||
                     item.Sensor.SensorName.Contains("Health", StringComparison.OrdinalIgnoreCase)) &&
                    item.Descriptor.Unit == MetricUnit.Percent, values => values.Min());
    }

    private static void AddCpuClock(
        List<MetricSample> samples,
        IReadOnlyList<(HardwareSensorDocument Sensor, MetricDescriptor Descriptor, double? Value)> mapped,
        DateTimeOffset timestampUtc,
        MetricSource source)
    {
        var clocks = mapped.Where(item =>
                IsCpu(item.Sensor) &&
                IsType(item.Sensor, "Clock") &&
                !item.Sensor.SensorName.Contains("Bus", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var preferred = clocks.Where(item =>
                item.Sensor.SensorName.Contains("Average Effective", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (preferred.Length == 0)
        {
            preferred = clocks.Where(item =>
                    item.Sensor.SensorName.Contains("Effective", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        if (preferred.Length == 0)
        {
            preferred = clocks.Where(item =>
                    item.Sensor.SensorName.Contains("Average", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        if (preferred.Length == 0)
        {
            preferred = clocks;
        }

        AddSelected(
            samples,
            preferred,
            WellKnownMetrics.CpuClock,
            timestampUtc,
            source,
            _ => true,
            values => values.Average());
    }

    private static void AddStorageTemperature(
        List<MetricSample> samples,
        IReadOnlyList<(HardwareSensorDocument Sensor, MetricDescriptor Descriptor, double? Value)> mapped,
        DateTimeOffset timestampUtc,
        MetricSource source)
    {
        var temperatures = mapped.Where(item =>
                IsStorage(item.Sensor) &&
                IsType(item.Sensor, "Temperature") &&
                !item.Sensor.SensorName.Contains("Warning", StringComparison.OrdinalIgnoreCase) &&
                !item.Sensor.SensorName.Contains("Critical", StringComparison.OrdinalIgnoreCase) &&
                !item.Sensor.SensorName.Contains("Limit", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var composite = temperatures.Where(item =>
                item.Sensor.SensorName.Contains("Composite", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        AddSelected(
            samples,
            composite.Length > 0 ? composite : temperatures,
            WellKnownMetrics.StorageTemperature,
            timestampUtc,
            source,
            _ => true,
            values => values.Max());
    }

    private static void AddSelected(
        List<MetricSample> samples,
        IReadOnlyList<(HardwareSensorDocument Sensor, MetricDescriptor Descriptor, double? Value)> mapped,
        MetricId id,
        DateTimeOffset timestampUtc,
        MetricSource source,
        Func<(HardwareSensorDocument Sensor, MetricDescriptor Descriptor, double? Value), bool> predicate,
        Func<IEnumerable<double>, double> aggregate)
    {
        double[] values = mapped.Where(predicate).Select(item => item.Value).Where(item => item.HasValue).Select(item => item!.Value).ToArray();
        samples.Add(values.Length > 0
            ? MetricSample.Available(id, aggregate(values), timestampUtc, source)
            : MetricSample.Missing(id, timestampUtc, source, MetricAvailability.Unavailable, MetricUnavailableReason.HardwareNotPresent, "No matching hardware sensor is exposed."));
    }

    private static void AddSystemDriveCapacity(List<MetricSample> samples, DateTimeOffset timestampUtc, MetricSource source)
    {
        try
        {
            string? root = Path.GetPathRoot(Environment.SystemDirectory);
            var drive = string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root);
            if (drive is { IsReady: true, TotalSize: > 0 })
            {
                double used = ((drive.TotalSize - drive.AvailableFreeSpace) / (double)drive.TotalSize) * 100;
                samples.Add(MetricSample.Available(
                    WellKnownMetrics.StorageUsedPercent,
                    Math.Clamp(used, 0, 100),
                    timestampUtc,
                    source,
                    new ReadOnlyDictionary<string, string>(new Dictionary<string, string> { ["drive"] = drive.Name })));
                return;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _ = exception;
        }

        samples.Add(MetricSample.Missing(WellKnownMetrics.StorageUsedPercent, timestampUtc, source, MetricAvailability.Unavailable, MetricUnavailableReason.SourceMissing, "The system drive capacity is unavailable."));
    }

    private static void AddMissingFixedHardware(
        List<MetricSample> samples,
        DateTimeOffset timestampUtc,
        MetricSource source,
        MetricUnavailableReason reason,
        string message,
        MetricAvailability availability = MetricAvailability.Unavailable)
    {
        foreach (MetricDescriptor descriptor in FixedDescriptors.Where(item => item.Id != WellKnownMetrics.StorageUsedPercent))
        {
            samples.Add(MetricSample.Missing(descriptor.Id, timestampUtc, source, availability, reason, message));
        }
    }

    private static ProviderPollResult Degraded(IReadOnlyList<MetricSample> samples, MetricUnavailableReason reason, string message) =>
        new() { Samples = samples, HealthState = ProviderHealthState.Degraded, Reason = reason, Message = message };

    private static bool IsValidSensor(HardwareSensorDocument sensor) =>
        !string.IsNullOrWhiteSpace(sensor.SensorIdentifier) &&
        !string.IsNullOrWhiteSpace(sensor.SensorName) &&
        !string.IsNullOrWhiteSpace(sensor.SensorType);

    private static bool IsCpu(HardwareSensorDocument sensor) =>
        sensor.HardwareType.Equals("Cpu", StringComparison.OrdinalIgnoreCase) ||
        sensor.HardwareIdentifier.Contains("cpu", StringComparison.OrdinalIgnoreCase);

    private static bool IsStorage(HardwareSensorDocument sensor) =>
        sensor.HardwareType.Equals("Storage", StringComparison.OrdinalIgnoreCase) ||
        sensor.HardwareIdentifier.Contains("storage", StringComparison.OrdinalIgnoreCase) ||
        sensor.HardwareIdentifier.Contains("nvme", StringComparison.OrdinalIgnoreCase);

    private static bool IsType(HardwareSensorDocument sensor, string type) =>
        sensor.SensorType.Equals(type, StringComparison.OrdinalIgnoreCase);

    private static MetricDescriptor CreateDynamicDescriptor(HardwareSensorDocument sensor)
    {
        (MetricUnit unit, int decimals, bool higherIsWorse) = UnitFor(sensor.SensorType);
        return new MetricDescriptor
        {
            Id = GetMetricId(sensor.SensorIdentifier),
            DisplayName = $"{sensor.HardwareName} — {sensor.SensorName}",
            ShortName = sensor.SensorName,
            Category = CategoryFor(sensor),
            Unit = unit,
            PreferredDecimals = decimals,
            HigherIsWorse = higherIsWorse,
            Description = $"{sensor.SensorType} sensor exposed by LibreHardwareMonitor ({sensor.SensorIdentifier})."
        };
    }

    private static MetricCategory CategoryFor(HardwareSensorDocument sensor) =>
        sensor.HardwareType.ToLowerInvariant() switch
        {
            var value when value.Contains("cpu") => MetricCategory.Cpu,
            var value when value.Contains("gpu") => MetricCategory.Gpu,
            var value when value.Contains("memory") => MetricCategory.Memory,
            var value when value.Contains("storage") => MetricCategory.Storage,
            var value when value.Contains("battery") => MetricCategory.Battery,
            var value when value.Contains("network") => MetricCategory.Network,
            _ when sensor.SensorType.Equals("Fan", StringComparison.OrdinalIgnoreCase) ||
                   sensor.SensorType.Equals("Control", StringComparison.OrdinalIgnoreCase) => MetricCategory.Cooling,
            _ => MetricCategory.System
        };

    private static (MetricUnit Unit, int Decimals, bool HigherIsWorse) UnitFor(string sensorType) =>
        sensorType.ToLowerInvariant() switch
        {
            "temperature" => (MetricUnit.Celsius, 0, true),
            "load" or "level" or "control" => (MetricUnit.Percent, 0, true),
            "clock" or "frequency" => (MetricUnit.Hertz, 0, false),
            "power" => (MetricUnit.Watts, 1, true),
            "voltage" => (MetricUnit.Volts, 3, false),
            "fan" => (MetricUnit.RevolutionsPerMinute, 0, false),
            "data" or "smalldata" => (MetricUnit.Bytes, 1, false),
            "throughput" => (MetricUnit.BytesPerSecond, 1, false),
            "timespan" => (MetricUnit.Seconds, 0, false),
            _ => (MetricUnit.None, 2, false)
        };

    private static double? ConvertValue(string sensorType, double? value)
    {
        if (value is not { } actual || !double.IsFinite(actual))
        {
            return null;
        }

        return sensorType.ToLowerInvariant() switch
        {
            "clock" or "frequency" => actual * 1_000_000d,
            "data" => actual * 1024d * 1024d * 1024d,
            "smalldata" => actual * 1024d * 1024d,
            "throughput" => actual,
            _ => actual
        };
    }

    private static string Slug(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        int length = 0;
        bool separator = false;
        foreach (char character in value.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (separator && length > 0)
                {
                    buffer[length++] = '.';
                }
                buffer[length++] = character;
                separator = false;
            }
            else
            {
                separator = true;
            }
        }
        return length == 0 ? "sensor" : new string(buffer[..length]);
    }

    private static MetricDescriptor Descriptor(
        MetricId id, string name, string shortName, MetricCategory category, MetricUnit unit,
        int decimals, bool higherIsWorse = false, double? minimum = null, double? maximum = null) =>
        new()
        {
            Id = id,
            DisplayName = name,
            ShortName = shortName,
            Category = category,
            Unit = unit,
            PreferredDecimals = decimals,
            HigherIsWorse = higherIsWorse,
            ExpectedMinimum = minimum,
            ExpectedMaximum = maximum
        };
}

internal sealed record HardwareSnapshotDocument
{
    public int SchemaVersion { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public IReadOnlyList<HardwareSensorDocument>? Sensors { get; init; }
}

internal sealed record HardwareSensorDocument
{
    public string HardwareName { get; init; } = string.Empty;
    public string HardwareIdentifier { get; init; } = string.Empty;
    public string HardwareType { get; init; } = string.Empty;
    public string SensorName { get; init; } = string.Empty;
    public string SensorIdentifier { get; init; } = string.Empty;
    public string SensorType { get; init; } = string.Empty;
    public double? Value { get; init; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(HardwareSnapshotDocument))]
internal sealed partial class HardwareSensorDocumentJsonContext : JsonSerializerContext;
