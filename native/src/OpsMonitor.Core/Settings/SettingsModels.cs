using OpsMonitor.Core.Alerts;
using OpsMonitor.Core.Metrics;

namespace OpsMonitor.Core.Settings;

public sealed record OpsSettingsDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public GeneralSettings General { get; init; } = new();
    public List<WidgetInstanceSettings> Widgets { get; init; } = [];
    public List<ThemeSettings> Themes { get; init; } = [];
    public List<SceneSettings> Scenes { get; init; } = [];
    public List<PerformanceProfileSettings> PerformanceProfiles { get; init; } = [];
    public List<HotkeySettings> Hotkeys { get; init; } = [];
    public DataRetentionSettings DataRetention { get; init; } = new();
    public List<AlertRule> AlertRules { get; init; } = [];

    public static OpsSettingsDocument CreateDefault()
    {
        var widgetId = "widget-system-rail";
        var themeId = "theme-carbon-glass";
        var profileId = "profile-balanced";
        var sceneId = "scene-default";

        return new OpsSettingsDocument
        {
            Widgets =
            [
                new WidgetInstanceSettings
                {
                    Id = widgetId,
                    Name = "Performance Pill",
                    Design = WidgetDesign.Pill,
                    ThemeId = themeId,
                    PerformanceProfileId = profileId,
                    Window = new WidgetWindowSettings
                    {
                        Width = 184,
                        Height = 396,
                        ScalePercent = 100
                    },
                    Modules =
                    [
                        ModuleSettings.Create(
                            "module-cpu",
                            "CPU",
                            0,
                            WellKnownMetrics.CpuTotalUtilization,
                            WellKnownMetrics.CpuTemperature),
                        ModuleSettings.Create(
                            "module-gpu",
                            "GPU",
                            1,
                            WellKnownMetrics.GpuUtilization,
                            WellKnownMetrics.GpuTemperature),
                        ModuleSettings.Create(
                            "module-memory",
                            "RAM",
                            2,
                            WellKnownMetrics.MemoryUsedBytes,
                            WellKnownMetrics.MemoryUtilization),
                        ModuleSettings.Create(
                            "module-network",
                            "NET",
                            3,
                            WellKnownMetrics.NetworkDownloadRate,
                            WellKnownMetrics.NetworkUploadRate),
                        ModuleSettings.Create(
                            "module-latency",
                            "PING",
                            4,
                            WellKnownMetrics.NetworkPing,
                            WellKnownMetrics.NetworkPacketLoss)
                    ]
                }
            ],
            Themes = [ThemeSettings.CreateCarbonGlass(themeId)],
            PerformanceProfiles =
            [
                PerformanceProfileSettings.CreateBalanced(profileId)
            ],
            Scenes =
            [
                new SceneSettings
                {
                    Id = sceneId,
                    Name = "Default",
                    IsDefault = true,
                    WidgetIds = [widgetId],
                    PerformanceProfileId = profileId
                }
            ],
            Hotkeys =
            [
                new HotkeySettings
                {
                    Id = "hotkey-toggle-widgets",
                    Action = HotkeyAction.ToggleAllWidgets,
                    Key = "O",
                    Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt
                }
            ],
            AlertRules =
            [
                new AlertRule
                {
                    Id = "alert-cpu-temperature",
                    Name = "CPU temperature",
                    MetricId = WellKnownMetrics.CpuTemperature,
                    Comparison = AlertComparison.GreaterThanOrEqual,
                    Threshold = 90,
                    PendingDuration = TimeSpan.FromSeconds(15),
                    RecoveryHysteresis = 5,
                    Cooldown = TimeSpan.FromMinutes(5),
                    Severity = AlertSeverity.Critical
                },
                new AlertRule
                {
                    Id = "alert-packet-loss",
                    Name = "Packet loss",
                    MetricId = WellKnownMetrics.NetworkPacketLoss,
                    Comparison = AlertComparison.GreaterThanOrEqual,
                    Threshold = 5,
                    PendingDuration = TimeSpan.FromSeconds(30),
                    RecoveryHysteresis = 2,
                    Cooldown = TimeSpan.FromMinutes(5),
                    Severity = AlertSeverity.Warning
                }
            ]
        };
    }
}

