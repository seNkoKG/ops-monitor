using System.Globalization;
using OpsMonitor.Core.Metrics;

namespace OpsMonitor.Core.Providers;

public sealed record CpuTemperatureBridgeOptions
{
    public string? ReadingPath { get; init; }
    public TimeSpan MaximumAge { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan Cadence { get; init; } = TimeSpan.FromSeconds(3);
    public double MinimumTemperatureCelsius { get; init; } = 5;
    public double MaximumTemperatureCelsius { get; init; } = 125;
}

public sealed class CpuTemperatureBridgeProvider : MetricProviderBase
{
    private static readonly MetricDescriptor[] MetricDescriptors =
    [
        new()
        {
            Id = WellKnownMetrics.CpuTemperature,
            DisplayName = "CPU package temperature",
            ShortName = "CPU temp",
            Category = MetricCategory.Cpu,
            Unit = MetricUnit.Celsius,
            ExpectedMinimum = 5,
            ExpectedMaximum = 125,
            PreferredDecimals = 0,
            HigherIsWorse = true,
            Description = "CPU temperature published by the isolated elevated sensor bridge."
        }
    ];

    private readonly CpuTemperatureBridgeOptions _options;

    public CpuTemperatureBridgeProvider(CpuTemperatureBridgeOptions? options = null)
    {
        _options = options ?? new CpuTemperatureBridgeOptions();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            _options.MaximumAge,
            TimeSpan.Zero);
        if (_options.MaximumTemperatureCelsius <= _options.MinimumTemperatureCelsius)
        {
            throw new ArgumentException(
                "The maximum CPU temperature must exceed the minimum.",
                nameof(options));
        }

        ReadingPath = _options.ReadingPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PerformancePill",
            "cpu-temperature.txt");
    }

    public string ReadingPath { get; }

    public override string Id => "cpu.temperature.bridge";
    public override string DisplayName => "CPU temperature bridge";
    public override IReadOnlyCollection<MetricDescriptor> Descriptors => MetricDescriptors;
    public override TimeSpan DefaultCadence => _options.Cadence;
    public override TimeSpan MinimumCadence => TimeSpan.FromSeconds(1);
    public override TimeSpan MaximumCadence => TimeSpan.FromMinutes(1);
    public override TimeSpan PollTimeout => TimeSpan.FromSeconds(2);

    public override async ValueTask<ProviderPollResult> PollAsync(
        MetricProviderContext context,
        CancellationToken cancellationToken)
    {
        var source = new MetricSource
        {
            Id = "amd.ryzenmaster.bridge",
            DisplayName = "AMD Ryzen Master sensor bridge",
            ProviderId = Id,
            Kind = MetricSourceKind.HardwareBridge,
            RequiresElevation = true,
            Detail = ReadingPath
        };

        if (!File.Exists(ReadingPath))
        {
            return Missing(
                context.TimestampUtc,
                source,
                MetricAvailability.Unavailable,
                MetricUnavailableReason.SourceMissing,
                "The CPU temperature bridge has not published a reading.");
        }

        try
        {
            await using var stream = new FileStream(
                ReadingPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                512,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);
            var contents = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var fields = contents.Trim().Split('|');

            if (fields.Length != 2 ||
                !double.TryParse(
                    fields[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var temperature) ||
                !long.TryParse(
                    fields[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var ticks) ||
                ticks < DateTime.MinValue.Ticks ||
                ticks > DateTime.MaxValue.Ticks)
            {
                return Missing(
                    context.TimestampUtc,
                    source,
                    MetricAvailability.Error,
                    MetricUnavailableReason.InvalidData,
                    "The bridge reading is malformed.");
            }

            if (temperature < _options.MinimumTemperatureCelsius ||
                temperature > _options.MaximumTemperatureCelsius)
            {
                return Missing(
                    context.TimestampUtc,
                    source,
                    MetricAvailability.Error,
                    MetricUnavailableReason.InvalidData,
                    $"The bridge published an implausible value ({temperature:0.0} °C).");
            }

            var publishedUtc = new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
            var age = context.TimestampUtc - publishedUtc;
            if (age < TimeSpan.Zero)
            {
                age = TimeSpan.Zero;
            }

            if (age > _options.MaximumAge)
            {
                return Missing(
                    context.TimestampUtc,
                    source,
                    MetricAvailability.Stale,
                    MetricUnavailableReason.SourceStale,
                    $"The last confirmed bridge reading is {age.TotalSeconds:0} seconds old; " +
                    "no current CPU temperature is available.");
            }

            return ProviderPollResult.Healthy(
                MetricSample.Available(
                    WellKnownMetrics.CpuTemperature,
                    temperature,
                    publishedUtc,
                    source));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            return Missing(
                context.TimestampUtc,
                source,
                MetricAvailability.Unavailable,
                MetricUnavailableReason.PermissionDenied,
                exception.Message);
        }
        catch (IOException exception)
        {
            return Missing(
                context.TimestampUtc,
                source,
                MetricAvailability.Error,
                MetricUnavailableReason.ProviderFaulted,
                exception.Message);
        }
    }

    private static ProviderPollResult Missing(
        DateTimeOffset timestampUtc,
        MetricSource source,
        MetricAvailability availability,
        MetricUnavailableReason reason,
        string message) =>
        new()
        {
            Samples =
            [
                MetricSample.Missing(
                    WellKnownMetrics.CpuTemperature,
                    timestampUtc,
                    source,
                    availability,
                    reason,
                    message)
            ],
            HealthState = availability switch
            {
                MetricAvailability.Error => ProviderHealthState.Faulted,
                MetricAvailability.Stale => ProviderHealthState.Degraded,
                _ => ProviderHealthState.Unavailable
            },
            Reason = reason,
            Message = message
        };
}
