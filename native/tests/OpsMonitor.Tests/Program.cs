using OpsMonitor.Core.Alerts;
using OpsMonitor.Core.History;
using OpsMonitor.Core.Metrics;
using OpsMonitor.Core.Providers;
using OpsMonitor.Core.Runtime;
using OpsMonitor.Core.Settings;
using OpsMonitor.Widget.Models;
using System.Globalization;

if (args.Contains("--live", StringComparer.OrdinalIgnoreCase))
{
    return await RunLiveProbeAsync();
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("metric store publishes only meaningful changes", TestMetricStoreAsync),
    ("stale CPU bridge readings are never presented as live values", TestStaleCpuTemperatureAsync),
    ("history is bounded and downsamples deterministically", TestHistoryAsync),
    ("alerts honor pending, hysteresis, and cooldown", TestAlertsAsync),
    ("settings save atomically and round-trip", TestSettingsRoundTripAsync),
    ("legacy null metric ids are repaired safely", TestLegacyMetricIdRepairAsync),
    ("invalid settings fall back without crashing", TestInvalidSettingsAsync),
    ("widget modules honor Core order and visibility", TestWidgetModulesAsync),
    ("widget battery changes preserve unrelated Core modules", TestWidgetBatterySaveAsync)
};

var started = DateTimeOffset.UtcNow;
var failures = new List<string>();

foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"FAIL  {test.Name}");
        Console.ResetColor();
        Console.WriteLine(exception);
    }
    finally
    {
        Console.ResetColor();
    }
}

var elapsed = DateTimeOffset.UtcNow - started;
Console.WriteLine();
Console.WriteLine(
    failures.Count == 0
        ? $"All {tests.Length} OPS Monitor core tests passed in {elapsed.TotalMilliseconds:N0} ms."
        : $"{failures.Count} of {tests.Length} OPS Monitor core tests failed.");

return failures.Count == 0 ? 0 : 1;