public sealed record GeneralSettings
{
    public bool LaunchAtSignIn { get; init; } = true;
    public bool MinimizeStudioToTray { get; init; } = true;
    public bool ShowTrayIcon { get; init; } = true;
    public bool PauseWhenWorkstationLocked { get; init; } = true;
    public bool ReducePollingOnBatterySaver { get; init; } = true;
    public bool ReducedMotion { get; init; }
}

public enum WidgetDesign
{
    Pill,
    Rail,
    Dock,
    Canvas
}

public enum WidgetDensity
{
    Compact,
    Normal,
    Comfortable
}

public enum WidgetAnchor
{
    Free,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    CenterLeft,
    CenterRight
}

public sealed record WidgetInstanceSettings
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool Enabled { get; init; } = true;
    public WidgetDesign Design { get; init; } = WidgetDesign.Pill;
    public WidgetDensity Density { get; init; } = WidgetDensity.Compact;
    public required string ThemeId { get; init; }
    public required string PerformanceProfileId { get; init; }
    public WidgetWindowSettings Window { get; init; } = new();
    public List<ModuleSettings> Modules { get; init; } = [];
}

public sealed record WidgetWindowSettings
{
    public string MonitorId { get; init; } = string.Empty;
    public double? Left { get; init; }
    public double? Top { get; init; }
    public double Width { get; init; } = 184;
    public double Height { get; init; } = 396;
    public int ScalePercent { get; init; } = 100;
    public double MinimumWidth { get; init; } = 112;
    public double MinimumHeight { get; init; } = 180;
    public double MaximumWidth { get; init; } = 1600;
    public double MaximumHeight { get; init; } = 1200;
    public WidgetAnchor Anchor { get; init; } = WidgetAnchor.Free;
    public double AnchorMargin { get; init; } = 16;
    public bool AlwaysOnTop { get; init; } = true;
    public bool Locked { get; init; }
    public bool Draggable { get; init; } = true;
    public bool Resizable { get; init; } = true;
    public bool ClickThrough { get; init; }
    public bool RememberPosition { get; init; } = true;
    public bool HideFromTaskbar { get; init; } = true;
    public double SurfaceOpacity { get; init; } = 0.82;
    public double ContentOpacity { get; init; } = 1;
    public double IdleOpacity { get; init; } = 0.65;
    public bool FadeWhenIdle { get; init; }
}

public enum ModuleSize
{
    Small,
    Medium,
    Large,
    Wide
}

public enum ModuleVisualization
{
    Value,
    Progress,
    Sparkline,
    Gauge,
    ValueAndSparkline
}

public sealed record ModuleSettings
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public bool Enabled { get; init; } = true;
    public int Order { get; init; }
    public ModuleSize Size { get; init; } = ModuleSize.Small;
    public ModuleVisualization Visualization { get; init; } =
        ModuleVisualization.ValueAndSparkline;
    public MetricId PrimaryMetric { get; init; }
    public MetricId? SecondaryMetric { get; init; }
    public List<MetricId> AdditionalMetrics { get; init; } = [];
    public string Icon { get; init; } = string.Empty;
    public string AccentColor { get; init; } = string.Empty;
    public bool ShowIcon { get; init; } = true;
    public bool ShowAccent { get; init; } = true;
    public double CardOpacity { get; init; } = 1;
    public double BorderOpacity { get; init; } = 1;
    public double? CardCornerRadiusOverride { get; init; }
    public double? CardPaddingOverride { get; init; }
    public double? AccentWidthOverride { get; init; }
    public double? ProgressHeightOverride { get; init; }
    public double? LabelSizeOverride { get; init; }
    public double? ValueSizeOverride { get; init; }
    public double? IconSizeOverride { get; init; }
    public bool ShowLabel { get; init; } = true;
    public bool ShowSecondaryValue { get; init; } = true;
    public bool ShowTrend { get; init; } = true;
    public TimeSpan TrendWindow { get; init; } = TimeSpan.FromMinutes(1);
    public int? DecimalPlacesOverride { get; init; }

    public static ModuleSettings Create(
        string id,
        string title,
        int order,
        MetricId primary,
        MetricId? secondary = null) =>
        new()
        {
            Id = id,
            Title = title,
            Order = order,
            PrimaryMetric = primary,
            SecondaryMetric = secondary
        };
}

