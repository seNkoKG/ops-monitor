using System.Collections.Concurrent;
using System.Diagnostics;
using OpsMonitor.Core.Metrics;
using OpsMonitor.Core.Providers;

namespace OpsMonitor.Core.Scheduling;

public sealed class MetricProviderRegistration
{
    private long _cadenceTicks;
    private int _enabled;

    public MetricProviderRegistration(
        IMetricProvider provider,
        TimeSpan? cadence = null,
        bool enabled = true)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Cadence = cadence ?? provider.DefaultCadence;
        Enabled = enabled;
    }

    public IMetricProvider Provider { get; }

    public TimeSpan Cadence
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _cadenceTicks));
        set
        {
            var clamped = value < Provider.MinimumCadence
                ? Provider.MinimumCadence
                : value > Provider.MaximumCadence
                    ? Provider.MaximumCadence
                    : value;
            Interlocked.Exchange(ref _cadenceTicks, clamped.Ticks);
        }
    }

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        set => Volatile.Write(ref _enabled, value ? 1 : 0);
    }
}

public sealed class ProviderHealthChangedEventArgs : EventArgs
{
    public ProviderHealthChangedEventArgs(ProviderHealth health) => Health = health;
    public ProviderHealth Health { get; }
}

public sealed class ProviderBatchPolledEventArgs : EventArgs
{
    public ProviderBatchPolledEventArgs(
        string providerId,
        IReadOnlyList<MetricSample> samples,
        TimeSpan duration)
    {
        ProviderId = providerId;
        Samples = samples;
        Duration = duration;
    }

    public string ProviderId { get; }
    public IReadOnlyList<MetricSample> Samples { get; }
    public TimeSpan Duration { get; }
}

public sealed class AdaptiveMetricScheduler : IAsyncDisposable
{
    private readonly MetricStore _store;
    private readonly IReadOnlyList<MetricProviderRegistration> _registrations;
    private readonly ConcurrentDictionary<string, ProviderHealth> _health =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _runCancellation;
    private Task[] _providerLoops = [];
    private double _cadenceMultiplier = 1d;
    private bool _disposed;

    public AdaptiveMetricScheduler(
        MetricStore store,
        IEnumerable<MetricProviderRegistration> registrations)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(registrations);