static async Task<int> RunLiveProbeAsync()
{
    Console.WriteLine("Collecting live OPS Monitor metrics for up to 12 seconds...");
    await using var runtime = OpsRuntime.CreateDefault();
    await runtime.StartAsync();

    var deadline = DateTimeOffset.UtcNow.AddSeconds(12);
    while (DateTimeOffset.UtcNow < deadline)
    {
        var metrics = runtime.Metrics.GetSnapshot();
        if (HasUsable(metrics, WellKnownMetrics.CpuTotalUtilization) &&
            HasUsable(metrics, WellKnownMetrics.MemoryTotalBytes) &&
            metrics.ContainsKey(WellKnownMetrics.NetworkDownloadRate))
        {
            break;
        }

        await Task.Delay(250);
    }

    var snapshot = runtime.GetSnapshot();
    foreach (var pair in snapshot.Metrics.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal))
    {
        var sample = pair.Value;
        var value = sample.Value?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            ?? "--";
        Console.WriteLine(
            $"{pair.Key.Value,-38} {value,12}  {sample.Availability,-11}  {sample.Source.DisplayName}");
        if (!string.IsNullOrWhiteSpace(sample.Message))
        {
            Console.WriteLine($"  {sample.Message}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("Provider health");
    foreach (var health in snapshot.ProviderHealth.Values.OrderBy(
                 health => health.ProviderId,
                 StringComparer.Ordinal))
    {
        Console.WriteLine(
            $"{health.ProviderId,-28} {health.State,-12} " +
            $"failures={health.ConsecutiveFailures}  {health.Message}");
    }

    var hasCpu = snapshot.Metrics.TryGetValue(
        WellKnownMetrics.CpuTotalUtilization,
        out var cpu) && cpu.HasUsableValue;
    var hasMemory = snapshot.Metrics.TryGetValue(
        WellKnownMetrics.MemoryTotalBytes,
        out var memory) && memory.HasUsableValue;
    var hasNetwork = snapshot.Metrics.ContainsKey(WellKnownMetrics.NetworkDownloadRate);

    Console.WriteLine();
    Console.WriteLine(
        $"Live smoke result: CPU={(hasCpu ? "OK" : "MISSING")}, " +
        $"RAM={(hasMemory ? "OK" : "MISSING")}, NET={(hasNetwork ? "OK" : "MISSING")}.");
    return hasCpu && hasMemory && hasNetwork ? 0 : 1;

    static bool HasUsable(
        IReadOnlyDictionary<MetricId, MetricSample> metrics,
        MetricId id) =>
        metrics.TryGetValue(id, out var sample) && sample.HasUsableValue;
}

static Task TestMetricStoreAsync()
{
    var store = new MetricStore();
    var source = TestSource();
    var changedEvents = 0;
    store.Changed += (_, args) => changedEvents += args.Changes.Count;

    var first = MetricSample.Available(
        WellKnownMetrics.CpuTotalUtilization,
        42,
        DateTimeOffset.Parse("2026-07-25T20:00:00Z", CultureInfo.InvariantCulture),
        source);

    Assert.Equal(1, store.Apply([first]).Count, "first sample must be published");
    Assert.Equal(0, store.Apply([first with
    {
        TimestampUtc = first.TimestampUtc.AddSeconds(1)
    }]).Count, "timestamp-only changes should not force a repaint");
    Assert.Equal(1, store.Apply([first with
    {
        Value = 43,
        TimestampUtc = first.TimestampUtc.AddSeconds(2)
    }]).Count, "value changes must be published");
    Assert.Equal(2, changedEvents, "event count");
    Assert.True(
        store.TryGetLatest(WellKnownMetrics.CpuTotalUtilization, out var latest),
        "latest sample missing");
    Assert.Equal(43d, latest!.Value!.Value, "latest value");
    return Task.CompletedTask;
}

static async Task TestStaleCpuTemperatureAsync()
{
    var directory = Directory.CreateTempSubdirectory("OpsMonitorCpuTempTests-");
    try
    {
        var readingPath = Path.Combine(directory.FullName, "cpu-temperature.txt");
        var now = DateTimeOffset.Parse(
            "2026-07-25T20:00:00Z",
            CultureInfo.InvariantCulture);
        var publishedUtc = now.AddMinutes(-2);
        await File.WriteAllTextAsync(
            readingPath,
            $"61.4|{publishedUtc.UtcDateTime.Ticks}");

        await using var provider = new CpuTemperatureBridgeProvider(
            new CpuTemperatureBridgeOptions
            {
                ReadingPath = readingPath,
                MaximumAge = TimeSpan.FromSeconds(20)
            });
        var result = await provider.PollAsync(
            new MetricProviderContext
            {
                TimestampUtc = now,
                LatestSamples = new Dictionary<MetricId, MetricSample>()
            },
            CancellationToken.None);
        var sample = result.Samples.Single();

        Assert.Equal(
            MetricAvailability.Stale,
            sample.Availability,
            "expired bridge reading availability");
        Assert.False(
            sample.Value.HasValue,
            "expired bridge reading must not expose an old numeric value");
        Assert.False(
            sample.HasUsableValue,
            "expired bridge reading must not be treated as usable");
        Assert.Equal(
            ProviderHealthState.Degraded,
            result.HealthState,
            "stale bridge provider health");
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

static Task TestHistoryAsync()
{
    var source = TestSource();
    var start = DateTimeOffset.Parse(
        "2026-07-25T20:00:00Z",
        CultureInfo.InvariantCulture);
    var history = new MetricHistoryStore(3, TimeSpan.FromHours(1));
    for (var index = 0; index < 5; index++)
    {
        history.Add(MetricSample.Available(
            WellKnownMetrics.CpuTotalUtilization,
            index * 10,
            start.AddSeconds(index),
            source));
    }

    var retained = history.Get(WellKnownMetrics.CpuTotalUtilization);
    Assert.Equal(3, retained.Count, "ring buffer capacity");
    Assert.Equal(20d, retained[0].Value!.Value, "oldest retained value");

    var single = history.Get(
        WellKnownMetrics.CpuTotalUtilization,
        maximumPoints: 1);
    Assert.Equal(1, single.Count, "single-point downsample count");
    Assert.Equal(40d, single[0].Value!.Value, "single-point downsample uses newest");

    var aggregates = history.Aggregate(
        WellKnownMetrics.CpuTotalUtilization,
        start.AddSeconds(2),
        start.AddSeconds(5),
        TimeSpan.FromSeconds(3));
    Assert.Equal(1, aggregates.Count, "aggregate bucket count");
    Assert.Equal(20d, aggregates[0].Minimum!.Value, "aggregate minimum");
    Assert.Equal(30d, aggregates[0].Average!.Value, "aggregate average");
    Assert.Equal(40d, aggregates[0].Maximum!.Value, "aggregate maximum");
    return Task.CompletedTask;
}

static Task TestAlertsAsync()
{
    var engine = new AlertEngine();
    var source = TestSource();
    var metricId = WellKnownMetrics.CpuTemperature;
    var start = DateTimeOffset.Parse(
        "2026-07-25T20:00:00Z",
        CultureInfo.InvariantCulture);
    var transitions = new List<AlertTransition>();

    engine.UpsertRule(new AlertRule
    {
        Id = "cpu-hot",
        Name = "CPU hot",
        MetricId = metricId,
        Comparison = AlertComparison.GreaterThanOrEqual,
        Threshold = 90,
        PendingDuration = TimeSpan.FromSeconds(5),
        RecoveryHysteresis = 5,
        Cooldown = TimeSpan.FromSeconds(10)
    });
    engine.Transitioned += (_, args) => transitions.Add(args.Transition);

    engine.Evaluate([MetricSample.Available(metricId, 92, start, source)]);
    engine.Evaluate([MetricSample.Available(metricId, 93, start.AddSeconds(4), source)]);
    Assert.SequenceEqual(
        [AlertTransition.PendingStarted],
        transitions,
        "alert fired before pending duration");

    engine.Evaluate([MetricSample.Available(metricId, 94, start.AddSeconds(5), source)]);
    engine.Evaluate([MetricSample.Available(metricId, 87, start.AddSeconds(6), source)]);
    Assert.Equal(
        AlertLifecycleState.Active,
        engine.GetStates()["cpu-hot"].State,
        "hysteresis recovered too early");

    engine.Evaluate([MetricSample.Available(metricId, 85, start.AddSeconds(7), source)]);
    Assert.Equal(
        AlertLifecycleState.Cooldown,
        engine.GetStates()["cpu-hot"].State,
        "recovery did not enter cooldown");

    engine.Evaluate([MetricSample.Available(metricId, 70, start.AddSeconds(17), source)]);
    Assert.SequenceEqual(
        [
            AlertTransition.PendingStarted,
            AlertTransition.Triggered,
            AlertTransition.Recovered,
            AlertTransition.Rearmed
        ],
        transitions,
        "alert transition sequence");
    return Task.CompletedTask;
}

static async Task TestSettingsRoundTripAsync()
{
    var directory = Directory.CreateTempSubdirectory("OpsMonitorTests-");
    try
    {
        var path = Path.Combine(directory.FullName, "settings.json");
        using var repository = new JsonSettingsRepository(path);
        var settings = OpsSettingsDocument.CreateDefault() with
        {
            General = new GeneralSettings
            {
                LaunchAtSignIn = false,
                ShowTrayIcon = true
            }
        };

        await repository.SaveAsync(settings);
        Assert.True(File.Exists(path), "settings file was not created");
        Assert.False(
            Directory.EnumerateFiles(directory.FullName, "*.tmp").Any(),
            "temporary settings file leaked");

        var loaded = await repository.LoadAsync();
        Assert.Equal(
            OpsSettingsDocument.CurrentSchemaVersion,
            loaded.SchemaVersion,
            "settings schema");
        Assert.False(loaded.General.LaunchAtSignIn, "boolean setting did not round-trip");
        Assert.True(loaded.Widgets.Count > 0, "default widget missing after round-trip");
        Assert.Equal(
            WellKnownMetrics.CpuTotalUtilization,
            loaded.Widgets[0].Modules[0].PrimaryMetric,
            "module metric id did not round-trip");
        Assert.Equal(
            WellKnownMetrics.CpuTemperature,
            loaded.AlertRules[0].MetricId,
            "alert metric id did not round-trip");
        Assert.True(
            string.IsNullOrWhiteSpace(repository.LastLoadWarning),
            "valid settings emitted a warning");
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

static async Task TestInvalidSettingsAsync()
{
    var directory = Directory.CreateTempSubdirectory("OpsMonitorTests-");
    try
    {
        var path = Path.Combine(directory.FullName, "settings.json");
        await File.WriteAllTextAsync(path, "{ not valid json");
        using var repository = new JsonSettingsRepository(path);
        var loaded = await repository.LoadAsync();
        Assert.True(loaded.Widgets.Count > 0, "corrupt file did not fall back to defaults");
        Assert.True(
            !string.IsNullOrWhiteSpace(repository.LastLoadWarning),
            "corrupt file did not report a warning");
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

static async Task TestLegacyMetricIdRepairAsync()
{
    var directory = Directory.CreateTempSubdirectory("OpsMonitorLegacySettings-");
    try
    {
        var path = Path.Combine(directory.FullName, "settings.json");
        using var repository = new JsonSettingsRepository(path);
        await repository.SaveAsync(OpsSettingsDocument.CreateDefault());

        var json = await File.ReadAllTextAsync(path);
        json = System.Text.RegularExpressions.Regex.Replace(
            json,
            "\"value\"\\s*:\\s*\"[^\"]+\"",
            "\"value\": null",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        await File.WriteAllTextAsync(path, json);

        var repaired = await repository.LoadAsync();
        Assert.Equal(
            WellKnownMetrics.CpuTotalUtilization,
            repaired.Widgets[0].Modules[0].PrimaryMetric,
            "legacy CPU module metric repair");
        Assert.Equal(
            WellKnownMetrics.CpuTemperature,
            repaired.AlertRules[0].MetricId,
            "legacy CPU alert metric repair");
        Assert.Equal(
            WellKnownMetrics.NetworkPacketLoss,
            repaired.AlertRules[1].MetricId,
            "legacy network alert metric repair");
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

static Task TestWidgetModulesAsync()
{
    var modules = new[]
    {
        ModuleSettings.Create(
            "module-fps",
            "FPS",
            -1,
            new MetricId("gaming.fps")) with
        {
            Enabled = true,
            AdditionalMetrics = [default]
        },
        ModuleSettings.Create(
            "module-gpu",
            "GPU",
            0,
            WellKnownMetrics.GpuUtilization) with
        {
            Enabled = false
        },
        ModuleSettings.Create(
            "module-memory",
            "RAM",
            1,
            WellKnownMetrics.MemoryUsedBytes),
        ModuleSettings.Create(
            "module-cpu",
            "CPU",
            2,
            WellKnownMetrics.CpuTotalUtilization),
        ModuleSettings.Create(
            "module-network",
            "NET",
            3,
            WellKnownMetrics.NetworkDownloadRate) with
        {
            Enabled = false
        },
        ModuleSettings.Create(
            "module-latency",
            "PING",
            4,
            WellKnownMetrics.NetworkPing),
        ModuleSettings.Create(
            "module-battery",
            "BATTERY",
            5,
            WellKnownMetrics.BatteryCharge),
        ModuleSettings.Create(
            "module-storage",
            "STORAGE",
            6,
            new MetricId("storage.disk.activity"))
    };

    var configuration = WidgetModuleCatalog.FromCoreModules(modules);

    Assert.SequenceEqual(
        [
            WidgetModuleCatalog.Gpu,
            WidgetModuleCatalog.Memory,
            WidgetModuleCatalog.Cpu,
            WidgetModuleCatalog.Network,
            WidgetModuleCatalog.Battery,
            WidgetModuleCatalog.Storage
        ],
        configuration.Order,
        "supported module order");
    Assert.SequenceEqual(
        [
            WidgetModuleCatalog.Memory,
            WidgetModuleCatalog.Cpu,
            WidgetModuleCatalog.Network,
            WidgetModuleCatalog.Battery,
            WidgetModuleCatalog.Storage
        ],
        configuration.Enabled,
        "supported module visibility");
    Assert.Equal(
        1,
        configuration.Order.Count(key =>
            key.Equals(WidgetModuleCatalog.Network, StringComparison.Ordinal)),
        "network and latency were not de-duplicated");
    return Task.CompletedTask;
}

static Task TestWidgetBatterySaveAsync()
{
    var modules = new[]
    {
        ModuleSettings.Create(
            "module-network",
            "NET",
            0,
            WellKnownMetrics.NetworkDownloadRate) with
        {
            Enabled = false
        },
        ModuleSettings.Create(
            "module-latency",
            "PING",
            1,
            WellKnownMetrics.NetworkPing),
        ModuleSettings.Create(
            "module-fps",
            "FPS",
            2,
            new MetricId("gaming.fps"))
    };

    var withBattery = WidgetModuleCatalog.ApplyBatteryVisibility(modules, true);
    Assert.Equal(4, withBattery.Count, "battery module was not added");
    Assert.False(withBattery[0].Enabled, "network module state was overwritten");
    Assert.True(withBattery[1].Enabled, "latency module state was overwritten");
    Assert.True(withBattery[2].Enabled, "unsupported module state was overwritten");
    Assert.True(
        withBattery[3].Enabled &&
        withBattery[3].PrimaryMetric == WellKnownMetrics.BatteryCharge,
        "added battery module is invalid");

    var withoutBattery =
        WidgetModuleCatalog.ApplyBatteryVisibility(withBattery, false);
    Assert.False(withoutBattery[3].Enabled, "battery module was not disabled");
    Assert.False(withoutBattery[0].Enabled, "network module changed while hiding battery");
    Assert.True(withoutBattery[1].Enabled, "latency module changed while hiding battery");
    Assert.True(withoutBattery[2].Enabled, "unsupported module changed while hiding battery");
    return Task.CompletedTask;
}

static MetricSource TestSource() =>
    new()
    {
        Id = "test-source",
        DisplayName = "Test source",
        ProviderId = "test-provider",
        Kind = MetricSourceKind.Custom
    };

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string message)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{message}. Expected '{expected}', received '{actual}'.");
        }
    }

    public static void SequenceEqual<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual,
        string message)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{message}. Expected [{string.Join(", ", expected)}], " +
                $"received [{string.Join(", ", actual)}].");
        }
    }
}
