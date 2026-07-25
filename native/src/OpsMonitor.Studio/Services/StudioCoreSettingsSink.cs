using System.Globalization;
using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpsMonitor.Core.Alerts;
using OpsMonitor.Core.Metrics;
using OpsMonitor.Core.Platform;
using OpsMonitor.Core.Settings;
using OpsMonitor.Studio.Models;

namespace OpsMonitor.Studio.Services;

/// <summary>
/// Keeps the lossless Studio editor document and the runtime's normalized Core
/// document in sync. Core remains authoritative for values the live widget uses;
/// Studio JSON retains design-time details Core does not model yet.
/// </summary>
public sealed partial class StudioCoreSettingsSink : IStudioSettingsSink
{
    private const string ManagedWidgetId = "studio-widget-primary";
    private const string ManagedProfileId = "studio-profile-active";
    private const string ManagedThemePrefix = "studio-theme-";
    private const string ManagedScenePrefix = "studio-scene-";
    private const string ManagedAlertPrefix = "studio-alert-";
    private readonly LocalStudioSettingsSink _editorStore;
    private readonly JsonSettingsRepository _runtimeRepository;
    private readonly WindowsStartupRegistration _startupRegistration;
    private bool _disposed;

    public StudioCoreSettingsSink(
        LocalStudioSettingsSink? editorStore = null,
        JsonSettingsRepository? runtimeRepository = null)
    {
        _editorStore = editorStore ?? new LocalStudioSettingsSink();
        _runtimeRepository = runtimeRepository ?? new JsonSettingsRepository();
        _startupRegistration = new WindowsStartupRegistration("OPS Monitor Widget");
    }

    public string SettingsPath => _editorStore.SettingsPath;
    public string RuntimeSettingsPath => _runtimeRepository.SettingsPath;
    public string? LastWarning { get; private set; }
    public event EventHandler<StudioSettingsSnapshot>? SettingsChanged;

    public StudioSettingsSnapshot? Reload()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var editorSnapshot = _editorStore.Reload();
        var warnings = new List<string>();

        if (!string.IsNullOrWhiteSpace(_editorStore.LastWarning))
        {
            warnings.Add(_editorStore.LastWarning);
        }

        if (!File.Exists(RuntimeSettingsPath))
        {
            LastWarning = JoinWarnings(warnings);
            return editorSnapshot;
        }

        try
        {
            var runtime = _runtimeRepository.LoadAsync().GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(_runtimeRepository.LastLoadWarning))
            {
                warnings.Add(_runtimeRepository.LastLoadWarning);
            }

