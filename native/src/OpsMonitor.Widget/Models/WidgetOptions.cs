using System.Text.Json.Serialization;

namespace OpsMonitor.Widget.Models;

public enum WidgetLayout
{
    Pill,
    Rail,
    Dock,
    Mini
}

public enum WidgetDensity
{
    Compact,
    Normal,
    Detail
}

public enum WidgetInteractionMode
{
    Edit,
    Locked,
    ClickThrough
}

public enum WidgetModuleSize
{
    Small,
    Medium,
    Large,
    Wide
}

public enum WidgetModuleVisualization
{
    Value,
    Progress,
    Sparkline,
    Gauge,
    ValueAndSparkline
}

internal enum SemanticAccent
{
    Cpu,
    Gpu,
    Memory,
    Network,
    Latency,
    Weather
}

public sealed class WidgetSettings
{
    public WidgetLayout Layout { get; set; } = WidgetLayout.Pill;

    public WidgetDensity Density { get; set; } = WidgetDensity.Compact;

    public WidgetInteractionMode InteractionMode { get; set; } = WidgetInteractionMode.Edit;

    public string Theme { get; set; } = "Void";

    public bool Topmost { get; set; } = true;

    public bool Draggable { get; set; } = true;

    public bool Resizable { get; set; } = true;

    public bool ShowBattery { get; set; }

    public bool ShowWeather { get; set; } = true;

    public string WeatherLocationName { get; set; } = "Celje";

    public string WeatherCountry { get; set; } = "Slovenia";

    public double WeatherLatitude { get; set; } = 46.2366;

    public double WeatherLongitude { get; set; } = 15.2259;

    public string WeatherTimeZone { get; set; } = "Europe/Ljubljana";

    public string? WeatherArsoStationCode { get; set; } = "CELJE_MEDLOG";

    public int WeatherRefreshMinutes { get; set; } = 10;

    public List<string> ModuleOrder { get; set; } =
        WidgetModuleCatalog.CreateDefaultOrder();

    public List<string> EnabledModules { get; set; } =
        WidgetModuleCatalog.CreateDefaultEnabled();

    public bool StartAtSignIn { get; set; } = true;

    public bool ReducedMotion { get; set; }

    public double UpdateCadenceSeconds { get; set; } = 1;

    public double SurfaceOpacity { get; set; } = 0.88;

    public double ContentOpacity { get; set; } = 1;

    public int ScalePercent { get; set; } = 100;

    [JsonIgnore]
    public string? CoreThemeId { get; set; }

    [JsonIgnore]
    public IReadOnlyList<WidgetRuntimeTheme> RuntimeThemes { get; set; } = [];

    [JsonIgnore]
    public IReadOnlyDictionary<string, WidgetModulePresentation> ModulePresentation { get; set; } =
        new Dictionary<string, WidgetModulePresentation>(StringComparer.Ordinal);

    [JsonIgnore]
    public IReadOnlyDictionary<string, WidgetModuleMetricBinding> ModuleMetricBindings { get; set; } =
        new Dictionary<string, WidgetModuleMetricBinding>(StringComparer.Ordinal);

    public double? Left { get; set; }

    public double? Top { get; set; }

    public double? Width { get; set; }

    public double? Height { get; set; }
}

public sealed record WidgetModulePresentation
{
    public WidgetModuleSize Size { get; init; } = WidgetModuleSize.Small;

    public WidgetModuleVisualization Visualization { get; init; } =
        WidgetModuleVisualization.ValueAndSparkline;

    public bool ShowLabel { get; init; } = true;

    public bool ShowSecondaryValue { get; init; } = true;

    public bool ShowTrend { get; init; } = true;

    public int? DecimalPlacesOverride { get; init; }

    public string Title { get; init; } = string.Empty;

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
}

public sealed record WidgetRuntimeTheme
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Background { get; init; }

    public required string Card { get; init; }

    public required string Border { get; init; }

    public required string PrimaryText { get; init; }

    public required string SecondaryText { get; init; }

    public required string CpuAccent { get; init; }

    public required string GpuAccent { get; init; }

    public string MemoryAccent { get; init; } = "#FF58E6B2";

    public required string NetworkAccent { get; init; }

    public string LatencyAccent { get; init; } = "#FFFFC35A";

    public string WeatherAccent { get; init; } = "#FF62A7FF";

    public string Track { get; init; } = "#55364258";

    public required string Warning { get; init; }

    public required string Critical { get; init; }

    public required string Success { get; init; }

    public string FontFamily { get; init; } = "Segoe UI Variable";

    public double HeaderSize { get; init; } = 11;

    public double LabelSize { get; init; } = 12;

    public double SecondarySize { get; init; } = 10;

    public double ValueSize { get; init; } = 18;

    public double MinimumReadableSize { get; init; } = 12;

    public int LabelWeight { get; init; } = 600;

    public int HeaderWeight { get; init; } = 650;

    public int SecondaryWeight { get; init; } = 450;

    public int ValueWeight { get; init; } = 600;

    public bool UseTabularNumbers { get; init; } = true;

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
    public bool MotionEnabled { get; init; } = true;
    public int TransitionMilliseconds { get; init; } = 160;
    public bool AnimateValueChanges { get; init; } = true;
    public bool RespectReducedMotion { get; init; } = true;
    public bool PulseStatusIndicator { get; init; } = true;
}