public sealed record ThemeSettings
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool BuiltIn { get; init; }
    public ThemePalette Palette { get; init; } = new();
    public ThemeSurface Surface { get; init; } = new();
    public ThemeTypography Typography { get; init; } = new();
    public ThemeMotion Motion { get; init; } = new();

    public static ThemeSettings CreateCarbonGlass(string id) =>
        new()
        {
            Id = id,
            Name = "Carbon Glass",
            BuiltIn = true,
            Palette = new ThemePalette
            {
                Background = "#E60A0D14",
                Card = "#B9141A24",
                Border = "#403D5366",
                PrimaryText = "#FFF5F8FC",
                SecondaryText = "#FF8D9AAF",
                CpuAccent = "#FF46E8E0",
                GpuAccent = "#FFF04DDE",
                MemoryAccent = "#FF49E6AD",
                NetworkAccent = "#FF46E8E0",
                LatencyAccent = "#FFFFC857",
                WeatherAccent = "#FF62A7FF",
                Track = "#553D5366",
                Warning = "#FFFFC857",
                Critical = "#FFFF5C71",
                Success = "#FF49E6AD"
            },
            Surface = new ThemeSurface
            {
                CornerRadius = 24,
                CardCornerRadius = 12,
                BlurEnabled = true,
                BlurStrength = 0.7,
                ShadowEnabled = true,
                ShadowOpacity = 0.32,
                GlowEnabled = true,
                GlowOpacity = 0.12,
                CardBorderWidth = 1,
                CardPadding = 10,
                AccentWidth = 3,
                ProgressHeight = 4,
                SparklineThickness = 1.5,
                CardGap = 6,
                ContentPadding = 10
            }
        };
}

public sealed record ThemePalette
{
    public string Background { get; init; } = "#E60A0D14";
    public string Card { get; init; } = "#B9141A24";
    public string Border { get; init; } = "#403D5366";
    public string PrimaryText { get; init; } = "#FFF5F8FC";
    public string SecondaryText { get; init; } = "#FF8D9AAF";
    public string CpuAccent { get; init; } = "#FF46E8E0";
    public string GpuAccent { get; init; } = "#FFF04DDE";
    public string MemoryAccent { get; init; } = "#FF49E6AD";
    public string NetworkAccent { get; init; } = "#FF46E8E0";
    public string LatencyAccent { get; init; } = "#FFFFC857";
    public string WeatherAccent { get; init; } = "#FF62A7FF";
    public string Track { get; init; } = "#553D5366";
    public string Warning { get; init; } = "#FFFFC857";
    public string Critical { get; init; } = "#FFFF5C71";
    public string Success { get; init; } = "#FF49E6AD";
}

public sealed record ThemeSurface
{
    public double CornerRadius { get; init; } = 24;
    public double CardCornerRadius { get; init; } = 12;
    public bool BlurEnabled { get; init; } = true;
    public double BlurStrength { get; init; } = 0.7;
    public bool ShadowEnabled { get; init; } = true;
    public double ShadowOpacity { get; init; } = 0.3;
    public bool GlowEnabled { get; init; } = true;
    public double GlowOpacity { get; init; } = 0.12;
    public double BorderWidth { get; init; } = 1;
    public double CardBorderWidth { get; init; } = 1;
    public double CardGap { get; init; } = 6;
    public double ContentPadding { get; init; } = 10;
    public double CardPadding { get; init; } = 10;
    public double CardOpacity { get; init; } = 0.72;
    public double AccentWidth { get; init; } = 3;
    public double ProgressHeight { get; init; } = 4;
    public double SparklineThickness { get; init; } = 1.5;
    public bool HeaderVisible { get; init; } = true;
    public bool StatusIndicatorVisible { get; init; } = true;
    public bool SettingsButtonVisible { get; init; } = true;
    public double HeaderHeight { get; init; } = 36;
}

