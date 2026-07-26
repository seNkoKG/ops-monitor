using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Media;
using OpsMonitor.Studio.Infrastructure;

namespace OpsMonitor.Studio.Models;

public sealed record NavigationItem(string Id, string Label, string Description, string Icon);

public sealed class ModuleItem : ObservableObject
{
    private bool _isVisible;
    private string _size;
    private string _primaryValue;
    private string _secondaryValue;
    private double _usagePercent;
    private bool _showLabel = true;
    private bool _showSparkline = true;
    private bool _showTemperature = true;
    private string _visualization = "Bar + sparkline";
    private string _precision = "Whole numbers";

    public ModuleItem(
        string id,
        string name,
        string icon,
        string category,
        string description,
        string source,
        Brush accent,
        string primaryValue,
        string secondaryValue,
        double usagePercent,
        bool isVisible = true,
        string size = "Medium")
    {
        Id = id;
        Name = name;
        Icon = icon;
        Category = category;
        Description = description;
        Source = source;
        Accent = accent;
        _primaryValue = primaryValue;
        _secondaryValue = secondaryValue;
        _usagePercent = usagePercent;
        _isVisible = isVisible;
        _size = size;

        SparklinePoints = new ObservableCollection<double>(
            Enumerable.Range(0, 20).Select(index =>
                Math.Clamp(usagePercent + Math.Sin(index * 0.72 + usagePercent) * 8, 4, 98)));
    }

    public string Id { get; }
    public string Name { get; set; }
    public string Icon { get; set; }
    public string Category { get; set; }
    public string Description { get; set; }
    public string Source { get; set; }
    public Brush Accent { get; set; }
    public ObservableCollection<double> SparklinePoints { get; }
    public event EventHandler? EditorValueChanging;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetEditorProperty(ref _isVisible, value);
    }

    public string Size
    {
        get => _size;
        set => SetEditorProperty(ref _size, value);
    }

    public string PrimaryValue
    {
        get => _primaryValue;
        set
        {
            if (SetProperty(ref _primaryValue, value))
            {
                OnPropertyChanged(nameof(PreviewPrimaryValue));
            }
        }
    }

    public string SecondaryValue
    {
        get => _secondaryValue;
        set
        {
            if (SetProperty(ref _secondaryValue, value))
            {
                OnPropertyChanged(nameof(PreviewSecondaryValue));
            }
        }
    }

    public double UsagePercent
    {
        get => _usagePercent;
        set => SetProperty(ref _usagePercent, value);
    }

    public bool ShowLabel
    {
        get => _showLabel;
        set => SetEditorProperty(ref _showLabel, value);
    }

    public bool ShowSparkline
    {
        get => _showSparkline;
        set => SetEditorProperty(ref _showSparkline, value);
    }

    public bool ShowTemperature
    {
        get => _showTemperature;
        set => SetEditorProperty(ref _showTemperature, value);
    }

    public string Visualization
    {
        get => _visualization;
        set => SetEditorProperty(ref _visualization, value);
    }

    public string Precision
    {
        get => _precision;
        set => SetEditorProperty(ref _precision, value);
    }

    public string PreviewPrimaryValue => ApplyPrecision(PrimaryValue, Precision);

    public string PreviewSecondaryValue => ApplyPrecision(SecondaryValue, Precision);

    private void SetEditorProperty<T>(
        ref T field,
        T value,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        EditorValueChanging?.Invoke(this, EventArgs.Empty);
        _ = SetProperty(ref field, value, propertyName);
        if (propertyName == nameof(Precision))
        {
            OnPropertyChanged(nameof(PreviewPrimaryValue));
            OnPropertyChanged(nameof(PreviewSecondaryValue));
        }
    }

    private static string ApplyPrecision(string value, string precision)
    {
        var decimals = precision switch
        {
            "Whole numbers" => 0,
            "1 decimal" => 1,
            "2 decimals" => 2,
            _ => -1
        };
        if (decimals < 0 || string.IsNullOrEmpty(value))
        {
            return value;
        }

        return Regex.Replace(
            value,
            @"-?\d+(?:\.\d+)?",
            match => double.TryParse(
                match.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number)
                ? number.ToString(
                    decimals == 0 ? "0" : $"0.{new string('0', decimals)}",
                    CultureInfo.InvariantCulture)
                : match.Value);
    }

    public ModuleItem Clone(string suffix)
        => new(
            $"{Id}-{suffix}",
            $"{Name} copy",
            Icon,
            Category,
            Description,
            Source,
            Accent,
            PrimaryValue,
            SecondaryValue,
            UsagePercent,
            IsVisible,
            Size)
        {
            ShowLabel = ShowLabel,
            ShowSparkline = ShowSparkline,
            ShowTemperature = ShowTemperature,
            Visualization = Visualization,
            Precision = Precision,
        };
}

