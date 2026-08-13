using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using OpsMonitor.Core.Metrics;
using OpsMonitor.Widget.Infrastructure;
using OpsMonitor.Widget.Models;
using OpsMonitor.Widget.Services;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace OpsMonitor.Widget.ViewModels;

internal sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan TemperatureGapGrace = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ConnectivityGapGrace = TimeSpan.FromSeconds(20);
    private static readonly IReadOnlyList<ThemeDefinition> BuiltInThemes =
    [
        CreateBuiltInTheme(
            "Void",
            Color.FromRgb(8, 11, 18),
            Color.FromRgb(15, 21, 33),
            Color.FromRgb(54, 66, 88),
            Color.FromRgb(246, 249, 255),
            Color.FromRgb(184, 196, 214),
            Color.FromRgb(72, 220, 249),
            Color.FromRgb(255, 79, 216),
            Color.FromRgb(88, 230, 178),
            Color.FromRgb(72, 220, 249),
            Color.FromRgb(255, 195, 90),
            Color.FromRgb(255, 86, 110)),
        CreateBuiltInTheme(
            "Aurora",
            Color.FromRgb(14, 10, 27),
            Color.FromRgb(25, 19, 45),
            Color.FromRgb(76, 62, 111),
            Color.FromRgb(249, 246, 255),
            Color.FromRgb(200, 190, 222),
            Color.FromRgb(86, 226, 255),
            Color.FromRgb(255, 91, 215),
            Color.FromRgb(91, 241, 190),
            Color.FromRgb(86, 226, 255),
            Color.FromRgb(255, 184, 91),
            Color.FromRgb(255, 88, 116)),
        CreateBuiltInTheme(
            "Slate",
            Color.FromRgb(18, 24, 31),
            Color.FromRgb(27, 36, 47),
            Color.FromRgb(67, 83, 101),
            Color.FromRgb(244, 249, 252),
            Color.FromRgb(190, 203, 215),
            Color.FromRgb(72, 207, 234),
            Color.FromRgb(235, 99, 207),
            Color.FromRgb(86, 221, 167),
            Color.FromRgb(72, 207, 234),
            Color.FromRgb(244, 184, 89),
            Color.FromRgb(255, 86, 110)),
        CreateBuiltInTheme(
            "Ember",
            Color.FromRgb(22, 14, 15),
            Color.FromRgb(38, 23, 25),
            Color.FromRgb(93, 56, 60),
            Color.FromRgb(255, 248, 244),
            Color.FromRgb(220, 196, 191),
            Color.FromRgb(77, 215, 239),
            Color.FromRgb(255, 93, 179),
            Color.FromRgb(94, 225, 168),
            Color.FromRgb(77, 215, 239),
            Color.FromRgb(255, 172, 72),
            Color.FromRgb(255, 84, 105)),
        CreateBuiltInTheme(
            "Contrast",
            Color.FromRgb(2, 3, 5),
            Color.FromRgb(7, 9, 13),
            Color.FromRgb(148, 170, 198),
            Color.FromRgb(255, 255, 255),
            Color.FromRgb(214, 224, 238),
            Color.FromRgb(71, 229, 255),
            Color.FromRgb(255, 92, 225),
            Color.FromRgb(98, 244, 187),
            Color.FromRgb(71, 229, 255),
            Color.FromRgb(255, 204, 92),
            Color.FromRgb(255, 92, 116))
    ];

    private readonly ITelemetrySource _telemetrySource;
    private readonly WeatherService _weatherService;
    private IReadOnlyList<ThemeDefinition> _themes;
    private readonly Dictionary<string, MetricCardViewModel> _metricIndex;
    private readonly List<AdditionalMetricSlot> _additionalMetricSlots = [];
    private WidgetLayout _layout;
    private WidgetDensity _density;
    private WidgetInteractionMode _interactionMode;
    private string _themeName;
    private bool _topmost;
    private bool _draggable;
    private bool _resizable;
    private bool _showBattery;
    private bool _showWeather;
    private bool _startAtSignIn;
    private int _scalePercent;
    private double _updateCadenceSeconds;
    private double _surfaceOpacity;
    private double _contentOpacity;
    private bool _editHotkeyAvailable = true;
    private string? _coreThemeId;
    private bool _isSettingsOpen;
    private string _lastUpdatedText = "Connecting to telemetry…";
    private Brush _surfaceBrush = Brushes.Black;
    private Brush _cardBrush = Brushes.Black;
    private Brush _readabilityPlateBrush = Brushes.Transparent;
    private Brush _borderBrush = Brushes.DimGray;
    private Brush _textPrimaryBrush = Brushes.White;
    private Brush _textSecondaryBrush = Brushes.LightGray;
    private Brush _flyoutBrush = Brushes.Black;
    private Brush _trackBrush = Brushes.DimGray;
    private Brush _successBrush = Brushes.SpringGreen;
    private Brush _shellGlowBrush = Brushes.Transparent;
    private FontFamily _widgetFontFamily = new("Segoe UI Variable");
    private double _labelFontSize = 12;
    private double _valueFontSize = 18;
    private double _minimumReadableFontSize = 12;
    private FontWeight _labelFontWeight = FontWeights.SemiBold;
    private FontWeight _valueFontWeight = FontWeights.SemiBold;
    private bool _useTabularNumbers = true;
    private CornerRadius _shellCornerRadius = new(24);
    private Thickness _shellBorderThickness = new(1);
    private Thickness _shellContentPadding = new(10);
    private double _shellShadowThemeOpacity = 0.3;
    private double _shellGlowThemeOpacity = 0.12;
    private bool _headerVisible = true;
    private bool _statusIndicatorVisible = true;
    private bool _settingsButtonVisible = true;
    private readonly bool _reducedMotion;
    private double _headerHeight = 36;
    private double _headerFontSize = 11;
    private double _secondaryFontSize = 10;
    private FontWeight _headerFontWeight = FontWeights.SemiBold;
    private FontWeight _secondaryFontWeight = FontWeights.Normal;
    private bool _motionEnabled = true;
    private bool _pulseStatusIndicator = true;
    private bool _respectReducedMotion = true;
    private int _transitionMilliseconds = 160;
    private bool _disposed;
    private LastKnownValue? _lastCpuTemperature;
    private LastKnownValue? _lastGpuTemperature;
    private LastKnownValue? _lastPing;
    private LastKnownValue? _lastPacketLoss;
    private LastKnownValue? _lastJitter;

    public MainWindowViewModel(ITelemetrySource telemetrySource, WidgetSettings settings)
    {
        ArgumentNullException.ThrowIfNull(telemetrySource);
        ArgumentNullException.ThrowIfNull(settings);

        _telemetrySource = telemetrySource;
        _themes = BuildThemes(settings.RuntimeThemes);
        _layout = settings.Layout;
        _density = NormalizeDensity(settings.Layout, settings.Density);
        _interactionMode = settings.InteractionMode;
        var selectedTheme = _themes.FirstOrDefault(theme =>
                                !string.IsNullOrWhiteSpace(settings.CoreThemeId) &&
                                StringComparer.Ordinal.Equals(
                                    theme.CoreThemeId,
                                    settings.CoreThemeId))
                            ?? _themes.FirstOrDefault(theme =>
                                theme.Name.Equals(
                                    settings.Theme,
                                    StringComparison.OrdinalIgnoreCase))
                            ?? _themes[0];
        _themeName = selectedTheme.Name;
        _coreThemeId = selectedTheme.CoreThemeId;
        _topmost = settings.Topmost;
        _draggable = settings.Draggable;
        _resizable = settings.Resizable;
        _showBattery = settings.ShowBattery;
        _showWeather = settings.ShowWeather;
        _startAtSignIn = settings.StartAtSignIn;
        _reducedMotion = settings.ReducedMotion;
        _scalePercent = Math.Clamp(settings.ScalePercent, 80, 160);
        _updateCadenceSeconds = NormalizeUpdateCadence(settings.UpdateCadenceSeconds);
        _surfaceOpacity = Math.Clamp(settings.SurfaceOpacity, 0.08, 1);
        _contentOpacity = Math.Clamp(settings.ContentOpacity, 0.82, 1);
        WeatherLocation = new WeatherLocation(
            settings.WeatherLocationName,
            settings.WeatherCountry,
            settings.WeatherLatitude,
            settings.WeatherLongitude,
            settings.WeatherTimeZone,
            settings.WeatherArsoStationCode);
        WeatherRefreshMinutes = Math.Clamp(settings.WeatherRefreshMinutes, 5, 60);
        _weatherService = new WeatherService(
            WeatherLocation,
            TimeSpan.FromMinutes(WeatherRefreshMinutes));

        MetricCardViewModel[] metrics =
        [
            CreateCpuMetric(),
            CreateGpuMetric(),
            CreateMemoryMetric(),
            CreateNetworkMetric(),
            CreateLatencyMetric(),
            CreateStorageMetric(),
            CreateBatteryMetric(),
            CreateWeatherMetric()
        ];
        Metrics = new ObservableCollection<MetricCardViewModel>(metrics);
        _metricIndex = metrics.ToDictionary(metric => metric.Key, StringComparer.Ordinal);
        ConfigureAdditionalMetrics(settings.ModuleMetricBindings);
        foreach (var theme in _themes)
        {
            ThemeOptions.Add(theme.Name);
        }
        foreach (var metric in metrics)
        {
            metric.PropertyChanged += Metric_OnPropertyChanged;
        }

        ApplyTheme();
        ApplyModuleConfiguration(
            settings.ModuleOrder,
            settings.EnabledModules,
            settings.ModulePresentation);
        _telemetrySource.SetUpdateCadence(
            TimeSpan.FromSeconds(_updateCadenceSeconds));
        _telemetrySource.SnapshotAvailable += OnSnapshotAvailable;
        _weatherService.SnapshotAvailable += OnWeatherSnapshotAvailable;
    }

    public event EventHandler? TelemetryUpdated;

    public ObservableCollection<MetricCardViewModel> Metrics { get; }

    public ITelemetrySource TelemetrySource => _telemetrySource;

    public WeatherService WeatherService => _weatherService;

    public WeatherLocation WeatherLocation { get; private set; }

    public int WeatherRefreshMinutes { get; }

    public IReadOnlyList<WidgetLayout> LayoutOptions { get; } = Enum.GetValues<WidgetLayout>();

    public IReadOnlyList<WidgetDensity> DensityOptions { get; } = Enum.GetValues<WidgetDensity>();

    public ObservableCollection<string> ThemeOptions { get; } = [];

    public string SourceName => _telemetrySource.Name;

    public bool IsDemo => _telemetrySource.IsDemo;

    public string SourceBadge => IsDemo ? "DEMO DATA" : "LIVE";

    public string HotkeyHint => InteractionMode == WidgetInteractionMode.ClickThrough
        ? EditHotkeyAvailable
            ? "CTRL+ALT+O · EDIT"
            : "TRAY MENU · EDIT"
        : EditHotkeyAvailable
            ? "DRAG · CTRL+ALT+O"
            : "DRAG · TRAY MENU";

    public MetricCardViewModel BatteryMetric => _metricIndex["battery"];

    public MetricCardViewModel StorageMetric => _metricIndex["storage"];

    public MetricCardViewModel WeatherMetric => _metricIndex[WidgetModuleCatalog.Weather];

    public int VisibleModuleCount => Metrics.Count(metric => metric.IsVisible);

    public WidgetLayout Layout
    {
        get => _layout;
        set
        {
            if (SetProperty(ref _layout, value))
            {
                if (_layout == WidgetLayout.Dock &&
                    _density != WidgetDensity.Compact)
                {
                    _density = WidgetDensity.Compact;
                    OnPropertyChanged(nameof(Density));
                }

                OnPropertyChanged(nameof(BrandLabel));
                OnPropertyChanged(nameof(CanChangeDensity));
                RaiseLayoutAwareThemeProperties();
            }
        }
    }

    public string BrandLabel => Layout switch
    {
        WidgetLayout.Pill => "PERFORMANCE",
        WidgetLayout.Dock => "OPS DOCK",
        WidgetLayout.Mini => "OPS MINI",
        _ => "SYSTEM RAIL"
    };

    public WidgetDensity Density
    {
        get => _density;
        set
        {
            if (SetProperty(ref _density, NormalizeDensity(Layout, value)))
            {
                RaiseLayoutAwareThemeProperties();
            }
        }
    }

    public bool CanChangeDensity => Layout != WidgetLayout.Dock;

    public WidgetInteractionMode InteractionMode
    {
        get => _interactionMode;
        set
        {
            if (!SetProperty(ref _interactionMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ModeLabel));
            OnPropertyChanged(nameof(IsEditMode));
            OnPropertyChanged(nameof(IsLockedMode));
            OnPropertyChanged(nameof(IsClickThroughMode));
            OnPropertyChanged(nameof(CanDrag));
            OnPropertyChanged(nameof(CanResize));
            OnPropertyChanged(nameof(HotkeyHint));
        }
    }

    public string ModeLabel => InteractionMode switch
    {
        WidgetInteractionMode.ClickThrough => "CLICK-THROUGH",
        WidgetInteractionMode.Locked => "LOCKED",
        _ => "EDIT"
    };

    public bool IsEditMode => InteractionMode == WidgetInteractionMode.Edit;

    public bool IsLockedMode => InteractionMode == WidgetInteractionMode.Locked;

    public bool IsClickThroughMode =>
        InteractionMode == WidgetInteractionMode.ClickThrough;

    public bool CanDrag => IsEditMode && Draggable;

    public bool CanResize => IsEditMode && Resizable;

    public bool Topmost
    {
        get => _topmost;
        set => SetProperty(ref _topmost, value);
    }

    public bool Draggable
    {
        get => _draggable;
        set
        {
            if (SetProperty(ref _draggable, value))
            {
                OnPropertyChanged(nameof(CanDrag));
            }
        }
    }

    public bool Resizable
    {
        get => _resizable;
        set
        {
            if (SetProperty(ref _resizable, value))
            {
                OnPropertyChanged(nameof(CanResize));
            }
        }
    }

    public bool ShowBattery
    {
        get => _showBattery;
        set
        {
            if (!SetProperty(ref _showBattery, value))
            {
                return;
            }

            if (_metricIndex.TryGetValue("battery", out var battery))
            {
                battery.IsVisible = value;
            }
        }
    }

    public bool StartAtSignIn
    {
        get => _startAtSignIn;
        set => SetProperty(ref _startAtSignIn, value);
    }

    public int ScalePercent
    {
        get => _scalePercent;
        set
        {
            if (SetProperty(ref _scalePercent, Math.Clamp(value, 80, 160)))
            {
                OnPropertyChanged(nameof(ScaleFactor));
                OnPropertyChanged(nameof(ContentScaleFactor));
                OnPropertyChanged(nameof(IsReducedScale));
                RaiseLayoutAwareThemeProperties();
            }
        }
    }

    public double ScaleFactor => ScalePercent / 100d;

    public double ContentScaleFactor => Math.Max(1, ScaleFactor);

    public bool IsReducedScale => ScalePercent < 100;

    public double UpdateCadenceSeconds
    {
        get => _updateCadenceSeconds;
        set
        {
            var normalized = NormalizeUpdateCadence(value);
            if (SetProperty(ref _updateCadenceSeconds, normalized))
            {
                _telemetrySource.SetUpdateCadence(
                    TimeSpan.FromSeconds(normalized));
            }
        }
    }

    public double SurfaceOpacity
    {
        get => _surfaceOpacity;
        set
        {
            if (SetProperty(ref _surfaceOpacity, Math.Clamp(value, 0.08, 1)))
            {
                ApplyTheme();
                OnPropertyChanged(nameof(ShellShadowOpacity));
                OnPropertyChanged(nameof(ShellGlowOpacity));
            }
        }
    }

    public double ShellShadowOpacity =>
        Math.Clamp(SurfaceOpacity * _shellShadowThemeOpacity, 0, 0.8);

    public double ShellGlowOpacity =>
        Math.Clamp(SurfaceOpacity * _shellGlowThemeOpacity, 0, 0.5);

    public double ContentOpacity
    {
        get => _contentOpacity;
        set => SetProperty(ref _contentOpacity, Math.Clamp(value, 0.82, 1));
    }

    public bool EditHotkeyAvailable
    {
        get => _editHotkeyAvailable;
        set
        {
            if (SetProperty(ref _editHotkeyAvailable, value))
            {
                OnPropertyChanged(nameof(HotkeyHint));
            }
        }
    }

    public string? CoreThemeId => _coreThemeId;

    public string ThemeName
    {
        get => _themeName;
        set
        {
            var theme = _themes.FirstOrDefault(candidate =>
                candidate.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (theme is null)
            {
                return;
            }

            if (SetProperty(ref _themeName, value))
            {
                _coreThemeId = theme.CoreThemeId;
                OnPropertyChanged(nameof(CoreThemeId));
                ApplyTheme();
            }
        }
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
    }

    public bool ShowWeather
    {
        get => _showWeather;
        set
        {
            if (!SetProperty(ref _showWeather, value))
            {
                return;
            }

            if (_metricIndex.TryGetValue(WidgetModuleCatalog.Weather, out var weather))
            {
                weather.IsVisible = value;
            }
        }
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => SetProperty(ref _lastUpdatedText, value);
    }

    public Brush SurfaceBrush
    {
        get => _surfaceBrush;
        private set => SetProperty(ref _surfaceBrush, value);
    }

    public Brush CardBrush
    {
        get => _cardBrush;
        private set => SetProperty(ref _cardBrush, value);
    }

    public Brush ReadabilityPlateBrush
    {
        get => _readabilityPlateBrush;
        private set => SetProperty(ref _readabilityPlateBrush, value);
    }

    public Brush BorderBrush
    {
        get => _borderBrush;
        private set => SetProperty(ref _borderBrush, value);
    }

    public Brush TextPrimaryBrush
    {
        get => _textPrimaryBrush;
        private set => SetProperty(ref _textPrimaryBrush, value);
    }

    public Brush TextSecondaryBrush
    {
        get => _textSecondaryBrush;
        private set => SetProperty(ref _textSecondaryBrush, value);
    }

    public Brush FlyoutBrush
    {
        get => _flyoutBrush;
        private set => SetProperty(ref _flyoutBrush, value);
    }

    public Brush TrackBrush
    {
        get => _trackBrush;
        private set => SetProperty(ref _trackBrush, value);
    }

    public FontFamily WidgetFontFamily
    {
        get => _widgetFontFamily;
        private set => SetProperty(ref _widgetFontFamily, value);
    }

    public double LabelFontSize
    {
        get => _labelFontSize;
        private set => SetProperty(ref _labelFontSize, value);
    }

    public double ValueFontSize
    {
        get => _valueFontSize;
        private set => SetProperty(ref _valueFontSize, value);
    }

    public double MinimumReadableFontSize
    {
        get => _minimumReadableFontSize;
        private set => SetProperty(ref _minimumReadableFontSize, value);
    }

    public FontWeight LabelFontWeight
    {
        get => _labelFontWeight;
        private set => SetProperty(ref _labelFontWeight, value);
    }

    public FontWeight ValueFontWeight
    {
        get => _valueFontWeight;
        private set => SetProperty(ref _valueFontWeight, value);
    }

    public bool UseTabularNumbers
    {
        get => _useTabularNumbers;
        private set => SetProperty(ref _useTabularNumbers, value);
    }

    public void Start()
    {
        _telemetrySource.Start();
        _weatherService.Start();
    }

    public Brush SuccessBrush { get => _successBrush; private set => SetProperty(ref _successBrush, value); }
    public Brush ShellGlowBrush { get => _shellGlowBrush; private set => SetProperty(ref _shellGlowBrush, value); }

    public CornerRadius ShellCornerRadius { get => _shellCornerRadius; private set => SetProperty(ref _shellCornerRadius, value); }
    public Thickness ShellBorderThickness { get => _shellBorderThickness; private set => SetProperty(ref _shellBorderThickness, value); }
    public Thickness ShellContentPadding { get => _shellContentPadding; private set => SetProperty(ref _shellContentPadding, value); }
    public Thickness LayoutShellContentPadding
    {
        get
        {
            double padding = ShellContentPadding.Left;
            double effective = Layout switch
            {
                WidgetLayout.Dock or WidgetLayout.Mini => Math.Clamp(padding * 0.2, 0, 2),
                _ when Density == WidgetDensity.Compact => Math.Clamp(padding * 0.2, 0, 2),
                _ => padding
            };
            return new Thickness(effective);
        }
    }
    public bool HeaderVisible { get => _headerVisible; private set => SetProperty(ref _headerVisible, value); }
    public bool StatusIndicatorVisible { get => _statusIndicatorVisible; private set => SetProperty(ref _statusIndicatorVisible, value); }
    public bool SettingsButtonVisible { get => _settingsButtonVisible; private set => SetProperty(ref _settingsButtonVisible, value); }
    public double HeaderHeight { get => _headerHeight; private set => SetProperty(ref _headerHeight, value); }
    public double HeaderFontSize { get => _headerFontSize; private set => SetProperty(ref _headerFontSize, value); }
    public double SecondaryFontSize { get => _secondaryFontSize; private set => SetProperty(ref _secondaryFontSize, value); }
    public double LayoutHeaderHeight => Layout switch
    {
        WidgetLayout.Mini when IsReducedScale => Math.Clamp(HeaderHeight * 0.45, 17, 26),
        WidgetLayout.Mini => Math.Clamp(HeaderHeight * 0.62, 17, 34),
        WidgetLayout.Pill when Density == WidgetDensity.Compact && IsReducedScale =>
            Math.Clamp(HeaderHeight * 0.82, 26, 38),
        _ => HeaderHeight
    };
    public double LayoutHeaderFontSize => Layout switch
    {
        WidgetLayout.Mini => Math.Clamp(HeaderFontSize, 10, 14),
        WidgetLayout.Pill when Density == WidgetDensity.Compact => Math.Clamp(HeaderFontSize, 10, 16),
        _ => HeaderFontSize
    };
    public double LayoutSecondaryFontSize => Layout switch
    {
        WidgetLayout.Mini => Math.Clamp(SecondaryFontSize, 10, 13),
        WidgetLayout.Pill when Density == WidgetDensity.Compact => Math.Clamp(SecondaryFontSize, 10, 15),
        _ => SecondaryFontSize
    };
    public FontWeight HeaderFontWeight { get => _headerFontWeight; private set => SetProperty(ref _headerFontWeight, value); }
    public FontWeight SecondaryFontWeight { get => _secondaryFontWeight; private set => SetProperty(ref _secondaryFontWeight, value); }
    public bool MotionEnabled { get => _motionEnabled; private set => SetProperty(ref _motionEnabled, value); }
    public bool PulseStatusIndicator { get => _pulseStatusIndicator; private set => SetProperty(ref _pulseStatusIndicator, value); }
    public bool RespectReducedMotion { get => _respectReducedMotion; private set => SetProperty(ref _respectReducedMotion, value); }
    public int TransitionMilliseconds { get => _transitionMilliseconds; private set => SetProperty(ref _transitionMilliseconds, value); }

    public async Task SetWeatherLocationAsync(WeatherLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        WeatherLocation = location;
        OnPropertyChanged(nameof(WeatherLocation));
        await _weatherService.SetLocationAsync(location).ConfigureAwait(false);
    }

    public void ApplyThemeConfiguration(
        string themeName,
        string? coreThemeId,
        IReadOnlyList<WidgetRuntimeTheme>? runtimeThemes)
    {
        _themes = BuildThemes(runtimeThemes);
        ThemeOptions.Clear();
        foreach (var theme in _themes)
        {
            ThemeOptions.Add(theme.Name);
        }

        var selected = _themes.FirstOrDefault(theme =>
                           !string.IsNullOrWhiteSpace(coreThemeId) &&
                           StringComparer.Ordinal.Equals(
                               theme.CoreThemeId,
                               coreThemeId))
                       ?? _themes.FirstOrDefault(theme =>
                           theme.Name.Equals(
                               themeName,
                               StringComparison.OrdinalIgnoreCase))
                       ?? _themes[0];
        _coreThemeId = selected.CoreThemeId;
        OnPropertyChanged(nameof(CoreThemeId));
        if (!SetProperty(ref _themeName, selected.Name, nameof(ThemeName)))
        {
            ApplyTheme();
            return;
        }

        ApplyTheme();
    }

    public void ApplyModuleConfiguration(
        IEnumerable<string>? order,
        IEnumerable<string>? enabled,
        IReadOnlyDictionary<string, WidgetModulePresentation>? presentation = null)
    {
        var normalizedOrder = WidgetModuleCatalog.NormalizeOrder(order);
        for (var targetIndex = 0; targetIndex < normalizedOrder.Count; targetIndex++)
        {
            var metric = _metricIndex[normalizedOrder[targetIndex]];
            var currentIndex = Metrics.IndexOf(metric);
            if (currentIndex != targetIndex)
            {
                Metrics.Move(currentIndex, targetIndex);
            }
        }

        var enabledKeys = WidgetModuleCatalog.NormalizeEnabled(enabled)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var metric in Metrics)
        {
            metric.IsVisible = enabledKeys.Contains(metric.Key);
            if (presentation is not null &&
                presentation.TryGetValue(metric.Key, out var options))
            {
                metric.ApplyPresentation(options);
            }
        }

        _ = SetProperty(
            ref _showBattery,
            enabledKeys.Contains(WidgetModuleCatalog.Battery),
            nameof(ShowBattery));
        _ = SetProperty(
            ref _showWeather,
            enabledKeys.Contains(WidgetModuleCatalog.Weather),
            nameof(ShowWeather));
        OnPropertyChanged(nameof(VisibleModuleCount));
    }

    public IReadOnlyList<string> GetModuleOrder() =>
        Metrics.Select(metric => metric.Key).ToArray();

    public IReadOnlyList<string> GetEnabledModules() =>
        Metrics
            .Where(metric => metric.IsVisible)
            .Select(metric => metric.Key)
            .ToArray();

    public IReadOnlyDictionary<string, WidgetModulePresentation> GetModulePresentation() =>
        Metrics.ToDictionary(
            metric => metric.Key,
            metric => new WidgetModulePresentation
            {
                Size = metric.Size,
                Visualization = metric.Visualization,
                ShowLabel = metric.ShowLabel,
                ShowSecondaryValue = metric.ShowSecondaryValue,
                ShowTrend = metric.ShowTrend,
                Title = metric.Title,
                Icon = metric.Icon,
                AccentColor = metric.CustomAccentColor,
                ShowIcon = metric.ShowIcon,
                ShowAccent = metric.ShowAccent,
                CardOpacity = metric.CardOpacity,
                BorderOpacity = metric.BorderOpacity,
                CardCornerRadiusOverride = metric.CardCornerRadiusOverride,
                CardPaddingOverride = metric.CardPaddingOverride,
                AccentWidthOverride = metric.AccentWidthOverride,
                ProgressHeightOverride = metric.ProgressHeightOverride,
                LabelSizeOverride = metric.LabelSizeOverride,
                ValueSizeOverride = metric.ValueSizeOverride,
                IconSizeOverride = metric.IconSizeOverride,
                DecimalPlacesOverride = metric.DecimalPlacesOverride
            },
            StringComparer.Ordinal);

    private static double NormalizeUpdateCadence(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0.5, 10) : 1;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _telemetrySource.SnapshotAvailable -= OnSnapshotAvailable;
        _weatherService.SnapshotAvailable -= OnWeatherSnapshotAvailable;
        foreach (var metric in Metrics)
        {
            metric.PropertyChanged -= Metric_OnPropertyChanged;
        }

        _telemetrySource.Dispose();
        _weatherService.Dispose();
    }

    private void Metric_OnPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        _ = sender;
        if (eventArgs.PropertyName == nameof(MetricCardViewModel.IsVisible))
        {
            OnPropertyChanged(nameof(VisibleModuleCount));
        }
    }

    private static MetricCardViewModel CreateCpuMetric()
    {
        var metric = new MetricCardViewModel(
            "cpu",
            "CPU",
            ParseGeometry("M4,7 L4,17 L20,17 L20,7 Z M8,3 L8,7 M12,3 L12,7 M16,3 L16,7 M8,17 L8,21 M12,17 L12,21 M16,17 L16,21 M1,10 L4,10 M1,14 L4,14 M20,10 L23,10 M20,14 L23,14"),
            SemanticAccent.Cpu);
        metric.ConfigureDetails(("TEMP", true), ("CLOCK", true), ("POWER", false));
        return metric;
    }

    private static MetricCardViewModel CreateGpuMetric()
    {
        var metric = new MetricCardViewModel(
            "gpu",
            "GPU",
            ParseGeometry("M3,5 L19,5 L19,19 L3,19 Z M7,9 A5,5 0 1 0 17,9 A5,5 0 1 0 7,9 M19,9 L23,9 M19,14 L23,14"),
            SemanticAccent.Gpu);
        metric.ConfigureDetails(("TEMP", true), ("VRAM", true), ("CLOCK", false));
        return metric;
    }

    private static MetricCardViewModel CreateMemoryMetric()
    {
        var metric = new MetricCardViewModel(
            "memory",
            "RAM",
            ParseGeometry("M3,7 L21,7 L21,17 L3,17 Z M7,10 L7,14 M11,10 L11,14 M15,10 L15,14 M5,17 L5,21 M9,17 L9,21 M13,17 L13,21 M17,17 L17,21"),
            SemanticAccent.Memory);
        metric.ConfigureDetails(("COMMIT", true), ("CACHED", true), ("HEADROOM", false));
        return metric;
    }

    private static MetricCardViewModel CreateNetworkMetric()
    {
        var metric = new MetricCardViewModel(
            "network",
            "NET",
            ParseGeometry("M8,3 L8,20 M4,7 L8,3 L12,7 M17,21 L17,4 M13,17 L17,21 L21,17"),
            SemanticAccent.Network);
        metric.ConfigureDetails(("DOWNLOAD", true), ("UPLOAD", true));
        return metric;
    }

    private static MetricCardViewModel CreateLatencyMetric()
    {
        var metric = new MetricCardViewModel(
            "latency",
            "PING",
            ParseGeometry("M3,13 L7,9 L11,13 L15,7 L21,13 M4,18 L20,18"),
            SemanticAccent.Latency);
        metric.ConfigureDetails(("LOSS", true), ("JITTER", true));
        return metric;
    }

    private static MetricCardViewModel CreateStorageMetric()
    {
        var metric = new MetricCardViewModel(
            "storage",
            "STORAGE",
            ParseGeometry("M4,6 C4,3 20,3 20,6 L20,18 C20,21 4,21 4,18 Z M4,6 C4,9 20,9 20,6 M4,12 C4,15 20,15 20,12"),
            SemanticAccent.Latency);
        metric.ConfigureDetails(("READ", true), ("WRITE", true), ("TEMP", false), ("HEALTH", false));
        return metric;
    }

    private static MetricCardViewModel CreateBatteryMetric()
    {
        var metric = new MetricCardViewModel(
            "battery",
            "BATTERY",
            ParseGeometry("M3,7 L19,7 L19,18 L3,18 Z M19,10 L22,10 L22,15 L19,15 M6,10 L14,10 L14,15 L6,15 Z"),
            SemanticAccent.Memory);
        metric.ConfigureDetails(("STATE", true), ("RUNTIME", true), ("DRAW", false));
        return metric;
    }

    private static MetricCardViewModel CreateWeatherMetric()
    {
        var metric = new MetricCardViewModel(
            WidgetModuleCatalog.Weather,
            "WEATHER",
            ParseGeometry("M7,17 A5,5 0 1 1 10,8 A7,7 0 0 1 23,11 A4,4 0 0 1 20,18 L7,18 Z M4,4 L4,7 M1,9 L4,9 M7,2 L7,5"),
            SemanticAccent.Weather);
        metric.ConfigureDetails(("RAIN", true), ("WIND", true), ("HUMIDITY", false));
        return metric;
    }

    private void OnWeatherSnapshotAvailable(object? sender, WeatherSnapshot snapshot)
    {
        _ = sender;
        var application = Application.Current;
        if (application is null || application.Dispatcher.CheckAccess())
        {
            ApplyWeatherSnapshot(snapshot);
            return;
        }

        _ = application.Dispatcher.BeginInvoke(() => ApplyWeatherSnapshot(snapshot));
    }

    private void ApplyWeatherSnapshot(WeatherSnapshot snapshot)
    {
        var metric = _metricIndex[WidgetModuleCatalog.Weather];
        metric.PrimaryValue = snapshot.TemperatureLabel;
        metric.Progress = snapshot.PrecipitationProbability;
        metric.IsProgressAvailable = true;
        metric.State = snapshot.IsStale
            ? SensorState.Stale
            : snapshot.Alert is { Level: >= 2, IsActive: true }
                ? SensorState.Warning
                : SensorState.Available;
        metric.Status = $"{snapshot.Location.Name} · {snapshot.Condition} · click for forecast and radar";
        metric.SetDetailValues(
            (snapshot.RainLabel, true),
            (snapshot.WindLabel, true),
            (snapshot.HumidityLabel, true));
        metric.PushSample(snapshot.TemperatureCelsius, 42);
    }

    private void OnSnapshotAvailable(object? sender, TelemetrySnapshot snapshot)
    {
        _ = sender;

        var application = Application.Current;
        if (application is null || application.Dispatcher.CheckAccess())
        {
            ApplySnapshot(snapshot);
            return;
        }

        _ = application.Dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(TelemetrySnapshot snapshot)
    {
        UpdateCpu(snapshot.Cpu);
        UpdateGpu(snapshot.Gpu);
        UpdateMemory(snapshot.Memory);
        UpdateNetwork(snapshot.Network);
        UpdateStorage(snapshot.Storage);
        UpdateBattery(snapshot.Battery);
        UpdateAdditionalMetrics(snapshot.Metrics);

        var age = DateTimeOffset.Now - snapshot.CapturedAt;
        LastUpdatedText = age.TotalSeconds < 3
            ? "Updated now"
            : $"Updated {Math.Max(1, (int)age.TotalSeconds)}s ago";
        TelemetryUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateCpu(CpuTelemetry sample)
    {
        var metric = _metricIndex["cpu"];
        var load = FiniteValue(sample.LoadPercent);
        var temperature = StabilizeBriefGap(
            FiniteValue(sample.TemperatureCelsius),
            ref _lastCpuTemperature,
            TemperatureGapGrace,
            out var temperatureHeld);
        var clock = FiniteValue(sample.ClockGhz);
        var power = FiniteValue(sample.PackagePowerWatts);
        metric.PrimaryValue = load is { } loadValue
            ? TelemetryTextFormatter.Percentage(
                loadValue,
                metric.DecimalPlacesOverride)
            : TelemetryTextFormatter.Unavailable;
        metric.Progress = load ?? 0;
        metric.IsProgressAvailable = load is not null;
        metric.State = temperatureHeld ? SensorState.Stale : sample.State;
        metric.Status = temperature is null
            ? "TEMP N/A"
            : $"TEMP {TelemetryTextFormatter.Temperature(temperature)}";
        metric.SetDetailValues(
            (
                TelemetryTextFormatter.Temperature(temperature),
                temperature is not null),
            (
                clock is { } clockValue
                    ? $"{TelemetryTextFormatter.Number(clockValue, 2)} GHz"
                    : TelemetryTextFormatter.Unavailable,
                clock is not null),
            (
                power is { } powerValue
                    ? $"{TelemetryTextFormatter.Number(powerValue, 0)} W"
                    : TelemetryTextFormatter.Unavailable,
                power is not null));
        metric.PushSample(load);
    }

    private void UpdateGpu(GpuTelemetry sample)
    {
        var metric = _metricIndex["gpu"];
        var load = FiniteValue(sample.LoadPercent);
        var temperature = StabilizeBriefGap(
            FiniteValue(sample.TemperatureCelsius),
            ref _lastGpuTemperature,
            TemperatureGapGrace,
            out var temperatureHeld);
        var clock = FiniteValue(sample.ClockGhz);
        var usedVram = FiniteValue(sample.UsedVramGigabytes);
        var totalVram = FiniteValue(sample.TotalVramGigabytes);
        var hasCompleteVram =
            usedVram is >= 0 &&
            totalVram is > 0;
        metric.PrimaryValue = load is { } loadValue
            ? TelemetryTextFormatter.Percentage(
                loadValue,
                metric.DecimalPlacesOverride)
            : TelemetryTextFormatter.Unavailable;
        metric.Progress = load ?? 0;
        metric.IsProgressAvailable = load is not null;
        metric.State = temperatureHeld ? SensorState.Stale : sample.State;
        metric.Status = temperature is null
            ? "TEMP N/A"
            : $"TEMP {TelemetryTextFormatter.Temperature(temperature)}";
        metric.SetDetailValues(
            (
                TelemetryTextFormatter.Temperature(temperature),
                temperature is not null),
            (
                usedVram is not null || totalVram is not null
                    ? TelemetryTextFormatter.Memory(usedVram, totalVram)
                    : TelemetryTextFormatter.Unavailable,
                hasCompleteVram),
            (
                clock is { } clockValue
                    ? $"{TelemetryTextFormatter.Number(clockValue, 2)} GHz"
                    : TelemetryTextFormatter.Unavailable,
                clock is not null));
        metric.PushSample(load);
    }

    private void UpdateMemory(MemoryTelemetry sample)
    {
        var metric = _metricIndex["memory"];
        var used = FiniteValue(sample.UsedGigabytes);
        var total = FiniteValue(sample.TotalGigabytes);
        var commit = FiniteValue(sample.CommitGigabytes);
        var cached = FiniteValue(sample.CachedGigabytes);
        var hasCapacity = used is >= 0 && total is > 0;
        var usedPercent = hasCapacity
            ? (used!.Value / total!.Value) * 100
            : (double?)null;
        metric.PrimaryValue = used is not null || total is not null
            ? TelemetryTextFormatter.Memory(
                used,
                total,
                metric.DecimalPlacesOverride)
            : TelemetryTextFormatter.Unavailable;
        metric.Progress = usedPercent ?? 0;
        metric.IsProgressAvailable = usedPercent is not null;
        metric.State = sample.State;
        metric.Status = usedPercent is { } percent
            ? $"{TelemetryTextFormatter.Percentage(percent)} USED"
            : "MEMORY N/A";
        metric.SetDetailValues(
            (
                commit is { } commitValue
                    ? $"{TelemetryTextFormatter.Number(commitValue, 1)} GB"
                    : TelemetryTextFormatter.Unavailable,
                commit is not null),
            (
                cached is { } cachedValue
                    ? $"{TelemetryTextFormatter.Number(cachedValue, 1)} GB"
                    : TelemetryTextFormatter.Unavailable,
                cached is not null),
            (
                hasCapacity
                    ? $"{TelemetryTextFormatter.Number(
                        Math.Max(0, total!.Value - used!.Value),
                        1)} GB"
                    : TelemetryTextFormatter.Unavailable,
                hasCapacity));
        metric.PushSample(usedPercent);
    }

    private void UpdateNetwork(NetworkTelemetry sample)
    {
        var throughput = _metricIndex["network"];
        var download = FiniteValue(sample.DownloadBytesPerSecond);
        var upload = FiniteValue(sample.UploadBytesPerSecond);
        var hasThroughput = download is not null || upload is not null;
        throughput.PrimaryValue = TelemetryTextFormatter.NetworkThroughput(
            download,
            upload,
            throughput.DecimalPlacesOverride);
        var measuredThroughput =
            Math.Max(0, download ?? 0) +
            Math.Max(0, upload ?? 0);
        throughput.Progress = hasThroughput
            ? Math.Clamp((Math.Log10(measuredThroughput + 1) / 8) * 100, 0, 100)
            : 0;
        throughput.IsProgressAvailable = hasThroughput;
        throughput.State = sample.ThroughputState;
        throughput.Status = hasThroughput ? "BYTES / SEC" : "NETWORK N/A";
        throughput.SetDetailValues(
            (
                TelemetryTextFormatter.Rate(download, throughput.DecimalPlacesOverride),
                download is not null),
            (
                TelemetryTextFormatter.Rate(upload, throughput.DecimalPlacesOverride),
                upload is not null));
        throughput.PushSample(
            download is { } downloadValue
                ? downloadValue / 1_000_000
                : null,
            100);

        var latency = _metricIndex["latency"];
        var ping = StabilizeBriefGap(
            FiniteValue(sample.PingMilliseconds),
            ref _lastPing,
            ConnectivityGapGrace,
            out var pingHeld);
        var packetLoss = StabilizeBriefGap(
            FiniteValue(sample.PacketLossPercent),
            ref _lastPacketLoss,
            ConnectivityGapGrace,
            out var packetLossHeld);
        var jitter = StabilizeBriefGap(
            FiniteValue(sample.JitterMilliseconds),
            ref _lastJitter,
            ConnectivityGapGrace,
            out var jitterHeld);
        var connectivityHeld = pingHeld || packetLossHeld || jitterHeld;
        latency.PrimaryValue = ping is { } pingValue
            ? TelemetryTextFormatter.Latency(
                pingValue,
                latency.DecimalPlacesOverride)
            : TelemetryTextFormatter.Unavailable;
        latency.Progress = ping is { } pingProgressValue
            ? Math.Clamp((pingProgressValue / 100) * 100, 0, 100)
            : 0;
        latency.IsProgressAvailable = ping is not null;
        latency.State = packetLoss switch
        {
            >= 5 => SensorState.Critical,
            >= 1 => SensorState.Warning,
            _ when connectivityHeld => SensorState.Stale,
            _ => sample.ConnectivityState
        };
        latency.Status = packetLoss is null
            ? "LOSS —"
            : $"LOSS {TelemetryTextFormatter.PacketLoss(packetLoss.Value)}";
        latency.SetDetailValues(
            (
                packetLoss is { } detailPacketLoss
                    ? TelemetryTextFormatter.PacketLoss(detailPacketLoss)
                    : TelemetryTextFormatter.Unavailable,
                packetLoss is not null),
            (
                jitter is { } jitterValue
                    ? TelemetryTextFormatter.Latency(jitterValue, 1)
                    : TelemetryTextFormatter.Unavailable,
                jitter is not null));
        if (ping is { } chartPing)
        {
            latency.PushSample(chartPing, 100);
        }
    }

    private void UpdateStorage(StorageTelemetry sample)
    {
        var metric = _metricIndex["storage"];
        var used = FiniteValue(sample.UsedPercent);
        var read = FiniteValue(sample.ReadBytesPerSecond);
        var write = FiniteValue(sample.WriteBytesPerSecond);
        var temperature = FiniteValue(sample.TemperatureCelsius);
        if (sample.State == SensorState.Unavailable || used is null)
        {
            metric.PrimaryValue = TelemetryTextFormatter.Unavailable;
            metric.Progress = 0;
            metric.IsProgressAvailable = false;
            metric.State = SensorState.Unavailable;
            metric.Status = sample.Health;
            metric.SetDetailValues(
                (TelemetryTextFormatter.Unavailable, false),
                (TelemetryTextFormatter.Unavailable, false),
                (TelemetryTextFormatter.Unavailable, false),
                (TelemetryTextFormatter.Unavailable, false));
            return;
        }

        metric.PrimaryValue =
            $"{TelemetryTextFormatter.Percentage(
                used.Value,
                metric.DecimalPlacesOverride)} used";
        metric.Progress = used.Value;
        metric.IsProgressAvailable = true;
        metric.State = sample.State;
        metric.Status = sample.State == SensorState.Stale ? "Sample delayed" : sample.Health;
        metric.SetDetailValues(
            (TelemetryTextFormatter.Rate(read), read is not null),
            (TelemetryTextFormatter.Rate(write), write is not null),
            (
                TelemetryTextFormatter.Temperature(temperature),
                temperature is not null),
            (sample.Health, !string.IsNullOrWhiteSpace(sample.Health)));
        metric.PushSample(used.Value);
    }

    private void UpdateBattery(BatteryTelemetry sample)
    {
        var metric = _metricIndex["battery"];
        var charge = FiniteValue(sample.ChargePercent);
        var draw = FiniteValue(sample.DrawWatts);
        var hasPowerState = !string.IsNullOrWhiteSpace(sample.PowerState);
        metric.State = sample.State;

        if (charge is not { } chargeValue)
        {
            metric.PrimaryValue = TelemetryTextFormatter.Unavailable;
            metric.Progress = 0;
            metric.IsProgressAvailable = false;
            metric.Status = hasPowerState
                ? sample.PowerState!
                : "BATTERY N/A";
            metric.SetDetailValues(
                (
                    hasPowerState
                        ? sample.PowerState!
                        : TelemetryTextFormatter.Unavailable,
                    hasPowerState),
                (TelemetryTextFormatter.Unavailable, false),
                (TelemetryTextFormatter.Unavailable, false));
            return;
        }

        metric.PrimaryValue = TelemetryTextFormatter.Percentage(
            chargeValue,
            metric.DecimalPlacesOverride);
        metric.Progress = chargeValue;
        metric.IsProgressAvailable = true;
        metric.Status = hasPowerState
            ? sample.PowerState!
            : "POWER N/A";
        metric.SetDetailValues(
            (
                hasPowerState
                    ? sample.PowerState!
                    : TelemetryTextFormatter.Unavailable,
                hasPowerState),
            (
                TelemetryTextFormatter.Duration(sample.Remaining),
                sample.Remaining is not null),
            (
                draw is { } drawValue
                    ? $"{TelemetryTextFormatter.Number(drawValue, 1)} W"
                    : TelemetryTextFormatter.Unavailable,
                draw is not null));
        metric.PushSample(chargeValue);
    }

    private void ConfigureAdditionalMetrics(
        IReadOnlyDictionary<string, WidgetModuleMetricBinding> bindings)
    {
        foreach (var pair in bindings)
        {
            if (!_metricIndex.TryGetValue(pair.Key, out MetricCardViewModel? card))
            {
                continue;
            }

            foreach (string metricId in pair.Value.AdditionalMetrics
                         .Where(item => !string.IsNullOrWhiteSpace(item))
                         .Distinct(StringComparer.Ordinal)
                         .Take(3))
            {
                int detailIndex = card.AddDetail(ShortMetricLabel(metricId));
                _additionalMetricSlots.Add(new AdditionalMetricSlot(card, detailIndex, metricId));
            }
        }
    }

    private void UpdateAdditionalMetrics(
        IReadOnlyDictionary<string, GenericMetricTelemetry>? metrics)
    {
        foreach (AdditionalMetricSlot slot in _additionalMetricSlots)
        {
            MetricDetailViewModel detail = slot.Card.Details[slot.DetailIndex];
            if (metrics is null ||
                !metrics.TryGetValue(slot.MetricId, out GenericMetricTelemetry? metric))
            {
                detail.Value = TelemetryTextFormatter.Unavailable;
                detail.IsAvailable = false;
                continue;
            }

            detail.Label = metric.DisplayName.ToUpperInvariant();
            detail.Value = FormatGenericMetric(metric);
            detail.IsAvailable = metric.Value is not null &&
                                 metric.State is SensorState.Available or SensorState.Stale;
        }
    }

    private static string FormatGenericMetric(GenericMetricTelemetry metric)
    {
        double? value = FiniteValue(metric.Value);
        if (value is null)
        {
            return TelemetryTextFormatter.Unavailable;
        }

        return metric.Unit switch
        {
            MetricUnit.Percent => TelemetryTextFormatter.Percentage(value.Value),
            MetricUnit.Celsius => TelemetryTextFormatter.Temperature(value),
            MetricUnit.Bytes => TelemetryTextFormatter.ByteSize(value.Value),
            MetricUnit.BytesPerSecond => TelemetryTextFormatter.Rate(value),
            MetricUnit.Hertz => value >= 1_000_000_000
                ? $"{TelemetryTextFormatter.Number(value.Value / 1_000_000_000d, 2)} GHz"
                : $"{TelemetryTextFormatter.Number(value.Value / 1_000_000d, 0)} MHz",
            MetricUnit.Watts => $"{TelemetryTextFormatter.Number(value.Value, 1)} W",
            MetricUnit.Volts => $"{TelemetryTextFormatter.Number(value.Value, 3)} V",
            MetricUnit.RevolutionsPerMinute => $"{TelemetryTextFormatter.Number(value.Value, 0)} RPM",
            MetricUnit.Milliseconds => TelemetryTextFormatter.Latency(value.Value, 1),
            MetricUnit.Seconds => $"{TelemetryTextFormatter.Number(value.Value, 0)} s",
            _ => TelemetryTextFormatter.Number(value.Value, 2)
        };
    }

    private static string ShortMetricLabel(string metricId)
    {
        string[] parts = metricId.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "SENSOR" : parts[^1].ToUpperInvariant();
    }

    private sealed record AdditionalMetricSlot(
        MetricCardViewModel Card,
        int DetailIndex,
        string MetricId);

    private static double? FiniteValue(double? value) =>
        value is { } candidate && double.IsFinite(candidate)
            ? candidate
            : null;

    private static double? StabilizeBriefGap(
        double? current,
        ref LastKnownValue? lastKnown,
        TimeSpan grace,
        out bool held)
    {
        var now = DateTimeOffset.UtcNow;
        if (current is { } value)
        {
            lastKnown = new LastKnownValue(value, now);
            held = false;
            return value;
        }

        if (lastKnown is { } cached && now - cached.CapturedAt <= grace)
        {
            held = true;
            return cached.Value;
        }

        held = false;
        lastKnown = null;
        return null;
    }

    private readonly record struct LastKnownValue(double Value, DateTimeOffset CapturedAt);

    private static WidgetDensity NormalizeDensity(
        WidgetLayout layout,
        WidgetDensity density) =>
        layout == WidgetLayout.Dock
            ? WidgetDensity.Compact
            : density;

    private void ApplyTheme()
    {
        var theme = _themes.First(candidate =>
            candidate.Name.Equals(ThemeName, StringComparison.OrdinalIgnoreCase));

        const double guardActivationOpacity = 0.82;
        const double fullGuardOpacity = 0.55;
        const double guardedCardOpacity = 0.78;
        var guardStrength = Math.Clamp(
            (guardActivationOpacity - SurfaceOpacity) /
            (guardActivationOpacity - fullGuardOpacity),
            0,
            1);
        var cardOpacity = Math.Clamp(theme.CardOpacity, 0, 1);

        SurfaceBrush = CreateBrush(WithOpacity(theme.Surface, SurfaceOpacity));
        CardBrush = CreateBrush(WithOpacity(theme.Card, cardOpacity));
        ReadabilityPlateBrush = CreateBrush(
            WithOpacity(theme.Card, guardedCardOpacity * guardStrength));
        BorderBrush = CreateBrush(
            WithOpacity(
                theme.Border,
                Math.Max(SurfaceOpacity * 0.54, 0.5 * guardStrength)));
        FlyoutBrush = CreateBrush(
            WithOpacity(theme.Card, Math.Max(SurfaceOpacity, 0.94)));
        TrackBrush = CreateBrush(WithOpacity(
            theme.Track,
            Math.Max(SurfaceOpacity * 0.5, 0.52 * guardStrength)));
        TextPrimaryBrush = CreateBrush(WithOpacity(theme.TextPrimary, 1));
        TextSecondaryBrush = CreateBrush(WithOpacity(theme.TextSecondary, 1));
        SuccessBrush = CreateBrush(WithOpacity(theme.Success, 1));
        var glowBrush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 0),
            GradientStops =
            {
                new GradientStop(theme.CpuAccent, 0),
                new GradientStop(Color.FromArgb(0, theme.MemoryAccent.R, theme.MemoryAccent.G, theme.MemoryAccent.B), 0.55),
                new GradientStop(theme.GpuAccent, 1)
            }
        };
        glowBrush.Freeze();
        ShellGlowBrush = glowBrush;
        WidgetFontFamily = new FontFamily(
            string.IsNullOrWhiteSpace(theme.FontFamily)
                ? "Segoe UI Variable"
                : theme.FontFamily);
        MinimumReadableFontSize = Math.Clamp(
            theme.MinimumReadableSize,
            11,
            24);
        LabelFontSize = Math.Max(
            MinimumReadableFontSize,
            Math.Clamp(theme.LabelSize, 8, 30));
        ValueFontSize = Math.Max(
            MinimumReadableFontSize + 2,
            Math.Clamp(theme.ValueSize, 10, 48));
        LabelFontWeight = FontWeight.FromOpenTypeWeight(
            Math.Clamp(theme.LabelWeight, 100, 999));
        ValueFontWeight = FontWeight.FromOpenTypeWeight(
            Math.Clamp(theme.ValueWeight, 100, 999));
        UseTabularNumbers = theme.UseTabularNumbers;
        ShellCornerRadius = new CornerRadius(Math.Clamp(theme.CornerRadius, 0, 48));
        ShellBorderThickness = new Thickness(Math.Clamp(theme.BorderWidth, 0, 4));
        ShellContentPadding = new Thickness(Math.Clamp(theme.ContentPadding, 0, 28));
        _shellShadowThemeOpacity = theme.ShadowEnabled
            ? Math.Clamp(theme.ShadowOpacity, 0, 0.8)
            : 0;
        _shellGlowThemeOpacity = theme.GlowEnabled
            ? Math.Clamp(theme.GlowOpacity, 0, 0.5)
            : 0;
        OnPropertyChanged(nameof(ShellShadowOpacity));
        OnPropertyChanged(nameof(ShellGlowOpacity));
        HeaderVisible = theme.HeaderVisible;
        StatusIndicatorVisible = theme.StatusIndicatorVisible;
        SettingsButtonVisible = theme.SettingsButtonVisible;
        HeaderHeight = Math.Clamp(theme.HeaderHeight, 18, 64);
        HeaderFontSize = Math.Max(MinimumReadableFontSize, Math.Clamp(theme.HeaderSize, 8, 24));
        SecondaryFontSize = Math.Max(MinimumReadableFontSize, Math.Clamp(theme.SecondarySize, 8, 24));
        HeaderFontWeight = FontWeight.FromOpenTypeWeight(Math.Clamp(theme.HeaderWeight, 100, 900));
        SecondaryFontWeight = FontWeight.FromOpenTypeWeight(Math.Clamp(theme.SecondaryWeight, 100, 900));
        MotionEnabled = theme.MotionEnabled && !_reducedMotion;
        PulseStatusIndicator = theme.PulseStatusIndicator && !_reducedMotion;
        RespectReducedMotion = theme.RespectReducedMotion;
        TransitionMilliseconds = _reducedMotion
            ? 0
            : Math.Clamp(theme.TransitionMilliseconds, 0, 600);
        RaiseLayoutAwareThemeProperties();

        if (Metrics is not null)
        {
            foreach (var metric in Metrics)
            {
                metric.ApplyTheme(theme);
            }
        }
    }

    private void RaiseLayoutAwareThemeProperties()
    {
        OnPropertyChanged(nameof(LayoutShellContentPadding));
        OnPropertyChanged(nameof(LayoutHeaderHeight));
        OnPropertyChanged(nameof(LayoutHeaderFontSize));
        OnPropertyChanged(nameof(LayoutSecondaryFontSize));
    }

    private static Geometry ParseGeometry(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    private static Color WithOpacity(Color color, double opacity)
        => Color.FromArgb(
            (byte)Math.Round(
                Math.Clamp(
                    (color.A / (double)byte.MaxValue) * opacity,
                    0,
                    1) *
                byte.MaxValue),
            color.R,
            color.G,
            color.B);

    private static IReadOnlyList<ThemeDefinition> BuildThemes(
        IReadOnlyList<WidgetRuntimeTheme>? runtimeThemes)
    {
        var fallback = BuiltInThemes[0];
        var mapped = runtimeThemes?
            .Where(theme =>
                !string.IsNullOrWhiteSpace(theme.Id) &&
                !string.IsNullOrWhiteSpace(theme.Name))
            .GroupBy(theme => theme.Id, StringComparer.Ordinal)
            .Select(group => ToThemeDefinition(group.First(), fallback))
            .ToArray() ?? [];
        if (mapped.Length == 0)
        {
            return BuiltInThemes;
        }

        var merged = BuiltInThemes.ToList();
        foreach (var runtimeTheme in mapped)
        {
            var matchIndex = merged.FindIndex(theme =>
                theme.Name.Equals(
                    runtimeTheme.Name,
                    StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(runtimeTheme.CoreThemeId) &&
                 StringComparer.Ordinal.Equals(
                     theme.CoreThemeId,
                     runtimeTheme.CoreThemeId)));
            if (matchIndex >= 0)
            {
                merged[matchIndex] = runtimeTheme;
            }
            else
            {
                merged.Add(runtimeTheme);
            }
        }

        return merged;
    }

    private static ThemeDefinition ToThemeDefinition(
        WidgetRuntimeTheme source,
        ThemeDefinition fallback) =>
        new(
            source.Name,
            source.Id,
            ParseColor(source.Background, fallback.Surface),
            ParseColor(source.Card, fallback.Card),
            ParseColor(source.Border, fallback.Border),
            ParseColor(source.PrimaryText, fallback.TextPrimary),
            ParseColor(source.SecondaryText, fallback.TextSecondary),
            ParseColor(source.CpuAccent, fallback.CpuAccent),
            ParseColor(source.GpuAccent, fallback.GpuAccent),
            ParseColor(source.MemoryAccent, fallback.MemoryAccent),
            ParseColor(source.NetworkAccent, fallback.NetworkAccent),
            ParseColor(source.Warning, fallback.Warning),
            ParseColor(source.Critical, fallback.Critical),
            source.FontFamily,
            source.LabelSize,
            source.ValueSize,
            source.MinimumReadableSize,
            source.LabelWeight,
            source.ValueWeight,
            source.UseTabularNumbers)
        {
            LatencyAccent = ParseColor(source.LatencyAccent, fallback.LatencyAccent),
            WeatherAccent = ParseColor(source.WeatherAccent, fallback.WeatherAccent),
            Success = ParseColor(source.Success, fallback.Success),
            Track = ParseColor(source.Track, fallback.Track),
            CornerRadius = source.CornerRadius,
            CardCornerRadius = source.CardCornerRadius,
            BlurEnabled = source.BlurEnabled,
            BlurStrength = source.BlurStrength,
            ShadowEnabled = source.ShadowEnabled,
            ShadowOpacity = source.ShadowOpacity,
            GlowEnabled = source.GlowEnabled,
            GlowOpacity = source.GlowOpacity,
            BorderWidth = source.BorderWidth,
            CardBorderWidth = source.CardBorderWidth,
            CardGap = source.CardGap,
            ContentPadding = source.ContentPadding,
            CardPadding = source.CardPadding,
            CardOpacity = source.CardOpacity,
            AccentWidth = source.AccentWidth,
            ProgressHeight = source.ProgressHeight,
            ProgressCornerRadius = source.ProgressCornerRadius,
            SparklineThickness = source.SparklineThickness,
            SparklineFillOpacity = source.SparklineFillOpacity,
            HeaderVisible = source.HeaderVisible,
            StatusIndicatorVisible = source.StatusIndicatorVisible,
            SettingsButtonVisible = source.SettingsButtonVisible,
            HeaderHeight = source.HeaderHeight,
            HeaderSize = source.HeaderSize,
            SecondarySize = source.SecondarySize,
            IconSize = source.IconSize,
            HeaderWeight = source.HeaderWeight,
            SecondaryWeight = source.SecondaryWeight,
            MotionEnabled = source.MotionEnabled,
            TransitionMilliseconds = source.TransitionMilliseconds,
            AnimateValueChanges = source.AnimateValueChanges,
            RespectReducedMotion = source.RespectReducedMotion,
            PulseStatusIndicator = source.PulseStatusIndicator
        };

    private static ThemeDefinition CreateBuiltInTheme(
        string name,
        Color surface,
        Color card,
        Color border,
        Color textPrimary,
        Color textSecondary,
        Color cpuAccent,
        Color gpuAccent,
        Color memoryAccent,
        Color networkAccent,
        Color warning,
        Color critical) =>
        new(
            name,
            null,
            surface,
            card,
            border,
            textPrimary,
            textSecondary,
            cpuAccent,
            gpuAccent,
            memoryAccent,
            networkAccent,
            warning,
            critical,
            "Segoe UI Variable",
            12,
            18,
            12,
            600,
            600,
            true);

    private static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            return MediaColorConverter.ConvertFromString(value) is Color color
                ? color
                : fallback;
        }
        catch (FormatException)
        {
            return fallback;
        }
        catch (NotSupportedException)
        {
            return fallback;
        }
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