            var baseline = editorSnapshot ?? CreateStudioDefault();
            LastWarning = JoinWarnings(warnings);
            return OverlayRuntime(baseline, runtime);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or JsonException)
        {
            warnings.Add($"Shared runtime settings could not be loaded: {exception.Message}");
            LastWarning = JoinWarnings(warnings);
            return editorSnapshot;
        }
    }

    public void Save(StudioSettingsSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);

        var warnings = new List<string>();
        _editorStore.Save(snapshot);

        try
        {
            var current = _runtimeRepository.LoadAsync().GetAwaiter().GetResult();
            var mapped = MapRuntime(snapshot, current);
            _runtimeRepository.SaveAsync(mapped).GetAwaiter().GetResult();

            if (!string.IsNullOrWhiteSpace(_runtimeRepository.LastLoadWarning))
            {
                warnings.Add(_runtimeRepository.LastLoadWarning);
            }

            SynchronizeStartup(snapshot.StartAtSignIn, warnings);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or JsonException
                or ArgumentException)
        {
            warnings.Add($"Shared runtime settings were not updated: {exception.Message}");
        }

        LastWarning = JoinWarnings(warnings);
        SettingsChanged?.Invoke(this, snapshot);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtimeRepository.Dispose();
        _editorStore.Dispose();
        GC.SuppressFinalize(this);
    }

    private static OpsSettingsDocument MapRuntime(
        StudioSettingsSnapshot snapshot,
        OpsSettingsDocument current)
    {
        var existingWidget = current.Widgets.FirstOrDefault(item =>
                                 item.Id.Equals(ManagedWidgetId, StringComparison.Ordinal))
                             ?? current.Widgets.FirstOrDefault()
                             ?? CreateWidgetShell();
        var widgetId = existingWidget.Id;
        var profileId = string.IsNullOrWhiteSpace(existingWidget.PerformanceProfileId)
            ? ManagedProfileId
            : existingWidget.PerformanceProfileId;
        var themeId = ManagedThemePrefix + Slug(snapshot.Theme);
        var modules = MapModules(snapshot);
        var widget = existingWidget with
        {
            Enabled = true,
            Design = ParseDesign(snapshot.Layout),
            Density = ParseDensity(snapshot.Density),
            ThemeId = themeId,
            PerformanceProfileId = profileId,
            Window = existingWidget.Window with
            {
                Width = Math.Clamp(snapshot.WidgetWidth, 112, 1_600),
                Height = Math.Clamp(snapshot.WidgetHeight, 140, 1_200),
                ScalePercent = Math.Clamp(snapshot.WidgetScalePercent, 80, 160),
                AlwaysOnTop = snapshot.AlwaysOnTop,
                Locked = snapshot.PositionLocked,
                Draggable = snapshot.Draggable && !snapshot.PositionLocked,
                Resizable = snapshot.Resizable && !snapshot.PositionLocked,
                ClickThrough = snapshot.ClickThrough,
                SurfaceOpacity = Math.Clamp(snapshot.BackgroundOpacity, 0.28, 1),
                ContentOpacity = Math.Clamp(snapshot.ContentOpacity, 0.72, 1),
            },
            Modules = modules,
        };

        var widgets = ReplaceById(current.Widgets, widget);
        var theme = MapTheme(snapshot, themeId);
        var themes = ReplaceById(current.Themes, theme);
        var profile = MapProfile(snapshot, current, profileId);
        var profiles = ReplaceById(current.PerformanceProfiles, profile);
        var scenes = MapScenes(snapshot, current.Scenes, widgetId, profileId);
        var alerts = MapAlerts(snapshot, current.AlertRules);

        return current with
        {
            General = current.General with
            {
                LaunchAtSignIn = snapshot.StartAtSignIn,
            },
            Widgets = widgets,
            Themes = themes,
            PerformanceProfiles = profiles,
            Scenes = scenes,
            AlertRules = alerts,
        };
    }

    private static WidgetInstanceSettings CreateWidgetShell()
        => new()
        {
            Id = ManagedWidgetId,
            Name = "OPS Monitor",
            Design = WidgetDesign.Pill,
            Density = WidgetDensity.Compact,
            ThemeId = ManagedThemePrefix + "abyss",
            PerformanceProfileId = ManagedProfileId,
        };

    private static List<T> ReplaceById<T>(IEnumerable<T> source, T replacement)
        where T : class
    {
        static string IdOf(T item) => item switch
        {
            WidgetInstanceSettings widget => widget.Id,
            ThemeSettings theme => theme.Id,
            PerformanceProfileSettings profile => profile.Id,
            _ => throw new InvalidOperationException($"Unsupported settings item {typeof(T).Name}."),
        };

        var replacementId = IdOf(replacement);
        var result = source.Where(item =>
                !IdOf(item).Equals(replacementId, StringComparison.Ordinal))
            .ToList();
        result.Add(replacement);
        return result;
    }

    private static List<ModuleSettings> MapModules(StudioSettingsSnapshot snapshot)
    {
        var source = snapshot.Modules;
        if (source is null || source.Count == 0)
        {
            source = snapshot.VisibleModules
                .Select((id, order) => new StudioModuleSnapshot(
                    id,
                    DisplayName(id),
                    order,
                    true,
                    "Medium",
                    "Bar + sparkline",
                    true,
                    true,
                    true,
                    "Whole numbers",
                    string.Empty,
                    AccentFor(id)))
                .ToArray();
        }

        return source
            .OrderBy(item => item.Order)
            .Select((item, order) =>
            {
                var metrics = MetricsFor(item.Id);
                return new ModuleSettings
                {
                    Id = CoreModuleId(item.Id),
                    Title = item.Name,
                    Enabled = item.Enabled,
                    Order = order,
                    Size = ParseModuleSize(item.Size),
                    Visualization = ParseVisualization(item.Visualization),
                    PrimaryMetric = metrics.Primary,
                    SecondaryMetric = metrics.Secondary,
                    Icon = item.Icon,
                    AccentColor = item.Accent,
                    ShowLabel = item.ShowLabel,
                    ShowSecondaryValue = metrics.Secondary.HasValue &&
                                         (item.ShowTemperature ||
                                          item.Id is not ("cpu" or "gpu")),
                    ShowTrend = item.ShowSparkline,
                    DecimalPlacesOverride = ParsePrecision(item.Precision),
                };
            })
            .ToList();
    }

    private static ThemeSettings MapTheme(StudioSettingsSnapshot snapshot, string themeId)
    {
        var details = snapshot.ThemeDetails ?? new StudioThemeSnapshot(
            snapshot.Theme,
            snapshot.Theme,
            "#FF05080D",
            "#FF0D131C",
            "#FF2A3849",
            "#FF43E7D2");
        var modules = snapshot.Modules ?? [];
        var cpuAccent = modules.FirstOrDefault(item => item.Id == "cpu")?.Accent ?? details.Accent;
        var gpuAccent = modules.FirstOrDefault(item => item.Id == "gpu")?.Accent ?? "#FFF05AD6";
        var networkAccent = modules.FirstOrDefault(item => item.Id == "net")?.Accent ?? details.Accent;

        return new ThemeSettings
        {
            Id = themeId,
            Name = details.Name,
            BuiltIn = false,
            Palette = new ThemePalette
            {
                Background = details.Surface,
                Card = details.Card,
                Border = details.Border,
                PrimaryText = "#FFF4F7FC",
                SecondaryText = "#FFB3BECE",
                CpuAccent = cpuAccent,
                GpuAccent = gpuAccent,
                NetworkAccent = networkAccent,
                Warning = "#FFFFC95C",
                Critical = "#FFFF6B81",
                Success = "#FF63E6A6",
            },
            Surface = new ThemeSurface
            {
                CornerRadius = ParseDesign(snapshot.Layout) == WidgetDesign.Dock ? 30 : 24,
                CardCornerRadius = 11,
                BlurEnabled = snapshot.BlurStrength > 0,
                BlurStrength = Math.Clamp(snapshot.BlurStrength / 40, 0, 1),
                ShadowEnabled = true,
                ShadowOpacity = 0.28,
                CardGap = snapshot.Density == "Compact" ? 4 : 6,
                ContentPadding = snapshot.Density == "Compact" ? 8 : 10,
            },
            Typography = new ThemeTypography
            {
                FontFamily = "Segoe UI Variable",
                LabelSize = Math.Max(12, 12 * snapshot.FontScale),
                ValueSize = Math.Max(16, 18 * snapshot.FontScale),
                MinimumReadableSize = 12,
                LabelWeight = 600,
                ValueWeight = 600,
                UseTabularNumbers = true,
            },
            Motion = new ThemeMotion
            {
                Enabled = !snapshot.ReducedMotion,
                TransitionMilliseconds = snapshot.ReducedMotion ? 0 : 145,
                AnimateValueChanges = !snapshot.ReducedMotion,
                RespectReducedMotion = true,
            },
        };
    }

    private static PerformanceProfileSettings MapProfile(
        StudioSettingsSnapshot snapshot,
        OpsSettingsDocument current,
        string profileId)
    {
        var cadence = TimeSpan.FromSeconds(Math.Clamp(snapshot.UpdateCadenceSeconds, 0.25, 10));
        var existing = current.PerformanceProfiles.FirstOrDefault(item =>
                           item.Id.Equals(profileId, StringComparison.Ordinal))
                       ?? PerformanceProfileSettings.CreateBalanced(profileId);
        var networkCadence = TimeSpan.FromSeconds(Math.Clamp(cadence.TotalSeconds * 1.5, 0.5, 15));
        var temperatureCadence = TimeSpan.FromSeconds(Math.Clamp(cadence.TotalSeconds * 2, 1, 20));

        return existing with
        {
            Name = "Studio " + snapshot.PerformanceMode,
            Mode = ParsePowerMode(snapshot.PerformanceMode),
            UiRefreshCadence = cadence,
            ProviderCadences = new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
            {
                ["windows.native"] = cadence,
                ["network.connectivity"] = networkCadence,
                ["cpu.temperature.bridge"] = temperatureCadence,
                ["nvidia"] = cadence,
            },
        };
    }

    private static List<SceneSettings> MapScenes(
        StudioSettingsSnapshot snapshot,
        IReadOnlyCollection<SceneSettings> existing,
        string widgetId,
        string profileId)
    {
        if (snapshot.Scenes is null || snapshot.Scenes.Count == 0)
        {
            return existing.ToList();
        }

        var names = snapshot.Scenes.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var preserved = existing
            .Where(item => !item.Id.StartsWith(ManagedScenePrefix, StringComparison.Ordinal) &&
                           !names.Contains(item.Name))
            .Select(item => item with { IsDefault = false })
            .ToList();
        var mapped = snapshot.Scenes.Select(item => new SceneSettings
        {
            Id = ManagedScenePrefix + Slug(item.Id),
            Name = item.Name,
            Enabled = true,
            IsDefault = item.IsActive,
            WidgetIds = [widgetId],
            PerformanceProfileId = profileId,
        }).ToList();

        if (mapped.Count > 0 && mapped.All(item => !item.IsDefault))
        {
            mapped[0] = mapped[0] with { IsDefault = true };
        }

        preserved.AddRange(mapped);
        return preserved;
    }

    private static List<AlertRule> MapAlerts(
        StudioSettingsSnapshot snapshot,
        IReadOnlyCollection<AlertRule> existing)
    {
        if (snapshot.Alerts is null)
        {
            return existing.Select(item => item with
            {
                Enabled = snapshot.AlertsEnabled && item.Enabled,
            }).ToList();
        }

        var mapped = snapshot.Alerts.Select(item => new AlertRule
        {
            Id = ManagedAlertPrefix + Slug(item.Id),
            Name = item.Name,
            MetricId = AlertMetric(item.Metric),
            Comparison = ParseComparison(item.Condition),
            Threshold = ParseFirstNumber(item.Threshold, 80),
            PendingDuration = ParseDuration(item.Duration),
            RecoveryHysteresis = item.Metric.Contains("temperature", StringComparison.OrdinalIgnoreCase) ? 5 : 2,
            Cooldown = TimeSpan.FromMinutes(5),
            Severity = ParseSeverity(item.Severity),
            Enabled = snapshot.AlertsEnabled && item.Enabled,
        }).ToList();

        var mappedMetrics = mapped.Select(item => item.MetricId).ToHashSet();
        var preserved = existing
            .Where(item => !item.Id.StartsWith(ManagedAlertPrefix, StringComparison.Ordinal) &&
                           !string.IsNullOrWhiteSpace(item.MetricId.Value) &&
                           !mappedMetrics.Contains(item.MetricId))
            .ToList();
        preserved.AddRange(mapped);
        return preserved;
    }

    private void SynchronizeStartup(bool enabled, List<string> warnings)
    {
        try
        {
            if (!enabled)
            {
                _ = _startupRegistration.Remove();
                return;
            }

            var executablePath = WidgetExecutableLocator.Find();
            if (executablePath is null)
            {
                warnings.Add(
                    "Launch-at-sign-in is saved, but registration is pending because OpsMonitor.Widget.exe was not found.");
                return;
            }

            _ = _startupRegistration.Register(executablePath);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or SecurityException
                or ArgumentException)
        {
            warnings.Add($"Windows startup registration was not changed: {exception.Message}");
        }
    }

    private static StudioSettingsSnapshot OverlayRuntime(
        StudioSettingsSnapshot baseline,
        OpsSettingsDocument runtime)
    {
        var widget = runtime.Widgets.FirstOrDefault(item =>
                         item.Id.Equals(ManagedWidgetId, StringComparison.Ordinal))
                     ?? runtime.Widgets.FirstOrDefault();
        if (widget is null)
        {
            return baseline with { StartAtSignIn = runtime.General.LaunchAtSignIn };
        }

        var theme = runtime.Themes.FirstOrDefault(item =>
            item.Id.Equals(widget.ThemeId, StringComparison.Ordinal));
        var profile = runtime.PerformanceProfiles.FirstOrDefault(item =>
            item.Id.Equals(widget.PerformanceProfileId, StringComparison.Ordinal));
        var mappedThemeId = widget.ThemeId.StartsWith(ManagedThemePrefix, StringComparison.Ordinal)
            ? widget.ThemeId[ManagedThemePrefix.Length..]
            : baseline.Theme;
        var modules = OverlayModules(baseline.Modules, widget.Modules);
        var enabledIds = modules.Where(item => item.Enabled)
            .OrderBy(item => item.Order)
            .Select(item => item.Id)
            .ToArray();
        var activeScene = runtime.Scenes.FirstOrDefault(item => item.IsDefault);

        return baseline with
        {
            Scene = activeScene?.Name ?? baseline.Scene,
            Layout = widget.Design.ToString(),
            Theme = mappedThemeId,
            Density = widget.Density switch
            {
                WidgetDensity.Compact => "Compact",
                WidgetDensity.Comfortable => "Airy",
                _ => "Comfortable",
            },
            BackgroundOpacity = widget.Window.SurfaceOpacity,
            ContentOpacity = widget.Window.ContentOpacity,
            FontScale = theme is null
                ? baseline.FontScale
                : Math.Clamp(theme.Typography.LabelSize / 12, 0.9, 1.35),
            AlwaysOnTop = widget.Window.AlwaysOnTop,
            PositionLocked = widget.Window.Locked,
            ClickThrough = widget.Window.ClickThrough,
            StartAtSignIn = runtime.General.LaunchAtSignIn,
            VisibleModules = enabledIds,
            Draggable = widget.Window.Draggable,
            Resizable = widget.Window.Resizable,
            WidgetWidth = widget.Window.Width,
            WidgetHeight = widget.Window.Height,
            WidgetScalePercent = widget.Window.ScalePercent,
            UpdateCadenceSeconds = profile?.UiRefreshCadence.TotalSeconds ??
                                   baseline.UpdateCadenceSeconds,
            PerformanceMode = profile?.Mode.ToString() ?? baseline.PerformanceMode,
            AlertsEnabled = runtime.AlertRules.Any(item => item.Enabled),
            ReducedMotion = theme is not null && !theme.Motion.Enabled,
            Modules = modules,
            ThemeDetails = theme is null
                ? baseline.ThemeDetails
                : new StudioThemeSnapshot(
                    mappedThemeId,
                    theme.Name,
                    theme.Palette.Background,
                    theme.Palette.Card,
                    theme.Palette.Border,
                    theme.Palette.CpuAccent),
            Scenes = OverlayScenes(baseline.Scenes, runtime.Scenes),
            Alerts = OverlayAlerts(baseline.Alerts, runtime.AlertRules),
        };
    }

    private static IReadOnlyList<StudioModuleSnapshot> OverlayModules(
        IReadOnlyList<StudioModuleSnapshot>? baseline,
        List<ModuleSettings> runtime)
    {
        if (runtime.Count == 0)
        {
            return baseline ?? [];
        }

        var baselineById = (baseline ?? []).ToDictionary(item => item.Id, StringComparer.Ordinal);
        return runtime.OrderBy(item => item.Order).Select((item, order) =>
        {
            var id = StudioModuleId(item.Id);
            baselineById.TryGetValue(id, out var prior);
            return new StudioModuleSnapshot(
                id,
                item.Title,
                order,
                item.Enabled,
                item.Size.ToString(),
                StudioVisualization(item.Visualization),
                item.ShowLabel,
                item.ShowTrend,
                item.ShowSecondaryValue,
                StudioPrecision(item.DecimalPlacesOverride),
                string.IsNullOrWhiteSpace(item.Icon) ? prior?.Icon ?? string.Empty : item.Icon,
                string.IsNullOrWhiteSpace(item.AccentColor)
                    ? prior?.Accent ?? AccentFor(id)
                    : item.AccentColor);
        }).ToArray();
    }

    private static StudioSceneSnapshot[] OverlayScenes(
        IReadOnlyList<StudioSceneSnapshot>? baseline,
        List<SceneSettings> runtime)
    {
        if (baseline is not null && baseline.Count > 0)
        {
            var activeName = runtime.FirstOrDefault(item => item.IsDefault)?.Name;
            return baseline.Select(item => item with
            {
                IsActive = item.Name.Equals(activeName, StringComparison.OrdinalIgnoreCase),
            }).ToArray();
        }

        return runtime.Select(item => new StudioSceneSnapshot(
            item.Id.StartsWith(ManagedScenePrefix, StringComparison.Ordinal)
                ? item.Id[ManagedScenePrefix.Length..]
                : item.Id,
            item.Name,
            "Pill",
            item.IsDefault)).ToArray();
    }

    private static StudioAlertSnapshot[] OverlayAlerts(
        IReadOnlyList<StudioAlertSnapshot>? baseline,
        List<AlertRule> runtime)
    {
        if (baseline is not null && baseline.Count > 0)
        {
            var runtimeByName = runtime.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
            return baseline.Select(item =>
            {
                if (!runtimeByName.TryGetValue(item.Name, out var mapped))
                {
                    return item;
                }

                return item with { Enabled = mapped.Enabled };
            }).ToArray();
        }

        return runtime.Select(item => new StudioAlertSnapshot(
            item.Id.StartsWith(ManagedAlertPrefix, StringComparison.Ordinal)
                ? item.Id[ManagedAlertPrefix.Length..]
                : item.Id,
            item.Name,
            item.MetricId.ToString(),
            item.Comparison.ToString(),
            item.Threshold.ToString(CultureInfo.InvariantCulture),
            item.PendingDuration.ToString(),
            item.Severity.ToString(),
            item.Enabled)).ToArray();
    }

    private static StudioSettingsSnapshot CreateStudioDefault()
        => new(
            "Daily driver",
            "Pill",
            "abyss",
            0.82,
            1,
            24,
            "Comfortable",
            1,
            true,
            false,
            false,
            true,
            true,
            ["cpu", "gpu", "ram", "net", "latency"]);

    private static (MetricId Primary, MetricId? Secondary) MetricsFor(string id)
        => BaseModuleId(id) switch
        {
            "cpu" => (WellKnownMetrics.CpuTotalUtilization, WellKnownMetrics.CpuTemperature),
            "gpu" => (WellKnownMetrics.GpuUtilization, WellKnownMetrics.GpuTemperature),
            "ram" => (WellKnownMetrics.MemoryUsedBytes, WellKnownMetrics.MemoryUtilization),
            "net" => (WellKnownMetrics.NetworkDownloadRate, WellKnownMetrics.NetworkUploadRate),
            "latency" => (WellKnownMetrics.NetworkPing, WellKnownMetrics.NetworkPacketLoss),
            "disk" => (new MetricId("storage.disk.activity"), new MetricId("storage.disk.free")),
            "fps" => (new MetricId("gaming.fps"), new MetricId("gaming.frame_time")),
            "battery" => (WellKnownMetrics.BatteryCharge, WellKnownMetrics.BatteryRemaining),
            _ => (new MetricId($"custom.{Slug(id)}"), (MetricId?)null),
        };

    private static MetricId AlertMetric(string metric)
    {
        if (metric.Contains("cpu", StringComparison.OrdinalIgnoreCase) &&
            (metric.Contains("temperature", StringComparison.OrdinalIgnoreCase) ||
             metric.Contains("package", StringComparison.OrdinalIgnoreCase)))
        {
            return WellKnownMetrics.CpuTemperature;
        }

        if (metric.Contains("gpu", StringComparison.OrdinalIgnoreCase))
        {
            return metric.Contains("temperature", StringComparison.OrdinalIgnoreCase)
                ? WellKnownMetrics.GpuTemperature
                : WellKnownMetrics.GpuUtilization;
        }

        if (metric.Contains("packet", StringComparison.OrdinalIgnoreCase))
        {
            return WellKnownMetrics.NetworkPacketLoss;
        }

        if (metric.Contains("memory", StringComparison.OrdinalIgnoreCase))
        {
            return WellKnownMetrics.MemoryUtilization;
        }

        return new MetricId("custom." + Slug(metric));
    }

    private static string CoreModuleId(string studioId)
        => studioId switch
        {
            "cpu" => "module-cpu",
            "gpu" => "module-gpu",
            "ram" => "module-memory",
            "net" => "module-network",
            "latency" => "module-latency",
            "disk" => "module-storage",
            "fps" => "module-fps",
            "battery" => "module-battery",
            _ => "module-studio-" + Slug(studioId),
        };

    private static string StudioModuleId(string coreId)
        => coreId switch
        {
            "module-cpu" => "cpu",
            "module-gpu" => "gpu",
            "module-memory" => "ram",
            "module-network" => "net",
            "module-latency" => "latency",
            "module-storage" => "disk",
            "module-fps" => "fps",
            "module-battery" => "battery",
            _ when coreId.StartsWith("module-studio-", StringComparison.Ordinal) =>
                coreId["module-studio-".Length..],
            _ => coreId,
        };

    private static string BaseModuleId(string id)
    {
        var separator = id.IndexOf('-', StringComparison.Ordinal);
        return separator > 0 ? id[..separator] : id;
    }

    private static WidgetDesign ParseDesign(string value)
        => Enum.TryParse<WidgetDesign>(value, true, out var parsed)
            ? parsed
            : WidgetDesign.Pill;

    private static WidgetDensity ParseDensity(string value)
        => value switch
        {
            "Compact" => WidgetDensity.Compact,
            "Airy" => WidgetDensity.Comfortable,
            "Comfortable" => WidgetDensity.Normal,
            _ when Enum.TryParse<WidgetDensity>(value, true, out var parsed) => parsed,
            _ => WidgetDensity.Normal,
        };

    private static ModuleSize ParseModuleSize(string value)
        => Enum.TryParse<ModuleSize>(value, true, out var parsed)
            ? parsed
            : ModuleSize.Medium;

    private static ModuleVisualization ParseVisualization(string value)
        => value switch
        {
            "Number only" => ModuleVisualization.Value,
            "Bar" => ModuleVisualization.Progress,
            "Sparkline" => ModuleVisualization.Sparkline,
            "Dial" => ModuleVisualization.Gauge,
            _ => ModuleVisualization.ValueAndSparkline,
        };

    private static string StudioVisualization(ModuleVisualization value)
        => value switch
        {
            ModuleVisualization.Value => "Number only",
            ModuleVisualization.Progress => "Bar",
            ModuleVisualization.Sparkline => "Sparkline",
            ModuleVisualization.Gauge => "Dial",
            _ => "Bar + sparkline",
        };

    private static int? ParsePrecision(string value)
        => value switch
        {
            "Whole numbers" => 0,
            "1 decimal" => 1,
            "2 decimals" => 2,
            _ => null,
        };

    private static string StudioPrecision(int? value)
        => value switch
        {
            0 => "Whole numbers",
            1 => "1 decimal",
            2 => "2 decimals",
            _ => "Adaptive",
        };

    private static PowerAwarenessMode ParsePowerMode(string value)
        => Enum.TryParse<PowerAwarenessMode>(value, true, out var parsed)
            ? parsed
            : PowerAwarenessMode.Balanced;

    private static AlertComparison ParseComparison(string value)
        => value.Contains("below", StringComparison.OrdinalIgnoreCase)
            ? AlertComparison.LessThanOrEqual
            : AlertComparison.GreaterThanOrEqual;

    private static AlertSeverity ParseSeverity(string value)
        => value switch
        {
            "Critical" => AlertSeverity.Critical,
            "Warning" => AlertSeverity.Warning,
            _ => AlertSeverity.Information,
        };

    private static double ParseFirstNumber(string value, double fallback)
    {
        var match = NumberPattern().Match(value);
        if (!match.Success)
        {
            return fallback;
        }

        var normalized = match.Value.Replace(',', '.');
        return double.TryParse(
            normalized,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : fallback;
    }

    private static TimeSpan ParseDuration(string value)
    {
        var amount = ParseFirstNumber(value, 0);
        if (value.Contains("minute", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromMinutes(amount);
        }

        if (value.Contains("hour", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromHours(amount);
        }

        return TimeSpan.FromSeconds(amount);
    }

    private static string DisplayName(string id)
        => BaseModuleId(id) switch
        {
            "cpu" => "CPU",
            "gpu" => "GPU",
            "ram" => "Memory",
            "net" => "Network",
            "latency" => "Latency",
            "disk" => "Storage",
            "fps" => "Frame rate",
            "battery" => "Power",
            _ => id,
        };

    private static string AccentFor(string id)
        => BaseModuleId(id) switch
        {
            "gpu" => "#FFF05AD6",
            "latency" => "#FFFFC95C",
            "net" => "#FF62A7FF",
            _ => "#FF43E7D2",
        };

    private static string Slug(string value)
    {
        var slug = NonSlugCharacters().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "item" : slug;
    }

    private static string? JoinWarnings(IEnumerable<string> warnings)
    {
        var materialized = warnings.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        return materialized.Length == 0 ? null : string.Join(" ", materialized);
    }

    [GeneratedRegex(@"-?\d+(?:[\.,]\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlugCharacters();
}
