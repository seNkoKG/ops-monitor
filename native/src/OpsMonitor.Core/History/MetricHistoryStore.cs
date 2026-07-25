using System.Collections.Concurrent;
using OpsMonitor.Core.Metrics;

namespace OpsMonitor.Core.History;

public sealed record MetricHistoryPoint(
    DateTimeOffset TimestampUtc,
    double? Value,
    MetricAvailability Availability,
    MetricUnavailableReason UnavailableReason,
    string SourceId);

public sealed record MetricHistoryAggregate(
    MetricId MetricId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    double? Minimum,
    double? Average,
    double? Maximum,
    double? Last,
    int SampleCount,
    int UsableSampleCount,
    MetricAvailability LastAvailability);

public sealed class MetricHistoryStore
{
    private readonly ConcurrentDictionary<MetricId, MetricRingBuffer> _series = new();
    private volatile int _capacityPerMetric;
    private long _retentionTicks;

    public MetricHistoryStore(
        int capacityPerMetric = 10_800,
        TimeSpan? retention = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityPerMetric, 2);
        _capacityPerMetric = capacityPerMetric;
        _retentionTicks = (retention ?? TimeSpan.FromDays(7)).Ticks;
    }

    public int CapacityPerMetric => _capacityPerMetric;
    public TimeSpan Retention => TimeSpan.FromTicks(Interlocked.Read(ref _retentionTicks));

    public void Configure(int capacityPerMetric, TimeSpan retention)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityPerMetric, 2);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);

        _capacityPerMetric = capacityPerMetric;
        Interlocked.Exchange(ref _retentionTicks, retention.Ticks);
        foreach (var series in _series.Values)
        {
            series.Resize(capacityPerMetric);
        }
    }

    public void Add(MetricSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var series = _series.GetOrAdd(
            sample.MetricId,
            _ => new MetricRingBuffer(_capacityPerMetric));

        series.Add(ToPoint(sample), sample.TimestampUtc - Retention);
    }

    public void AddRange(IEnumerable<MetricSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        foreach (var sample in samples)
        {
            Add(sample);
        }
    }

    public IReadOnlyList<MetricHistoryPoint> Get(
        MetricId id,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int? maximumPoints = null)
    {
        if (maximumPoints <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPoints),
                "The maximum point count must be positive when specified.");
        }

        if (!_series.TryGetValue(id, out var series))
        {
            return [];
        }

        var values = series.Snapshot();
        var filtered = values.Where(point =>
            (!fromUtc.HasValue || point.TimestampUtc >= fromUtc.Value) &&
            (!toUtc.HasValue || point.TimestampUtc <= toUtc.Value));

        if (maximumPoints is > 0)
        {
            var materialized = filtered.ToArray();
            if (materialized.Length <= maximumPoints.Value)
            {
                return materialized;
            }

            if (maximumPoints.Value == 1)
            {
                return [materialized[^1]];
            }

            var result = new MetricHistoryPoint[maximumPoints.Value];
            var step = (materialized.Length - 1d) / (maximumPoints.Value - 1d);
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = materialized[(int)Math.Round(index * step)];
            }

            return result;
        }

        return filtered.ToArray();
    }

    public IReadOnlyList<MetricHistoryAggregate> Aggregate(
        MetricId id,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        TimeSpan bucketWidth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bucketWidth, TimeSpan.Zero);
        if (toUtc <= fromUtc)
        {
            throw new ArgumentException("The end of the range must be after its start.", nameof(toUtc));
        }

        var points = Get(id, fromUtc, toUtc);
        if (points.Count == 0)
        {
            return [];
        }

        List<MetricHistoryAggregate> result = [];
        var bucketStart = fromUtc;
        while (bucketStart < toUtc)
        {
            var bucketEnd = bucketStart + bucketWidth;
            if (bucketEnd > toUtc)
            {
                bucketEnd = toUtc;
            }

            var bucket = points
                .Where(point => point.TimestampUtc >= bucketStart && point.TimestampUtc < bucketEnd)
                .ToArray();

            if (bucket.Length > 0)
            {
                var usable = bucket
                    .Where(point =>
                        point.Value.HasValue &&
                        point.Availability is MetricAvailability.Available or MetricAvailability.Stale)
                    .Select(point => point.Value!.Value)
                    .ToArray();

                result.Add(new MetricHistoryAggregate(
                    id,
                    bucketStart,
                    bucketEnd,
                    usable.Length == 0 ? null : usable.Min(),
                    usable.Length == 0 ? null : usable.Average(),
                    usable.Length == 0 ? null : usable.Max(),
                    usable.Length == 0 ? null : usable[^1],
                    bucket.Length,
                    usable.Length,
                    bucket[^1].Availability));
            }

            bucketStart = bucketEnd;
        }

        return result;
    }

    private static MetricHistoryPoint ToPoint(MetricSample sample) =>
        new(
            sample.TimestampUtc,
            sample.Value,
            sample.Availability,
            sample.UnavailableReason,
            sample.Source.Id);

    private sealed class MetricRingBuffer
    {
        private readonly Lock _gate = new();
        private MetricHistoryPoint[] _items;
        private int _head;
        private int _count;

        public MetricRingBuffer(int capacity) => _items = new MetricHistoryPoint[capacity];

        public void Add(MetricHistoryPoint point, DateTimeOffset oldestAllowed)
        {
            lock (_gate)
            {
                RemoveOlderThan(oldestAllowed);
                var tail = (_head + _count) % _items.Length;
                _items[tail] = point;
                if (_count == _items.Length)
                {
                    _head = (_head + 1) % _items.Length;
                }
                else
                {
                    _count++;
                }
            }
        }

        public MetricHistoryPoint[] Snapshot()
        {
            lock (_gate)
            {
                var copy = new MetricHistoryPoint[_count];
                for (var index = 0; index < _count; index++)
                {
                    copy[index] = _items[(_head + index) % _items.Length];
                }

                return copy;
            }
        }

        public void Resize(int capacity)
        {
            lock (_gate)
            {
                if (_items.Length == capacity)
                {
                    return;
                }

                var keep = Math.Min(_count, capacity);
                var resized = new MetricHistoryPoint[capacity];
                var skip = _count - keep;
                for (var index = 0; index < keep; index++)
                {
                    resized[index] = _items[(_head + skip + index) % _items.Length];
                }

                _items = resized;
                _head = 0;
                _count = keep;
            }
        }

        private void RemoveOlderThan(DateTimeOffset oldestAllowed)
        {
            while (_count > 0 && _items[_head].TimestampUtc < oldestAllowed)
            {
                _head = (_head + 1) % _items.Length;
                _count--;
            }
        }
    }
}
