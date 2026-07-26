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
    Latency
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

    public List<string> ModuleOrder { get; set; } =
        WidgetModuleCatalog.CreateDefaultOrder();

    public List<string> EnabledModules { get; set; } =
        WidgetModuleCatalog.CreateDefaultEnabled();

    public bool StartAtSignIn { get; set; } = true;

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

    public required string NetworkAccent { get; init; }

    public required string Warning { get; init; }

    public required string Critical { get; init; }

    public required string Success { get; init; }

    public string FontFamily { get; init; } = "Segoe UI Variable";

    public double LabelSize { get; init; } = 12;

    public double ValueSize { get; init; } = 18;

    public double MinimumReadableSize { get; init; } = 12;

    public int LabelWeight { get; init; } = 600;

    public int ValueWeight { get; init; } = 600;

    public bool UseTabularNumbers { get; init; } = true;
}
