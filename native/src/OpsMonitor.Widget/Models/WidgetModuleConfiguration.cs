using OpsMonitor.Core.Metrics;
using OpsMonitor.Core.Settings;

namespace OpsMonitor.Widget.Models;

public sealed record WidgetModuleConfiguration(
    IReadOnlyList<string> Order,
    IReadOnlyList<string> Enabled);

public sealed record WidgetModuleMetricBinding(
    string PrimaryMetric,
    string? SecondaryMetric,
    IReadOnlyList<string> AdditionalMetrics);

public static class WidgetModuleCatalog
{
    public const string Cpu = "cpu";
    public const string Gpu = "gpu";
    public const string Memory = "memory";
    public const string Network = "network";
    public const string Latency = "latency";
    public const string Storage = "storage";
    public const string Battery = "battery";

    private static readonly string[] DefaultOrder =
    [
        Cpu,
        Gpu,
        Memory,
        Network,
        Latency,
        Storage,
        Battery
    ];

    private static readonly HashSet<string> SupportedKeys =
        new(DefaultOrder, StringComparer.Ordinal);

    public static List<string> CreateDefaultOrder() => [.. DefaultOrder];

    public static List<string> CreateDefaultEnabled() =>
        [Cpu, Gpu, Memory, Network, Latency];

    public static List<string> NormalizeOrder(IEnumerable<string>? keys)
    {
        var normalized = NormalizeKeys(keys);
        foreach (var key in DefaultOrder)
        {
            if (!normalized.Contains(key, StringComparer.Ordinal))
            {
                normalized.Add(key);
            }
        }

        return normalized;
    }

    public static List<string> NormalizeEnabled(IEnumerable<string>? keys) =>
        NormalizeKeys(keys);

    public static WidgetModuleConfiguration FromCoreModules(
        IEnumerable<ModuleSettings> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var mapped = modules
            .Select((module, index) => new
            {
                Module = module,
                Index = index,
                Key = MapCoreModule(module)
            })
            .Where(item => item.Key is not null)
            .OrderBy(item => item.Module.Order)
            .ThenBy(item => item.Index)
            .ToArray();

        var order = NormalizeOrder(mapped.Select(item => item.Key!));
        var enabled = mapped
            .Where(item => item.Module.Enabled)
            .Select(item => item.Key!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new WidgetModuleConfiguration(order, enabled);
    }

    public static IReadOnlyDictionary<string, WidgetModulePresentation> GetPresentation(
        IEnumerable<ModuleSettings> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var result = new Dictionary<string, WidgetModulePresentation>(StringComparer.Ordinal);
        foreach (var module in modules.OrderBy(module => module.Order))
        {
            var key = MapCoreModule(module);
            if (key is null)
            {
                continue;
            }

            result[key] = new WidgetModulePresentation
            {
                Size = module.Size switch
                {
                    ModuleSize.Medium => WidgetModuleSize.Medium,
                    ModuleSize.Large => WidgetModuleSize.Large,
                    ModuleSize.Wide => WidgetModuleSize.Wide,
                    _ => WidgetModuleSize.Small
                },
                Visualization = module.Visualization switch
                {
                    ModuleVisualization.Value => WidgetModuleVisualization.Value,
                    ModuleVisualization.Progress => WidgetModuleVisualization.Progress,
                    ModuleVisualization.Sparkline => WidgetModuleVisualization.Sparkline,
                    ModuleVisualization.Gauge => WidgetModuleVisualization.Gauge,
                    _ => WidgetModuleVisualization.ValueAndSparkline
                },
                ShowLabel = module.ShowLabel,
                ShowSecondaryValue = module.ShowSecondaryValue,
                ShowTrend = module.ShowTrend,
                DecimalPlacesOverride = module.DecimalPlacesOverride is { } decimals
                    ? Math.Clamp(decimals, 0, 3)
                    : null
            };
        }

        return result;
    }

    public static IReadOnlyDictionary<string, WidgetModuleMetricBinding> GetMetricBindings(
        IEnumerable<ModuleSettings> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var result = new Dictionary<string, WidgetModuleMetricBinding>(StringComparer.Ordinal);
        foreach (var module in modules.OrderBy(module => module.Order))
        {
            string? key = MapCoreModule(module);
            if (key is null)
            {
                continue;
            }

            result[key] = new WidgetModuleMetricBinding(
                module.PrimaryMetric.Value,
                module.SecondaryMetric?.Value,
                (module.AdditionalMetrics ?? [])
                    .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                    .Select(item => item.Value)
                    .Distinct(StringComparer.Ordinal)
                    .Take(3)
                    .ToArray());
        }

        return result;
    }

    public static List<ModuleSettings> ApplyBatteryVisibility(
        IEnumerable<ModuleSettings> modules,
        bool isVisible)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var result = modules.ToList();
        var batteryIndexes = result
            .Select((module, index) => new { Module = module, Index = index })
            .Where(item => StringComparer.Ordinal.Equals(
                MapCoreModule(item.Module),
                Battery))
            .Select(item => item.Index)
            .ToArray();

        if (!isVisible)
        {
            foreach (var index in batteryIndexes)
            {
                result[index] = result[index] with { Enabled = false };
            }

            return result;
        }

        if (batteryIndexes.Length == 0)
        {
            result.Add(ModuleSettings.Create(
                "module-battery",
                "BATTERY",
                NextOrder(result),
                WellKnownMetrics.BatteryCharge,
                WellKnownMetrics.BatteryRemaining));
            return result;
        }

        if (batteryIndexes.Any(index => result[index].Enabled))
        {
            return result;
        }

        var firstIndex = batteryIndexes[0];
        result[firstIndex] = result[firstIndex] with { Enabled = true };
        return result;
    }