        _registrations = registrations.ToArray();
        var duplicate = _registrations
            .GroupBy(registration => registration.Provider.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Provider id '{duplicate.Key}' is registered more than once.",
                nameof(registrations));
        }

        foreach (var registration in _registrations)
        {
            _store.RegisterDescriptors(registration.Provider.Descriptors);
            _health[registration.Provider.Id] = new ProviderHealth
            {
                ProviderId = registration.Provider.Id,
                State = registration.Enabled
                    ? ProviderHealthState.Initializing
                    : ProviderHealthState.Disabled,
                Reason = registration.Enabled
                    ? MetricUnavailableReason.FirstSamplePending
                    : MetricUnavailableReason.Disabled
            };
        }
    }

    public event EventHandler<ProviderHealthChangedEventArgs>? ProviderHealthChanged;
    public event EventHandler<ProviderBatchPolledEventArgs>? BatchPolled;

    public bool IsRunning => _runCancellation is { IsCancellationRequested: false };

    public double CadenceMultiplier
    {
        get => Volatile.Read(ref _cadenceMultiplier);
        set
        {
            if (!double.IsFinite(value) || value < 0.25d || value > 16d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "The cadence multiplier must be between 0.25 and 16.");
            }

            Volatile.Write(ref _cadenceMultiplier, value);
        }
    }

    public IReadOnlyDictionary<string, ProviderHealth> GetProviderHealth() =>
        new Dictionary<string, ProviderHealth>(_health, StringComparer.Ordinal);

    public bool TryGetRegistration(
        string providerId,
        out MetricProviderRegistration? registration)
    {
        registration = _registrations.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Provider.Id, providerId));
        return registration is not null;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                return;
            }

            _runCancellation?.Dispose();
            _runCancellation = new CancellationTokenSource();
            _providerLoops = _registrations
                .Select(registration =>
                    RunProviderLoopAsync(registration, _runCancellation.Token))
                .ToArray();
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
            if (_runCancellation is null)
            {
                return;
            }

            await _runCancellation.CancelAsync().ConfigureAwait(false);
            if (_providerLoops.Length > 0)
            {
                await Task.WhenAll(_providerLoops)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var registration in _registrations)
            {
                PublishHealth(new ProviderHealth
                {
                    ProviderId = registration.Provider.Id,
                    State = ProviderHealthState.Stopped,
                    Reason = MetricUnavailableReason.Cancelled,
                    LastAttemptUtc = _health[registration.Provider.Id].LastAttemptUtc,
                    LastSuccessUtc = _health[registration.Provider.Id].LastSuccessUtc
                });
            }

            _providerLoops = [];
            _runCancellation.Dispose();
            _runCancellation = null;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RunProviderLoopAsync(
        MetricProviderRegistration registration,
        CancellationToken cancellationToken)
    {
        var provider = registration.Provider;
        var failures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!registration.Enabled)
            {
                PublishHealth(new ProviderHealth
                {
                    ProviderId = provider.Id,
                    State = ProviderHealthState.Disabled,
                    Reason = MetricUnavailableReason.Disabled
                });

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            var startedUtc = DateTimeOffset.UtcNow;
            var timer = Stopwatch.StartNew();
            ProviderPollResult? result = null;
            ProviderHealth health;

            try
            {
                using var pollCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                pollCancellation.CancelAfter(provider.PollTimeout);

                result = await provider.PollAsync(
                        new MetricProviderContext
                        {
                            TimestampUtc = startedUtc,
                            LatestSamples = _store.GetSnapshot()
                        },
                        pollCancellation.Token)
                    .ConfigureAwait(false);

                timer.Stop();
                failures = result.HealthState is ProviderHealthState.Faulted
                    or ProviderHealthState.Unavailable
                    ? failures + 1
                    : 0;

                if (result.Samples.Count > 0)
                {
                    _store.Apply(result.Samples);
                    BatchPolled?.Invoke(
                        this,
                        new ProviderBatchPolledEventArgs(
                            provider.Id,
                            result.Samples,
                            timer.Elapsed));
                }

                var previous = _health[provider.Id];
                health = new ProviderHealth
                {
                    ProviderId = provider.Id,
                    State = result.HealthState,
                    Reason = result.Reason,
                    Message = result.Message,
                    LastAttemptUtc = startedUtc,
                    LastSuccessUtc = result.HealthState is ProviderHealthState.Healthy
                        or ProviderHealthState.Degraded
                        ? startedUtc
                        : previous.LastSuccessUtc,
                    LastPollDuration = timer.Elapsed,
                    ConsecutiveFailures = failures
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                timer.Stop();
                failures++;
                var samples = CreateFailureSamples(
                    provider,
                    startedUtc,
                    MetricAvailability.Error,
                    MetricUnavailableReason.Timeout,
                    $"Provider exceeded its {provider.PollTimeout.TotalSeconds:0.##} s timeout.");
                _store.Apply(samples);
                BatchPolled?.Invoke(
                    this,
                    new ProviderBatchPolledEventArgs(provider.Id, samples, timer.Elapsed));

                var previous = _health[provider.Id];
                health = new ProviderHealth
                {
                    ProviderId = provider.Id,
                    State = ProviderHealthState.Faulted,
                    Reason = MetricUnavailableReason.Timeout,
                    Message = "The provider poll timed out.",
                    LastAttemptUtc = startedUtc,
                    LastSuccessUtc = previous.LastSuccessUtc,
                    LastPollDuration = timer.Elapsed,
                    ConsecutiveFailures = failures
                };
            }
            catch (Exception exception)
            {
                timer.Stop();
                failures++;
                var samples = CreateFailureSamples(
                    provider,
                    startedUtc,
                    MetricAvailability.Error,
                    MetricUnavailableReason.ProviderFaulted,
                    exception.Message);
                _store.Apply(samples);
                BatchPolled?.Invoke(
                    this,
                    new ProviderBatchPolledEventArgs(provider.Id, samples, timer.Elapsed));

                var previous = _health[provider.Id];
                health = new ProviderHealth
                {
                    ProviderId = provider.Id,
                    State = ProviderHealthState.Faulted,
                    Reason = MetricUnavailableReason.ProviderFaulted,
                    Message = exception.Message,
                    LastAttemptUtc = startedUtc,
                    LastSuccessUtc = previous.LastSuccessUtc,
                    LastPollDuration = timer.Elapsed,
                    ConsecutiveFailures = failures
                };
            }

            PublishHealth(health);

            var targetCadence = GetAdaptiveCadence(registration, failures);
            var delay = targetCadence - timer.Elapsed;
            if (delay <= TimeSpan.Zero)
            {
                await Task.Yield();
                continue;
            }

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private TimeSpan GetAdaptiveCadence(
        MetricProviderRegistration registration,
        int consecutiveFailures)
    {
        var failureBackoff = Math.Pow(2d, Math.Min(consecutiveFailures, 4));
        var ticks = registration.Cadence.Ticks * CadenceMultiplier * failureBackoff;
        var boundedTicks = Math.Clamp(
            ticks,
            registration.Provider.MinimumCadence.Ticks,
            registration.Provider.MaximumCadence.Ticks);
        return TimeSpan.FromTicks((long)boundedTicks);
    }

    private void PublishHealth(ProviderHealth health)
    {
        var changed = !_health.TryGetValue(health.ProviderId, out var previous) ||
                      previous.State != health.State ||
                      previous.Reason != health.Reason ||
                      !StringComparer.Ordinal.Equals(previous.Message, health.Message) ||
                      previous.ConsecutiveFailures != health.ConsecutiveFailures;

        _health[health.ProviderId] = health;
        if (changed)
        {
            ProviderHealthChanged?.Invoke(
                this,
                new ProviderHealthChangedEventArgs(health));
        }
    }

    private static MetricSample[] CreateFailureSamples(
        IMetricProvider provider,
        DateTimeOffset timestampUtc,
        MetricAvailability availability,
        MetricUnavailableReason reason,
        string message)
    {
        var source = new MetricSource
        {
            Id = $"{provider.Id}.scheduler",
            DisplayName = provider.DisplayName,
            ProviderId = provider.Id,
            Kind = MetricSourceKind.Derived
        };

        return provider.Descriptors
            .Select(descriptor =>
                MetricSample.Missing(
                    descriptor.Id,
                    timestampUtc,
                    source,
                    availability,
                    reason,
                    message))
            .ToArray();
    }
}
