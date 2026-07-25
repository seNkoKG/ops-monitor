namespace OpsMonitor.Widget.Models;

public enum WidgetLayout
{
    Pill,
    Rail,
    Dock
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

internal enum SemanticAccent
{
    Cyan,
    Magenta,
    Mint,
    Amber
}

public sealed class WidgetSettings
{
    public WidgetLayout Layout { get; set; } = WidgetLayout.Rail;

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

    public double? Left { get; set; }

    public double? Top { get; set; }

    public double? Width { get; set; }

    public double? Height { get; set; }
}