public sealed record ThemeTypography
{
    public string FontFamily { get; init; } = "Segoe UI Variable";
    public double HeaderSize { get; init; } = 11;
    public double LabelSize { get; init; } = 11;
    public double SecondarySize { get; init; } = 10;
    public double ValueSize { get; init; } = 18;
    public double MinimumReadableSize { get; init; } = 10;
    public int LabelWeight { get; init; } = 600;
    public int HeaderWeight { get; init; } = 650;
    public int SecondaryWeight { get; init; } = 450;
    public int ValueWeight { get; init; } = 600;
    public bool UseTabularNumbers { get; init; } = true;
}

public sealed record ThemeMotion
{
    public bool Enabled { get; init; } = true;
    public int TransitionMilliseconds { get; init; } = 160;
    public bool AnimateValueChanges { get; init; } = true;
    public bool RespectReducedMotion { get; init; } = true;
    public bool PulseStatusIndicator { get; init; } = true;
}

public sealed record SceneSettings
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool Enabled { get; init; } = true;
    public bool IsDefault { get; init; }
    public List<string> WidgetIds { get; init; } = [];
    public required string PerformanceProfileId { get; init; }
    public SceneActivationSettings Activation { get; init; } = new();
}

public sealed record SceneActivationSettings
{
    public List<string> ProcessNames { get; init; } = [];
    public bool RequireFullscreenApplication { get; init; }
    public bool RequireAcPower { get; init; }
    public List<DayOfWeek> Days { get; init; } = [];
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }
}

public enum PowerAwarenessMode
{
    Performance,
    Balanced,
    Efficiency
}

public sealed record PerformanceProfileSettings
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public PowerAwarenessMode Mode { get; init; } = PowerAwarenessMode.Balanced;
    public bool Enabled { get; init; } = true;
    public TimeSpan UiRefreshCadence { get; init; } = TimeSpan.FromSeconds(1);
    public double BatterySaverCadenceMultiplier { get; init; } = 3;
    public double WorkstationLockedCadenceMultiplier { get; init; } = 8;
    public Dictionary<string, TimeSpan> ProviderCadences { get; init; } =
        new(StringComparer.Ordinal);
    public HashSet<string> DisabledProviderIds { get; init; } =
        new(StringComparer.Ordinal);

    public static PerformanceProfileSettings CreateBalanced(string id) =>
        new()
        {
            Id = id,
            Name = "Balanced",
            Mode = PowerAwarenessMode.Balanced,
            ProviderCadences = new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
            {
                ["windows.native"] = TimeSpan.FromSeconds(1),
                ["network.connectivity"] = TimeSpan.FromSeconds(2),
                ["cpu.temperature.bridge"] = TimeSpan.FromSeconds(3),
                ["nvidia"] = TimeSpan.FromSeconds(2)
            }
        };
}

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

public enum HotkeyAction
{
    ToggleAllWidgets,
    ToggleClickThrough,
    ToggleLock,
    OpenStudio,
    CycleScene,
    PauseMonitoring
}

public sealed record HotkeySettings
{
    public required string Id { get; init; }
    public bool Enabled { get; init; } = true;
    public HotkeyAction Action { get; init; }
    public required string Key { get; init; }
    public HotkeyModifiers Modifiers { get; init; }
    public string? TargetWidgetId { get; init; }
    public string? TargetSceneId { get; init; }
}

public sealed record DataRetentionSettings
{
    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(7);
    public int MaximumSamplesPerMetric { get; init; } = 10_800;
    public bool RecordUnavailableSamples { get; init; } = true;
}
