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
    private string _customCardColor = string.Empty;
    private string _customBorderColor = string.Empty;
    private string _customPrimaryTextColor = string.Empty;
    private string _customSecondaryTextColor = string.Empty;
    private string _customTrackColor = string.Empty;
    private bool _showIcon = true;
    private bool _showAccent = true;
    private double _cardOpacity = 1;
    private double _borderOpacity = 1;
    private double? _cardCornerRadiusOverride;
    private double? _cardBorderWidthOverride;
    private double? _cardGapOverride;
    private double? _cardPaddingOverride;
    private double? _accentWidthOverride;
    private double? _progressHeightOverride;
    private double? _progressCornerRadiusOverride;
    private double? _sparklineThicknessOverride;
    private double? _sparklineFillOpacityOverride;
    private double? _labelSizeOverride;
    private double? _secondarySizeOverride;
    private double? _valueSizeOverride;
    private double? _iconSizeOverride;
    private int? _labelWeightOverride;
    private int? _valueWeightOverride;
    private Brush _cardSurfaceBrush = Brushes.Black;
    private Brush _cardBorderBrush = Brushes.DimGray;
    private Brush _primaryTextBrush = Brushes.White;
    private Brush _secondaryTextBrush = Brushes.LightGray;
    private Brush _trackBrush = Brushes.DimGray;
    private CornerRadius _cardCornerRadius = new(12);
    private Thickness _cardBorderThickness = new(1);
    private Thickness _cardPadding = new(10);
    private Thickness _cardMargin = new(0, 0, 0, 6);
    private CornerRadius _compactCardCornerRadius = new(9);
    private Thickness _compactCardPadding = new(7, 3, 7, 3);
    private Thickness _compactCardMargin = new(0, 0, 0, 4);
    private CornerRadius _miniCardCornerRadius = new(6);
    private Thickness _miniCardPadding = new(4, 0, 4, 0);
    private Thickness _miniCardMargin = new(0, 0, 0, 1);
    private CornerRadius _dockCardCornerRadius = new(11);
    private Thickness _dockCardPadding = new(8, 6, 8, 6);
    private Thickness _dockCardMargin = new(0, 0, 5, 0);
    private double _accentWidth = 3;
    private double _progressHeight = 4;
    private double _compactProgressHeight = 3;
    private double _miniProgressHeight = 1;
    private CornerRadius _progressCornerRadius = new(2);
    private double _sparklineThickness = 1.5;
    private double _sparklineFillOpacity = 0.16;
    private double _labelFontSize = 11;
    private double _secondaryFontSize = 10;
    private double _valueFontSize = 18;
    private double _iconSize = 14;
    private FontWeight _labelFontWeight = FontWeights.SemiBold;
    private FontWeight _secondaryFontWeight = FontWeights.Medium;
    private FontWeight _valueFontWeight = FontWeights.SemiBold;
    private double _compactLabelFontSize = 10;
    private double _compactSecondaryFontSize = 9.5;
    private double _compactValueFontSize = 14;
    private double _compactIconSize = 9;
    private double _compactSparklineThickness = 0.8;
    private double _miniLabelFontSize = 9;
    private double _miniSecondaryFontSize = 8.5;
    private double _miniValueFontSize = 12;
    private double _miniIconSize = 8;
    private double _miniSparklineThickness = 0.7;

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
            ? "WEATHER"
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
    public string CustomCardColor => _customCardColor;
    public string CustomBorderColor => _customBorderColor;
    public string CustomPrimaryTextColor => _customPrimaryTextColor;
    public string CustomSecondaryTextColor => _customSecondaryTextColor;
    public string CustomTrackColor => _customTrackColor;

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
                OnPropertyChanged(nameof(CompactValueMargin));
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
    public Thickness CompactValueMargin => new(CompactIconColumnWidth, 0, 0, 0);
    public double CardOpacity => _cardOpacity;
    public double BorderOpacity => _borderOpacity;
    public double? CardCornerRadiusOverride => _cardCornerRadiusOverride;
    public double? CardBorderWidthOverride => _cardBorderWidthOverride;
    public double? CardGapOverride => _cardGapOverride;
    public double? CardPaddingOverride => _cardPaddingOverride;
    public double? AccentWidthOverride => _accentWidthOverride;
    public double? ProgressHeightOverride => _progressHeightOverride;
    public double? ProgressCornerRadiusOverride => _progressCornerRadiusOverride;
    public double? SparklineThicknessOverride => _sparklineThicknessOverride;
    public double? SparklineFillOpacityOverride => _sparklineFillOpacityOverride;
    public double? LabelSizeOverride => _labelSizeOverride;
    public double? SecondarySizeOverride => _secondarySizeOverride;
    public double? ValueSizeOverride => _valueSizeOverride;
    public double? IconSizeOverride => _iconSizeOverride;
    public int? LabelWeightOverride => _labelWeightOverride;
    public int? ValueWeightOverride => _valueWeightOverride;
    public Brush CardSurfaceBrush { get => _cardSurfaceBrush; private set => SetProperty(ref _cardSurfaceBrush, value); }
    public Brush CardBorderBrush { get => _cardBorderBrush; private set => SetProperty(ref _cardBorderBrush, value); }
    public Brush PrimaryTextBrush { get => _primaryTextBrush; private set => SetProperty(ref _primaryTextBrush, value); }
    public Brush SecondaryTextBrush { get => _secondaryTextBrush; private set => SetProperty(ref _secondaryTextBrush, value); }
    public Brush TrackBrush { get => _trackBrush; private set => SetProperty(ref _trackBrush, value); }
    public CornerRadius CardCornerRadius { get => _cardCornerRadius; private set => SetProperty(ref _cardCornerRadius, value); }
    public Thickness CardBorderThickness { get => _cardBorderThickness; private set => SetProperty(ref _cardBorderThickness, value); }
    public Thickness CardPadding { get => _cardPadding; private set => SetProperty(ref _cardPadding, value); }
    public Thickness CardMargin { get => _cardMargin; private set => SetProperty(ref _cardMargin, value); }
    public CornerRadius CompactCardCornerRadius { get => _compactCardCornerRadius; private set => SetProperty(ref _compactCardCornerRadius, value); }
    public Thickness CompactCardPadding { get => _compactCardPadding; private set => SetProperty(ref _compactCardPadding, value); }
    public Thickness CompactCardMargin { get => _compactCardMargin; private set => SetProperty(ref _compactCardMargin, value); }
    public CornerRadius MiniCardCornerRadius { get => _miniCardCornerRadius; private set => SetProperty(ref _miniCardCornerRadius, value); }
    public Thickness MiniCardPadding { get => _miniCardPadding; private set => SetProperty(ref _miniCardPadding, value); }
    public Thickness MiniCardMargin { get => _miniCardMargin; private set => SetProperty(ref _miniCardMargin, value); }
    public CornerRadius DockCardCornerRadius { get => _dockCardCornerRadius; private set => SetProperty(ref _dockCardCornerRadius, value); }
    public Thickness DockCardPadding { get => _dockCardPadding; private set => SetProperty(ref _dockCardPadding, value); }
    public Thickness DockCardMargin { get => _dockCardMargin; private set => SetProperty(ref _dockCardMargin, value); }
    public double AccentWidth { get => _accentWidth; private set => SetProperty(ref _accentWidth, value); }
    public double ProgressHeight { get => _progressHeight; private set => SetProperty(ref _progressHeight, value); }
    public double CompactProgressHeight { get => _compactProgressHeight; private set => SetProperty(ref _compactProgressHeight, value); }
    public double MiniProgressHeight { get => _miniProgressHeight; private set => SetProperty(ref _miniProgressHeight, value); }
    public CornerRadius ProgressCornerRadius { get => _progressCornerRadius; private set => SetProperty(ref _progressCornerRadius, value); }
    public double SparklineThickness { get => _sparklineThickness; private set => SetProperty(ref _sparklineThickness, value); }
    public double SparklineFillOpacity { get => _sparklineFillOpacity; private set => SetProperty(ref _sparklineFillOpacity, value); }
    public double LabelFontSize { get => _labelFontSize; private set => SetProperty(ref _labelFontSize, value); }
    public double SecondaryFontSize { get => _secondaryFontSize; private set => SetProperty(ref _secondaryFontSize, value); }
    public double ValueFontSize { get => _valueFontSize; private set => SetProperty(ref _valueFontSize, value); }
    public double IconSize { get => _iconSize; private set => SetProperty(ref _iconSize, value); }
    public FontWeight LabelFontWeight { get => _labelFontWeight; private set => SetProperty(ref _labelFontWeight, value); }
    public FontWeight SecondaryFontWeight { get => _secondaryFontWeight; private set => SetProperty(ref _secondaryFontWeight, value); }
    public FontWeight ValueFontWeight { get => _valueFontWeight; private set => SetProperty(ref _valueFontWeight, value); }
    public double CompactLabelFontSize { get => _compactLabelFontSize; private set => SetProperty(ref _compactLabelFontSize, value); }
    public double CompactSecondaryFontSize { get => _compactSecondaryFontSize; private set => SetProperty(ref _compactSecondaryFontSize, value); }
    public double CompactValueFontSize { get => _compactValueFontSize; private set => SetProperty(ref _compactValueFontSize, value); }
    public double CompactIconSize { get => _compactIconSize; private set => SetProperty(ref _compactIconSize, value); }
    public double CompactSparklineThickness { get => _compactSparklineThickness; private set => SetProperty(ref _compactSparklineThickness, value); }
    public double MiniLabelFontSize { get => _miniLabelFontSize; private set => SetProperty(ref _miniLabelFontSize, value); }
    public double MiniSecondaryFontSize { get => _miniSecondaryFontSize; private set => SetProperty(ref _miniSecondaryFontSize, value); }
    public double MiniValueFontSize { get => _miniValueFontSize; private set => SetProperty(ref _miniValueFontSize, value); }
    public double MiniIconSize { get => _miniIconSize; private set => SetProperty(ref _miniIconSize, value); }
    public double MiniSparklineThickness { get => _miniSparklineThickness; private set => SetProperty(ref _miniSparklineThickness, value); }

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
            WidgetModuleVisualization.Gauge or
            WidgetModuleVisualization.ValueAndProgress);

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
        var cardColor = ParseColor(_customCardColor, theme.Card);
        var borderColor = ParseColor(_customBorderColor, theme.Border);
        var primaryTextColor = ParseColor(_customPrimaryTextColor, theme.TextPrimary);
        var secondaryTextColor = ParseColor(_customSecondaryTextColor, theme.TextSecondary);
        var trackColor = ParseColor(_customTrackColor, theme.Track);

        _warningColor = theme.Warning;
        _criticalColor = theme.Critical;
        AccentBrush = CreateBrush(color);
        SparklineFillOpacity = Math.Clamp(
            _sparklineFillOpacityOverride ?? theme.SparklineFillOpacity,
            0,
            0.5);
        HistoryFillBrush = CreateBrush(WithOpacity(color, SparklineFillOpacity));
        CardSurfaceBrush = CreateBrush(WithOpacity(cardColor, theme.CardOpacity * _cardOpacity));
        CardBorderBrush = CreateBrush(WithOpacity(borderColor, _borderOpacity));
        PrimaryTextBrush = CreateBrush(primaryTextColor);
        SecondaryTextBrush = CreateBrush(secondaryTextColor);
        TrackBrush = CreateBrush(trackColor);
        double cardCornerRadius = Math.Clamp(
            _cardCornerRadiusOverride ?? theme.CardCornerRadius, 0, 40);
        CardCornerRadius = new CornerRadius(cardCornerRadius);
        CardBorderThickness = new Thickness(Math.Clamp(
            _cardBorderWidthOverride ?? theme.CardBorderWidth, 0, 4));
        double cardPadding = Math.Clamp(_cardPaddingOverride ?? theme.CardPadding, 0, 28);
        double cardGap = Math.Clamp(_cardGapOverride ?? theme.CardGap, 0, 20);
        CardPadding = new Thickness(cardPadding);
        CardMargin = new Thickness(0, 0, 0, cardGap);
        CompactCardCornerRadius = new CornerRadius(Math.Clamp(cardCornerRadius * 0.75, 0, 14));
        CompactCardPadding = new Thickness(
            Math.Clamp(cardPadding * 0.7, 0, 8),
            Math.Clamp(cardPadding * 0.3, 0, 4),
            Math.Clamp(cardPadding * 0.7, 0, 8),
            Math.Clamp(cardPadding * 0.3, 0, 4));
        CompactCardMargin = new Thickness(0, 0, 0, Math.Clamp(cardGap, 0, 6));
        MiniCardCornerRadius = new CornerRadius(Math.Clamp(cardCornerRadius * 0.5, 0, 8));
        MiniCardPadding = new Thickness(
            Math.Clamp(cardPadding * 0.4, 0, 5),
            0,
            Math.Clamp(cardPadding * 0.4, 0, 5),
            0);
        MiniCardMargin = new Thickness(0, 0, 0, Math.Clamp(cardGap * 0.2, 0, 2));
        DockCardCornerRadius = new CornerRadius(Math.Clamp(cardCornerRadius * 0.9, 0, 14));
        DockCardPadding = new Thickness(
            Math.Clamp(cardPadding * 0.8, 0, 10),
            Math.Clamp(cardPadding * 0.6, 0, 8),
            Math.Clamp(cardPadding * 0.8, 0, 10),
            Math.Clamp(cardPadding * 0.6, 0, 8));
        DockCardMargin = new Thickness(0, 0, Math.Clamp(cardGap, 0, 10), 0);
        AccentWidth = Math.Clamp(_accentWidthOverride ?? theme.AccentWidth, 0, 10);
        OnPropertyChanged(nameof(EffectiveAccentWidth));
        ProgressHeight = Math.Clamp(_progressHeightOverride ?? theme.ProgressHeight, 1, 12);
        CompactProgressHeight = Math.Clamp(ProgressHeight * 0.75, 1, 4);
        MiniProgressHeight = Math.Clamp(ProgressHeight * 0.35, 1, 2);
        ProgressCornerRadius = new CornerRadius(Math.Clamp(
            _progressCornerRadiusOverride ?? theme.ProgressCornerRadius, 0, 6));
        SparklineThickness = Math.Clamp(
            _sparklineThicknessOverride ?? theme.SparklineThickness, 0.5, 5);
        double readableMinimum = Math.Clamp(theme.MinimumReadableSize, 8, 18);
        LabelFontSize = Math.Max(
            readableMinimum,
            Math.Clamp(_labelSizeOverride ?? theme.LabelSize, 8, 26));
        SecondaryFontSize = Math.Max(
            readableMinimum,
            Math.Clamp(_secondarySizeOverride ?? theme.SecondarySize, 8, 24));
        ValueFontSize = Math.Max(
            readableMinimum + 2,
            Math.Clamp(_valueSizeOverride ?? theme.ValueSize, 10, 42));
        IconSize = Math.Max(
            readableMinimum,
            Math.Clamp(_iconSizeOverride ?? theme.IconSize, 8, 32));
        LabelFontWeight = FontWeight.FromOpenTypeWeight(Math.Clamp(
            _labelWeightOverride ?? theme.LabelWeight, 100, 900));
        SecondaryFontWeight = FontWeight.FromOpenTypeWeight(Math.Clamp(
            theme.SecondaryWeight, 100, 900));
        ValueFontWeight = FontWeight.FromOpenTypeWeight(Math.Clamp(
            _valueWeightOverride ?? theme.ValueWeight, 100, 900));
        CompactLabelFontSize = Math.Clamp(LabelFontSize * 0.92, 9, 13);
        CompactSecondaryFontSize = Math.Clamp(SecondaryFontSize * 0.96, 9.5, 12);
        CompactValueFontSize = Math.Clamp(ValueFontSize * 0.86, 11, 18);
        CompactIconSize = Math.Clamp(IconSize * 0.76, 8.5, 11);
        CompactSparklineThickness = Math.Clamp(SparklineThickness * 0.58, 0.6, 1.5);
        MiniLabelFontSize = Math.Clamp(LabelFontSize * 0.9, 9, 11);
        MiniSecondaryFontSize = Math.Clamp(SecondaryFontSize, 10, 11.5);
        MiniValueFontSize = Math.Clamp(ValueFontSize * 0.58, 10, 10.5);
        MiniIconSize = Math.Clamp(IconSize * 0.7, 8.5, 10.5);
        MiniSparklineThickness = Math.Clamp(SparklineThickness * 0.48, 0.5, 1);
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
        _customCardColor = presentation.CardColor ?? string.Empty;
        _customBorderColor = presentation.BorderColor ?? string.Empty;
        _customPrimaryTextColor = presentation.PrimaryTextColor ?? string.Empty;
        _customSecondaryTextColor = presentation.SecondaryTextColor ?? string.Empty;
        _customTrackColor = presentation.TrackColor ?? string.Empty;
        OnPropertyChanged(nameof(CustomCardColor));
        OnPropertyChanged(nameof(CustomBorderColor));
        OnPropertyChanged(nameof(CustomPrimaryTextColor));
        OnPropertyChanged(nameof(CustomSecondaryTextColor));
        OnPropertyChanged(nameof(CustomTrackColor));
        ShowIcon = presentation.ShowIcon;
        ShowAccent = presentation.ShowAccent;
        _cardOpacity = Math.Clamp(presentation.CardOpacity, 0.2, 1);
        _borderOpacity = Math.Clamp(presentation.BorderOpacity, 0, 1);
        _cardCornerRadiusOverride = NormalizeOverride(presentation.CardCornerRadiusOverride, 0, 40);
        _cardBorderWidthOverride = NormalizeOverride(presentation.CardBorderWidthOverride, 0, 4);
        _cardGapOverride = NormalizeOverride(presentation.CardGapOverride, 0, 20);
        _cardPaddingOverride = NormalizeOverride(presentation.CardPaddingOverride, 0, 28);
        _accentWidthOverride = NormalizeOverride(presentation.AccentWidthOverride, 0, 10);
        _progressHeightOverride = NormalizeOverride(presentation.ProgressHeightOverride, 1, 12);
        _progressCornerRadiusOverride = NormalizeOverride(presentation.ProgressCornerRadiusOverride, 0, 6);
        _sparklineThicknessOverride = NormalizeOverride(presentation.SparklineThicknessOverride, 0.5, 5);
        _sparklineFillOpacityOverride = NormalizeOverride(presentation.SparklineFillOpacityOverride, 0, 0.5);
        _labelSizeOverride = NormalizeOverride(presentation.LabelSizeOverride, 8, 26);
        _secondarySizeOverride = NormalizeOverride(presentation.SecondarySizeOverride, 8, 24);
        _valueSizeOverride = NormalizeOverride(presentation.ValueSizeOverride, 10, 42);
        _iconSizeOverride = NormalizeOverride(presentation.IconSizeOverride, 8, 32);
        _labelWeightOverride = NormalizeOverride(presentation.LabelWeightOverride, 100, 900);
        _valueWeightOverride = NormalizeOverride(presentation.ValueWeightOverride, 100, 900);
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

    private static int? NormalizeOverride(int? value, int minimum, int maximum) =>
        value is { } candidate
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
