using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using OpsMonitor.Widget.Infrastructure;
using OpsMonitor.Widget.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace OpsMonitor.Widget.ViewModels;

public sealed class MetricCardViewModel : ObservableObject
{
    private const int MaximumHistorySamples = 60;
    private readonly Queue<double> _history = new(MaximumHistorySamples);
    private readonly SemanticAccent _semanticAccent;
    private WidgetModuleSize _size = WidgetModuleSize.Small;
    private WidgetModuleVisualization _visualization =
        WidgetModuleVisualization.ValueAndSparkline;
    private bool _showLabel = true;
    private bool _showSecondaryValue = true;
    private bool _showTrend = true;
    private int? _decimalPlacesOverride;
    private string _primaryValue = "—";
    private string _status = "Waiting for data";
    private double _progress;
    private bool _isProgressAvailable;
    private bool _isVisible = true;
    private SensorState _state = SensorState.Stale;
    private double _visualOpacity = 1;
    private Brush _accentBrush = Brushes.DeepSkyBlue;
    private Brush _historyFillBrush = Brushes.Transparent;
    private Brush _stateBrush = Brushes.Gray;
    private Color _warningColor = Color.FromRgb(255, 190, 80);
    private Color _criticalColor = Color.FromRgb(255, 86, 110);
    private Geometry _historyGeometry = Geometry.Empty;
    private Geometry _historyAreaGeometry = Geometry.Empty;
    private ThemeDefinition? _theme;
    private string _title;
    private string _icon = string.Empty;
    private string _customAccentColor = string.Empty;
    private bool _showIcon = true;
    private bool _showAccent = true;
    private double _cardOpacity = 1;
    private double _borderOpacity = 1;
    private double? _cardCornerRadiusOverride;
    private double? _cardPaddingOverride;
    private double? _accentWidthOverride;
    private double? _progressHeightOverride;
    private double? _labelSizeOverride;
    private double? _valueSizeOverride;
    private double? _iconSizeOverride;
    private Brush _cardSurfaceBrush = Brushes.Black;
    private Brush _cardBorderBrush = Brushes.DimGray;
    private CornerRadius _cardCornerRadius = new(12);
    private Thickness _cardBorderThickness = new(1);
    private Thickness _cardPadding = new(10);
    private Thickness _cardMargin = new(0, 0, 0, 6);
    private double _accentWidth = 3;
    private double _progressHeight = 4;
    private double _sparklineThickness = 1.5;
    private double _labelFontSize = 11;
    private double _valueFontSize = 18;
    private double _iconSize = 14;

    public MetricCardViewModel(
        string key,
        string title,
        Geometry iconData,
        SemanticAccent semanticAccent)
    {
        Key = key;
        _title = title;
        IconData = iconData;
        _semanticAccent = semanticAccent;
    }

    public string Key { get; }

    public string Title
    {
        get => _title;
        private set
        {
            if (SetProperty(ref _title, value))
            {
                OnPropertyChanged(nameof(CompactTitle));
            }
        }
    }
    public string CompactTitle =>
        StringComparer.Ordinal.Equals(Key, WidgetModuleCatalog.Weather) &&
        Title.Equals("Weather", StringComparison.OrdinalIgnoreCase)
            ? "WX"
            : Title;

    public string Icon
    {
        get => _icon;
        private set
        {
            if (SetProperty(ref _icon, value))
            {
                OnPropertyChanged(nameof(ShowDefaultIcon));
                OnPropertyChanged(nameof(ShowCustomIcon));
            }
        }
    }

    public string CustomAccentColor => _customAccentColor;

    public Geometry IconData { get; }

