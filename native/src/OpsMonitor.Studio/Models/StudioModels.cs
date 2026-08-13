using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Media;
using OpsMonitor.Studio.Infrastructure;
using OpsMonitor.Widget.Models;
using OpsMonitor.Widget.ViewModels;

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
    private string _cardHex = "#FF0F1521";
    private string _borderHex = "#FF364258";
    private string _primaryTextHex = "#FFF6F9FF";
    private string _secondaryTextHex = "#FFB8C4D6";
    private string _trackHex = "#55364258";
    private bool _useCustomCardColor;
    private bool _useCustomBorderColor;
    private bool _useCustomPrimaryTextColor;
    private bool _useCustomSecondaryTextColor;
    private bool _useCustomTrackColor;
    private bool _showIcon = true;
    private bool _showAccent = true;
    private double _cardOpacity = 1;
    private double _borderOpacity = 1;
    private double _cardCornerRadius = -1;
    private double _cardBorderWidth = -1;
    private double _cardGap = -1;
    private double _cardPadding = -1;
    private double _accentWidth = -1;
    private double _progressHeight = -1;
    private double _progressCornerRadius = -1;
    private double _sparklineThickness = -1;
    private double _sparklineFillOpacity = -1;
    private double _labelSize = -1;
    private double _secondarySize = -1;
    private double _valueSize = -1;
    private double _iconSize = -1;
    private int _labelWeight = -1;
    private int _valueWeight = -1;
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

        ProductionCard = new MetricCardViewModel(
            ProductionKey(id),
            name,
            Geometry.Empty,
            SemanticFor(id));

        SparklinePoints = new ObservableCollection<double>(
            Enumerable.Range(0, 20).Select(index =>
                Math.Clamp(usagePercent + Math.Sin(index * 0.72 + usagePercent) * 8, 4, 98)));
        foreach (var sample in SparklinePoints)
        {
            ProductionCard.PushSample(sample);
        }
        SyncProductionValues();
    }

    public string Id { get; }
    public string Name { get; set; }
    public string Icon { get; set; }
    public string Category { get; set; }
    public string Description { get; set; }
    public string Source { get; set; }
    public Brush Accent { get; set; }
    public ObservableCollection<double> SparklinePoints { get; }
    public MetricCardViewModel ProductionCard { get; }
    public event EventHandler? EditorValueChanging;

    public void SetPreviewAccent(Brush accent)
    {
        Accent = accent;
        OnPropertyChanged(nameof(Accent));
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetEditorProperty(ref _isVisible, value))
            {
                ProductionCard.IsVisible = value;
            }
        }
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
                SyncProductionValues();
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
                SyncProductionValues();
            }
        }
    }

    public double UsagePercent
    {
        get => _usagePercent;
        set
        {
            if (SetProperty(ref _usagePercent, value))
            {
                SyncProductionValues();
            }
        }
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

    public string CardHex
    {
        get => _cardHex;
        set => SetColorOverride(ref _cardHex, value, nameof(CardHex), nameof(CardSwatch));
    }

    public string BorderHex
    {
        get => _borderHex;
        set => SetColorOverride(ref _borderHex, value, nameof(BorderHex), nameof(BorderSwatch));
    }

    public string PrimaryTextHex
    {
        get => _primaryTextHex;
        set => SetColorOverride(ref _primaryTextHex, value, nameof(PrimaryTextHex), nameof(PrimaryTextSwatch));
    }

    public string SecondaryTextHex
    {
        get => _secondaryTextHex;
        set => SetColorOverride(ref _secondaryTextHex, value, nameof(SecondaryTextHex), nameof(SecondaryTextSwatch));
    }

    public string TrackHex
    {
        get => _trackHex;
        set => SetColorOverride(ref _trackHex, value, nameof(TrackHex), nameof(TrackSwatch));
    }

    public Brush CardSwatch => FrozenBrush(CardHex, Colors.Black);
    public Brush BorderSwatch => FrozenBrush(BorderHex, Colors.DimGray);
    public Brush PrimaryTextSwatch => FrozenBrush(PrimaryTextHex, Colors.White);
    public Brush SecondaryTextSwatch => FrozenBrush(SecondaryTextHex, Colors.LightGray);
    public Brush TrackSwatch => FrozenBrush(TrackHex, Colors.DimGray);

    public bool UseCustomCardColor { get => _useCustomCardColor; set => SetEditorProperty(ref _useCustomCardColor, value); }
    public bool UseCustomBorderColor { get => _useCustomBorderColor; set => SetEditorProperty(ref _useCustomBorderColor, value); }
    public bool UseCustomPrimaryTextColor { get => _useCustomPrimaryTextColor; set => SetEditorProperty(ref _useCustomPrimaryTextColor, value); }
    public bool UseCustomSecondaryTextColor { get => _useCustomSecondaryTextColor; set => SetEditorProperty(ref _useCustomSecondaryTextColor, value); }
    public bool UseCustomTrackColor { get => _useCustomTrackColor; set => SetEditorProperty(ref _useCustomTrackColor, value); }

    public bool ShowIcon { get => _showIcon; set => SetEditorProperty(ref _showIcon, value); }
    public bool ShowAccent { get => _showAccent; set => SetEditorProperty(ref _showAccent, value); }
    public double CardOpacity { get => _cardOpacity; set => SetEditorProperty(ref _cardOpacity, Math.Clamp(value, 0.2, 1)); }
    public double BorderOpacity { get => _borderOpacity; set => SetEditorProperty(ref _borderOpacity, Math.Clamp(value, 0, 1)); }
    public double CardCornerRadius { get => _cardCornerRadius; set => SetOverride(ref _cardCornerRadius, value, 0, 40); }
    public double CardBorderWidth { get => _cardBorderWidth; set => SetOverride(ref _cardBorderWidth, value, 0, 4); }
    public double CardGap { get => _cardGap; set => SetOverride(ref _cardGap, value, 0, 20); }
    public double CardPadding { get => _cardPadding; set => SetOverride(ref _cardPadding, value, 0, 28); }
    public double AccentWidth { get => _accentWidth; set => SetOverride(ref _accentWidth, value, 0, 10); }
    public double ProgressHeight { get => _progressHeight; set => SetOverride(ref _progressHeight, value, 1, 12); }
    public double ProgressCornerRadius { get => _progressCornerRadius; set => SetOverride(ref _progressCornerRadius, value, 0, 6); }
    public double SparklineThickness { get => _sparklineThickness; set => SetOverride(ref _sparklineThickness, value, 0.5, 5); }
    public double SparklineFillOpacity { get => _sparklineFillOpacity; set => SetOverride(ref _sparklineFillOpacity, value, 0, 0.5); }
    public double LabelSize { get => _labelSize; set => SetOverride(ref _labelSize, value, 8, 26); }
    public double SecondarySize { get => _secondarySize; set => SetOverride(ref _secondarySize, value, 8, 24); }
    public double ValueSize { get => _valueSize; set => SetOverride(ref _valueSize, value, 10, 42); }
    public double IconSize { get => _iconSize; set => SetOverride(ref _iconSize, value, 8, 32); }
    public int LabelWeight { get => _labelWeight; set => SetOverride(ref _labelWeight, value, 100, 900); }
    public int ValueWeight { get => _valueWeight; set => SetOverride(ref _valueWeight, value, 100, 900); }

    public bool HasOverrides => OverrideCount > 0;
    public string OverrideSummary => OverrideCount == 0
        ? "Inheriting the global design system"
        : $"{OverrideCount} custom module setting{(OverrideCount == 1 ? string.Empty : "s")}";

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
        ApplyProductionDesign(designer, maximumCardPadding);
    }

    private void ApplyProductionDesign(WidgetDesignerState designer, double maximumCardPadding)
    {
        var theme = new ThemeDefinition(
            "Studio preview",
            null,
            ColorText.Parse(designer.Surface, Colors.Black),
            ColorText.Parse(designer.Card, Colors.Black),
            ColorText.Parse(designer.Border, Colors.DimGray),
            ColorText.Parse(designer.PrimaryText, Colors.White),
            ColorText.Parse(designer.SecondaryText, Colors.LightGray),
            ColorText.Parse(designer.CpuAccent, Colors.Cyan),
            ColorText.Parse(designer.GpuAccent, Colors.Magenta),
            ColorText.Parse(designer.MemoryAccent, Colors.SpringGreen),
            ColorText.Parse(designer.NetworkAccent, Colors.DeepSkyBlue),
            ColorText.Parse(designer.Warning, Colors.Gold),
            ColorText.Parse(designer.Critical, Colors.OrangeRed),
            designer.FontFamily,
            designer.LabelSize,
            designer.ValueSize,
            designer.MinimumReadableSize,
            designer.LabelWeight,
            designer.ValueWeight,
            designer.UseTabularNumbers)
        {
            LatencyAccent = ColorText.Parse(designer.LatencyAccent, Colors.Gold),
            WeatherAccent = ColorText.Parse(designer.WeatherAccent, Colors.DeepSkyBlue),
            Success = ColorText.Parse(designer.Success, Colors.SpringGreen),
            Track = ColorText.Parse(designer.Track, Colors.DimGray),
            CornerRadius = designer.CornerRadius,
            CardCornerRadius = designer.CardCornerRadius,
            ShadowEnabled = designer.ShadowEnabled,
            ShadowOpacity = designer.ShadowOpacity,
            GlowEnabled = designer.GlowEnabled,
            GlowOpacity = designer.GlowOpacity,
            BorderWidth = designer.BorderWidth,
            CardBorderWidth = designer.CardBorderWidth,
            CardGap = designer.CardGap,
            ContentPadding = designer.ContentPadding,
            CardPadding = Math.Min(designer.CardPadding, maximumCardPadding),
            CardOpacity = designer.CardOpacity,
            AccentWidth = designer.AccentWidth,
            ProgressHeight = designer.ProgressHeight,
            ProgressCornerRadius = designer.ProgressCornerRadius,
            SparklineThickness = designer.SparklineThickness,
            SparklineFillOpacity = designer.SparklineFillOpacity,
            HeaderVisible = designer.HeaderVisible,
            StatusIndicatorVisible = designer.StatusIndicatorVisible,
            SettingsButtonVisible = designer.SettingsButtonVisible,
            HeaderHeight = designer.HeaderHeight,
            HeaderSize = designer.HeaderSize,
            SecondarySize = designer.SecondarySize,
            IconSize = designer.IconSize,
            HeaderWeight = designer.HeaderWeight,
            SecondaryWeight = designer.SecondaryWeight,
            MotionEnabled = designer.MotionEnabled,
            TransitionMilliseconds = designer.TransitionMilliseconds,
            AnimateValueChanges = designer.AnimateValueChanges,
            RespectReducedMotion = designer.RespectReducedMotion,
            PulseStatusIndicator = designer.PulseStatusIndicator
        };
        ProductionCard.ApplyPresentation(new WidgetModulePresentation
        {
            Size = Size switch
            {
                "Small" => WidgetModuleSize.Small,
                "Large" => WidgetModuleSize.Large,
                "Wide" => WidgetModuleSize.Wide,
                _ => WidgetModuleSize.Medium
            },
            Visualization = Visualization switch
            {
                "Value only" or "Number only" => WidgetModuleVisualization.Value,
                "Bar only" or "Bar" => WidgetModuleVisualization.Progress,
                "Value + bar" or "Dial" => WidgetModuleVisualization.ValueAndProgress,
                "Sparkline only" or "Sparkline" => WidgetModuleVisualization.Sparkline,
                _ => WidgetModuleVisualization.ValueAndSparkline
            },
            ShowLabel = ShowLabel,
            ShowSecondaryValue = ShowTemperature,
            ShowTrend = ShowSparkline,
            DecimalPlacesOverride = Precision switch
            {
                "Whole numbers" => 0,
                "1 decimal" => 1,
                "2 decimals" => 2,
                _ => null
            },
            Title = CustomTitle,
            Icon = CustomIcon,
            AccentColor = UseCustomAccent ? AccentHex : string.Empty,
            CardColor = UseCustomCardColor ? CardHex : string.Empty,
            BorderColor = UseCustomBorderColor ? BorderHex : string.Empty,
            PrimaryTextColor = UseCustomPrimaryTextColor ? PrimaryTextHex : string.Empty,
            SecondaryTextColor = UseCustomSecondaryTextColor ? SecondaryTextHex : string.Empty,
            TrackColor = UseCustomTrackColor ? TrackHex : string.Empty,
            ShowIcon = ShowIcon,
            ShowAccent = ShowAccent,
            CardOpacity = CardOpacity,
            BorderOpacity = BorderOpacity,
            CardCornerRadiusOverride = CardCornerRadius < 0 ? null : CardCornerRadius,
            CardBorderWidthOverride = CardBorderWidth < 0 ? null : CardBorderWidth,
            CardGapOverride = CardGap < 0 ? null : CardGap,
            CardPaddingOverride = CardPadding < 0
                ? null
                : Math.Min(CardPadding, maximumCardPadding),
            AccentWidthOverride = AccentWidth < 0 ? null : AccentWidth,
            ProgressHeightOverride = ProgressHeight < 0 ? null : ProgressHeight,
            ProgressCornerRadiusOverride = ProgressCornerRadius < 0 ? null : ProgressCornerRadius,
            SparklineThicknessOverride = SparklineThickness < 0 ? null : SparklineThickness,
            SparklineFillOpacityOverride = SparklineFillOpacity < 0 ? null : SparklineFillOpacity,
            LabelSizeOverride = LabelSize < 0 ? null : LabelSize,
            SecondarySizeOverride = SecondarySize < 0 ? null : SecondarySize,
            ValueSizeOverride = ValueSize < 0 ? null : ValueSize,
            IconSizeOverride = IconSize < 0 ? null : IconSize,
            LabelWeightOverride = LabelWeight < 0 ? null : LabelWeight,
            ValueWeightOverride = ValueWeight < 0 ? null : ValueWeight
        });
        ProductionCard.ApplyTheme(theme);
        SyncProductionValues();
    }

    private void SyncProductionValues()
    {
        ProductionCard.PrimaryValue = PreviewPrimaryValue;
        ProductionCard.Status = ShowTemperature ? PreviewSecondaryValue : string.Empty;
        ProductionCard.Progress = Math.Clamp(UsagePercent, 0, 100);
        ProductionCard.IsProgressAvailable = true;
        ProductionCard.IsVisible = IsVisible;
        ProductionCard.State = SensorState.Available;
    }

    private static string ProductionKey(string id) => id switch
    {
        "ram" => WidgetModuleCatalog.Memory,
        "net" => WidgetModuleCatalog.Network,
        "disk" => WidgetModuleCatalog.Storage,
        _ => id
    };

    private static SemanticAccent SemanticFor(string id) => id switch
    {
        "gpu" => SemanticAccent.Gpu,
        "ram" or "disk" or "battery" => SemanticAccent.Memory,
        "net" => SemanticAccent.Network,
        "latency" => SemanticAccent.Latency,
        "weather" => SemanticAccent.Weather,
        _ => SemanticAccent.Cpu
    };

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
        OnPropertyChanged(nameof(HasOverrides));
        OnPropertyChanged(nameof(OverrideSummary));
        return true;
    }

    private void SetColorOverride(
        ref string field,
        string? value,
        string propertyName,
        string swatchPropertyName)
    {
        var normalized = ColorText.Normalize(value, field);
        if (SetEditorProperty(ref field, normalized, propertyName))
        {
            OnPropertyChanged(swatchPropertyName);
        }
    }

    private void SetOverride(ref double field, double value, double minimum, double maximum)
    {
        var normalized = value < 0 ? -1 : Math.Clamp(value, minimum, maximum);
        _ = SetEditorProperty(ref field, normalized);
    }

    private void SetOverride(ref int field, int value, int minimum, int maximum)
    {
        var normalized = value < 0 ? -1 : Math.Clamp(value, minimum, maximum);
        _ = SetEditorProperty(ref field, normalized);
    }

    private int OverrideCount =>
        (UseCustomAccent ? 1 : 0) +
        (UseCustomCardColor ? 1 : 0) +
        (UseCustomBorderColor ? 1 : 0) +
        (UseCustomPrimaryTextColor ? 1 : 0) +
        (UseCustomSecondaryTextColor ? 1 : 0) +
        (UseCustomTrackColor ? 1 : 0) +
        (Math.Abs(CardOpacity - 1) > 0.0001 ? 1 : 0) +
        (Math.Abs(BorderOpacity - 1) > 0.0001 ? 1 : 0) +
        (CardCornerRadius >= 0 ? 1 : 0) +
        (CardBorderWidth >= 0 ? 1 : 0) +
        (CardGap >= 0 ? 1 : 0) +
        (CardPadding >= 0 ? 1 : 0) +
        (AccentWidth >= 0 ? 1 : 0) +
        (ProgressHeight >= 0 ? 1 : 0) +
        (ProgressCornerRadius >= 0 ? 1 : 0) +
        (SparklineThickness >= 0 ? 1 : 0) +
        (SparklineFillOpacity >= 0 ? 1 : 0) +
        (LabelSize >= 0 ? 1 : 0) +
        (SecondarySize >= 0 ? 1 : 0) +
        (ValueSize >= 0 ? 1 : 0) +
        (IconSize >= 0 ? 1 : 0) +
        (LabelWeight >= 0 ? 1 : 0) +
        (ValueWeight >= 0 ? 1 : 0);

    public void ResetVisualOverrides()
    {
        UseCustomAccent = false;
        UseCustomCardColor = false;
        UseCustomBorderColor = false;
        UseCustomPrimaryTextColor = false;
        UseCustomSecondaryTextColor = false;
        UseCustomTrackColor = false;
        CardOpacity = 1;
        BorderOpacity = 1;
        CardCornerRadius = -1;
        CardBorderWidth = -1;
        CardGap = -1;
        CardPadding = -1;
        AccentWidth = -1;
        ProgressHeight = -1;
        ProgressCornerRadius = -1;
        SparklineThickness = -1;
        SparklineFillOpacity = -1;
        LabelSize = -1;
        SecondarySize = -1;
        ValueSize = -1;
        IconSize = -1;
        LabelWeight = -1;
        ValueWeight = -1;
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

    private static SolidColorBrush FrozenBrush(string colorText, Color fallback)
    {
        var brush = new SolidColorBrush(ColorText.Parse(colorText, fallback));
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
            CardHex = CardHex,
            BorderHex = BorderHex,
            PrimaryTextHex = PrimaryTextHex,
            SecondaryTextHex = SecondaryTextHex,
            TrackHex = TrackHex,
            UseCustomCardColor = UseCustomCardColor,
            UseCustomBorderColor = UseCustomBorderColor,
            UseCustomPrimaryTextColor = UseCustomPrimaryTextColor,
            UseCustomSecondaryTextColor = UseCustomSecondaryTextColor,
            UseCustomTrackColor = UseCustomTrackColor,
            ShowIcon = ShowIcon,
            ShowAccent = ShowAccent,
            CardOpacity = CardOpacity,
            BorderOpacity = BorderOpacity,
            CardCornerRadius = CardCornerRadius,
            CardBorderWidth = CardBorderWidth,
            CardGap = CardGap,
            CardPadding = CardPadding,
            AccentWidth = AccentWidth,
            ProgressHeight = ProgressHeight,
            ProgressCornerRadius = ProgressCornerRadius,
            SparklineThickness = SparklineThickness,
            SparklineFillOpacity = SparklineFillOpacity,
            LabelSize = LabelSize,
            SecondarySize = SecondarySize,
            ValueSize = ValueSize,
            IconSize = IconSize,
            LabelWeight = LabelWeight,
            ValueWeight = ValueWeight,
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
    public string CardColor { get; init; } = string.Empty;
    public string BorderColor { get; init; } = string.Empty;
    public string PrimaryTextColor { get; init; } = string.Empty;
    public string SecondaryTextColor { get; init; } = string.Empty;
    public string TrackColor { get; init; } = string.Empty;
    public bool ShowIcon { get; init; } = true;
    public bool ShowAccent { get; init; } = true;
    public double CardOpacity { get; init; } = 1;
    public double BorderOpacity { get; init; } = 1;
    public double? CardCornerRadiusOverride { get; init; }
    public double? CardBorderWidthOverride { get; init; }
    public double? CardGapOverride { get; init; }
    public double? CardPaddingOverride { get; init; }
    public double? AccentWidthOverride { get; init; }
    public double? ProgressHeightOverride { get; init; }
    public double? ProgressCornerRadiusOverride { get; init; }
    public double? SparklineThicknessOverride { get; init; }
    public double? SparklineFillOpacityOverride { get; init; }
    public double? LabelSizeOverride { get; init; }
    public double? SecondarySizeOverride { get; init; }
    public double? ValueSizeOverride { get; init; }
    public double? IconSizeOverride { get; init; }
    public int? LabelWeightOverride { get; init; }
    public int? ValueWeightOverride { get; init; }
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
    public double ProgressCornerRadius { get; init; } = 2;
    public double SparklineThickness { get; init; } = 1.5;
    public double SparklineFillOpacity { get; init; } = 0.16;
    public bool HeaderVisible { get; init; } = true;
    public bool StatusIndicatorVisible { get; init; } = true;
    public bool SettingsButtonVisible { get; init; } = true;
    public double HeaderHeight { get; init; } = 36;
    public string FontFamily { get; init; } = "Segoe UI Variable";
    public double HeaderSize { get; init; } = 11;
    public double LabelSize { get; init; } = 11;
    public double SecondarySize { get; init; } = 10;
    public double ValueSize { get; init; } = 18;
    public double IconSize { get; init; } = 14;
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
    int SchemaVersion = 5,
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
