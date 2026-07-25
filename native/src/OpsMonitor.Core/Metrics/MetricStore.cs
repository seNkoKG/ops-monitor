using System.Collections.ObjectModel;

namespace OpsMonitor.Core.Metrics;

public sealed record MetricSampleChange(MetricSample? Previous, MetricSample Current);

public sealed class MetricSamplesChangedEventArgs : EventArgs
{
    public MetricSamplesChangedEventArgs(IReadOnlyList<MetricSampleChange> changes) =>
        Changes = changes;

    public IReadOnlyList<MetricSampleChange> Changes { get; }
}

public interface IMetricReader
{
    bool TryGetLatest(MetricId id, out MetricSample? sample);
    IReadOnlyDictionary<MetricId, MetricSample> GetSnapshot();
    bool TryGetDescriptor(MetricId id, out MetricDescriptor? descriptor);
}

public sealed class MetricStore : IMetricReader
{
    private readonly Lock _gate = new();
    private readonly Dictionary<MetricId, MetricSample> _latest = [];
    private readonly Dictionary<MetricId, MetricDescriptor> _descriptors = [];

    public event EventHandler<MetricSamplesChangedEventArgs>? Changed;

    public void RegisterDescriptors(IEnumerable<MetricDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        lock (_gate)
        {
            foreach (var descriptor in descriptors)
            {
                ArgumentNullException.ThrowIfNull(descriptor);
                _descriptors[descriptor.Id] = descriptor;
            }
        }
    }

    public IReadOnlyList<MetricSampleChange> Apply(IEnumerable<MetricSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        List<MetricSampleChange> changes = [];

        lock (_gate)
        {
            foreach (var sample in samples)
            {
                ArgumentNullException.ThrowIfNull(sample);
                _latest.TryGetValue(sample.MetricId, out var previous);
                _latest[sample.MetricId] = sample;

                if (!IsMeaningfullyEqual(previous, sample))
                {
                    changes.Add(new MetricSampleChange(previous, sample));
                }
            }
        }

        if (changes.Count > 0)
        {
            Changed?.Invoke(this, new MetricSamplesChangedEventArgs(changes.AsReadOnly()));
        }

        return changes.AsReadOnly();
    }

    public bool TryGetLatest(MetricId id, out MetricSample? sample)
    {
        lock (_gate)
        {
            return _latest.TryGetValue(id, out sample);
        }
    }

    public IReadOnlyDictionary<MetricId, MetricSample> GetSnapshot()
    {
        lock (_gate)
        {
            return new ReadOnlyDictionary<MetricId, MetricSample>(
                new Dictionary<MetricId, MetricSample>(_latest));
        }
    }

    public bool TryGetDescriptor(MetricId id, out MetricDescriptor? descriptor)
    {
        lock (_gate)
        {
            return _descriptors.TryGetValue(id, out descriptor);
        }
    }

    public IReadOnlyCollection<MetricDescriptor> GetDescriptors()
    {
        lock (_gate)
        {
            return _descriptors.Values.ToArray();
        }
    }

    private static bool IsMeaningfullyEqual(MetricSample? left, MetricSample right)
    {
        if (left is null)
        {
            return false;
        }

        return left.Value.Equals(right.Value) &&
               left.Availability == right.Availability &&
               left.UnavailableReason == right.UnavailableReason &&
               StringComparer.Ordinal.Equals(left.Source.Id, right.Source.Id) &&
               StringComparer.Ordinal.Equals(left.Message, right.Message) &&
               DictionaryEquals(left.Tags, right.Tags);
    }

    private static bool DictionaryEquals(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) ||
                !StringComparer.Ordinal.Equals(pair.Value, value))
            {
                return false;
            }
        }

        return true;
    }
}
