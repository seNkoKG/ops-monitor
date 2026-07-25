using OpsMonitor.Core.Metrics;

namespace OpsMonitor.Core.Alerts;

public enum AlertComparison
{
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    OutsideRange,
    InsideRange
}

public enum AlertSeverity
{
    Information,
    Warning,
    Critical
}

public enum AlertLifecycleState
{
    Inactive,
    Pending,
    Active,
    Cooldown
}

public enum AlertTransition
{
    PendingStarted,
    PendingCancelled,
    Triggered,
    Recovered,
    Rearmed
}

public sealed record AlertRule
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required MetricId MetricId { get; init; }
    public AlertComparison Comparison { get; init; }
    public double Threshold { get; init; }
    public double? SecondaryThreshold { get; init; }
    public TimeSpan PendingDuration { get; init; } = TimeSpan.Zero;
    public double RecoveryHysteresis { get; init; }
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(5);
    public AlertSeverity Severity { get; init; } = AlertSeverity.Warning;
    public bool Enabled { get; init; } = true;
}

public sealed record AlertStateSnapshot
{
    public required string RuleId { get; init; }
    public AlertLifecycleState State { get; init; }
    public DateTimeOffset? PendingSinceUtc { get; init; }
    public DateTimeOffset? ActiveSinceUtc { get; init; }
    public DateTimeOffset? LastTriggeredUtc { get; init; }
    public DateTimeOffset? LastRecoveredUtc { get; init; }
    public DateTimeOffset? CooldownUntilUtc { get; init; }
    public double? LastValue { get; init; }
}

public sealed class AlertTransitionEventArgs : EventArgs
{
    public AlertTransitionEventArgs(
        AlertRule rule,
        AlertTransition transition,
        AlertStateSnapshot previous,
        AlertStateSnapshot current,
        MetricSample sample)
    {
        Rule = rule;
        Transition = transition;
        Previous = previous;
        Current = current;
        Sample = sample;
    }

    public AlertRule Rule { get; }
    public AlertTransition Transition { get; }
    public AlertStateSnapshot Previous { get; }
    public AlertStateSnapshot Current { get; }
    public MetricSample Sample { get; }
}

