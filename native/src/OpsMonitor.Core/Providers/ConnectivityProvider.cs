using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using OpsMonitor.Core.Metrics;

namespace OpsMonitor.Core.Providers;

public sealed record ConnectivityProviderOptions
{
    public IReadOnlyList<string> Targets { get; init; } =
        ["1.1.1.1", "8.8.8.8"];
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(800);
    public int RollingWindowSize { get; init; } = 30;
    public int PayloadBytes { get; init; } = 32;
    public TimeSpan Cadence { get; init; } = TimeSpan.FromSeconds(2);
}

public sealed class ConnectivityProvider : MetricProviderBase
{
    private static readonly MetricDescriptor[] MetricDescriptors =
    [
        new()
        {
            Id = WellKnownMetrics.NetworkPing,
            DisplayName = "Internet latency",
            ShortName = "Ping",
            Category = MetricCategory.Network,
            Unit = MetricUnit.Milliseconds,
            Aggregation = MetricAggregationKind.Duration,
            PreferredDecimals = 0,
            HigherIsWorse = true
        },
        new()
        {
            Id = WellKnownMetrics.NetworkJitter,
            DisplayName = "Internet jitter",
            ShortName = "Jitter",
            Category = MetricCategory.Network,
            Unit = MetricUnit.Milliseconds,
            PreferredDecimals = 1,
            HigherIsWorse = true
        },
        new()
        {
            Id = WellKnownMetrics.NetworkPacketLoss,
            DisplayName = "Rolling packet loss",
            ShortName = "Loss",
            Category = MetricCategory.Network,
            Unit = MetricUnit.Percent,
            ExpectedMinimum = 0,
            ExpectedMaximum = 100,
            PreferredDecimals = 0,
            HigherIsWorse = true
        }
    ];

    private readonly ConnectivityProviderOptions _options;
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private readonly Queue<bool> _outcomes = [];
    private readonly Queue<double> _latencies = [];
    private readonly byte[] _payload;
    private bool _disposed;

    public ConnectivityProvider(ConnectivityProviderOptions? options = null)
    {
        _options = options ?? new ConnectivityProviderOptions();
        if (_options.Targets.Count == 0 ||
            _options.Targets.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "At least one non-empty connectivity target is required.",
                nameof(options));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(_options.RollingWindowSize, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.PayloadBytes, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(_options.PayloadBytes, 65_000);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_options.Timeout, TimeSpan.Zero);
        _payload = new byte[_options.PayloadBytes];
    }

    public override string Id => "network.connectivity";
    public override string DisplayName => "Internet connectivity";
    public override IReadOnlyCollection<MetricDescriptor> Descriptors => MetricDescriptors;
    public override TimeSpan DefaultCadence => _options.Cadence;
    public override TimeSpan MinimumCadence => TimeSpan.FromMilliseconds(500);
    public override TimeSpan MaximumCadence => TimeSpan.FromMinutes(1);
    public override TimeSpan PollTimeout =>
        TimeSpan.FromTicks(
            (_options.Timeout.Ticks * _options.Targets.Count) + TimeSpan.FromSeconds(1).Ticks);

