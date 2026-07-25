using OpsMonitor.Core.Metrics;

namespace OpsMonitor.Core.Providers;

public enum ProviderHealthState
{
    Initializing,
    Healthy,
    Degraded,
    Unavailable,
    Faulted,
    Disabled,
    Stopped
}

public sealed record ProviderHealth
{
    public required string ProviderId { get; init; }
    public ProviderHealthState State { get; init; } = ProviderHealthState.Initializing;
    public MetricUnavailableReason Reason { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset? LastAttemptUtc { get; init; }
    public DateTimeOffset? LastSuccessUtc { get; init; }
    public TimeSpan LastPollDuration { get; init; }
    public int ConsecutiveFailures { get; init; }
}

public sealed record ProviderPollResult
{
    public required IReadOnlyList<MetricSample> Samples { get; init; }
    public ProviderHealthState HealthState { get; init; } = ProviderHealthState.Healthy;
    public MetricUnavailableReason Reason { get; init; }
    public string Message { get; init; } = string.Empty;

    public static ProviderPollResult Healthy(params MetricSample[] samples) =>
        new() { Samples = samples };
}

public sealed record MetricProviderContext
{
    public required DateTimeOffset TimestampUtc { get; init; }
    public required IReadOnlyDictionary<MetricId, MetricSample> LatestSamples { get; init; }
}

public interface IMetricProvider : IAsyncDisposable
{
    string Id { get; }
    string DisplayName { get; }
    IReadOnlyCollection<MetricDescriptor> Descriptors { get; }
    TimeSpan DefaultCadence { get; }
    TimeSpan MinimumCadence { get; }
    TimeSpan MaximumCadence { get; }
    TimeSpan PollTimeout { get; }

    ValueTask<ProviderPollResult> PollAsync(
        MetricProviderContext context,
        CancellationToken cancellationToken);
}

public abstract class MetricProviderBase : IMetricProvider
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract IReadOnlyCollection<MetricDescriptor> Descriptors { get; }
    public abstract TimeSpan DefaultCadence { get; }
    public virtual TimeSpan MinimumCadence => TimeSpan.FromMilliseconds(250);
    public virtual TimeSpan MaximumCadence => TimeSpan.FromMinutes(5);
    public virtual TimeSpan PollTimeout => TimeSpan.FromSeconds(5);

    public abstract ValueTask<ProviderPollResult> PollAsync(
        MetricProviderContext context,
        CancellationToken cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    protected virtual ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;
}