public sealed class AlertEngine
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, AlertRule> _rules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AlertStateSnapshot> _states =
        new(StringComparer.Ordinal);

    public event EventHandler<AlertTransitionEventArgs>? Transitioned;

    public void ReplaceRules(IEnumerable<AlertRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var validated = rules.Select(Validate).ToArray();
        var duplicate = validated
            .GroupBy(rule => rule.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Duplicate alert rule id '{duplicate.Key}'.", nameof(rules));
        }

        lock (_gate)
        {
            var retainedStates = _states
                .Where(pair => validated.Any(rule =>
                    StringComparer.Ordinal.Equals(rule.Id, pair.Key)))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

            _rules.Clear();
            foreach (var rule in validated)
            {
                _rules[rule.Id] = rule;
            }

            _states.Clear();
            foreach (var pair in retainedStates)
            {
                _states[pair.Key] = pair.Value;
            }
        }
    }

    public void UpsertRule(AlertRule rule)
    {
        rule = Validate(rule);
        lock (_gate)
        {
            _rules[rule.Id] = rule;
        }
    }

    public bool RemoveRule(string ruleId)
    {
        lock (_gate)
        {
            _states.Remove(ruleId);
            return _rules.Remove(ruleId);
        }
    }

    public IReadOnlyList<AlertRule> GetRules()
    {
        lock (_gate)
        {
            return _rules.Values.ToArray();
        }
    }

    public IReadOnlyDictionary<string, AlertStateSnapshot> GetStates()
    {
        lock (_gate)
        {
            return new Dictionary<string, AlertStateSnapshot>(_states, StringComparer.Ordinal);
        }
    }

    public void Evaluate(IEnumerable<MetricSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        List<AlertTransitionEventArgs> transitions = [];

        lock (_gate)
        {
            foreach (var sample in samples)
            {
                if (!sample.HasUsableValue)
                {
                    CancelPendingForUnavailableSample(sample, transitions);
                    continue;
                }

                foreach (var rule in _rules.Values.Where(rule =>
                             rule.Enabled && rule.MetricId == sample.MetricId))
                {
                    EvaluateRule(rule, sample, transitions);
                }
            }
        }

        foreach (var transition in transitions)
        {
            Transitioned?.Invoke(this, transition);
        }
    }

    public void Reset(string? ruleId = null)
    {
        lock (_gate)
        {
            if (ruleId is null)
            {
                _states.Clear();
            }
            else
            {
                _states.Remove(ruleId);
            }
        }
    }

    private void EvaluateRule(
        AlertRule rule,
        MetricSample sample,
        ICollection<AlertTransitionEventArgs> transitions)
    {
        var value = sample.Value!.Value;
        var state = _states.GetValueOrDefault(rule.Id) ?? NewState(rule.Id);
        var timestamp = sample.TimestampUtc;

        if (state.State == AlertLifecycleState.Cooldown &&
            state.CooldownUntilUtc <= timestamp)
        {
            var rearmed = state with
            {
                State = AlertLifecycleState.Inactive,
                CooldownUntilUtc = null,
                LastValue = value
            };
            AddTransition(rule, AlertTransition.Rearmed, state, rearmed, sample, transitions);
            state = rearmed;
        }

        var conditionMet = IsConditionMet(rule, value);
        switch (state.State)
        {
            case AlertLifecycleState.Inactive when conditionMet:
                if (rule.PendingDuration <= TimeSpan.Zero)
                {
                    var active = state with
                    {
                        State = AlertLifecycleState.Active,
                        ActiveSinceUtc = timestamp,
                        LastTriggeredUtc = timestamp,
                        LastValue = value
                    };
                    AddTransition(rule, AlertTransition.Triggered, state, active, sample, transitions);
                    state = active;
                }
                else
                {
                    var pending = state with
                    {
                        State = AlertLifecycleState.Pending,
                        PendingSinceUtc = timestamp,
                        LastValue = value
                    };
                    AddTransition(
                        rule,
                        AlertTransition.PendingStarted,
                        state,
                        pending,
                        sample,
                        transitions);
                    state = pending;
                }

                break;

            case AlertLifecycleState.Pending:
                if (!conditionMet)
                {
                    var inactive = state with
                    {
                        State = AlertLifecycleState.Inactive,
                        PendingSinceUtc = null,
                        LastValue = value
                    };
                    AddTransition(
                        rule,
                        AlertTransition.PendingCancelled,
                        state,
                        inactive,
                        sample,
                        transitions);
                    state = inactive;
                }
                else if (timestamp - state.PendingSinceUtc >= rule.PendingDuration)
                {
                    var active = state with
                    {
                        State = AlertLifecycleState.Active,
                        PendingSinceUtc = null,
                        ActiveSinceUtc = timestamp,
                        LastTriggeredUtc = timestamp,
                        LastValue = value
                    };
                    AddTransition(rule, AlertTransition.Triggered, state, active, sample, transitions);
                    state = active;
                }

                break;

            case AlertLifecycleState.Active when IsRecovered(rule, value):
                var recovered = state with
                {
                    State = rule.Cooldown > TimeSpan.Zero
                        ? AlertLifecycleState.Cooldown
                        : AlertLifecycleState.Inactive,
                    ActiveSinceUtc = null,
                    LastRecoveredUtc = timestamp,
                    CooldownUntilUtc = rule.Cooldown > TimeSpan.Zero
                        ? timestamp + rule.Cooldown
                        : null,
                    LastValue = value
                };
                AddTransition(rule, AlertTransition.Recovered, state, recovered, sample, transitions);
                state = recovered;
                break;
        }

        _states[rule.Id] = state with { LastValue = value };
    }

    private void CancelPendingForUnavailableSample(
        MetricSample sample,
        ICollection<AlertTransitionEventArgs> transitions)
    {
        foreach (var rule in _rules.Values.Where(rule =>
                     rule.Enabled && rule.MetricId == sample.MetricId))
        {
            if (!_states.TryGetValue(rule.Id, out var state) ||
                state.State != AlertLifecycleState.Pending)
            {
                continue;
            }

            var inactive = state with
            {
                State = AlertLifecycleState.Inactive,
                PendingSinceUtc = null,
                LastValue = null
            };
            AddTransition(
                rule,
                AlertTransition.PendingCancelled,
                state,
                inactive,
                sample,
                transitions);
            _states[rule.Id] = inactive;
        }
    }

    private static bool IsConditionMet(AlertRule rule, double value) =>
        rule.Comparison switch
        {
            AlertComparison.GreaterThan => value > rule.Threshold,
            AlertComparison.GreaterThanOrEqual => value >= rule.Threshold,
            AlertComparison.LessThan => value < rule.Threshold,
            AlertComparison.LessThanOrEqual => value <= rule.Threshold,
            AlertComparison.OutsideRange =>
                value < rule.Threshold || value > rule.SecondaryThreshold!.Value,
            AlertComparison.InsideRange =>
                value >= rule.Threshold && value <= rule.SecondaryThreshold!.Value,
            _ => false
        };

    private static bool IsRecovered(AlertRule rule, double value) =>
        rule.Comparison switch
        {
            AlertComparison.GreaterThan or AlertComparison.GreaterThanOrEqual =>
                value <= rule.Threshold - rule.RecoveryHysteresis,
            AlertComparison.LessThan or AlertComparison.LessThanOrEqual =>
                value >= rule.Threshold + rule.RecoveryHysteresis,
            AlertComparison.OutsideRange =>
                value >= rule.Threshold + rule.RecoveryHysteresis &&
                value <= rule.SecondaryThreshold!.Value - rule.RecoveryHysteresis,
            AlertComparison.InsideRange =>
                value < rule.Threshold - rule.RecoveryHysteresis ||
                value > rule.SecondaryThreshold!.Value + rule.RecoveryHysteresis,
            _ => true
        };

    private static AlertRule Validate(AlertRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(rule.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(rule.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(rule.MetricId.Value);
        if (rule.PendingDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rule),
                "Alert pending duration cannot be negative.");
        }

        if (rule.Cooldown < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rule),
                "Alert cooldown cannot be negative.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(rule.RecoveryHysteresis);

        if (rule.Comparison is AlertComparison.OutsideRange or AlertComparison.InsideRange)
        {
            if (!rule.SecondaryThreshold.HasValue ||
                rule.SecondaryThreshold.Value <= rule.Threshold)
            {
                throw new ArgumentException(
                    "Range alerts require a secondary threshold greater than the first threshold.",
                    nameof(rule));
            }
        }

        return rule;
    }

    private static AlertStateSnapshot NewState(string ruleId) =>
        new() { RuleId = ruleId, State = AlertLifecycleState.Inactive };

    private static void AddTransition(
        AlertRule rule,
        AlertTransition transition,
        AlertStateSnapshot previous,
        AlertStateSnapshot current,
        MetricSample sample,
        ICollection<AlertTransitionEventArgs> transitions) =>
        transitions.Add(
            new AlertTransitionEventArgs(rule, transition, previous, current, sample));
}
