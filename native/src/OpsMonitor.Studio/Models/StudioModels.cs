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
    private string _customTitle;
    private string _customIcon;
    private string _accentHex;
    private bool _useCustomAccent;
    private bool _showIcon = true;
    private bool _showAccent = true;
    private double _cardOpacity = 1;
    private double _borderOpacity = 1;
    private double _cardCornerRadius = -1;
    private double _cardPadding = -1;
    private double _accentWidth = -1;
    private double _progressHeight = -1;
    private double _labelSize = -1;
    private double _valueSize = -1;
    private double _iconSize = -1;
    private Brush _previewCardBrush = Brushes.Transparent;
    private Brush _previewBorderBrush = Brushes.Transparent;
    private double _previewCardCornerRadius = 12;
    private double _previewCardPadding = 10;
    private double _previewAccentWidth = 3;
    private double _previewProgressHeight = 4;
    private double _previewLabelSize = 11;
    private double _previewValueSize = 18;
    private double _previewIconSize = 14;
    private double _previewCompactLabelSize = 9;
    private double _previewCompactValueSize = 13;
    private double _previewCompactIconSize = 10;

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
        _customTitle = name;
        _customIcon = icon;
        _accentHex = ColorText.ToHex((accent as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue);
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

    public void SetPreviewAccent(Brush accent)
    {
        Accent = accent;
        OnPropertyChanged(nameof(Accent));
    }

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

    public string CustomTitle
    {
        get => _customTitle;
        set => SetEditorProperty(ref _customTitle, (value ?? string.Empty).Trim());
    }

    public string CustomIcon
    {
        get => _customIcon;
        set => SetEditorProperty(ref _customIcon, value ?? string.Empty);
    }

    public string AccentHex
    {
        get => _accentHex;
        set
        {
            var normalized = ColorText.Normalize(value, _accentHex);
            if (SetEditorProperty(ref _accentHex, normalized))
            {
                Accent = new SolidColorBrush(ColorText.Parse(normalized, Colors.DeepSkyBlue));
                OnPropertyChanged(nameof(Accent));
            }
        }
    }

    public bool UseCustomAccent { get => _useCustomAccent; set => SetEditorProperty(ref _useCustomAccent, value); }

    public bool ShowIcon { get => _showIcon; set => SetEditorProperty(ref _showIcon, value); }
    public bool ShowAccent { get => _showAccent; set => SetEditorProperty(ref _showAccent, value); }
    public double CardOpacity { get => _cardOpacity; set => SetEditorProperty(ref _cardOpacity, Math.Clamp(value, 0.2, 1)); }
    public double BorderOpacity { get => _borderOpacity; set => SetEditorProperty(ref _borderOpacity, Math.Clamp(value, 0, 1)); }
    public double CardCornerRadius { get => _cardCornerRadius; set => SetOverride(ref _cardCornerRadius, value, 0, 40); }
    public double CardPadding { get => _cardPadding; set => SetOverride(ref _cardPadding, value, 0, 28); }
    public double AccentWidth { get => _accentWidth; set => SetOverride(ref _accentWidth, value, 0, 10); }
    public double ProgressHeight { get => _progressHeight; set => SetOverride(ref _progressHeight, value, 1, 12); }
    public double LabelSize { get => _labelSize; set => SetOverride(ref _labelSize, value, 8, 26); }
    public double ValueSize { get => _valueSize; set => SetOverride(ref _valueSize, value, 10, 42); }
    public double IconSize { get => _iconSize; set => SetOverride(ref _iconSize, value, 8, 32); }

    public string PreviewPrimaryValue => ApplyPrecision(PrimaryValue, Precision);

    public string PreviewSecondaryValue => ApplyPrecision(SecondaryValue, Precision);

    public Brush PreviewCardBrush { get => _previewCardBrush; private set => SetProperty(ref _previewCardBrush, value); }
    public Brush PreviewBorderBrush { get => _previewBorderBrush; private set => SetProperty(ref _previewBorderBrush, value); }
    public double PreviewCardCornerRadius { get => _previewCardCornerRadius; private set => SetProperty(ref _previewCardCornerRadius, value); }
    public double PreviewCardPadding { get => _previewCardPadding; private set => SetProperty(ref _previewCardPadding, value); }
    public double PreviewAccentWidth { get => _previewAccentWidth; private set => SetProperty(ref _previewAccentWidth, value); }
    public double PreviewProgressHeight { get => _previewProgressHeight; private set => SetProperty(ref _previewProgressHeight, value); }
    public double PreviewLabelSize { get => _previewLabelSize; private set => SetProperty(ref _previewLabelSize, value); }
    public double PreviewValueSize { get => _previewValueSize; private set => SetProperty(ref _previewValueSize, value); }
    public double PreviewIconSize { get => _previewIconSize; private set => SetProperty(ref _previewIconSize, value); }
    public double PreviewCompactLabelSize { get => _previewCompactLabelSize; private set => SetProperty(ref _previewCompactLabelSize, value); }
    public double PreviewCompactValueSize { get => _previewCompactValueSize; private set => SetProperty(ref _previewCompactValueSize, value); }
    public double PreviewCompactIconSize { get => _previewCompactIconSize; private set => SetProperty(ref _previewCompactIconSize, value); }

    public void ApplyPreviewDesign(WidgetDesignerState designer, double maximumCardPadding = 28)
    {
        ArgumentNullException.ThrowIfNull(designer);
        PreviewCardBrush = OpacityBrush(designer.Card, designer.CardOpacity * CardOpacity);
        PreviewBorderBrush = OpacityBrush(designer.Border, BorderOpacity);
        PreviewCardCornerRadius = CardCornerRadius < 0 ? designer.CardCornerRadius : CardCornerRadius;
        PreviewCardPadding = Math.Min(
            CardPadding < 0 ? designer.CardPadding : CardPadding,
            Math.Max(0, maximumCardPadding));
        PreviewAccentWidth = AccentWidth < 0 ? designer.AccentWidth : AccentWidth;
        PreviewProgressHeight = ProgressHeight < 0 ? designer.ProgressHeight : ProgressHeight;
        PreviewLabelSize = LabelSize < 0 ? designer.LabelSize : LabelSize;
        PreviewValueSize = ValueSize < 0 ? designer.ValueSize : ValueSize;
        PreviewIconSize = IconSize < 0 ? Math.Max(designer.LabelSize + 2, 10) : IconSize;
        PreviewCompactLabelSize = Math.Clamp(PreviewLabelSize * 0.72, 8, 10);
        PreviewCompactValueSize = Math.Clamp(PreviewValueSize * 0.72, 10, 14);
        PreviewCompactIconSize = Math.Clamp(PreviewIconSize * 0.72, 8, 11);
    }

    private bool SetEditorProperty<T>(
        ref T field,
        T value,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        EditorValueChanging?.Invoke(this, EventArgs.Empty);
        _ = SetProperty(ref field, value, propertyName);
        if (propertyName == nameof(Precision))
        {
            OnPropertyChanged(nameof(PreviewPrimaryValue));
            OnPropertyChanged(nameof(PreviewSecondaryValue));
        }
        return true;
    }

    private void SetOverride(ref double field, double value, double minimum, double maximum)
    {
        var normalized = value < 0 ? -1 : Math.Clamp(value, minimum, maximum);
        _ = SetEditorProperty(ref field, normalized);
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

    private static SolidColorBrush OpacityBrush(string colorText, double opacity)
    {
        var color = ColorText.Parse(colorText, Colors.Transparent);
        color.A = (byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1));
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
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
            CustomTitle = CustomTitle,
            CustomIcon = CustomIcon,
            AccentHex = AccentHex,
            UseCustomAccent = UseCustomAccent,
            ShowIcon = ShowIcon,
            ShowAccent = ShowAccent,
            CardOpacity = CardOpacity,
            BorderOpacity = BorderOpacity,
            CardCornerRadius = CardCornerRadius,
            CardPadding = CardPadding,
            AccentWidth = AccentWidth,
            ProgressHeight = ProgressHeight,
            LabelSize = LabelSize,
            ValueSize = ValueSize,
            IconSize = IconSize,
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
    public Brush PreviewTextBrush => new SolidColorBrush(IsLight(Surface)
        ? Color.FromRgb(12, 23, 34)
        : Color.FromRgb(244, 247, 252));
    public Brush PreviewSecondaryTextBrush => new SolidColorBrush(IsLight(Surface)
        ? Color.FromRgb(66, 84, 102)
        : Color.FromRgb(179, 190, 206));

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private static bool IsLight(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255d > 0.6;
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

public sealed class SensorCatalogItem : ObservableObject
{
    private bool _isPinned;

    public SensorCatalogItem(
        string metricId,
        string name,
        string hardware,
        string sensorType,
        string value,
        string moduleId,
        bool isPinned)
    {
        MetricId = metricId;
        Name = name;
        Hardware = hardware;
        SensorType = sensorType;
        Value = value;
        ModuleId = moduleId;
        _isPinned = isPinned;
    }

    public string MetricId { get; }
    public string Name { get; }
    public string Hardware { get; }
    public string SensorType { get; }
    public string Value { get; }
    public string ModuleId { get; }
    public string ModuleLabel => ModuleId switch
    {
        "cpu" => "CPU details",
        "gpu" => "GPU details",
        "ram" => "Memory details",
        "disk" => "Storage details",
        _ => "System details"
    };

    public event EventHandler? PinChanged;

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (SetProperty(ref _isPinned, value))
            {
                PinChanged?.Invoke(this, EventArgs.Empty);
            }
        }
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
    string Accent)
{
    public bool UseCustomAccent { get; init; }
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

public sealed record StudioThemeSnapshot(
    string Id,
    string Name,
    string Surface,
    string Card,
    string Border,
    string Accent)
{
    public string PrimaryText { get; init; } = "#FFF6F9FF";
    public string SecondaryText { get; init; } = "#FFB8C4D6";
    public string CpuAccent { get; init; } = "#FF48DCF9";
    public string GpuAccent { get; init; } = "#FFFF4FD8";
    public string MemoryAccent { get; init; } = "#FF58E6B2";
    public string NetworkAccent { get; init; } = "#FF62A7FF";
    public string LatencyAccent { get; init; } = "#FFFFC35A";
    public string WeatherAccent { get; init; } = "#FF62A7FF";
    public string Track { get; init; } = "#55364258";
    public string Warning { get; init; } = "#FFFFC35A";
    public string Critical { get; init; } = "#FFFF566E";
    public string Success { get; init; } = "#FF58E6B2";
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
    public string FontFamily { get; init; } = "Segoe UI Variable";
    public double HeaderSize { get; init; } = 11;
    public double LabelSize { get; init; } = 11;
    public double SecondarySize { get; init; } = 10;
    public double ValueSize { get; init; } = 18;
    public double MinimumReadableSize { get; init; } = 10;
    public int HeaderWeight { get; init; } = 650;
    public int LabelWeight { get; init; } = 600;
    public int SecondaryWeight { get; init; } = 450;
    public int ValueWeight { get; init; } = 600;
    public bool UseTabularNumbers { get; init; } = true;
    public bool MotionEnabled { get; init; } = true;
    public int TransitionMilliseconds { get; init; } = 160;
    public bool AnimateValueChanges { get; init; } = true;
    public bool RespectReducedMotion { get; init; } = true;
    public bool PulseStatusIndicator { get; init; } = true;
}

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

public sealed record StudioSensorPinSnapshot(
    string MetricId,
    string ModuleId);

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
    int SchemaVersion = 4,
    IReadOnlyList<StudioSensorPinSnapshot>? SensorPins = null);

public sealed record StudioDesignPackage(
    int SchemaVersion,
    string Name,
    string Layout,
    string Density,
    StudioThemeSnapshot Theme,
    IReadOnlyList<StudioModuleSnapshot>? Modules);

internal static class ColorText
{
    public static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            return ColorConverter.ConvertFromString(value.Trim()) is Color color
                ? ToHex(color)
                : fallback;
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException)
        {
            return fallback;
        }
    }

    public static Color Parse(string? value, Color fallback) =>
        (Color)ColorConverter.ConvertFromString(Normalize(value, ToHex(fallback)))!;

    public static string ToHex(Color color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