    public bool ShowIcon
    {
        get => _showIcon;
        private set
        {
            if (SetProperty(ref _showIcon, value))
            {
                OnPropertyChanged(nameof(ShowDefaultIcon));
                OnPropertyChanged(nameof(ShowCustomIcon));
                OnPropertyChanged(nameof(CompactIconColumnWidth));
            }
        }
    }
    public bool ShowDefaultIcon => ShowIcon && string.IsNullOrWhiteSpace(Icon);
    public bool ShowCustomIcon => ShowIcon && !string.IsNullOrWhiteSpace(Icon);
    public bool ShowAccent
    {
        get => _showAccent;
        private set
        {
            if (SetProperty(ref _showAccent, value))
            {
                OnPropertyChanged(nameof(EffectiveAccentWidth));
            }
        }
    }
    public double EffectiveAccentWidth => ShowAccent ? AccentWidth : 0;
    public double CompactIconColumnWidth => ShowIcon ? 16 : 0;
    public double CardOpacity => _cardOpacity;
    public double BorderOpacity => _borderOpacity;
    public double? CardCornerRadiusOverride => _cardCornerRadiusOverride;
    public double? CardPaddingOverride => _cardPaddingOverride;
    public double? AccentWidthOverride => _accentWidthOverride;
    public double? ProgressHeightOverride => _progressHeightOverride;
    public double? LabelSizeOverride => _labelSizeOverride;
    public double? ValueSizeOverride => _valueSizeOverride;
    public double? IconSizeOverride => _iconSizeOverride;
    public Brush CardSurfaceBrush { get => _cardSurfaceBrush; private set => SetProperty(ref _cardSurfaceBrush, value); }
    public Brush CardBorderBrush { get => _cardBorderBrush; private set => SetProperty(ref _cardBorderBrush, value); }
    public CornerRadius CardCornerRadius { get => _cardCornerRadius; private set => SetProperty(ref _cardCornerRadius, value); }
    public Thickness CardBorderThickness { get => _cardBorderThickness; private set => SetProperty(ref _cardBorderThickness, value); }
    public Thickness CardPadding { get => _cardPadding; private set => SetProperty(ref _cardPadding, value); }
    public Thickness CardMargin { get => _cardMargin; private set => SetProperty(ref _cardMargin, value); }
    public double AccentWidth { get => _accentWidth; private set => SetProperty(ref _accentWidth, value); }
    public double ProgressHeight { get => _progressHeight; private set => SetProperty(ref _progressHeight, value); }
    public double SparklineThickness { get => _sparklineThickness; private set => SetProperty(ref _sparklineThickness, value); }
    public double LabelFontSize { get => _labelFontSize; private set => SetProperty(ref _labelFontSize, value); }
    public double ValueFontSize { get => _valueFontSize; private set => SetProperty(ref _valueFontSize, value); }
    public double IconSize { get => _iconSize; private set => SetProperty(ref _iconSize, value); }

    public ObservableCollection<MetricDetailViewModel> Details { get; } = [];

    public WidgetModuleSize Size
    {
        get => _size;
        private set => SetProperty(ref _size, value);
    }

    public WidgetModuleVisualization Visualization
    {
        get => _visualization;
        private set
        {
            if (!SetProperty(ref _visualization, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ShowValue));
            OnPropertyChanged(nameof(ShowProgress));
            OnPropertyChanged(nameof(ShowSparkline));
        }
    }

    public bool ShowLabel
    {
        get => _showLabel;
        private set => SetProperty(ref _showLabel, value);
    }

    public bool ShowSecondaryValue
    {
        get => _showSecondaryValue;
        private set
        {
            if (SetProperty(ref _showSecondaryValue, value))
            {
                OnPropertyChanged(nameof(ShowDetails));
            }
        }
    }

    public bool ShowDetails => ShowSecondaryValue;

    public bool ShowTrend
    {
        get => _showTrend;
        private set
        {
            if (SetProperty(ref _showTrend, value))
            {
                OnPropertyChanged(nameof(ShowSparkline));
            }
        }
    }

    public bool ShowValue =>
        Visualization is not WidgetModuleVisualization.Progress and
        not WidgetModuleVisualization.Sparkline;

    public bool ShowProgress =>
        IsProgressAvailable &&
        (Visualization is WidgetModuleVisualization.Progress or
            WidgetModuleVisualization.Gauge);

    public bool ShowSparkline =>
        Visualization == WidgetModuleVisualization.Sparkline ||
        (ShowTrend &&
         Visualization == WidgetModuleVisualization.ValueAndSparkline);

    public int? DecimalPlacesOverride
    {
        get => _decimalPlacesOverride;
        private set => SetProperty(ref _decimalPlacesOverride, value);
    }

    public string PrimaryValue
    {
        get => _primaryValue;
        set
        {
            if (SetProperty(ref _primaryValue, value))
            {
                OnPropertyChanged(nameof(CompactPrimaryValue));
            }
        }
    }

