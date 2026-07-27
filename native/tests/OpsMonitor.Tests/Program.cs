using System.Globalization;
using System.Security.Principal;
using System.Text.Json;
using OpsMonitor.Core.Alerts;
using OpsMonitor.Core.Diagnostics;
using OpsMonitor.Core.History;
using OpsMonitor.Core.Metrics;
using OpsMonitor.Core.Platform;
using OpsMonitor.Core.Providers;
using OpsMonitor.Core.Runtime;
using OpsMonitor.Core.Settings;
using OpsMonitor.Studio.Models;
using OpsMonitor.Studio.Services;
using OpsMonitor.Studio.ViewModels;
using OpsMonitor.Widget.Models;
using OpsMonitor.Widget.Services;
using OpsMonitor.Widget.ViewModels;
using LiveWidgetSizingPolicy = OpsMonitor.Widget.Models.WidgetSizingPolicy;

if (args.Contains("--live", StringComparer.OrdinalIgnoreCase))
{
    return await RunLiveProbeAsync();
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("metric store publishes only meaningful changes", TestMetricStoreAsync),
    ("stale CPU bridge readings are never presented as live values", TestStaleCpuTemperatureAsync),
    ("invalid and future CPU bridge readings are rejected", TestInvalidCpuTemperatureAsync),
    ("CPU bridge defaults to protected per-user sensor data", TestCpuBridgeDefaultPathAsync),
    ("history is bounded and downsamples deterministically", TestHistoryAsync),
    ("alerts honor pending, hysteresis, and cooldown", TestAlertsAsync),
    ("settings save atomically and round-trip", TestSettingsRoundTripAsync),
    ("future settings schemas remain read-only", TestFutureSettingsSchemaAsync),
    ("explicit null settings members fall back safely", TestNullSettingsMembersAsync),
    ("Studio settings migrate safely and save atomically", TestStudioSettingsMigrationAsync),
    ("Studio mapping preserves interaction and independent modules", TestStudioRuntimeMappingAsync),
    ("Studio merges concurrent edits without losing widget changes", TestStudioConflictMergeAsync),
    ("Studio controls support complete undo and valid command states", TestStudioControlSemanticsAsync),
    ("legacy null metric ids are repaired safely", TestLegacyMetricIdRepairAsync),
    ("invalid settings fall back without crashing", TestInvalidSettingsAsync),
    ("widget modules honor Core order and visibility", TestWidgetModulesAsync),
    ("widget battery changes preserve unrelated Core modules", TestWidgetBatterySaveAsync),
    ("widget module presentation saves without collapsing latency", TestWidgetModuleSaveAsync),
    ("widget sizing preserves classic footprints and expands by module count", TestWidgetSizingAsync),
    ("Studio and widget sizing policies remain identical", TestSizingParityAsync),
    ("widget text remains invariant under Slovenian locale", TestWidgetFormattingAsync),
    ("widget settings preserve scale and interaction preferences", TestWidgetSettingsMappingAsync),
    ("built-in widget themes survive shared-settings round trips", TestBuiltInThemePersistenceAsync),
    ("demo and reset launches are ephemeral", TestEphemeralLaunchAsync),
    ("geometry saves do not request telemetry reloads", TestRuntimeReloadPolicyAsync),
    ("CPU sensor watchdog accepts only fresh valid bridge readings", TestCpuSensorWatchdogAsync),
    ("CPU sensor recovery uses gated monotonic backoff", TestCpuSensorRecoveryPolicyAsync),
    ("widget view model applies live telemetry and readable clamps", TestWidgetViewModelAsync),
    ("unavailable connectivity never renders false zero values", TestUnavailableConnectivityAsync),
    ("partial CPU telemetry never renders a false zero", TestPartialCpuTelemetryAsync),
    ("partial GPU telemetry keeps VRAM availability independent", TestPartialGpuTelemetryAsync),
    ("partial RAM telemetry never fabricates used memory", TestPartialMemoryTelemetryAsync),
    ("missing download telemetry preserves an available upload", TestPartialDownloadTelemetryAsync),
    ("missing upload telemetry preserves an available download", TestPartialUploadTelemetryAsync),
    ("diagnostic logs remain bounded and rotate", TestDiagnosticLogRotationAsync),
    ("concurrent settings updates preserve every writer", TestConcurrentSettingsUpdatesAsync),
    ("single-instance lease rejects a second process owner", TestSingleInstanceAsync),
    // WPF permits one process-global Application. Keep this smoke test last.
    ("Studio application resources load on an STA thread", TestStudioApplicationResourcesAsync)
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

static async Task TestStudioSettingsMigrationAsync()
{
    var directory = Directory.CreateTempSubdirectory("OpsMonitorStudioSettings-");
    try
    {
        var settingsPath = Path.Combine(directory.FullName, "studio-settings.json");
        var legacy = new StudioSettingsSnapshot(
            Scene: "  Daily driver  ",
            Layout: "Canvas",
            Theme: "abyss",
            BackgroundOpacity: 0.01,
            ContentOpacity: 0.2,
            BlurStrength: 200,
            Density: "Invalid",
            FontScale: 4,
            AlwaysOnTop: true,
            PositionLocked: false,
            ClickThrough: false,
            StartAtSignIn: false,
            SnapToGrid: true,
            VisibleModules: ["cpu", "cpu", "net"],
            WidgetWidth: 40,
            WidgetHeight: 40,
            WidgetScalePercent: 20,
            UpdateCadenceSeconds: 100,
            PerformanceMode: "Invalid",
            SchemaVersion: 0);
        await File.WriteAllTextAsync(
            settingsPath,
            JsonSerializer.Serialize(legacy));

        using var store = new LocalStudioSettingsSink(settingsPath);
        var migrated = store.Reload() ??
            throw new InvalidOperationException("legacy Studio settings were not loaded");

        Assert.Equal("Mini", migrated.Layout, "Canvas migration");
        Assert.Equal("void", migrated.Theme, "legacy theme migration");
        Assert.Equal(0.08, migrated.BackgroundOpacity, "surface opacity floor");
        Assert.Equal(0.82, migrated.ContentOpacity, "content opacity floor");
        Assert.Equal(176d, migrated.WidgetWidth, "readable width floor");
        Assert.Equal(140d, migrated.WidgetHeight, "readable height floor");
        Assert.Equal(80, migrated.WidgetScalePercent, "scale floor");
        Assert.Equal(10d, migrated.UpdateCadenceSeconds, "cadence ceiling");
        Assert.SequenceEqual(
            ["cpu", "net"],
            migrated.VisibleModules,
            "visible module normalization");

        store.Save(migrated);
        var roundTrip = store.Reload() ??
            throw new InvalidOperationException("saved Studio settings were not reloaded");
        Assert.Equal(2, roundTrip.SchemaVersion, "Studio schema version");
        Assert.False(
            Directory.EnumerateFiles(directory.FullName, "*.tmp").Any(),
            "temporary Studio settings file leaked");
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

static Task TestStudioRuntimeMappingAsync()
{
    var preservedAlert = new AlertRule
    {
        Id = "preserved-alert",
        Name = "Preserved",
        MetricId = WellKnownMetrics.CpuTemperature,
        Threshold = 90,
        Enabled = true
    };
    var current = OpsSettingsDocument.CreateDefault() with
    {
        AlertRules = [preservedAlert]
    };
    var modules = new[]
    {
        new StudioModuleSnapshot(
            "net",
            "Network",
            0,
            true,
            "Small",
            "Bar + sparkline",
            true,
            true,
            true,
            "Adaptive",
            "↕",
            "#FF62A7FF"),
        new StudioModuleSnapshot(
            "latency",
            "Latency",
            1,
            true,
            "Medium",
            "Number only",
            true,
            false,
            false,
            "Whole numbers",
            "⌁",
            "#FFFFC95C")
    };
    var snapshot = new StudioSettingsSnapshot(
        Scene: "Daily driver",
        Layout: "Mini",
        Theme: "slate",
        BackgroundOpacity: 0.01,
        ContentOpacity: 0.2,
        BlurStrength: 0,
        Density: "Compact",
        FontScale: 1,
        AlwaysOnTop: true,
        PositionLocked: true,
        ClickThrough: false,
        StartAtSignIn: false,
        SnapToGrid: false,
        VisibleModules: ["net", "latency"],
        Draggable: true,
        Resizable: true,
        WidgetWidth: 160,
        WidgetHeight: 100,
        WidgetScalePercent: 80,
        UpdateCadenceSeconds: 1,
        PerformanceMode: "Efficiency",
        Modules: modules);

    var mapped = StudioCoreSettingsSink.MapRuntime(snapshot, current);
    var widget = mapped.Widgets.Single(item =>
        item.Id == current.Widgets[0].Id);
    Assert.Equal(WidgetDesign.Canvas, widget.Design, "Mini runtime design");
    Assert.Equal(176d, widget.Window.Width, "runtime width floor");
    Assert.Equal(140d, widget.Window.Height, "runtime height floor");
    Assert.Equal(0.08, widget.Window.SurfaceOpacity, "runtime surface opacity floor");
    Assert.Equal(0.82, widget.Window.ContentOpacity, "runtime content opacity floor");
    Assert.True(widget.Window.Locked, "runtime position lock");
    Assert.True(widget.Window.Draggable, "lock erased draggable preference");
    Assert.True(widget.Window.Resizable, "lock erased resizable preference");
    Assert.SequenceEqual(
        ["module-network", "module-latency"],
        widget.Modules.Select(module => module.Id),
        "network and latency modules were collapsed");
    Assert.False(
        widget.Modules[1].ShowSecondaryValue,
        "secondary-value preference was ignored");
    Assert.Equal(
        "preserved-alert",
        mapped.AlertRules.Single().Id,
        "Studio overwrote an alert it does not edit");

    var upperBounded = StudioCoreSettingsSink.MapRuntime(
        snapshot with
        {
            WidgetWidth = 2_000,
            WidgetHeight = 1_200
        },
        current).Widgets.Single(item => item.Id == current.Widgets[0].Id);
    Assert.Equal(1_600d, upperBounded.Window.Width, "runtime width ceiling");
    Assert.Equal(1_000d, upperBounded.Window.Height, "runtime height ceiling");
    return Task.CompletedTask;
}

static Task TestStudioConflictMergeAsync()
{
    var baseline = CreateStudioSnapshot();
    var edited = baseline with
    {
        Density = "Airy",
        WidgetScalePercent = 125
    };
    var external = baseline with
    {
        BackgroundOpacity = 0.42,
        AlwaysOnTop = false,
        UpdateCadenceSeconds = 0.5
    };

    var merged = StudioCoreSettingsSink.MergeUserEdits(baseline, edited, external);
    Assert.Equal("Airy", merged.Density, "Studio density edit was lost");
    Assert.Equal(125, merged.WidgetScalePercent, "Studio scale edit was lost");
    Assert.Equal(0.42, merged.BackgroundOpacity, "external opacity update was overwritten");
    Assert.False(merged.AlwaysOnTop, "external topmost update was overwritten");
    Assert.Equal(0.5, merged.UpdateCadenceSeconds, "external cadence update was overwritten");
    return Task.CompletedTask;
}

static Task TestStudioControlSemanticsAsync()
{
    using var viewModel = new StudioViewModel(new FakeStudioSettingsSink());
    Assert.Equal(
        "cpu,gpu,ram,net,latency,disk,battery",
        string.Join(',', viewModel.Modules.Select(module => module.Id)),
        "Studio exposed an unsupported widget module");

    Assert.False(
        viewModel.SelectLayoutCommand.CanExecute(viewModel.SelectedLayout),
        "active layout remained clickable");
    Assert.False(
        viewModel.ApplyThemeCommand.CanExecute(viewModel.SelectedTheme),
        "active theme remained clickable");
    var activeScene = viewModel.Scenes.Single(scene => scene.IsActive);
    Assert.False(
        viewModel.ActivateSceneCommand.CanExecute(activeScene),
        "active scene remained clickable");
    Assert.False(
        viewModel.MoveModuleUpCommand.CanExecute(viewModel.Modules[0]),
        "top module could move beyond the first position");

    viewModel.BackgroundOpacity = 0.41;
    Assert.True(viewModel.UndoCommand.CanExecute(null), "opacity edit was not undoable");
    viewModel.UndoCommand.Execute(null);
    Assert.Equal(0.82, viewModel.BackgroundOpacity, "opacity undo failed");
    viewModel.RedoCommand.Execute(null);
    Assert.Equal(0.41, viewModel.BackgroundOpacity, "opacity redo failed");

    var cpu = viewModel.Modules.Single(module => module.Id == "cpu");
    cpu.ShowTemperature = false;
    viewModel.UndoCommand.Execute(null);
    Assert.True(cpu.ShowTemperature, "module presentation undo failed");
    cpu.Precision = "2 decimals";
    Assert.Equal("42.00%", cpu.PreviewPrimaryValue, "preview precision was not applied");

    viewModel.Density = "Airy";
    viewModel.SelectedLayout = "Dock";
    Assert.Equal("Compact", viewModel.Density, "Studio Dock density was not normalized");
    Assert.False(
        viewModel.CanChangeDensity,
        "Studio enabled density choices that Dock cannot render");
    viewModel.Density = "Comfortable";
    Assert.Equal("Compact", viewModel.Density, "Studio accepted a non-compact Dock density");

    viewModel.ContentOpacity = 0.9;
    viewModel.ReloadCommand.Execute(null);
    Assert.False(viewModel.UndoCommand.CanExecute(null), "reload retained stale undo history");
    return Task.CompletedTask;
}

static Task TestSizingParityAsync()
{
    var layouts = new[]
    {
        ("Pill", WidgetLayout.Pill),
        ("Rail", WidgetLayout.Rail),
        ("Dock", WidgetLayout.Dock),
        ("Mini", WidgetLayout.Mini)
    };
    var densities = new[]
    {
        ("Compact", OpsMonitor.Widget.Models.WidgetDensity.Compact),
        ("Comfortable", OpsMonitor.Widget.Models.WidgetDensity.Normal),
        ("Airy", OpsMonitor.Widget.Models.WidgetDensity.Detail)
    };

    foreach (var (studioLayout, widgetLayout) in layouts)
    {
        foreach (var (studioDensity, widgetDensity) in densities)
        {
            foreach (var moduleCount in new[] { 1, 5, 7 })
            {
                foreach (var scale in new[] { 80, 100, 160 })
                {
                    var studio = StudioViewModel.CalculateWidgetSize(
                        studioLayout,
                        studioDensity,
                        moduleCount,
                        scale);
                    var widget = LiveWidgetSizingPolicy.Calculate(
                        widgetLayout,
                        widgetDensity,
                        moduleCount,
                        scale);
                    Assert.Equal(
                        widget.SuggestedWidth,
                        studio.SuggestedWidth,
                        $"{studioLayout}/{studioDensity} width parity");
                    Assert.Equal(
                        widget.SuggestedHeight,
                        studio.SuggestedHeight,
                        $"{studioLayout}/{studioDensity} height parity");
                }
            }
        }
    }

    return Task.CompletedTask;
}

static async Task TestBuiltInThemePersistenceAsync()
{
    var defaults = OpsSettingsDocument.CreateDefault();
    var merged = WidgetSettingsStore.MergeWidgetSettings(
        defaults,
        new WidgetSettings
        {
            Theme = "Contrast",
            CoreThemeId = null
        });
    var contrast = merged.Themes.Single(theme =>
        theme.Name.Equals("Contrast", StringComparison.OrdinalIgnoreCase));
    var widget = merged.Widgets.Single(item => item.Enabled);
    Assert.Equal(contrast.Id, widget.ThemeId, "built-in theme was not selected in Core");

    var reloaded = WidgetSettingsStore.MergeCoreSettings(
        new WidgetSettings { Theme = "Void" },
        merged);
    Assert.Equal("Contrast", reloaded.Theme, "built-in theme did not survive reload");

    var directory = Directory.CreateTempSubdirectory("OpsMonitorThemeRoundTrip-");
    try
    {
        var runtimePath = Path.Combine(directory.FullName, "settings.json");
        var editorPath = Path.Combine(directory.FullName, "studio-settings.json");
        var runtimeWithLegacyFps = merged with
        {
            Widgets = merged.Widgets.Select(item =>
                item.Id.Equals(widget.Id, StringComparison.Ordinal)
                    ? item with
                    {
                        Modules =
                        [
                            .. item.Modules,
                            ModuleSettings.Create(
                                "module-fps",
                                "Frame rate",
                                item.Modules.Count,
                                new MetricId("gaming.fps"))
                        ]
                    }
                    : item).ToList()
        };
        using (var repository = new JsonSettingsRepository(runtimePath))
        {
            await repository.SaveAsync(runtimeWithLegacyFps);
        }

        StudioSettingsSnapshot studioSnapshot;
        using (var sink = new StudioCoreSettingsSink(
                   new LocalStudioSettingsSink(editorPath),
                   new JsonSettingsRepository(runtimePath)))
        {
            studioSnapshot = sink.Reload() ??
                             throw new InvalidOperationException(
                                 "Studio did not load shared runtime settings");
        }

        Assert.Equal(
            "contrast",
            studioSnapshot.Theme,
            "Widget Contrast selection was not reflected in Studio");
        Assert.False(
            studioSnapshot.Modules?.Any(module => module.Id == "fps") == true,
            "legacy FPS module was exposed as a working Studio control");

        var roundTripped = StudioCoreSettingsSink.MapRuntime(
            studioSnapshot,
            runtimeWithLegacyFps);
        var mappedWidget = roundTripped.Widgets.Single(item =>
            item.Id.Equals(widget.Id, StringComparison.Ordinal));
        var mappedTheme = roundTripped.Themes.Single(item =>
            item.Id.Equals(mappedWidget.ThemeId, StringComparison.Ordinal));
        Assert.Equal("Contrast", mappedTheme.Name, "Studio reverted the Contrast theme");
        Assert.Equal(
            "#FF020305",
            mappedTheme.Palette.Background,
            "Studio changed the Contrast surface palette");
        Assert.True(
            mappedWidget.Modules.Any(module => module.Id == "module-fps"),
            "Studio deleted an unsupported runtime module while hiding its control");

        var widgetReload = WidgetSettingsStore.MergeCoreSettings(
            new WidgetSettings { Theme = "Void" },
            roundTripped);
        Assert.Equal(
            "Contrast",
            widgetReload.Theme,
            "Studio round-trip reverted the live Widget theme");
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

static StudioSettingsSnapshot CreateStudioSnapshot() =>
    new(
        Scene: "Daily driver",
        Layout: "Pill",
        Theme: "void",
        BackgroundOpacity: 0.82,
        ContentOpacity: 1,
        BlurStrength: 24,
        Density: "Compact",
        FontScale: 1,
        AlwaysOnTop: true,
        PositionLocked: false,
        ClickThrough: false,
        StartAtSignIn: true,
        SnapToGrid: false,
        VisibleModules: ["cpu", "gpu", "ram", "net", "latency"]);

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

static async Task TestInvalidCpuTemperatureAsync()
{
    var directory = Directory.CreateTempSubdirectory("OpsMonitorCpuTempValidation-");
    try
    {
        var readingPath = Path.Combine(directory.FullName, "cpu-temperature.txt");
        var now = DateTimeOffset.Parse(
            "2026-07-25T20:00:00Z",
            CultureInfo.InvariantCulture);
        await using var provider = new CpuTemperatureBridgeProvider(
            new CpuTemperatureBridgeOptions
            {
                ReadingPath = readingPath,
                MaximumFutureSkew = TimeSpan.FromSeconds(5)
            });
        var context = new MetricProviderContext
        {
            TimestampUtc = now,
            LatestSamples = new Dictionary<MetricId, MetricSample>()
        };

        await File.WriteAllTextAsync(
            readingPath,
            $"NaN|{now.UtcDateTime.Ticks}");
        var nonFinite = await provider.PollAsync(context, CancellationToken.None);
        Assert.Equal(
            MetricAvailability.Error,
            nonFinite.Samples.Single().Availability,
            "NaN temperature availability");
        Assert.False(
            nonFinite.Samples.Single().HasUsableValue,
            "NaN temperature must never be usable");

        await File.WriteAllTextAsync(
            readingPath,
            $"61.4|{now.AddMinutes(1).UtcDateTime.Ticks}");
        var future = await provider.PollAsync(context, CancellationToken.None);
        Assert.Equal(
            MetricAvailability.Error,
            future.Samples.Single().Availability,
            "future timestamp availability");
        Assert.False(
            future.Samples.Single().HasUsableValue,
            "future temperature must never be usable");
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

static Task TestCpuBridgeDefaultPathAsync()
{
    using var identity = WindowsIdentity.GetCurrent();
    var userSid = identity.User?.Value ??
        throw new InvalidOperationException("Current Windows SID is unavailable.");
    var expectedSuffix = Path.Combine(
        "OPS Monitor Sensor",
        "Data",
        userSid,
        "cpu-temperature.txt");
    var actual = CpuTemperatureBridgeProvider.GetDefaultReadingPath();
    Assert.True(
        actual.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase),
        $"protected CPU bridge path mismatch: {actual}");
    return Task.CompletedTask;
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
            WidgetModuleCatalog.Latency,
            WidgetModuleCatalog.Battery,
            WidgetModuleCatalog.Storage
        ],
        configuration.Order,
        "supported module order");
    Assert.SequenceEqual(
        [
            WidgetModuleCatalog.Memory,
            WidgetModuleCatalog.Cpu,
            WidgetModuleCatalog.Latency,
            WidgetModuleCatalog.Battery,
            WidgetModuleCatalog.Storage
        ],
        configuration.Enabled,
        "supported module visibility");
    Assert.Equal(
        1,
        configuration.Order.Count(key =>
            key.Equals(WidgetModuleCatalog.Latency, StringComparison.Ordinal)),
        "latency module was not preserved independently");
    Assert.False(
        configuration.Enabled.Contains(
            WidgetModuleCatalog.Network,
            StringComparer.Ordinal),
        "disabled network module was incorrectly enabled by latency");
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

static Task TestWidgetModuleSaveAsync()
{
    var modules = new[]
    {
        ModuleSettings.Create(
            "module-network",
            "NET",
            0,
            WellKnownMetrics.NetworkDownloadRate),
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
    var presentation = new Dictionary<string, WidgetModulePresentation>(
        StringComparer.Ordinal)
    {
        [WidgetModuleCatalog.Latency] = new()
        {
            Size = WidgetModuleSize.Medium,
            Visualization = WidgetModuleVisualization.Value,
            ShowLabel = false,
            ShowSecondaryValue = false,
            ShowTrend = false,
            DecimalPlacesOverride = 2
        }
    };

    var configured = WidgetModuleCatalog.ApplyConfiguration(
        modules,
        [WidgetModuleCatalog.Latency, WidgetModuleCatalog.Network],
        [WidgetModuleCatalog.Latency],
        presentation);
    var network = configured.Single(module => module.Id == "module-network");
    var latency = configured.Single(module => module.Id == "module-latency");
    var unsupported = configured.Single(module => module.Id == "module-fps");

    Assert.False(network.Enabled, "network visibility did not save independently");
    Assert.True(latency.Enabled, "latency visibility did not save independently");
    Assert.Equal(0, latency.Order, "latency order did not save");
    Assert.Equal(1, network.Order, "network order did not save");
    Assert.Equal(ModuleSize.Medium, latency.Size, "module size did not save");
    Assert.Equal(
        ModuleVisualization.Value,
        latency.Visualization,
        "module visualization did not save");
    Assert.False(latency.ShowLabel, "module label visibility did not save");
    Assert.False(
        latency.ShowSecondaryValue,
        "module secondary visibility did not save");
    Assert.False(latency.ShowTrend, "module trend visibility did not save");
    Assert.Equal(2, latency.DecimalPlacesOverride!.Value, "module precision did not save");
    Assert.True(unsupported.Enabled, "unsupported module visibility changed");
    Assert.True(unsupported.Order >= 7, "unsupported module order collided with widget modules");
    return Task.CompletedTask;
}

static Task TestWidgetSizingAsync()
{
    var pillFive = LiveWidgetSizingPolicy.Calculate(
        WidgetLayout.Pill,
        OpsMonitor.Widget.Models.WidgetDensity.Compact,
        5,
        100);
    Assert.Equal(184d, pillFive.SuggestedWidth, "classic Pill width");
    Assert.Equal(368d, pillFive.SuggestedHeight, "footer-free Pill height");

    var pillThree = LiveWidgetSizingPolicy.Calculate(
        WidgetLayout.Pill,
        OpsMonitor.Widget.Models.WidgetDensity.Compact,
        3,
        100);
    var pillSeven = LiveWidgetSizingPolicy.Calculate(
        WidgetLayout.Pill,
        OpsMonitor.Widget.Models.WidgetDensity.Compact,
        7,
        100);
    Assert.Equal(248d, pillThree.SuggestedHeight, "three-module Pill height");
    Assert.Equal(488d, pillSeven.SuggestedHeight, "seven-module Pill height");

    var pillEighty = LiveWidgetSizingPolicy.Calculate(
        WidgetLayout.Pill,
        OpsMonitor.Widget.Models.WidgetDensity.Compact,
        5,
        80);
    Assert.Equal(176d, pillEighty.SuggestedWidth, "80% readable width clamp");
    Assert.Equal(294d, pillEighty.SuggestedHeight, "80% Pill footprint");

    var pillNinety = LiveWidgetSizingPolicy.Calculate(
        WidgetLayout.Pill,
        OpsMonitor.Widget.Models.WidgetDensity.Compact,
        5,
        90);
    Assert.Equal(176d, pillNinety.SuggestedWidth, "90% readable width clamp");
    Assert.Equal(331d, pillNinety.SuggestedHeight, "90% Pill footprint");

    var miniFive = LiveWidgetSizingPolicy.Calculate(
        WidgetLayout.Mini,
        OpsMonitor.Widget.Models.WidgetDensity.Compact,
        5,
        100);
    Assert.Equal(176d, miniFive.SuggestedWidth, "Mini width");
    Assert.Equal(188d, miniFive.SuggestedHeight, "single-line Mini height");

    var miniEighty = LiveWidgetSizingPolicy.Calculate(
        WidgetLayout.Mini,
        OpsMonitor.Widget.Models.WidgetDensity.Compact,
        5,
        80);
    Assert.Equal(176d, miniEighty.SuggestedHeight, "80% Mini readable height floor");

    var dockFive = LiveWidgetSizingPolicy.Calculate(
        WidgetLayout.Dock,
        OpsMonitor.Widget.Models.WidgetDensity.Compact,
        5,
        100);
    var dockSix = LiveWidgetSizingPolicy.Calculate(
        WidgetLayout.Dock,
        OpsMonitor.Widget.Models.WidgetDensity.Compact,
        6,
        100);
    Assert.Equal(836d, dockFive.SuggestedWidth, "five-module Dock width");
    Assert.Equal(84d, dockFive.SuggestedHeight, "compact Dock height");
    Assert.Equal(968d, dockSix.SuggestedWidth, "Dock did not expand for the sixth module");
    var dockDetail = LiveWidgetSizingPolicy.Calculate(
        WidgetLayout.Dock,
        OpsMonitor.Widget.Models.WidgetDensity.Detail,
        5,
        100);
    Assert.Equal(
        dockFive,
        dockDetail,
        "Dock density changed its fixed slim one-row footprint");
    var dockEightAtMaximumScale = LiveWidgetSizingPolicy.Calculate(
        WidgetLayout.Dock,
        OpsMonitor.Widget.Models.WidgetDensity.Normal,
        8,
        160);
    Assert.Equal(
        OpsMonitor.Core.Settings.WidgetSizingPolicy.MaximumWindowWidth,
        dockEightAtMaximumScale.SuggestedWidth,
        "eight-module Dock exceeded the supported window width");
    Assert.Equal(
        134d,
        dockEightAtMaximumScale.SuggestedHeight,
        "maximum-scale Dock lost its slim height");

    var railFive = LiveWidgetSizingPolicy.Calculate(
        WidgetLayout.Rail,
        OpsMonitor.Widget.Models.WidgetDensity.Compact,
        5,
        100);
    Assert.Equal(184d, railFive.SuggestedWidth, "compact Rail width");
    Assert.Equal(258d, railFive.SuggestedHeight, "footer-free compact Rail height");
    return Task.CompletedTask;
}

static Task TestWidgetFormattingAsync()
{
    var previousCulture = CultureInfo.CurrentCulture;
    var previousUiCulture = CultureInfo.CurrentUICulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sl-SI");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("sl-SI");

        Assert.Equal("43.2%", TelemetryTextFormatter.Percentage(43.24, 1), "percentage");
        Assert.Equal("56.3°C", TelemetryTextFormatter.Temperature(56.25, 1), "temperature");
        Assert.Equal(
            "15.3 / 30.9 GB",
            TelemetryTextFormatter.Memory(15.3, 30.9),
            "memory");
        Assert.Equal(
            "↓5.7M/s  ↑27K/s",
            TelemetryTextFormatter.NetworkThroughput(5_700_000, 27_000),
            "network throughput");
        Assert.Equal(
            "↓1.25M/s  ↑2.25K/s",
            TelemetryTextFormatter.NetworkThroughput(1_250_000, 2_250, 2),
            "network precision override");
        Assert.Equal("26 ms", TelemetryTextFormatter.Latency(26), "latency");
        Assert.Equal("0.4%", TelemetryTextFormatter.PacketLoss(0.4), "packet loss");
    }
    finally
    {
        CultureInfo.CurrentCulture = previousCulture;
        CultureInfo.CurrentUICulture = previousUiCulture;
    }

    return Task.CompletedTask;
}

static Task TestWidgetSettingsMappingAsync()
{
    var lowWidthHighHeight = WidgetSettingsStore.Normalize(new WidgetSettings
    {
        Width = 170,
        Height = 1_200
    });
    Assert.Equal(176d, lowWidthHighHeight.Width!.Value, "widget width floor");
    Assert.Equal(1_000d, lowWidthHighHeight.Height!.Value, "widget height ceiling");

    var highWidthLowHeight = WidgetSettingsStore.Normalize(new WidgetSettings
    {
        Width = 2_000,
        Height = 100
    });
    Assert.Equal(1_600d, highWidthLowHeight.Width!.Value, "widget width ceiling");
    Assert.Equal(140d, highWidthLowHeight.Height!.Value, "widget height floor");

    var defaults = OpsSettingsDocument.CreateDefault();
    var source = new WidgetSettings
    {
        Layout = WidgetLayout.Mini,
        Density = OpsMonitor.Widget.Models.WidgetDensity.Detail,
        InteractionMode = WidgetInteractionMode.Locked,
        Draggable = true,
        Resizable = true,
        ScalePercent = 133,
        SurfaceOpacity = 0.25,
        ContentOpacity = 0.9,
        UpdateCadenceSeconds = 2.5,
        Theme = defaults.Themes[0].Name,
        CoreThemeId = defaults.Themes[0].Id,
        EnabledModules =
        [
            WidgetModuleCatalog.Cpu,
            WidgetModuleCatalog.Network,
            WidgetModuleCatalog.Latency
        ]
    };

    var mapped = WidgetSettingsStore.MergeWidgetSettings(defaults, source);
    var mappedWidget = mapped.Widgets.Single(widget => widget.Enabled);
    Assert.Equal(WidgetDesign.Canvas, mappedWidget.Design, "Mini design mapping");
    Assert.Equal(133, mappedWidget.Window.ScalePercent, "scale save mapping");
    Assert.True(mappedWidget.Window.Locked, "locked mode did not save");
    Assert.False(mappedWidget.Window.ClickThrough, "click-through was incorrectly saved");
    Assert.True(mappedWidget.Window.Draggable, "draggable preference was discarded");
    Assert.True(mappedWidget.Window.Resizable, "resizable preference was discarded");
    Assert.Equal(
        TimeSpan.FromSeconds(2.5),
        mapped.PerformanceProfiles.Single(profile =>
            profile.Id == mappedWidget.PerformanceProfileId).UiRefreshCadence,
        "update cadence did not save");

    var loaded = WidgetSettingsStore.MergeCoreSettings(
        new WidgetSettings
        {
            Draggable = true,
            Resizable = true
        },
        mapped);
    Assert.Equal(WidgetLayout.Mini, loaded.Layout, "Mini design load mapping");
    Assert.Equal(133, loaded.ScalePercent, "scale load mapping");
    Assert.Equal(
        WidgetInteractionMode.Locked,
        loaded.InteractionMode,
        "locked mode load mapping");
    Assert.True(loaded.Draggable, "lock reload discarded draggable preference");
    Assert.True(loaded.Resizable, "lock reload discarded resizable preference");

    var conflictingWindow = mappedWidget.Window with
    {
        Locked = true,
        ClickThrough = true,
        Draggable = false,
        Resizable = false
    };
    var conflictingDocument = mapped with
    {
        Widgets =
        [
            mappedWidget with
            {
                Window = conflictingWindow
            }
        ]
    };
    var clickThrough = WidgetSettingsStore.MergeCoreSettings(
        new WidgetSettings
        {
            Draggable = true,
            Resizable = true
        },
        conflictingDocument);
    Assert.Equal(
        WidgetInteractionMode.ClickThrough,
        clickThrough.InteractionMode,
        "click-through must win conflicting saved modes");
    Assert.False(
        clickThrough.Draggable,
        "Core draggable preference was ignored while click-through");
    Assert.False(
        clickThrough.Resizable,
        "Core resizable preference was ignored while click-through");
    return Task.CompletedTask;
}

static Task TestWidgetViewModelAsync()
{
    var source = new FakeTelemetrySource();
    var updatePulses = 0;
    using var viewModel = new MainWindowViewModel(
        source,
        new WidgetSettings
        {
            InteractionMode = WidgetInteractionMode.Locked,
            Draggable = true,
            Resizable = true,
            SurfaceOpacity = 0.01,
            ContentOpacity = 0.1,
            ScalePercent = 50,
            UpdateCadenceSeconds = 0.1,
            RuntimeThemes =
            [
                new WidgetRuntimeTheme
                {
                    Id = "carbon-glass",
                    Name = "Carbon Glass",
                    Background = "#080B12",
                    Card = "#0F1521",
                    Border = "#364258",
                    PrimaryText = "#F6F9FF",
                    SecondaryText = "#B8C4D6",
                    CpuAccent = "#48DCF9",
                    GpuAccent = "#FF4FD8",
                    NetworkAccent = "#48DCF9",
                    Warning = "#FFC35A",
                    Critical = "#FF566E",
                    Success = "#58E6B2"
                }
            ],
            ModulePresentation = new Dictionary<string, WidgetModulePresentation>(
                StringComparer.Ordinal)
            {
                [WidgetModuleCatalog.Network] = new()
                {
                    Visualization = WidgetModuleVisualization.ValueAndSparkline,
                    ShowTrend = false
                }
            }
        });
    viewModel.TelemetryUpdated += (_, _) => updatePulses++;

    Assert.Equal(0.08d, viewModel.SurfaceOpacity, "surface opacity floor");
    Assert.Equal(0.82d, viewModel.ContentOpacity, "content opacity floor");
    var surfaceBrush =
        (System.Windows.Media.SolidColorBrush)viewModel.SurfaceBrush;
    var cardBrush =
        (System.Windows.Media.SolidColorBrush)viewModel.CardBrush;
    var readabilityPlateBrush =
        (System.Windows.Media.SolidColorBrush)viewModel.ReadabilityPlateBrush;
    var flyoutBrush =
        (System.Windows.Media.SolidColorBrush)viewModel.FlyoutBrush;
    Assert.True(
        surfaceBrush.Color.A <= 24,
        "minimum shell opacity was not honored");
    Assert.True(
        cardBrush.Color.A >= 190,
        "metric cards lost the minimum readability guard");
    Assert.True(
        readabilityPlateBrush.Color.A >= 190,
        "header/footer readability plate disappeared");
    Assert.True(
        flyoutBrush.Color.A >= 230,
        "quick settings became unreadable with a transparent shell");
    Assert.Equal(80, viewModel.ScalePercent, "scale floor");
    viewModel.ScalePercent = 125;
    Assert.Equal(1.25d, viewModel.ContentScaleFactor, "content scale was not applied above 100%");
    viewModel.ScalePercent = 90;
    Assert.Equal(1d, viewModel.ContentScaleFactor, "content shrank below the readable floor");
    Assert.True(
        viewModel.MinimumReadableFontSize >= 11,
        "minimum readable typography floor");
    Assert.True(
        viewModel.ThemeOptions.Contains("Void") &&
        viewModel.ThemeOptions.Contains("Contrast") &&
        viewModel.ThemeOptions.Contains("Carbon Glass"),
        "runtime themes replaced rather than augmented built-in themes");
    Assert.Equal(
        TimeSpan.FromSeconds(0.5),
        source.LastCadence,
        "live cadence was not applied");
    Assert.True(viewModel.IsLockedMode, "locked selected-state flag");
    Assert.False(viewModel.CanDrag, "locked widget remained draggable");
    Assert.False(viewModel.CanResize, "locked widget remained resizable");

    viewModel.InteractionMode = WidgetInteractionMode.Edit;
    Assert.True(viewModel.CanDrag, "unlock did not restore draggable preference");
    Assert.True(viewModel.CanResize, "unlock did not restore resizable preference");
    viewModel.Density = OpsMonitor.Widget.Models.WidgetDensity.Detail;
    viewModel.Layout = WidgetLayout.Dock;
    Assert.Equal(
        OpsMonitor.Widget.Models.WidgetDensity.Compact,
        viewModel.Density,
        "Dock did not normalize to compact density");
    Assert.False(
        viewModel.CanChangeDensity,
        "Dock exposed a density control that cannot change its rendering");
    viewModel.Density = OpsMonitor.Widget.Models.WidgetDensity.Normal;
    Assert.Equal(
        OpsMonitor.Widget.Models.WidgetDensity.Compact,
        viewModel.Density,
        "Dock accepted a non-compact density");

    source.Publish(new TelemetrySnapshot(
        DateTimeOffset.Now,
        new CpuTelemetry(43.4, 56.2, 4.25, 88, SensorState.Available),
        new GpuTelemetry(6.2, 41.4, 2.4, 3.2, 16, SensorState.Available),
        new MemoryTelemetry(15.3, 30.9, 17.1, 6.4, SensorState.Available),
        new NetworkTelemetry(
            5_700_000,
            27_000,
            26,
            3.4,
            0.4,
            SensorState.Available),
        new StorageTelemetry(
            0,
            0,
            0,
            null,
            "Unavailable",
            SensorState.Unavailable),
        new BatteryTelemetry(
            null,
            "Not present",
            null,
            null,
            SensorState.Unavailable)));

    var cpu = viewModel.Metrics.Single(metric => metric.Key == WidgetModuleCatalog.Cpu);
    var memory = viewModel.Metrics.Single(metric => metric.Key == WidgetModuleCatalog.Memory);
    var network = viewModel.Metrics.Single(metric => metric.Key == WidgetModuleCatalog.Network);
    var latency = viewModel.Metrics.Single(metric => metric.Key == WidgetModuleCatalog.Latency);
    Assert.Equal("43%", cpu.PrimaryValue, "CPU value formatting");
    Assert.Equal("TEMP 56°C", cpu.Status, "CPU temperature was not shown");
    Assert.Equal("15.3 / 30.9 GB", memory.PrimaryValue, "RAM value formatting");
    Assert.Equal(
        "↓5.7M/s  ↑27K/s",
        network.PrimaryValue,
        "network throughput formatting");
    Assert.Equal("26 ms", latency.PrimaryValue, "latency value formatting");
    Assert.Equal("LOSS 0.4%", latency.Status, "packet-loss value formatting");
    Assert.False(network.ShowSparkline, "disabled module trend remained visible");
    network.ApplyPresentation(new WidgetModulePresentation
    {
        Visualization = WidgetModuleVisualization.Sparkline,
        ShowTrend = false
    });
    Assert.True(
        network.ShowSparkline,
        "pure sparkline mode lost its only primary visualization");
    Assert.Equal(5, viewModel.VisibleModuleCount, "default visible module count");
    Assert.Equal("Updated now", viewModel.LastUpdatedText, "snapshot update state");
    Assert.Equal(1, updatePulses, "each applied snapshot raises one visual update pulse");
    return Task.CompletedTask;
}

static Task TestUnavailableConnectivityAsync()
{
    var source = new FakeTelemetrySource();
    using var viewModel = new MainWindowViewModel(source, new WidgetSettings());
    source.Publish(new TelemetrySnapshot(
        DateTimeOffset.Now,
        new CpuTelemetry(10, null, 0, 0, SensorState.Available),
        new GpuTelemetry(10, null, 0, 0, 0, SensorState.Available),
        new MemoryTelemetry(4, 16, 0, 0, SensorState.Available),
        new NetworkTelemetry(
            1_000_000,
            250_000,
            null,
            null,
            null,
            SensorState.Available,
            SensorState.Unavailable),
        new StorageTelemetry(
            0,
            0,
            0,
            null,
            "Unavailable",
            SensorState.Unavailable),
        new BatteryTelemetry(
            null,
            "Not present",
            null,
            null,
            SensorState.Unavailable)));

    var throughput = viewModel.Metrics.Single(metric =>
        metric.Key == WidgetModuleCatalog.Network);
    var connectivity = viewModel.Metrics.Single(metric =>
        metric.Key == WidgetModuleCatalog.Latency);
    Assert.Equal(
        "↓1.0M/s  ↑250K/s",
        throughput.PrimaryValue,
        "available throughput disappeared with unavailable connectivity");
    Assert.Equal(
        TelemetryTextFormatter.Unavailable,
        connectivity.PrimaryValue,
        "missing ping rendered as a numeric value");
    Assert.Equal(
        "LOSS —",
        connectivity.Status,
        "missing packet loss rendered as zero");
    Assert.Equal(
        SensorState.Unavailable,
        connectivity.State,
        "connectivity availability was not independent from throughput");
    Assert.True(
        connectivity.Details.All(detail => !detail.IsAvailable),
        "missing connectivity details were presented as available");
    return Task.CompletedTask;
}

static Task TestDiagnosticLogRotationAsync()
{
    var directory = Directory.CreateTempSubdirectory("OpsMonitorLogs-");
    try
    {
        var path = Path.Combine(directory.FullName, "release.log");
        var log = new RollingFileLog(path, 4 * 1024, 2);
        for (var index = 0; index < 200; index++)
        {
            log.Write("INFO", $"{index:D3} {new string('x', 96)}");
        }

        var files = Directory.GetFiles(directory.FullName, "release*.log");
        Assert.Equal(3, files.Length, "rolling log retention count");
        Assert.True(
            files.All(file => new FileInfo(file).Length is > 0 and <= 4 * 1024),
            "a diagnostic log exceeded its configured bound");
    }
    finally
    {
        directory.Delete(recursive: true);
    }

    return Task.CompletedTask;
}

static async Task TestConcurrentSettingsUpdatesAsync()
{
    var directory = Directory.CreateTempSubdirectory("OpsMonitorConcurrentSettings-");
    try
    {
        var path = Path.Combine(directory.FullName, "settings.json");
        using var first = new JsonSettingsRepository(path);
        using var second = new JsonSettingsRepository(path);
        var defaults = OpsSettingsDocument.CreateDefault();
        var initialPort = defaults.General.LocalApiPort;
        await first.SaveAsync(defaults);

        var updates = Enumerable.Range(0, 24)
            .Select(index =>
            {
                var repository = index % 2 == 0 ? first : second;
                return repository.UpdateAsync(current => current with
                {
                    General = current.General with
                    {
                        LocalApiPort = current.General.LocalApiPort + 1
                    }
                });
            });
        await Task.WhenAll(updates);

        var loaded = await first.LoadAsync();
        Assert.Equal(
            initialPort + 24,
            loaded.General.LocalApiPort,
            "cross-process update lost a writer");
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

static Task TestSingleInstanceAsync()
{
    var mutexName = $@"Local\OpsMonitor.Tests.{Guid.NewGuid():N}";
    Assert.True(
        SingleInstanceCoordinator.TryAcquire(mutexName, out var first),
        "first single-instance lease was not acquired");
    try
    {
        bool? secondAcquired = null;
        Exception? threadFailure = null;
        var contender = new Thread(() =>
        {
            try
            {
                secondAcquired = SingleInstanceCoordinator.TryAcquire(
                    mutexName,
                    out var second);
                second?.Dispose();
            }
            catch (Exception exception)
            {
                threadFailure = exception;
            }
        });
        contender.Start();
        Assert.True(contender.Join(TimeSpan.FromSeconds(5)), "mutex contender hung");
        if (threadFailure is not null)
        {
            throw new InvalidOperationException(
                "mutex contender failed unexpectedly",
                threadFailure);
        }

        Assert.False(
            secondAcquired ?? true,
            "a second instance acquired an existing process lease");
    }
    finally
    {
        first?.Dispose();
    }

    Assert.True(
        SingleInstanceCoordinator.TryAcquire(mutexName, out var replacement),
        "single-instance lease was not released");
    replacement?.Dispose();
    return Task.CompletedTask;
}

static Task TestStudioApplicationResourcesAsync()
{
    Exception? threadFailure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var application = new OpsMonitor.Studio.App();
            application.InitializeComponent();
            Assert.True(
                application.Resources.Contains("BooleanToVisibilityConverter"),
                "Studio visibility converter resource was not registered");
            application.Shutdown();
        }
        catch (Exception exception)
        {
            threadFailure = exception;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(
        thread.Join(TimeSpan.FromSeconds(10)),
        "Studio resource initialization hung");
    if (threadFailure is not null)
    {
        throw new InvalidOperationException(
            "Studio resources failed to initialize",
            threadFailure);
    }

    return Task.CompletedTask;
}

static Task TestEphemeralLaunchAsync()
{
    Assert.True(
        OpsMonitor.Widget.MainWindow.IsEphemeralLaunch(["--demo"]),
        "demo launch was not ephemeral");
    Assert.True(
        OpsMonitor.Widget.MainWindow.IsEphemeralLaunch(["--RESET-UI"]),
        "reset launch was not ephemeral");
    Assert.True(
        OpsMonitor.Widget.MainWindow.IsEphemeralLaunch(
            ["--layout=Pill", "--demo"]),
        "combined demo launch was not ephemeral");
    Assert.False(
        OpsMonitor.Widget.MainWindow.IsEphemeralLaunch(["--layout=Pill"]),
        "normal launch was incorrectly ephemeral");
    var launchSettings = OpsMonitor.Widget.MainWindow.LoadLaunchSettings(
        [
            "--reset-ui",
            "--layout=Pill",
            "--scale=80",
            "--show-storage",
            "--show-battery"
        ]);
    Assert.Equal(WidgetLayout.Pill, launchSettings.Layout, "launch layout override");
    Assert.Equal(80, launchSettings.ScalePercent, "launch scale override");
    Assert.True(
        launchSettings.EnabledModules.Contains(
            WidgetModuleCatalog.Storage,
            StringComparer.Ordinal),
        "storage QA override was ignored");
    Assert.True(
        launchSettings.EnabledModules.Contains(
            WidgetModuleCatalog.Battery,
            StringComparer.Ordinal),
        "battery QA override was ignored");
    return Task.CompletedTask;
}

static Task TestRuntimeReloadPolicyAsync()
{
    Assert.False(
        OpsMonitor.Widget.MainWindow.RuntimeCadenceChanged(1, 1),
        "unchanged cadence requested a provider reload");
    Assert.False(
        OpsMonitor.Widget.MainWindow.RuntimeCadenceChanged(1, 1.0005),
        "insignificant cadence noise requested a provider reload");
    Assert.True(
        OpsMonitor.Widget.MainWindow.RuntimeCadenceChanged(1, 2),
        "meaningful cadence change did not request a provider reload");
    return Task.CompletedTask;
}

static async Task TestCpuSensorWatchdogAsync()
{
    var directory = Directory.CreateTempSubdirectory("OpsMonitorCpuWatchdog-");
    try
    {
        string path = Path.Combine(directory.FullName, "cpu-temperature.txt");
        var now = DateTimeOffset.Parse(
            "2026-07-27T17:30:00Z",
            CultureInfo.InvariantCulture);
        var maximumAge = TimeSpan.FromSeconds(12);

        Assert.False(
            CpuSensorBridgeLauncher.IsReadingFresh(path, now, maximumAge),
            "a missing bridge reading was treated as fresh");

        await File.WriteAllTextAsync(
            path,
            FormattableString.Invariant(
                $"61.5|{now.AddSeconds(-5).UtcDateTime.Ticks}"));
        Assert.True(
            CpuSensorBridgeLauncher.IsReadingFresh(path, now, maximumAge),
            "a fresh valid bridge reading was rejected");

        await File.WriteAllTextAsync(
            path,
            FormattableString.Invariant(
                $"61.5|{now.AddSeconds(-13).UtcDateTime.Ticks}"));
        Assert.False(
            CpuSensorBridgeLauncher.IsReadingFresh(path, now, maximumAge),
            "a stale bridge reading suppressed recovery");

        await File.WriteAllTextAsync(
            path,
            FormattableString.Invariant(
                $"61.5|{now.AddSeconds(6).UtcDateTime.Ticks}"));
        Assert.False(
            CpuSensorBridgeLauncher.IsReadingFresh(path, now, maximumAge),
            "an invalid future bridge reading suppressed recovery");

        await File.WriteAllTextAsync(
            path,
            FormattableString.Invariant(
                $"0|{now.UtcDateTime.Ticks}"));
        Assert.False(
            CpuSensorBridgeLauncher.IsReadingFresh(path, now, maximumAge),
            "an implausible bridge reading suppressed recovery");
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

static Task TestCpuSensorRecoveryPolicyAsync()
{
    Assert.Equal(
        TimeSpan.FromSeconds(5),
        CpuSensorRecoveryPolicy.GetRetryDelay(1),
        "the first recovery retry was not fast");
    Assert.Equal(
        TimeSpan.FromSeconds(10),
        CpuSensorRecoveryPolicy.GetRetryDelay(2),
        "the second recovery retry did not back off");
    Assert.Equal(
        TimeSpan.FromSeconds(20),
        CpuSensorRecoveryPolicy.GetRetryDelay(3),
        "the third recovery retry did not back off");
    Assert.Equal(
        CpuSensorRecoveryPolicy.MaximumRetryDelay,
        CpuSensorRecoveryPolicy.GetRetryDelay(20),
        "recovery retry delay was not capped");

    var failedStartPolicy = new CpuSensorRecoveryPolicy();
    Assert.Equal(
        CpuSensorRecoveryAction.StartTask,
        failedStartPolicy.GetAction(TimeSpan.Zero, bridgeRunning: false),
        "a missing bridge was not eligible for its initial start");
    failedStartPolicy.RecordAttempt(TimeSpan.Zero);
    Assert.False(
        failedStartPolicy.IsDue(TimeSpan.FromSeconds(4.999)),
        "the first retry ignored its backoff");
    Assert.True(
        failedStartPolicy.IsDue(TimeSpan.FromSeconds(5)),
        "the first retry was delayed beyond its backoff");
    failedStartPolicy.RecordAttempt(TimeSpan.FromSeconds(5));
    Assert.False(
        failedStartPolicy.IsDue(TimeSpan.FromSeconds(14.999)),
        "the second retry ignored exponential backoff");
    Assert.True(
        failedStartPolicy.IsDue(TimeSpan.FromSeconds(15)),
        "the second retry was delayed beyond exponential backoff");

    var absentCapabilityPolicy = new CpuSensorRecoveryPolicy();
    absentCapabilityPolicy.RecordCapabilityUnavailable(TimeSpan.FromSeconds(7));
    Assert.False(
        absentCapabilityPolicy.IsDue(
            TimeSpan.FromSeconds(7) +
            CpuSensorRecoveryPolicy.CapabilityRetryDelay -
            TimeSpan.FromMilliseconds(1)),
        "an absent optional sensor was queried continuously");
    Assert.True(
        absentCapabilityPolicy.IsDue(
            TimeSpan.FromSeconds(7) +
            CpuSensorRecoveryPolicy.CapabilityRetryDelay),
        "an optional sensor was never reconsidered after being enabled");

    var runningPolicy = new CpuSensorRecoveryPolicy();
    Assert.Equal(
        CpuSensorRecoveryAction.None,
        runningPolicy.GetAction(TimeSpan.FromSeconds(11), bridgeRunning: true),
        "a running bridge was restarted before receiving a grace period");
    Assert.False(
        runningPolicy.IsDue(
            TimeSpan.FromSeconds(11) +
            CpuSensorRecoveryPolicy.RunningBridgeGracePeriod -
            TimeSpan.FromMilliseconds(1)),
        "a running bridge was rechecked before its grace period ended");
    TimeSpan graceEnd =
        TimeSpan.FromSeconds(11) +
        CpuSensorRecoveryPolicy.RunningBridgeGracePeriod;
    Assert.Equal(
        CpuSensorRecoveryAction.RestartTask,
        runningPolicy.GetAction(graceEnd, bridgeRunning: true),
        "a persistently stale running bridge was never restarted");

    runningPolicy.RecordAttempt(graceEnd);
    Assert.True(
        runningPolicy.ConsecutiveAttempts == 1,
        "a restart attempt was not tracked");
    runningPolicy.RecordHealthy();
    Assert.True(
        runningPolicy.IsDue(graceEnd),
        "a healthy sample did not clear the retry gate");
    Assert.True(
        runningPolicy.ConsecutiveAttempts == 0,
        "a healthy sample did not reset recovery backoff");

    return Task.CompletedTask;
}

static Task TestPartialCpuTelemetryAsync()
{
    var capturedAt = DateTimeOffset.UtcNow;
    var metrics = new Dictionary<MetricId, MetricSample>
    {
        [WellKnownMetrics.CpuTotalUtilization] = ErrorMetric(
            WellKnownMetrics.CpuTotalUtilization,
            capturedAt,
            0),
        [WellKnownMetrics.CpuTemperature] = MetricSample.Available(
            WellKnownMetrics.CpuTemperature,
            63,
            capturedAt,
            TestSource())
    };

    var snapshot = CoreTelemetrySource.CreateSnapshot(metrics, capturedAt);
    Assert.True(
        snapshot.Cpu.LoadPercent is null,
        "an error CPU load payload survived projection");
    using var viewModel = RenderTelemetry(snapshot);
    var cpu = viewModel.Metrics.Single(metric =>
        metric.Key == WidgetModuleCatalog.Cpu);
    Assert.Equal(
        TelemetryTextFormatter.Unavailable,
        cpu.PrimaryValue,
        "missing CPU load rendered as zero");
    Assert.Equal("TEMP 63°C", cpu.Status, "available CPU temperature was lost");
    Assert.False(cpu.IsProgressAvailable, "missing CPU load exposed a zero progress bar");
    Assert.Equal(0, cpu.HistorySampleCount, "missing CPU load entered chart history");
    return Task.CompletedTask;
}

static Task TestPartialGpuTelemetryAsync()
{
    const double gibibyte = 1024d * 1024d * 1024d;
    var capturedAt = DateTimeOffset.UtcNow;
    var metrics = new Dictionary<MetricId, MetricSample>
    {
        [WellKnownMetrics.GpuUtilization] = MetricSample.Available(
            WellKnownMetrics.GpuUtilization,
            17,
            capturedAt,
            TestSource()),
        [WellKnownMetrics.GpuMemoryUsedBytes] = ErrorMetric(
            WellKnownMetrics.GpuMemoryUsedBytes,
            capturedAt,
            0),
        [WellKnownMetrics.GpuMemoryTotalBytes] = MetricSample.Available(
            WellKnownMetrics.GpuMemoryTotalBytes,
            16 * gibibyte,
            capturedAt,
            TestSource())
    };

    var snapshot = CoreTelemetrySource.CreateSnapshot(metrics, capturedAt);
    Assert.True(
        snapshot.Gpu.UsedVramGigabytes is null,
        "an error GPU VRAM payload survived projection");
    using var viewModel = RenderTelemetry(snapshot);
    var gpu = viewModel.Metrics.Single(metric =>
        metric.Key == WidgetModuleCatalog.Gpu);
    Assert.Equal("17%", gpu.PrimaryValue, "available GPU load was lost");
    Assert.Equal(
        "N/A / 16.0 GB",
        gpu.Details[1].Value,
        "partial GPU VRAM did not identify the missing value");
    Assert.False(
        gpu.Details[1].IsAvailable,
        "partial GPU VRAM was presented as complete");
    return Task.CompletedTask;
}

static Task TestPartialMemoryTelemetryAsync()
{
    const double gibibyte = 1024d * 1024d * 1024d;
    var capturedAt = DateTimeOffset.UtcNow;
    var metrics = new Dictionary<MetricId, MetricSample>
    {
        [WellKnownMetrics.MemoryUsedBytes] = ErrorMetric(
            WellKnownMetrics.MemoryUsedBytes,
            capturedAt,
            0),
        [WellKnownMetrics.MemoryTotalBytes] = MetricSample.Available(
            WellKnownMetrics.MemoryTotalBytes,
            16 * gibibyte,
            capturedAt,
            TestSource())
    };

    var snapshot = CoreTelemetrySource.CreateSnapshot(metrics, capturedAt);
    Assert.True(
        snapshot.Memory.UsedGigabytes is null,
        "an error RAM-used payload survived projection");
    using var viewModel = RenderTelemetry(snapshot);
    var memory = viewModel.Metrics.Single(metric =>
        metric.Key == WidgetModuleCatalog.Memory);
    Assert.Equal(
        "N/A / 16.0 GB",
        memory.PrimaryValue,
        "missing used RAM rendered as zero");
    Assert.Equal("MEMORY N/A", memory.Status, "partial RAM showed a fake percentage");
    Assert.False(
        memory.IsProgressAvailable,
        "partial RAM exposed a zero progress bar");
    Assert.Equal(0, memory.HistorySampleCount, "partial RAM entered chart history");
    Assert.False(
        memory.Details[2].IsAvailable,
        "headroom was fabricated without used RAM");
    return Task.CompletedTask;
}

static Task TestPartialDownloadTelemetryAsync()
{
    var capturedAt = DateTimeOffset.UtcNow;
    var metrics = new Dictionary<MetricId, MetricSample>
    {
        [WellKnownMetrics.NetworkDownloadRate] = ErrorMetric(
            WellKnownMetrics.NetworkDownloadRate,
            capturedAt,
            0),
        [WellKnownMetrics.NetworkUploadRate] = MetricSample.Available(
            WellKnownMetrics.NetworkUploadRate,
            27_000,
            capturedAt,
            TestSource()),
        [WellKnownMetrics.BatteryCharge] = MetricSample.Available(
            WellKnownMetrics.BatteryCharge,
            73,
            capturedAt,
            TestSource()),
        [WellKnownMetrics.BatteryAcOnline] = ErrorMetric(
            WellKnownMetrics.BatteryAcOnline,
            capturedAt,
            0)
    };

    var snapshot = CoreTelemetrySource.CreateSnapshot(metrics, capturedAt);
    Assert.True(
        snapshot.Network.DownloadBytesPerSecond is null,
        "an error download payload survived projection");
    Assert.True(
        snapshot.Battery.PowerState is null,
        "an error AC-state payload became 'On battery'");
    using var viewModel = RenderTelemetry(snapshot);
    var network = viewModel.Metrics.Single(metric =>
        metric.Key == WidgetModuleCatalog.Network);
    Assert.Equal(
        "↓N/A  ↑27K/s",
        network.PrimaryValue,
        "missing download did not remain independent from upload");
    Assert.False(network.Details[0].IsAvailable, "missing download was marked available");
    Assert.True(network.Details[1].IsAvailable, "available upload was hidden");
    Assert.Equal(
        TelemetryTextFormatter.Unavailable,
        network.Details[0].Value,
        "missing download rendered as zero");
    Assert.Equal(0, network.HistorySampleCount, "missing download entered chart history");
    var battery = viewModel.Metrics.Single(metric =>
        metric.Key == WidgetModuleCatalog.Battery);
    Assert.Equal("POWER N/A", battery.Status, "missing AC state rendered as battery power");
    Assert.False(battery.Details[0].IsAvailable, "missing AC state was marked available");
    return Task.CompletedTask;
}

static Task TestPartialUploadTelemetryAsync()
{
    var capturedAt = DateTimeOffset.UtcNow;
    var metrics = new Dictionary<MetricId, MetricSample>
    {
        [WellKnownMetrics.NetworkDownloadRate] = MetricSample.Available(
            WellKnownMetrics.NetworkDownloadRate,
            1_000_000,
            capturedAt,
            TestSource()),
        [WellKnownMetrics.NetworkUploadRate] = ErrorMetric(
            WellKnownMetrics.NetworkUploadRate,
            capturedAt,
            0)
    };

    var snapshot = CoreTelemetrySource.CreateSnapshot(metrics, capturedAt);
    Assert.True(
        snapshot.Network.UploadBytesPerSecond is null,
        "an error upload payload survived projection");
    using var viewModel = RenderTelemetry(snapshot);
    var network = viewModel.Metrics.Single(metric =>
        metric.Key == WidgetModuleCatalog.Network);
    Assert.Equal(
        "↓1.0M/s  ↑N/A",
        network.PrimaryValue,
        "missing upload did not remain independent from download");
    Assert.True(network.Details[0].IsAvailable, "available download was hidden");
    Assert.False(network.Details[1].IsAvailable, "missing upload was marked available");
    Assert.Equal(
        TelemetryTextFormatter.Unavailable,
        network.Details[1].Value,
        "missing upload rendered as zero");
    Assert.Equal(1, network.HistorySampleCount, "available download was not charted");
    return Task.CompletedTask;
}

static MainWindowViewModel RenderTelemetry(TelemetrySnapshot snapshot)
{
    var source = new FakeTelemetrySource();
    var viewModel = new MainWindowViewModel(source, new WidgetSettings());
    source.Publish(snapshot);
    return viewModel;
}

static MetricSample ErrorMetric(
    MetricId id,
    DateTimeOffset timestamp,
    double misleadingValue) =>
    new()
    {
        MetricId = id,
        Value = misleadingValue,
        TimestampUtc = timestamp,
        Availability = MetricAvailability.Error,
        UnavailableReason = MetricUnavailableReason.ProviderFaulted,
        Source = TestSource(),
        Message = "Synthetic provider error"
    };

static async Task TestFutureSettingsSchemaAsync()
{
    var directory = Directory.CreateTempSubdirectory("OpsMonitorFutureSettings-");
    try
    {
        var path = Path.Combine(directory.FullName, "settings.json");
        var originalBytes = System.Text.Encoding.UTF8.GetBytes(
            "{\r\n" +
            $"  \"schemaVersion\": {OpsSettingsDocument.CurrentSchemaVersion + 1},\r\n" +
            "  \"futureOnly\": { \"opaque\": true },\r\n" +
            "  \"sentinel\": \"preserve these exact bytes\"\r\n" +
            "}\r\n");
        await File.WriteAllBytesAsync(path, originalBytes);

        using var repository = new JsonSettingsRepository(path);
        var readOnlyFallback = await repository.LoadAsync();
        Assert.True(
            readOnlyFallback.Widgets.Count > 0,
            "future schema did not expose safe read-only defaults");
        Assert.True(
            repository.LastLoadWarning?.Contains(
                "read-only",
                StringComparison.OrdinalIgnoreCase) == true,
            "future schema warning did not explain read-only protection");

        var updateWasInvoked = false;
        UnsupportedSettingsSchemaException? updateFailure = null;
        try
        {
            await repository.UpdateAsync(current =>
            {
                updateWasInvoked = true;
                return current with
                {
                    General = current.General with
                    {
                        LocalApiPort = current.General.LocalApiPort + 1
                    }
                };
            });
        }
        catch (UnsupportedSettingsSchemaException exception)
        {
            updateFailure = exception;
        }

        Assert.True(updateFailure is not null, "future schema update was not refused");
        Assert.False(
            updateWasInvoked,
            "update callback ran against defaults from a future schema");
        Assert.SequenceEqual(
            originalBytes,
            await File.ReadAllBytesAsync(path),
            "future schema update changed the file bytes");

        UnsupportedSettingsSchemaException? saveFailure = null;
        try
        {
            await repository.SaveAsync(OpsSettingsDocument.CreateDefault());
        }
        catch (UnsupportedSettingsSchemaException exception)
        {
            saveFailure = exception;
        }

        Assert.True(saveFailure is not null, "future schema save was not refused");
        Assert.SequenceEqual(
            originalBytes,
            await File.ReadAllBytesAsync(path),
            "future schema save changed the file bytes");
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

static async Task TestNullSettingsMembersAsync()
{
    var directory = Directory.CreateTempSubdirectory("OpsMonitorNullSettings-");
    try
    {
        var path = Path.Combine(directory.FullName, "settings.json");
        using var repository = new JsonSettingsRepository(path);
        await repository.SaveAsync(OpsSettingsDocument.CreateDefault());
        var baseline = System.Text.Json.Nodes.JsonNode
            .Parse(await File.ReadAllTextAsync(path))?
            .AsObject() ??
            throw new InvalidOperationException("baseline settings JSON was empty");

        var cases = new (
            string Name,
            Action<System.Text.Json.Nodes.JsonObject> Mutate)[]
        {
            ("general", root => root["general"] = null),
            ("widgets", root => root["widgets"] = null),
            ("themes", root => root["themes"] = null),
            ("scenes", root => root["scenes"] = null),
            ("performanceProfiles", root => root["performanceProfiles"] = null),
            ("hotkeys", root => root["hotkeys"] = null),
            ("dataRetention", root => root["dataRetention"] = null),
            ("alertRules", root => root["alertRules"] = null),
            ("widget.window", root => First(root, "widgets")["window"] = null),
            ("widget.modules", root => First(root, "widgets")["modules"] = null),
            (
                "module.additionalMetrics",
                root => First(First(root, "widgets"), "modules")[
                    "additionalMetrics"] = null),
            ("theme.palette", root => First(root, "themes")["palette"] = null),
            ("theme.surface", root => First(root, "themes")["surface"] = null),
            (
                "theme.typography",
                root => First(root, "themes")["typography"] = null),
            ("theme.motion", root => First(root, "themes")["motion"] = null),
            ("scene.widgetIds", root => First(root, "scenes")["widgetIds"] = null),
            ("scene.activation", root => First(root, "scenes")["activation"] = null),
            (
                "profile.providerCadences",
                root => First(root, "performanceProfiles")[
                    "providerCadences"] = null),
            (
                "profile.disabledProviderIds",
                root => First(root, "performanceProfiles")[
                    "disabledProviderIds"] = null)
        };

        foreach (var (name, mutate) in cases)
        {
            var mutated = baseline.DeepClone().AsObject();
            mutate(mutated);
            await File.WriteAllTextAsync(path, mutated.ToJsonString());

            var loaded = await repository.LoadAsync();
            Assert.True(
                loaded.Widgets.Count > 0,
                $"{name} null did not fall back to defaults");
            Assert.True(
                repository.LastLoadWarning?.Contains(
                    "null",
                    StringComparison.OrdinalIgnoreCase) == true,
                $"{name} null did not produce a safe load warning");
        }
    }
    finally
    {
        directory.Delete(recursive: true);
    }

    static System.Text.Json.Nodes.JsonObject First(
        System.Text.Json.Nodes.JsonObject root,
        string propertyName) =>
        root[propertyName]?.AsArray()[0]?.AsObject() ??
        throw new InvalidOperationException(
            $"settings JSON collection '{propertyName}' was empty");
}

static MetricSource TestSource() =>
    new()
    {
        Id = "test-source",
        DisplayName = "Test source",
        ProviderId = "test-provider",
        Kind = MetricSourceKind.Custom
    };

internal sealed class FakeTelemetrySource : ITelemetrySource
{
    public string Name => "Test telemetry";

    public bool IsDemo => false;

    public TimeSpan LastCadence { get; private set; }

    public event EventHandler<TelemetrySnapshot>? SnapshotAvailable;

    public void SetUpdateCadence(TimeSpan cadence) => LastCadence = cadence;

    public void Start()
    {
    }

    public void Publish(TelemetrySnapshot snapshot) =>
        SnapshotAvailable?.Invoke(this, snapshot);

    public void Dispose()
    {
    }
}

internal sealed class FakeStudioSettingsSink : IStudioSettingsSink
{
    public string SettingsPath => string.Empty;

    public string RuntimeSettingsPath => string.Empty;

    public string? LastWarning => null;

    public event EventHandler<StudioSettingsSnapshot>? SettingsChanged;

    public StudioSettingsSnapshot? Reload() => null;

    public void Save(StudioSettingsSnapshot snapshot) =>
        SettingsChanged?.Invoke(this, snapshot);

    public void Dispose()
    {
    }
}

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