public sealed class ThemePreset : ObservableObject
{
    private bool _isSelected;

    public ThemePreset(
        string id,
        string name,
        string description,
        Color surface,
        Color card,
        Color border,
        Color accent)
    {
        Id = id;
        Name = name;
        Description = description;
        Surface = surface;
        Card = card;
        Border = border;
        Accent = accent;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public Color Surface { get; }
    public Color Card { get; }
    public Color Border { get; }
    public Color Accent { get; }
    public Brush SurfaceBrush => new SolidColorBrush(Surface);
    public Brush CardBrush => new SolidColorBrush(Card);
    public Brush BorderBrush => new SolidColorBrush(Border);
    public Brush AccentBrush => new SolidColorBrush(Accent);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class SceneItem : ObservableObject
{
    private bool _isActive;

    public SceneItem(string id, string name, string layout, string description, string hotkey, Brush accent)
    {
        Id = id;
        Name = name;
        Layout = layout;
        Description = description;
        Hotkey = hotkey;
        Accent = accent;
    }

    public string Id { get; }
    public string Name { get; }
    public string Layout { get; }
    public string Description { get; }
    public string Hotkey { get; }
    public Brush Accent { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}

public sealed class AlertRuleItem : ObservableObject
{
    private bool _isEnabled;

    public AlertRuleItem(
        string name,
        string metric,
        string condition,
        string threshold,
        string duration,
        string severity,
        Brush severityBrush,
        bool isEnabled)
    {
        Name = name;
        Metric = metric;
        Condition = condition;
        Threshold = threshold;
        Duration = duration;
        Severity = severity;
        SeverityBrush = severityBrush;
        _isEnabled = isEnabled;
    }

    public string Name { get; }
    public string Metric { get; }
    public string Condition { get; }
    public string Threshold { get; }
    public string Duration { get; }
    public string Severity { get; }
    public Brush SeverityBrush { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }
}

public sealed class ProviderItem : ObservableObject
{
    private string _status;
    private string _latency;
    private Brush _statusBrush;

    public ProviderItem(
        string name,
        string description,
        string status,
        string latency,
        string sensors,
        string version,
        bool needsElevation,
        Brush statusBrush)
    {
        Name = name;
        Description = description;
        _status = status;
        _latency = latency;
        Sensors = sensors;
        Version = version;
        NeedsElevation = needsElevation;
        _statusBrush = statusBrush;
    }

    public string Name { get; }
    public string Description { get; }
    public string Sensors { get; }
    public string Version { get; }
    public bool NeedsElevation { get; }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string Latency
    {
        get => _latency;
        set => SetProperty(ref _latency, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        set => SetProperty(ref _statusBrush, value);
    }
}

public sealed record ActivityItem(string Time, string Title, string Detail, string Icon, Brush Accent);

public sealed class LayoutPreset : ObservableObject
{
    private bool _isSelected;

    public LayoutPreset(string id, string name, string description, string icon)
    {
        Id = id;
        Name = name;
        Description = description;
        Icon = icon;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Icon { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed record StudioModuleSnapshot(
    string Id,
    string Name,
    int Order,
    bool Enabled,
    string Size,
    string Visualization,
    bool ShowLabel,
    bool ShowSparkline,
    bool ShowTemperature,
    string Precision,
    string Icon,
    string Accent);

public sealed record StudioThemeSnapshot(
    string Id,
    string Name,
    string Surface,
    string Card,
    string Border,
    string Accent);

public sealed record StudioSceneSnapshot(
    string Id,
    string Name,
    string Layout,
    bool IsActive);

public sealed record StudioAlertSnapshot(
    string Id,
    string Name,
    string Metric,
    string Condition,
    string Threshold,
    string Duration,
    string Severity,
    bool Enabled);

public sealed record StudioSettingsSnapshot(
    string Scene,
    string Layout,
    string Theme,
    double BackgroundOpacity,
    double ContentOpacity,
    double BlurStrength,
    string Density,
    double FontScale,
    bool AlwaysOnTop,
    bool PositionLocked,
    bool ClickThrough,
    bool StartAtSignIn,
    bool SnapToGrid,
    IReadOnlyList<string> VisibleModules,
    bool Draggable = true,
    bool Resizable = true,
    double WidgetWidth = 184,
    double WidgetHeight = 396,
    int WidgetScalePercent = 100,
    double UpdateCadenceSeconds = 2,
    string PerformanceMode = "Balanced",
    bool AlertsEnabled = true,
    bool ReducedMotion = false,
    IReadOnlyList<StudioModuleSnapshot>? Modules = null,
    StudioThemeSnapshot? ThemeDetails = null,
    IReadOnlyList<StudioSceneSnapshot>? Scenes = null,
    IReadOnlyList<StudioAlertSnapshot>? Alerts = null,
    bool DemoMetrics = true,
    int SchemaVersion = 2);