    public static List<ModuleSettings> ApplyConfiguration(
        IEnumerable<ModuleSettings> modules,
        IEnumerable<string>? order,
        IEnumerable<string>? enabled,
        IReadOnlyDictionary<string, WidgetModulePresentation>? presentation = null)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var result = modules.ToList();
        var normalizedOrder = NormalizeOrder(order);
        var enabledKeys = NormalizeEnabled(enabled)
            .ToHashSet(StringComparer.Ordinal);
        var nextUnsupportedOrder = normalizedOrder.Count;

        for (var index = 0; index < result.Count; index++)
        {
            var module = result[index];
            var key = MapCoreModule(module);
            if (key is null)
            {
                result[index] = module with
                {
                    Order = Math.Max(module.Order, nextUnsupportedOrder++)
                };
                continue;
            }

            var moduleOrder = normalizedOrder.IndexOf(key);
            var mapped = module with
            {
                Enabled = enabledKeys.Contains(key),
                Order = moduleOrder >= 0 ? moduleOrder : nextUnsupportedOrder++
            };

            if (presentation is not null &&
                presentation.TryGetValue(key, out var options))
            {
                mapped = mapped with
                {
                    Size = options.Size switch
                    {
                        WidgetModuleSize.Medium => ModuleSize.Medium,
                        WidgetModuleSize.Large => ModuleSize.Large,
                        WidgetModuleSize.Wide => ModuleSize.Wide,
                        _ => ModuleSize.Small
                    },
                    Visualization = options.Visualization switch
                    {
                        WidgetModuleVisualization.Value => ModuleVisualization.Value,
                        WidgetModuleVisualization.Progress => ModuleVisualization.Progress,
                        WidgetModuleVisualization.Sparkline => ModuleVisualization.Sparkline,
                        WidgetModuleVisualization.Gauge => ModuleVisualization.Gauge,
                        _ => ModuleVisualization.ValueAndSparkline
                    },
                    ShowLabel = options.ShowLabel,
                    ShowSecondaryValue = options.ShowSecondaryValue,
                    ShowTrend = options.ShowTrend,
                    DecimalPlacesOverride = options.DecimalPlacesOverride
                };
            }

            result[index] = mapped;
        }

        return result;
    }

    private static List<string> NormalizeKeys(IEnumerable<string>? keys)
    {
        if (keys is null)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>();
        foreach (var rawKey in keys)
        {
            var key = NormalizeKey(rawKey);
            if (key is not null && seen.Add(key))
            {
                normalized.Add(key);
            }
        }

        return normalized;
    }

    private static string? NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var normalized = key.Trim().ToLowerInvariant() switch
        {
            "ram" => Memory,
            "net" => Network,
            "ping" => Latency,
            "disk" => Storage,
            var value => value
        };
        return SupportedKeys.Contains(normalized) ? normalized : null;
    }

    private static string? MapCoreModule(ModuleSettings module)
    {
        var id = module.Id?.Trim().ToLowerInvariant() ?? string.Empty;
        if (id is "module-cpu")
        {
            return Cpu;
        }

        if (id is "module-gpu")
        {
            return Gpu;
        }

        if (id is "module-memory" or "module-ram")
        {
            return Memory;
        }

        if (id is "module-network")
        {
            return Network;
        }

        if (id is "module-latency")
        {
            return Latency;
        }

        if (id is "module-storage" or "module-disk")
        {
            return Storage;
        }

        if (id is "module-battery")
        {
            return Battery;
        }

        foreach (var metric in EnumerateMetrics(module))
        {
            var value = metric.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (value.StartsWith("cpu.", StringComparison.Ordinal))
            {
                return Cpu;
            }

            if (value.StartsWith("gpu.", StringComparison.Ordinal))
            {
                return Gpu;
            }

            if (value.StartsWith("memory.", StringComparison.Ordinal))
            {
                return Memory;
            }

            if (value.StartsWith("network.", StringComparison.Ordinal))
            {
                return metric == WellKnownMetrics.NetworkPing ||
                       metric == WellKnownMetrics.NetworkPacketLoss ||
                       metric == WellKnownMetrics.NetworkJitter
                    ? Latency
                    : Network;
            }

            if (value.StartsWith("storage.", StringComparison.Ordinal) ||
                value.StartsWith("disk.", StringComparison.Ordinal))
            {
                return Storage;
            }

            if (value.StartsWith("battery.", StringComparison.Ordinal))
            {
                return Battery;
            }
        }

        return null;
    }

    private static IEnumerable<MetricId> EnumerateMetrics(ModuleSettings module)
    {
        yield return module.PrimaryMetric;
        if (module.SecondaryMetric is { } secondary)
        {
            yield return secondary;
        }

        foreach (var metric in module.AdditionalMetrics ?? [])
        {
            yield return metric;
        }
    }

    private static int NextOrder(List<ModuleSettings> modules) =>
        modules.Count == 0 ? 0 : modules.Max(module => module.Order) + 1;
}
