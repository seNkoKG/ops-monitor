using OpsMonitor.Core.Metrics;
using OpsMonitor.Core.Settings;

namespace OpsMonitor.Widget.Models;

public sealed record WidgetModuleConfiguration(
    IReadOnlyList<string> Order,
    IReadOnlyList<string> Enabled);

public static class WidgetModuleCatalog
{
    public const string Cpu = "cpu";
    public const string Gpu = "gpu";
    public const string Memory = "memory";
    public const string Network = "network";
    public const string Storage = "storage";
    public const string Battery = "battery";

    private static readonly string[] DefaultOrder =
    [
        Cpu,
        Gpu,
        Memory,
        Network,
        Storage,
        Battery
    ];

    private static readonly HashSet<string> SupportedKeys =
        new(DefaultOrder, StringComparer.Ordinal);

    public static List<string> CreateDefaultOrder() => [.. DefaultOrder];

    public static List<string> CreateDefaultEnabled() =>
        [Cpu, Gpu, Memory, Network];

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
            "net" or "latency" => Network,
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

        if (id is "module-network" or "module-latency")
        {
            return Network;
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
                return Network;
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