    public override async ValueTask<ProviderPollResult> PollAsync(
        MetricProviderContext context,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _pollGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var probes = _options.Targets
                .Select(target => ProbeAsync(target, cancellationToken))
                .ToArray();
            var results = await Task.WhenAll(probes).ConfigureAwait(false);

            foreach (var result in results)
            {
                AddBounded(_outcomes, result.Success, _options.RollingWindowSize);
            }

            var successful = results
                .Where(result => result.Success)
                .OrderBy(result => result.RoundTripMilliseconds)
                .ToArray();
            var packetLoss = 100d * _outcomes.Count(success => !success) / _outcomes.Count;
            var source = CreateSource(results);

            List<MetricSample> samples =
            [
                MetricSample.Available(
                    WellKnownMetrics.NetworkPacketLoss,
                    packetLoss,
                    context.TimestampUtc,
                    source)
            ];

            if (successful.Length == 0)
            {
                samples.Add(MetricSample.Missing(
                    WellKnownMetrics.NetworkPing,
                    context.TimestampUtc,
                    source,
                    MetricAvailability.Unavailable,
                    MetricUnavailableReason.NetworkUnavailable,
                    "No configured ICMP target replied."));
                samples.Add(MetricSample.Missing(
                    WellKnownMetrics.NetworkJitter,
                    context.TimestampUtc,
                    source,
                    MetricAvailability.Unavailable,
                    MetricUnavailableReason.NetworkUnavailable,
                    "Jitter requires successful latency samples."));

                return new ProviderPollResult
                {
                    Samples = samples,
                    HealthState = ProviderHealthState.Degraded,
                    Reason = MetricUnavailableReason.NetworkUnavailable,
                    Message = "No configured connectivity target replied."
                };
            }

            var best = successful[0];
            AddBounded(_latencies, best.RoundTripMilliseconds, _options.RollingWindowSize);
            var tags = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["target"] = best.Target,
                    ["successfulTargets"] = successful.Length.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["targetCount"] = results.Length.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                });

            samples.Add(MetricSample.Available(
                WellKnownMetrics.NetworkPing,
                best.RoundTripMilliseconds,
                context.TimestampUtc,
                source,
                tags));

            if (_latencies.Count < 2)
            {
                samples.Add(MetricSample.Missing(
                    WellKnownMetrics.NetworkJitter,
                    context.TimestampUtc,
                    source,
                    MetricAvailability.Initializing,
                    MetricUnavailableReason.FirstSamplePending,
                    "A second successful latency sample is required."));
            }
            else
            {
                var values = _latencies.ToArray();
                var jitter = values
                    .Zip(values.Skip(1), (left, right) => Math.Abs(right - left))
                    .Average();
                samples.Add(MetricSample.Available(
                    WellKnownMetrics.NetworkJitter,
                    jitter,
                    context.TimestampUtc,
                    source,
                    tags));
            }

            var failedTargets = results.Length - successful.Length;
            return new ProviderPollResult
            {
                Samples = samples,
                HealthState = failedTargets == 0
                    ? ProviderHealthState.Healthy
                    : ProviderHealthState.Degraded,
                Reason = failedTargets == 0
                    ? MetricUnavailableReason.None
                    : MetricUnavailableReason.NetworkUnavailable,
                Message = failedTargets == 0
                    ? string.Empty
                    : $"{failedTargets} of {results.Length} targets did not reply."
            };
        }
        finally
        {
            _pollGate.Release();
        }
    }

    protected override ValueTask DisposeAsyncCore()
    {
        if (!_disposed)
        {
            _disposed = true;
            _pollGate.Dispose();
        }

        return base.DisposeAsyncCore();
    }

    private async Task<ProbeResult> ProbeAsync(
        string target,
        CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(
                    target,
                    (int)Math.Ceiling(_options.Timeout.TotalMilliseconds),
                    _payload)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return reply.Status == IPStatus.Success
                ? new ProbeResult(target, true, reply.RoundtripTime, reply.Status)
                : new ProbeResult(target, false, 0, reply.Status);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is PingException or InvalidOperationException)
        {
            return new ProbeResult(target, false, 0, IPStatus.Unknown);
        }
    }

    private static MetricSource CreateSource(IEnumerable<ProbeResult> results)
    {
        var targets = string.Join(", ", results.Select(result => result.Target));
        return new MetricSource
        {
            Id = "network.icmp",
            DisplayName = "ICMP probes",
            ProviderId = "network.connectivity",
            Kind = MetricSourceKind.NetworkProbe,
            Detail = targets
        };
    }

    private static void AddBounded<T>(Queue<T> queue, T value, int capacity)
    {
        queue.Enqueue(value);
        while (queue.Count > capacity)
        {
            queue.Dequeue();
        }
    }

    private sealed record ProbeResult(
        string Target,
        bool Success,
        long RoundTripMilliseconds,
        IPStatus Status);
}