    public string CompactPrimaryValue =>
        Key.Equals(WidgetModuleCatalog.Network, StringComparison.Ordinal)
            ? PrimaryValue.Replace("/s", string.Empty, StringComparison.Ordinal)
            : PrimaryValue;

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, Math.Clamp(value, 0, 100));
    }

    public bool IsProgressAvailable
    {
        get => _isProgressAvailable;
        set
        {
            if (SetProperty(ref _isProgressAvailable, value))
            {
                OnPropertyChanged(nameof(ShowProgress));
            }
        }
    }

    internal int HistorySampleCount => _history.Count;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public SensorState State
    {
        get => _state;
        set
        {
            if (!SetProperty(ref _state, value))
            {
                return;
            }

            VisualOpacity = 1;
            UpdateStateBrush();
        }
    }

    public double VisualOpacity
    {
        get => _visualOpacity;
        private set => SetProperty(ref _visualOpacity, value);
    }

    public Brush AccentBrush
    {
        get => _accentBrush;
        private set => SetProperty(ref _accentBrush, value);
    }

    public Brush HistoryFillBrush
    {
        get => _historyFillBrush;
        private set => SetProperty(ref _historyFillBrush, value);
    }

    public Brush StateBrush
    {
        get => _stateBrush;
        private set => SetProperty(ref _stateBrush, value);
    }

    public Geometry HistoryGeometry
    {
        get => _historyGeometry;
        private set => SetProperty(ref _historyGeometry, value);
    }

    public Geometry HistoryAreaGeometry
    {
        get => _historyAreaGeometry;
        private set => SetProperty(ref _historyAreaGeometry, value);
    }

    public void ConfigureDetails(params (string Label, bool NormalVisible)[] details)
    {
        Details.Clear();
        foreach (var detail in details)
        {
            Details.Add(new MetricDetailViewModel(detail.Label, detail.NormalVisible));
        }
    }

    public void SetDetailValues(params (string Value, bool Available)[] values)
    {
        var count = Math.Min(values.Length, Details.Count);
        for (var index = 0; index < count; index++)
        {
            Details[index].Value = values[index].Value;
            Details[index].IsAvailable = values[index].Available;
        }
    }

    public int AddDetail(string label, bool normalVisible = false)
    {
        Details.Add(new MetricDetailViewModel(label, normalVisible));
        return Details.Count - 1;
    }

    public void ApplyTheme(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
        var semanticColor = _semanticAccent switch
        {
            SemanticAccent.Gpu => theme.GpuAccent,
            SemanticAccent.Memory => theme.MemoryAccent,
            SemanticAccent.Network => theme.NetworkAccent,
            SemanticAccent.Latency => theme.LatencyAccent,
            SemanticAccent.Weather => theme.WeatherAccent,
            _ => theme.CpuAccent
        };
        var color = ParseColor(_customAccentColor, semanticColor);

        _warningColor = theme.Warning;
        _criticalColor = theme.Critical;
        AccentBrush = CreateBrush(color);
        HistoryFillBrush = CreateBrush(Color.FromArgb(42, color.R, color.G, color.B));
        CardSurfaceBrush = CreateBrush(WithOpacity(theme.Card, theme.CardOpacity * _cardOpacity));
        CardBorderBrush = CreateBrush(WithOpacity(theme.Border, _borderOpacity));
        CardCornerRadius = new CornerRadius(Math.Clamp(
            _cardCornerRadiusOverride ?? theme.CardCornerRadius, 0, 40));
        CardBorderThickness = new Thickness(Math.Clamp(theme.CardBorderWidth, 0, 4));
        CardPadding = new Thickness(Math.Clamp(_cardPaddingOverride ?? theme.CardPadding, 0, 28));
        CardMargin = new Thickness(0, 0, 0, Math.Clamp(theme.CardGap, 0, 20));
        AccentWidth = Math.Clamp(_accentWidthOverride ?? theme.AccentWidth, 0, 10);
        OnPropertyChanged(nameof(EffectiveAccentWidth));
        ProgressHeight = Math.Clamp(_progressHeightOverride ?? theme.ProgressHeight, 1, 12);
        SparklineThickness = Math.Clamp(theme.SparklineThickness, 0.5, 5);
        LabelFontSize = Math.Clamp(_labelSizeOverride ?? theme.LabelSize, 8, 26);
        ValueFontSize = Math.Clamp(_valueSizeOverride ?? theme.ValueSize, 10, 42);
        IconSize = Math.Clamp(_iconSizeOverride ?? 14, 8, 32);
        UpdateStateBrush();
    }

    public void ApplyPresentation(WidgetModulePresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        Size = presentation.Size;
        Visualization = presentation.Visualization;
        ShowLabel = presentation.ShowLabel;
        ShowSecondaryValue = presentation.ShowSecondaryValue;
        ShowTrend = presentation.ShowTrend;
        DecimalPlacesOverride = presentation.DecimalPlacesOverride;
        Title = string.IsNullOrWhiteSpace(presentation.Title) ? Title : presentation.Title.Trim();
        Icon = presentation.Icon ?? string.Empty;
        _customAccentColor = presentation.AccentColor ?? string.Empty;
        OnPropertyChanged(nameof(CustomAccentColor));
        ShowIcon = presentation.ShowIcon;
        ShowAccent = presentation.ShowAccent;
        _cardOpacity = Math.Clamp(presentation.CardOpacity, 0.2, 1);
        _borderOpacity = Math.Clamp(presentation.BorderOpacity, 0, 1);
        _cardCornerRadiusOverride = NormalizeOverride(presentation.CardCornerRadiusOverride, 0, 40);
        _cardPaddingOverride = NormalizeOverride(presentation.CardPaddingOverride, 0, 28);
        _accentWidthOverride = NormalizeOverride(presentation.AccentWidthOverride, 0, 10);
        _progressHeightOverride = NormalizeOverride(presentation.ProgressHeightOverride, 1, 12);
        _labelSizeOverride = NormalizeOverride(presentation.LabelSizeOverride, 8, 26);
        _valueSizeOverride = NormalizeOverride(presentation.ValueSizeOverride, 10, 42);
        _iconSizeOverride = NormalizeOverride(presentation.IconSizeOverride, 8, 32);
        if (_theme is not null)
        {
            ApplyTheme(_theme);
        }
    }

    public void PushSample(double? value, double suggestedCeiling = 100)
    {
        if (value is not { } sample || !double.IsFinite(sample))
        {
            return;
        }

        if (_history.Count == MaximumHistorySamples)
        {
            _history.Dequeue();
        }

        _history.Enqueue(Math.Max(0, sample));
        BuildHistoryGeometry(suggestedCeiling);
    }

    private void UpdateStateBrush()
    {
        StateBrush = State switch
        {
            SensorState.Warning => CreateBrush(_warningColor),
            SensorState.Critical => CreateBrush(_criticalColor),
            SensorState.Stale => CreateBrush(Color.FromRgb(148, 161, 181)),
            SensorState.Unavailable => CreateBrush(Color.FromRgb(105, 116, 135)),
            _ => AccentBrush
        };
    }

    private static double? NormalizeOverride(double? value, double minimum, double maximum) =>
        value is { } candidate && double.IsFinite(candidate)
            ? Math.Clamp(candidate, minimum, maximum)
            : null;

    private static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            return System.Windows.Media.ColorConverter.ConvertFromString(value) is Color color ? color : fallback;
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException)
        {
            return fallback;
        }
    }

    private static Color WithOpacity(Color color, double opacity) =>
        Color.FromArgb(
            (byte)Math.Round(Math.Clamp(opacity * color.A / 255d, 0, 1) * 255),
            color.R,
            color.G,
            color.B);

    private void BuildHistoryGeometry(double suggestedCeiling)
    {
        if (_history.Count < 2)
        {
            return;
        }

        var samples = _history.ToArray();
        var ceiling = Math.Max(suggestedCeiling, samples.Max() * 1.08);
        if (ceiling <= 0)
        {
            ceiling = 1;
        }

        const double width = 100;
        const double height = 28;
        var step = width / (samples.Length - 1);
        var points = new PointCollection(samples.Length);

        for (var index = 0; index < samples.Length; index++)
        {
            var x = index * step;
            var normalized = Math.Clamp(samples[index] / ceiling, 0, 1);
            var y = 1 + ((1 - normalized) * (height - 2));
            points.Add(new System.Windows.Point(x, y));
        }

        HistoryGeometry = BuildLine(points);
        HistoryAreaGeometry = BuildArea(points, height);
    }

    private static StreamGeometry BuildLine(PointCollection points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], false, false);
            context.PolyLineTo(points.Skip(1).ToArray(), true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry BuildArea(PointCollection points, double height)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new System.Windows.Point(points[0].X, height), true, true);
            context.LineTo(points[0], false, false);
            context.PolyLineTo(points.Skip(1).ToArray(), true, false);
            context.LineTo(new System.Windows.Point(points[^1].X, height), false, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
