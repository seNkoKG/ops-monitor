using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OpsMonitor.Core.Platform;
using OpsMonitor.Core.Providers;
using OpsMonitor.Studio.Infrastructure;
using OpsMonitor.Studio.Models;
using OpsMonitor.Studio.Services;
using WidgetDensityModel = OpsMonitor.Widget.Models.WidgetDensity;
using WidgetLayoutModel = OpsMonitor.Widget.Models.WidgetLayout;

namespace OpsMonitor.Studio.ViewModels;

public sealed class StudioViewModel : ObservableObject, IDisposable
{
    private readonly IStudioSettingsSink _settingsSink;
    private readonly DispatcherTimer _applyTimer;
    private readonly Dispatcher _dispatcher;
    private readonly DebouncedSettingsFileWatcher? _runtimeSettingsWatcher;
    private readonly Stack<EditorState> _undo = new();
    private readonly Stack<EditorState> _redo = new();
    private bool _isInitializing = true;
    private bool _isRestoring;
    private bool _isApplyingLayoutMetrics;
    private double _telemetryPhase;
    private NavigationItem? _selectedNavigation;
    private ModuleItem? _selectedModule;
    private ThemePreset? _selectedTheme;
    private string _designId = "void";
    private string _designName = "Void";
    private string _selectedLayout = "Pill";
    private string _activeScene = "Daily driver";
    private string _searchText = string.Empty;
    private string _sensorSearchText = string.Empty;
    private string _sensorCatalogStatus = "Waiting for the hardware broker";
    private double _backgroundOpacity = 0.82;
    private double _contentOpacity = 1;
    private double _blurStrength = 24;
    private double _fontScale = 1;
    private string _density = "Compact";
    private bool _alwaysOnTop = true;
    private bool _positionLocked;
    private bool _clickThrough;
    private bool _draggable = true;
    private bool _resizable = true;
    private bool _startAtSignIn = true;
    private bool _reducedMotion;
    private bool _demoMetrics = true;
    private int _widgetScalePercent = 100;
    private string _updateRate = "2 seconds";
    private string _performanceMode = "Balanced";
    private string _statusMessage = "Ready";
    private string _lastAppliedText = "Loading settings…";
    private Brush _previewSurfaceBrush = Brushes.Transparent;
    private Brush _previewCardBrush = Brushes.Transparent;
    private Brush _previewBorderBrush = Brushes.Transparent;
    private Brush _previewAccentBrush = Brushes.Aquamarine;
    private double _previewWidth;
    private double _previewHeight;
    private double _previewModuleWidth;
    private double _previewModuleHeight;
    private double _previewCornerRadius;
    private string _widgetExecutablePath = "Detecting…";
    private string _widgetActionLabel = "Open widget";
    private string _resourceCpu = "Sampling…";
    private string _resourceMemory = "Sampling…";
    private double _resourceCpuPercent;
    private double _resourceMemoryMegabytes;
    private string _impactLabel = "LIVE";
    private DateTimeOffset? _lastResourceSampleUtc;
    private TimeSpan _lastResourceProcessorTime;
    private bool _disposed;

    public StudioViewModel(IStudioSettingsSink? settingsSink = null)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _settingsSink = settingsSink ?? new StudioCoreSettingsSink();
        _settingsSink.SettingsChanged += (_, snapshot) => SettingsChanged?.Invoke(this, snapshot);

        Navigation = new ObservableCollection<NavigationItem>
        {
            new("overview", "Command center", "Runtime health, scenes and quick actions", "⌂"),
            new("widgets", "Structure", "Layout, module order and card content", "▦"),
            new("appearance", "Visual designer", "Presets, colors, geometry, type and motion", "✦"),
            new("window", "Behavior", "Position, interaction, size and Windows startup", "⌗"),
            new("providers", "Data sources", "Sensor health, polling and pinned readings", "◇"),
            new("diagnostics", "System", "Resource impact, paths and maintenance", "ⓘ"),
        };

        NavigationView = CollectionViewSource.GetDefaultView(Navigation);
        NavigationView.Filter = item =>
        {
            if (item is not NavigationItem navigationItem || string.IsNullOrWhiteSpace(SearchText))
            {
                return true;
            }

            return navigationItem.Label.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || navigationItem.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        };

        Layouts = new ObservableCollection<LayoutPreset>
        {
            new("Pill", "Pill", "Slim vertical focus", "▯"),
            new("Rail", "Rail", "Compact single column", "▥"),
            new("Dock", "Dock", "Horizontal desktop strip", "▬"),
            new("Mini", "Mini", "Tiny essentials capsule", "▫"),
        };

        Themes = new ObservableCollection<ThemePreset>
        {
            new("void", "Void", "Near-black glass with cyan and magenta signals", Color.FromRgb(8, 11, 18), Color.FromRgb(15, 21, 33), Color.FromRgb(54, 66, 88), Color.FromRgb(72, 220, 249)),
            new("aurora", "Aurora", "Deep violet glass with electric color", Color.FromRgb(14, 10, 27), Color.FromRgb(25, 19, 45), Color.FromRgb(76, 62, 111), Color.FromRgb(86, 226, 255)),
            new("slate", "Slate / High Contrast", "Brighter borders and text for difficult desktops", Color.FromRgb(18, 24, 31), Color.FromRgb(27, 36, 47), Color.FromRgb(67, 83, 101), Color.FromRgb(72, 207, 234)),
            new("ember", "Ember", "Warm graphite with amber highlights", Color.FromRgb(22, 14, 15), Color.FromRgb(38, 23, 25), Color.FromRgb(93, 56, 60), Color.FromRgb(255, 172, 72)),
            new("contrast", "Contrast", "Maximum contrast with crisp cyan and magenta signals", Color.FromRgb(2, 3, 5), Color.FromRgb(7, 9, 13), Color.FromRgb(148, 170, 198), Color.FromRgb(71, 229, 255)),
            new("ghost", "Ghost", "Bright frost glass with ink-dark typography", Color.FromRgb(232, 240, 246), Color.FromRgb(250, 253, 255), Color.FromRgb(78, 100, 122), Color.FromRgb(0, 126, 154)),
            new("terminal", "Terminal", "Tight phosphor-green instrumentation", Color.FromRgb(5, 10, 7), Color.FromRgb(9, 19, 13), Color.FromRgb(29, 94, 55), Color.FromRgb(92, 255, 157)),
            new("frameless", "Frameless", "Near-invisible chrome with floating readings", Color.FromRgb(5, 7, 11), Color.FromRgb(8, 12, 18), Color.FromArgb(0, 0, 0, 0), Color.FromRgb(98, 215, 255)),
        };

        Modules = new ObservableCollection<ModuleItem>
        {
            NewModule("cpu", "CPU", "▦", "Compute", "Load, package temperature and clock", "Windows + hardware sensor", "#43E7F5", "42%", "TEMP 68°C", 42, true, "Large"),
            NewModule("gpu", "GPU", "▣", "Compute", "3D load, temperature and VRAM", "NVIDIA NVML with bounded fallback", "#F05AD6", "17%", "TEMP 41°C", 17, true, "Large"),
            NewModule("ram", "Memory", "▤", "System", "Physical memory pressure", "Windows memory status", "#43E7D2", "15.4 / 30.9 GB", "50% USED", 50),
            NewModule("net", "Network", "↕", "Network", "Download and upload throughput", "Active network adapter", "#62A7FF", "937K / 27K", "BYTES / SEC", 36),
            NewModule("latency", "Latency", "⌁", "Network", "Ping, jitter and packet loss", "ICMP health probe", "#FFC95C", "26 ms", "LOSS 0%", 22),
            NewModule("disk", "Storage", "◫", "Storage", "System capacity, activity, temperature and health", "Windows + protected hardware broker", "#63E6A6", "68%", "TEMP 42°C", 68, false),
            NewModule("battery", "Power", "▥", "Power", "Battery, draw and remaining time", "Windows power API", "#8EEA78", "86%", "2h 48m", 86, false),
            NewModule("weather", "Weather", "☀", "Environment", "Local conditions, forecast and radar launcher", "Open-Meteo + ARSO", "#62A7FF", "23°", "CELJE", 46, true),
        };

        VisibleModulesView = new ListCollectionView(Modules)
        {
            Filter = item => item is ModuleItem module && module.IsVisible,
        };

        Designer = new WidgetDesignerState();
        Designer.EditorValueChanging += (_, _) => PushUndo();
        Designer.DesignChanged += (_, _) =>
        {
            RefreshModuleAccentBrushes();
            RefreshModulePresentation();
            RefreshPreviewBrushes();
            RaiseProductionPreviewContext();
            QueueLiveApply();
        };
        SensorCatalog = [];
        SensorCatalogView = new ListCollectionView(SensorCatalog)
        {
            Filter = FilterSensorCatalog,
        };
        foreach (var module in Modules)
        {
            module.EditorValueChanging += OnModuleEditorValueChanging;
            module.PropertyChanged += OnModulePropertyChanged;
        }

        Scenes = new ObservableCollection<SceneItem>
        {
            new("daily", "Daily driver", "Pill", "Quiet desktop essentials", "Ctrl + Alt + 1", Brush("#43E7D2")) { IsActive = true },
            new("gaming", "Gaming guard", "Dock", "Wide temperatures and network view", "Ctrl + Alt + 2", Brush("#F05AD6")),
            new("stream", "Stream check", "Rail", "Network quality and system load", "Ctrl + Alt + 3", Brush("#62A7FF")),
            new("debug", "Thermal audit", "Rail", "Temperatures and packet loss", "Ctrl + Alt + 4", Brush("#FFC95C")),
        };

        Providers = new ObservableCollection<ProviderItem>
        {
            new("Windows native", "CPU, memory, adapters, uptime and power", "Enabled", "Adaptive", "Core metrics", "Built in", false, Brush("#FF63E6A6")),
            new("NVIDIA NVML", "GPU load, temperature, VRAM, power and fan", "Automatic", "Adaptive", "When supported", "Built in", false, Brush("#FF63E6A6")),
            CreateCpuTemperatureProviderItem(),
            new("Network quality", "Ping, jitter and rolling packet loss", "Enabled", "Adaptive", "3 quality metrics", "Built in", false, Brush("#FF63E6A6")),
            new("Hardware sensor catalog", "Batched temperatures, fans, clocks, power, voltage and storage health", "Automatic", "3 s poll / 6 s cache", "Protected broker", "Built in", true, Brush("#FF63E6A6")),
        };

        Activities = new ObservableCollection<ActivityItem>
        {
            new("Now", "Shared settings synchronized", RuntimeSettingsPath, "●", Brush("#FF43E7D2")),
            new("Now", "Adaptive polling is active", "Providers run independently and never overlap themselves", "↕", Brush("#FF63E6A6")),
            new("Local", "Configuration stored on this PC", SettingsPath, "✓", Brush("#FF62A7FF")),
        };

        SelectLayoutCommand = new RelayCommand(
            SelectLayout,
            parameter => parameter is string layout &&
                         !layout.Equals(SelectedLayout, StringComparison.Ordinal));
        SetScaleCommand = new RelayCommand(SetScale);
        NavigateCommand = new RelayCommand(Navigate);
        ApplyThemeCommand = new RelayCommand(
            ApplyTheme,
            parameter => parameter is ThemePreset);
        FixContrastCommand = new RelayCommand(_ => RunDesignerTransaction(Designer.FixContrast));
        ImportDesignCommand = new RelayCommand(_ => RequestImportDesign?.Invoke(this, EventArgs.Empty));
        ExportDesignCommand = new RelayCommand(_ => RequestExportDesign?.Invoke(this, EventArgs.Empty));
        ActivateSceneCommand = new RelayCommand(
            ActivateScene,
            parameter => parameter is SceneItem scene &&
                         (!scene.IsActive ||
                          !scene.Name.Equals(ActiveScene, StringComparison.Ordinal)));
        MoveModuleUpCommand = new RelayCommand(
            parameter => MoveModule(parameter as ModuleItem, -1),
            parameter => CanMoveModule(parameter as ModuleItem, -1));
        MoveModuleDownCommand = new RelayCommand(
            parameter => MoveModule(parameter as ModuleItem, 1),
            parameter => CanMoveModule(parameter as ModuleItem, 1));
        ResetModuleOverridesCommand = new RelayCommand(
            parameter => ResetModuleOverrides(parameter as ModuleItem),
            parameter => parameter is ModuleItem module && module.HasOverrides);
        AddModuleCommand = new RelayCommand(AddModule);
        UndoCommand = new RelayCommand(_ => Undo(), _ => _undo.Count > 0);
        RedoCommand = new RelayCommand(_ => Redo(), _ => _redo.Count > 0);
        TestProviderCommand = new RelayCommand(TestProvider);
        RefreshSensorsCommand = new RelayCommand(_ => RefreshSensorCatalog());
        CopyDiagnosticsCommand = new RelayCommand(_ => CopyDiagnostics());
        ResetDemoCommand = new RelayCommand(_ => ResetDemo());
        SaveCommand = new RelayCommand(_ => SaveSettings());
        ReloadCommand = new RelayCommand(_ => ReloadSettings());
        OpenOrRestartWidgetCommand = new RelayCommand(_ => OpenOrRestartWidget());
        CheckForUpdatesCommand = new RelayCommand(_ => CheckForUpdates());

        SelectedNavigation = Navigation[2];
        SelectedModule = Modules[0];
        SelectedTheme = Themes[0];
        Designer.ApplyPreset(Themes[0]);
        SelectedTheme.IsSelected = true;
        Layouts[0].IsSelected = true;
        ApplyLayoutMetrics();
        RefreshPreviewBrushes();
        RefreshModulePresentation();

        _applyTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(240),
        };
        _applyTimer.Tick += (_, _) =>
        {
            _applyTimer.Stop();
            SaveSettings();
        };

        _isInitializing = false;
        ReloadSettings();
        RefreshWidgetStatus();
        if (!string.IsNullOrWhiteSpace(RuntimeSettingsPath))
        {
            _runtimeSettingsWatcher = new DebouncedSettingsFileWatcher(
                RuntimeSettingsPath,
                new DebouncedSettingsFileWatcherOptions
                {
                    DebounceInterval = TimeSpan.FromMilliseconds(150),
                });
            _runtimeSettingsWatcher.ReloadRequested +=
                RuntimeSettingsWatcher_OnReloadRequested;
            _ = StartRuntimeSettingsWatcherAsync();
        }
    }

    public event EventHandler<StudioSettingsSnapshot>? SettingsChanged;
    public event EventHandler? RequestCopyDiagnostics;
    public event EventHandler? RequestImportDesign;
    public event EventHandler? RequestExportDesign;

    public ObservableCollection<NavigationItem> Navigation { get; }
    public ICollectionView NavigationView { get; }
    public ObservableCollection<LayoutPreset> Layouts { get; }
    public ObservableCollection<ThemePreset> Themes { get; }
    public WidgetDesignerState Designer { get; }
    public ObservableCollection<ModuleItem> Modules { get; }
    public ListCollectionView VisibleModulesView { get; }
    public ObservableCollection<SceneItem> Scenes { get; }
    public ObservableCollection<ProviderItem> Providers { get; }
    public ObservableCollection<SensorCatalogItem> SensorCatalog { get; }
    public ListCollectionView SensorCatalogView { get; }
    public ObservableCollection<ActivityItem> Activities { get; }
    public IReadOnlyList<string> Visualizations { get; } =
        ["Value only", "Value + bar", "Bar only", "Sparkline only", "Value + sparkline"];
    public IReadOnlyList<string> ModuleSizes { get; } = ["Small", "Medium", "Large"];
    public IReadOnlyList<string> InstalledFontFamilies { get; } =
        Fonts.SystemFontFamilies
            .Select(font => font.Source)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    public IReadOnlyList<string> PrecisionOptions { get; } =
        ["Whole numbers", "1 decimal", "2 decimals", "Adaptive"];
    public IReadOnlyList<string> DensityOptions { get; } = ["Compact", "Comfortable", "Airy"];
    public IReadOnlyList<string> UpdateRates { get; } = ["0.5 seconds", "1 second", "2 seconds", "5 seconds", "10 seconds"];
    public IReadOnlyList<string> PerformanceModes { get; } = ["Performance", "Balanced", "Efficiency"];
    public string SettingsPath => _settingsSink.SettingsPath;
    public string RuntimeSettingsPath => _settingsSink.RuntimeSettingsPath;
    public string WidgetExecutablePath
    {
        get => _widgetExecutablePath;
        private set => SetProperty(ref _widgetExecutablePath, value);
    }

    public ICommand SelectLayoutCommand { get; }
    public ICommand SetScaleCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand ApplyThemeCommand { get; }
    public ICommand FixContrastCommand { get; }
    public ICommand ImportDesignCommand { get; }
    public ICommand ExportDesignCommand { get; }
    public ICommand ActivateSceneCommand { get; }
    public ICommand MoveModuleUpCommand { get; }
    public ICommand MoveModuleDownCommand { get; }
    public ICommand ResetModuleOverridesCommand { get; }
    public ICommand AddModuleCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand TestProviderCommand { get; }
    public ICommand RefreshSensorsCommand { get; }
    public ICommand CopyDiagnosticsCommand { get; }
    public ICommand ResetDemoCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand OpenOrRestartWidgetCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }

    public NavigationItem? SelectedNavigation
    {
        get => _selectedNavigation;
        set
        {
            if (SetProperty(ref _selectedNavigation, value) && value is not null)
            {
                OnPropertyChanged(nameof(CurrentPageId));
                OnPropertyChanged(nameof(CurrentPageTitle));
                OnPropertyChanged(nameof(CurrentPageDescription));
            }
        }
    }

    public string CurrentPageId => SelectedNavigation?.Id ?? "overview";
    public string CurrentPageTitle => SelectedNavigation?.Label ?? "Overview";
    public string CurrentPageDescription => SelectedNavigation?.Description ?? string.Empty;

    public ModuleItem? SelectedModule
    {
        get => _selectedModule;
        set
        {
            if (SetProperty(ref _selectedModule, value))
            {
                RaiseEditorCommandState();
            }
        }
    }

    public ThemePreset? SelectedTheme
    {
        get => _selectedTheme;
        private set
        {
            if (SetProperty(ref _selectedTheme, value) && value is not null)
            {
                RefreshPreviewBrushes();
                (ApplyThemeCommand as RelayCommand)?.RaiseCanExecuteChanged();
                QueueLiveApply();
            }
        }
    }

    public string SelectedLayout
    {
        get => _selectedLayout;
        set
        {
            if (SetProperty(ref _selectedLayout, value))
            {
                foreach (var layout in Layouts)
                {
                    layout.IsSelected = layout.Id.Equals(value, StringComparison.Ordinal);
                }

                if (!CanChangeDensity &&
                    !_density.Equals("Compact", StringComparison.Ordinal))
                {
                    _density = "Compact";
                    OnPropertyChanged(nameof(Density));
                    OnPropertyChanged(nameof(PreviewWidgetDensity));
                }

                OnPropertyChanged(nameof(CanChangeDensity));
                OnPropertyChanged(nameof(PreviewWidgetLayout));
                OnPropertyChanged(nameof(PreviewWidgetDensity));
                ApplyLayoutMetrics();
                (SelectLayoutCommand as RelayCommand)?.RaiseCanExecuteChanged();
                QueueLiveApply();
            }
        }
    }

    public string ActiveScene
    {
        get => _activeScene;
        private set
        {
            if (SetProperty(ref _activeScene, value))
            {
                OnPropertyChanged(nameof(PreviewTitle));
                (ActivateSceneCommand as RelayCommand)?.RaiseCanExecuteChanged();
                QueueLiveApply();
            }
        }
    }

    public string PreviewTitle => ActiveScene.ToUpperInvariant();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                NavigationView.Refresh();
            }
        }
    }

    public string SensorSearchText
    {
        get => _sensorSearchText;
        set
        {
            if (SetProperty(ref _sensorSearchText, value))
            {
                SensorCatalogView.Refresh();
            }
        }
    }

    public string SensorCatalogStatus
    {
        get => _sensorCatalogStatus;
        private set => SetProperty(ref _sensorCatalogStatus, value);
    }

    public double BackgroundOpacity
    {
        get => _backgroundOpacity;
        set
        {
            if (SetEditorProperty(
                    ref _backgroundOpacity,
                    Math.Clamp(value, 0.08, 1)))
            {
                RefreshPreviewBrushes();
                QueueLiveApply();
            }
        }
    }

    public double ContentOpacity
    {
        get => _contentOpacity;
        set
        {
            if (SetEditorProperty(
                    ref _contentOpacity,
                    Math.Clamp(value, 0.82, 1)))
            {
                QueueLiveApply();
            }
        }
    }

    public double BlurStrength
    {
        get => _blurStrength;
        set
        {
            if (SetEditorProperty(ref _blurStrength, value))
            {
                QueueLiveApply();
            }
        }
    }

    public double FontScale
    {
        get => _fontScale;
        set
        {
            if (SetEditorProperty(ref _fontScale, value))
            {
                RaiseProductionPreviewContext();
                QueueLiveApply();
            }
        }
    }

    public string Density
    {
        get => _density;
        set
        {
            var normalized = CanChangeDensity &&
                             DensityOptions.Contains(value)
                ? value
                : "Compact";
            if (SetEditorProperty(ref _density, normalized))
            {
                OnPropertyChanged(nameof(PreviewWidgetDensity));
                ApplyLayoutMetrics();
                QueueLiveApply();
            }
        }
    }

    public bool CanChangeDensity =>
        !SelectedLayout.Equals("Dock", StringComparison.Ordinal);

    public bool AlwaysOnTop { get => _alwaysOnTop; set => SetAndQueue(ref _alwaysOnTop, value); }
    public bool PositionLocked
    {
        get => _positionLocked;
        set
        {
            if (!SetEditorProperty(ref _positionLocked, value))
            {
                return;
            }

            if (value && _clickThrough)
            {
                _clickThrough = false;
                OnPropertyChanged(nameof(ClickThrough));
            }

            QueueLiveApply();
        }
    }

    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            if (!SetEditorProperty(ref _clickThrough, value))
            {
                return;
            }

            if (value && _positionLocked)
            {
                _positionLocked = false;
                OnPropertyChanged(nameof(PositionLocked));
            }

            QueueLiveApply();
        }
    }

    public bool Draggable { get => _draggable; set => SetAndQueue(ref _draggable, value); }
    public bool Resizable { get => _resizable; set => SetAndQueue(ref _resizable, value); }
    public bool StartAtSignIn { get => _startAtSignIn; set => SetAndQueue(ref _startAtSignIn, value); }
    public bool ReducedMotion { get => _reducedMotion; set => SetAndQueue(ref _reducedMotion, value); }
    public bool DemoMetrics { get => _demoMetrics; set => SetAndQueue(ref _demoMetrics, value); }

    public int WidgetScalePercent
    {
        get => _widgetScalePercent;
        set
        {
            if (SetEditorProperty(
                    ref _widgetScalePercent,
                    Math.Clamp(value, 80, 160)))
            {
                OnPropertyChanged(nameof(IsReducedScale));
                ApplyLayoutMetrics();
                QueueLiveApply();
            }
        }
    }

    public string UpdateRate
    {
        get => _updateRate;
        set => SetAndQueue(ref _updateRate, value);
    }

    public string PerformanceMode
    {
        get => _performanceMode;
        set => SetAndQueue(ref _performanceMode, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string LastAppliedText
    {
        get => _lastAppliedText;
        private set => SetProperty(ref _lastAppliedText, value);
    }

    public Brush PreviewSurfaceBrush
    {
        get => _previewSurfaceBrush;
        private set => SetProperty(ref _previewSurfaceBrush, value);
    }

    public Brush PreviewCardBrush
    {
        get => _previewCardBrush;
        private set => SetProperty(ref _previewCardBrush, value);
    }

    public Brush PreviewBorderBrush
    {
        get => _previewBorderBrush;
        private set => SetProperty(ref _previewBorderBrush, value);
    }

    public Brush PreviewAccentBrush
    {
        get => _previewAccentBrush;
        private set => SetProperty(ref _previewAccentBrush, value);
    }

    public double PreviewWidth => _previewWidth;
    public double PreviewHeight => _previewHeight;
    public WidgetLayoutModel PreviewWidgetLayout => SelectedLayout switch
    {
        "Rail" => WidgetLayoutModel.Rail,
        "Dock" => WidgetLayoutModel.Dock,
        "Mini" => WidgetLayoutModel.Mini,
        _ => WidgetLayoutModel.Pill
    };
    public WidgetDensityModel PreviewWidgetDensity => Density switch
    {
        "Airy" => WidgetDensityModel.Detail,
        "Comfortable" => WidgetDensityModel.Normal,
        _ => WidgetDensityModel.Compact
    };
    public Brush TextPrimaryBrush => Designer.PrimaryTextBrush;
    public Brush TextSecondaryBrush => Designer.SecondaryTextBrush;
    public Brush TrackBrush => FrozenBrush(Designer.Track, Colors.DimGray);
    public FontFamily WidgetFontFamily => new(Designer.FontFamily);
    public double LabelFontSize => Designer.LabelSize;
    public double ValueFontSize => Designer.ValueSize;
    public FontWeight LabelFontWeight => FontWeight.FromOpenTypeWeight(Designer.LabelWeight);
    public FontWeight ValueFontWeight => FontWeight.FromOpenTypeWeight(Designer.ValueWeight);
    public bool UseTabularNumbers => Designer.UseTabularNumbers;
    public bool IsReducedScale => WidgetScalePercent < 100;
    public double WidgetWidth
    {
        get => _previewWidth;
        set
        {
            var normalized = Math.Clamp(value, 176, 1_600);
            var changed = _isApplyingLayoutMetrics
                ? SetProperty(ref _previewWidth, normalized, nameof(WidgetWidth))
                : SetEditorProperty(ref _previewWidth, normalized, nameof(WidgetWidth));
            if (changed)
            {
                OnPropertyChanged(nameof(PreviewWidth));
                QueueLiveApply();
            }
        }
    }

    public double WidgetHeight
    {
        get => _previewHeight;
        set
        {
            var normalized = Math.Clamp(value, 140, 1_000);
            var changed = _isApplyingLayoutMetrics
                ? SetProperty(ref _previewHeight, normalized, nameof(WidgetHeight))
                : SetEditorProperty(ref _previewHeight, normalized, nameof(WidgetHeight));
            if (changed)
            {
                OnPropertyChanged(nameof(PreviewHeight));
                QueueLiveApply();
            }
        }
    }
    public double PreviewModuleWidth { get => _previewModuleWidth; private set => SetProperty(ref _previewModuleWidth, value); }
    public double PreviewModuleHeight { get => _previewModuleHeight; private set => SetProperty(ref _previewModuleHeight, value); }
    public double PreviewCornerRadius { get => _previewCornerRadius; private set => SetProperty(ref _previewCornerRadius, value); }
    public string ResourceCpu
    {
        get => _resourceCpu;
        private set => SetProperty(ref _resourceCpu, value);
    }

    public string ResourceMemory
    {
        get => _resourceMemory;
        private set => SetProperty(ref _resourceMemory, value);
    }

    public double ResourceCpuPercent
    {
        get => _resourceCpuPercent;
        private set => SetProperty(ref _resourceCpuPercent, value);
    }

    public double ResourceMemoryMegabytes
    {
        get => _resourceMemoryMegabytes;
        private set => SetProperty(ref _resourceMemoryMegabytes, value);
    }

    public string ImpactLabel
    {
        get => _impactLabel;
        private set => SetProperty(ref _impactLabel, value);
    }

    public string ResourceWakeups { get; } = "Adaptive";
    public string AppVersion { get; } =
        $"OPS Monitor Studio · v{typeof(StudioViewModel).Assembly.GetName().Version?.ToString(3) ?? "3.3.0"}";
    public string WidgetActionLabel
    {
        get => _widgetActionLabel;
        private set => SetProperty(ref _widgetActionLabel, value);
    }

    public void AdvanceTelemetry()
    {
        RefreshResourceImpact();
        if (!DemoMetrics)
        {
            return;
        }

        _telemetryPhase += 0.34;
        UpdateModule("cpu", 43 + Math.Sin(_telemetryPhase) * 9, value => $"{value:0}%", value => $"TEMP {62 + value * 0.13:0}°C");
        UpdateModule("gpu", 24 + Math.Sin(_telemetryPhase * 0.72 + 1.1) * 15, value => $"{value:0}%", value => $"TEMP {39 + value * 0.18:0}°C");
        UpdateModule("ram", 50 + Math.Sin(_telemetryPhase * 0.23) * 2, value => $"{30.9 * value / 100:0.0} / 30.9 GB", value => $"{value:0}% USED");
        UpdateModule("net", 38 + Math.Sin(_telemetryPhase * 1.42) * 27, value => $"{Math.Max(44, 980 + Math.Sin(_telemetryPhase) * 420):0}K / {Math.Max(8, 31 + Math.Cos(_telemetryPhase * 1.4) * 14):0}K", _ => "BYTES / SEC");
        UpdateModule("latency", 22 + Math.Sin(_telemetryPhase * 0.87) * 6, value => $"{value:0} ms", _ => "LOSS 0%");
        UpdateModule("disk", 12 + Math.Sin(_telemetryPhase * 1.9) * 8, value => $"{value:0}%", _ => "TEMP 42°C");
        UpdateModule("battery", 86 - (_telemetryPhase % 8) * 0.05, value => $"{value:0}%", _ => "2h 48m");
    }

    private void RefreshResourceImpact()
    {
        var processes = new List<Process> { Process.GetCurrentProcess() };
        processes.AddRange(Process.GetProcessesByName("OpsMonitor.Widget"));

        try
        {
            long workingSetBytes = 0;
            var processorTime = TimeSpan.Zero;
            foreach (var process in processes)
            {
                try
                {
                    process.Refresh();
                    workingSetBytes += process.WorkingSet64;
                    processorTime += process.TotalProcessorTime;
                }
                catch (InvalidOperationException)
                {
                    // A widget restart between enumeration and sampling is harmless.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // A protected process should not break the Studio overview.
                }
            }

            ResourceMemoryMegabytes = workingSetBytes / (1024d * 1024d);
            ResourceMemory = string.Create(
                CultureInfo.InvariantCulture,
                $"{ResourceMemoryMegabytes:0} MB");

            var sampledAt = DateTimeOffset.UtcNow;
            if (_lastResourceSampleUtc is { } previousSample)
            {
                var elapsedSeconds = (sampledAt - previousSample).TotalSeconds;
                var processorSeconds =
                    (processorTime - _lastResourceProcessorTime).TotalSeconds;
                if (elapsedSeconds > 0 && processorSeconds >= 0)
                {
                    var wholeMachinePercent = Math.Clamp(
                        (processorSeconds / elapsedSeconds) /
                        Environment.ProcessorCount *
                        100,
                        0,
                        100);
                    ResourceCpuPercent = wholeMachinePercent;
                    ResourceCpu = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{wholeMachinePercent:0.00}%");
                    ImpactLabel = wholeMachinePercent switch
                    {
                        < 0.75 => "LOW",
                        < 2.5 => "MODERATE",
                        _ => "HIGH"
                    };
                }
            }

            _lastResourceSampleUtc = sampledAt;
            _lastResourceProcessorTime = processorTime;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    public void FlushSettings()
    {
        if (!_applyTimer.IsEnabled)
        {
            return;
        }

        _applyTimer.Stop();
        SaveSettings();
    }

    public void RefreshWidgetStatus()
    {
        WidgetActionLabel = WidgetProcessController.IsRunning
            ? "Restart widget"
            : "Open widget";
        WidgetExecutablePath = WidgetProcessController.ExecutablePath ?? "Not found";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _applyTimer.Stop();
        foreach (var module in Modules)
        {
            module.EditorValueChanging -= OnModuleEditorValueChanging;
            module.PropertyChanged -= OnModulePropertyChanged;
        }

        foreach (var sensor in SensorCatalog)
        {
            sensor.PinChanged -= SensorPin_OnChanged;
        }

        if (_runtimeSettingsWatcher is not null)
        {
            _runtimeSettingsWatcher.ReloadRequested -=
                RuntimeSettingsWatcher_OnReloadRequested;
            _runtimeSettingsWatcher.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _settingsSink.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task StartRuntimeSettingsWatcherAsync()
    {
        if (_runtimeSettingsWatcher is null)
        {
            return;
        }

        try
        {
            await _runtimeSettingsWatcher.StartAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                ObjectDisposedException)
        {
            _ = _dispatcher.BeginInvoke(() =>
            {
                if (!_disposed)
                {
                    StatusMessage =
                        $"Live settings sync is unavailable: {exception.Message}";
                }
            });
        }
    }

    private void RuntimeSettingsWatcher_OnReloadRequested(
        object? sender,
        SettingsReloadRequestedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (_disposed)
        {
            return;
        }

        _ = _dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () =>
            {
                if (_disposed || _applyTimer.IsEnabled)
                {
                    return;
                }

                ReloadSettings();
                StatusMessage = "External widget changes synchronized";
                LastAppliedText = "Synced from widget";
            });
    }

    public void ReloadSettings()
    {
        var snapshot = _settingsSink.Reload();
        if (snapshot is null)
        {
            ResetDemo();
            _applyTimer.Stop();
            StatusMessage = "Using polished defaults";
            LastAppliedText = "Defaults ready";
            return;
        }

        _isRestoring = true;
        try
        {
            SelectedLayout = Layouts.Any(layout => layout.Id == snapshot.Layout)
                ? snapshot.Layout
                : "Pill";
            ActiveScene = snapshot.Scene;
            BackgroundOpacity = Math.Clamp(snapshot.BackgroundOpacity, 0.08, 1);
            ContentOpacity = Math.Clamp(snapshot.ContentOpacity, 0.82, 1);
            BlurStrength = Math.Clamp(snapshot.BlurStrength, 0, 40);
            Density = DensityOptions.Contains(snapshot.Density) ? snapshot.Density : "Compact";
            FontScale = Math.Clamp(snapshot.FontScale, 0.9, 1.35);
            AlwaysOnTop = snapshot.AlwaysOnTop;
            PositionLocked = snapshot.PositionLocked;
            ClickThrough = snapshot.ClickThrough;
            StartAtSignIn = snapshot.StartAtSignIn;
            Draggable = snapshot.Draggable;
            Resizable = snapshot.Resizable;
            WidgetScalePercent = snapshot.WidgetScalePercent;
            WidgetWidth = snapshot.WidgetWidth;
            WidgetHeight = snapshot.WidgetHeight;
            UpdateRate = RateLabel(snapshot.UpdateCadenceSeconds);
            PerformanceMode = PerformanceModes.Contains(snapshot.PerformanceMode)
                ? snapshot.PerformanceMode
                : "Balanced";
            ReducedMotion = snapshot.ReducedMotion;
            DemoMetrics = snapshot.DemoMetrics;

            var theme = Themes.FirstOrDefault(item => item.Id == snapshot.Theme) ?? Themes[0];
            foreach (var item in Themes)
            {
                item.IsSelected = item == theme;
            }
            SelectedTheme = theme;
            _designId = snapshot.ThemeDetails?.Id ?? theme.Id;
            _designName = snapshot.ThemeDetails?.Name ?? theme.Name;
            Designer.ApplyPreset(theme);
            if (snapshot.ThemeDetails is not null)
            {
                Designer.Apply(snapshot.ThemeDetails);
            }

            foreach (var module in Modules)
            {
                module.IsVisible = snapshot.VisibleModules.Contains(module.Id);
            }
            ApplyModuleSnapshots(snapshot.Modules);
            RefreshSensorCatalog(snapshot.SensorPins ?? []);

            if (snapshot.Scenes is not null)
            {
                foreach (var scene in Scenes)
                {
                    var mapped = snapshot.Scenes.FirstOrDefault(item =>
                        item.Id.Equals(scene.Id, StringComparison.Ordinal) ||
                        item.Name.Equals(scene.Name, StringComparison.OrdinalIgnoreCase));
                    scene.IsActive = mapped?.IsActive ?? false;
                }
            }
        }
        finally
        {
            _isRestoring = false;
        }
        VisibleModulesView.Refresh();
        RefreshPreviewBrushes();
        RefreshModulePresentation();
        _undo.Clear();
        _redo.Clear();
        RaiseEditorCommandState();
        StatusMessage = string.IsNullOrWhiteSpace(_settingsSink.LastWarning)
            ? "Editor and shared runtime settings reloaded"
            : _settingsSink.LastWarning;
        LastAppliedText = "Loaded from disk";
    }

    public bool ExportDesign(string path)
    {
        try
        {
            var snapshot = CaptureSettings();
            DesignPackageService.Save(path, new StudioDesignPackage(
                LocalStudioSettingsSink.CurrentSchemaVersion,
                $"{_designName} · {SelectedLayout}",
                SelectedLayout,
                Density,
                snapshot.ThemeDetails ?? Designer.Capture("custom", "Custom design"),
                snapshot.Modules ?? []));
            StatusMessage = $"Design exported to {Path.GetFileName(path)}";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            StatusMessage = $"Design export failed: {exception.Message}";
            return false;
        }
    }

    public bool ImportDesign(string path)
    {
        try
        {
            var package = DesignPackageService.Load(path);
            PushUndo();
            _isRestoring = true;
            try
            {
                SelectedLayout = package.Layout;
                Density = package.Density;
                var theme = Themes.FirstOrDefault(item => item.Id == package.Theme.Id) ??
                            SelectedTheme ?? Themes[0];
                foreach (var item in Themes)
                {
                    item.IsSelected = item == theme;
                }

                SelectedTheme = theme;
                _designId = package.Theme.Id;
                _designName = package.Theme.Name;
                Designer.Apply(package.Theme);
                ApplyModuleSnapshots(package.Modules ?? []);
            }
            finally
            {
                _isRestoring = false;
            }

            ApplyLayoutMetrics();
            RefreshPreviewBrushes();
            QueueLiveApply();
            StatusMessage = $"{package.Name} imported and applied";
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            StatusMessage = $"Design import failed: {exception.Message}";
            return false;
        }
    }

    public void SaveSettings()
    {
        if (_isInitializing || _isRestoring)
        {
            return;
        }

        _applyTimer.Stop();
        IDisposable? suppression = null;
        try
        {
            if (_runtimeSettingsWatcher?.IsRunning == true)
            {
                suppression = _runtimeSettingsWatcher.SuppressNotifications();
            }

            var snapshot = CaptureSettings();
            _settingsSink.Save(snapshot);
            StatusMessage = string.IsNullOrWhiteSpace(_settingsSink.LastWarning)
                ? "Live preview and shared widget settings synchronized"
                : _settingsSink.LastWarning;
            LastAppliedText = $"Applied {DateTime.Now:HH:mm:ss}";
        }
        catch (IOException)
        {
            StatusMessage = "Could not write settings; preview remains active";
            LastAppliedText = "Save pending";
        }
        catch (UnauthorizedAccessException)
        {
            StatusMessage = "Settings folder is not writable";
            LastAppliedText = "Save blocked";
        }
        finally
        {
            suppression?.Dispose();
        }
    }

    private static ModuleItem NewModule(
        string id,
        string name,
        string icon,
        string category,
        string description,
        string source,
        string color,
        string primaryValue,
        string secondaryValue,
        double usagePercent,
        bool isVisible = true,
        string size = "Medium")
        => new(id, name, icon, category, description, source, Brush(color), primaryValue, secondaryValue, usagePercent, isVisible, size);

    private static ProviderItem CreateCpuTemperatureProviderItem()
    {
        var probe = ProbeCpuTemperatureBridge();
        return new ProviderItem(
            "CPU temperature sensor",
            "Validated Ryzen package temperature from the secure broker",
            probe.Status,
            probe.Detail,
            probe.IsAvailable ? "1 metric" : "0 live metrics",
            "Optional sensor",
            true,
            Brush(probe.IsAvailable ? "#FF63E6A6" : "#FFFFC95C"));
    }

    private static SolidColorBrush Brush(string value)
    {
        Color color;
        try
        {
            color = ColorConverter.ConvertFromString(value) is Color parsed
                ? parsed
                : Color.FromRgb(67, 231, 210);
        }
        catch (Exception exception) when (
            exception is FormatException or NotSupportedException)
        {
            color = Color.FromRgb(67, 231, 210);
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void UpdateModule(string id, double value, Func<double, string> primary, Func<double, string> secondary)
    {
        var module = Modules.First(item => item.Id == id);
        var normalized = Math.Clamp(value, 0, 100);
        module.UsagePercent = normalized;
        module.PrimaryValue = primary(normalized);
        module.SecondaryValue = secondary(normalized);
        module.SparklinePoints.RemoveAt(0);
        module.SparklinePoints.Add(normalized);
    }

    private void Navigate(object? parameter)
    {
        if (parameter is NavigationItem item)
        {
            SelectedNavigation = item;
        }
        else if (parameter is string id)
        {
            SelectedNavigation = Navigation.FirstOrDefault(item => item.Id == id) ?? SelectedNavigation;
        }
    }

    private void SelectLayout(object? parameter)
    {
        if (parameter is not string layout || !Layouts.Any(item => item.Id == layout))
        {
            return;
        }

        PushUndo();
        SelectedLayout = layout;
        StatusMessage = $"{layout} preview active";
    }

    private void SetScale(object? parameter)
    {
        if (parameter is not string text ||
            !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale))
        {
            return;
        }

        WidgetScalePercent = scale;
        StatusMessage = $"{WidgetScalePercent}% widget scale applied";
    }

    private void ApplyTheme(object? parameter)
    {
        if (parameter is not ThemePreset theme)
        {
            return;
        }

        PushUndo();
        foreach (var item in Themes)
        {
            item.IsSelected = item == theme;
        }

        SelectedTheme = theme;
        _designId = theme.Id;
        _designName = theme.Name;
        Designer.ApplyPreset(theme);
        StatusMessage = $"{theme.Name} theme applied";
    }

    private void ActivateScene(object? parameter)
    {
        if (parameter is not SceneItem scene)
        {
            return;
        }

        PushUndo();
        foreach (var item in Scenes)
        {
            item.IsActive = item == scene;
        }

        ActiveScene = scene.Name;
        SelectedLayout = scene.Layout;
        StatusMessage = $"{scene.Name} scene is now live";
    }

    private void AddModule(object? parameter)
    {
        if (parameter is not ModuleItem module)
        {
            return;
        }

        module.IsVisible = true;
        SelectedModule = module;
        StatusMessage = $"{module.Name} added to {SelectedLayout}";
    }

    private void MoveModule(ModuleItem? module, int direction)
    {
        if (module is null)
        {
            return;
        }

        var current = Modules.IndexOf(module);
        var target = current + direction;
        if (current < 0 || target < 0 || target >= Modules.Count)
        {
            return;
        }

        PushUndo();
        Modules.Move(current, target);
        VisibleModulesView.Refresh();
        StatusMessage = $"{module.Name} reordered";
        QueueLiveApply();
        RaiseEditorCommandState();
    }

    private bool CanMoveModule(ModuleItem? module, int direction)
    {
        if (module is null)
        {
            return false;
        }

        var current = Modules.IndexOf(module);
        var target = current + direction;
        return current >= 0 && target >= 0 && target < Modules.Count;
    }

    private void ResetModuleOverrides(ModuleItem? module)
    {
        if (module is null || !module.HasOverrides)
        {
            return;
        }

        PushUndo();
        _isRestoring = true;
        try
        {
            module.ResetVisualOverrides();
        }
        finally
        {
            _isRestoring = false;
        }

        ApplyModulePreviewDesign(module);
        QueueLiveApply();
        StatusMessage = $"{module.Name} visual overrides cleared";
        RaiseEditorCommandState();
    }

    private void TestProvider(object? parameter)
    {
        if (parameter is not ProviderItem provider)
        {
            return;
        }

        provider.Status = "Testing…";
        provider.Latency = "Bounded probe";
        provider.StatusBrush = Brush("#FFFFC95C");
        _ = TestProviderAsync(provider);
    }

    private async Task TestProviderAsync(ProviderItem provider)
    {
        ProviderProbeResult result;
        try
        {
            result = await Task.Run(() => ProbeProvider(provider.Name));
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            System.Net.NetworkInformation.PingException or
            System.ComponentModel.Win32Exception)
        {
            result = new ProviderProbeResult(
                "Probe failed",
                exception.Message,
                false);
        }

        provider.Status = result.Status;
        provider.Latency = result.Detail;
        provider.StatusBrush = Brush(result.IsAvailable ? "#FF63E6A6" : "#FFFFC95C");
        if (provider.Name.Equals("Hardware sensor catalog", StringComparison.Ordinal))
        {
            RefreshSensorCatalog();
        }
        StatusMessage = $"{provider.Name}: {result.Status}";
    }

    private static ProviderProbeResult ProbeProvider(string providerName) =>
        providerName switch
        {
            "Windows native" => new ProviderProbeResult(
                "Available",
                $"{Environment.ProcessorCount} logical CPUs",
                true),
            "NVIDIA NVML" => ProbeNvidia(),
            "CPU temperature sensor" => ProbeCpuTemperatureBridge(),
            "Network quality" => ProbeNetwork(),
            "Hardware sensor catalog" => ProbeHardwareSensorCatalog(),
            _ => new ProviderProbeResult("Unknown provider", "No probe", false)
        };

    private static ProviderProbeResult ProbeNvidia()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "nvidia-smi.exe",
            Arguments = "--query-gpu=name --format=csv,noheader",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        if (process is null)
        {
            return new ProviderProbeResult("Unavailable", "nvidia-smi did not start", false);
        }

        if (!process.WaitForExit(2_000))
        {
            process.Kill(entireProcessTree: true);
            return new ProviderProbeResult("Timed out", "2 s limit", false);
        }

        var output = process.StandardOutput.ReadToEnd().Trim();
        return process.ExitCode == 0 && output.Length > 0
            ? new ProviderProbeResult("Available", output.Split('\n')[0].Trim(), true)
            : new ProviderProbeResult("Unavailable", "NVIDIA driver did not answer", false);
    }

    private static ProviderProbeResult ProbeCpuTemperatureBridge()
    {
        var readingPath = CpuTemperatureBridgeProvider.GetDefaultReadingPath();
        if (!File.Exists(readingPath))
        {
            var brokerPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "OPS Monitor Sensor",
                "OpsMonitor.SensorBridge.exe");
            return File.Exists(brokerPath)
                ? new ProviderProbeResult(
                    "Ready",
                    "Secure broker installed; open the Widget to start it",
                    false)
                : new ProviderProbeResult(
                    "Setup required",
                    "Run Enable CPU Temperature from the Start menu",
                    false);
        }

        try
        {
            var fields = File.ReadAllText(readingPath).Trim().Split('|');
            if (fields.Length != 2 ||
                !double.TryParse(
                    fields[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var temperature) ||
                !long.TryParse(
                    fields[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var ticks) ||
                !double.IsFinite(temperature) ||
                temperature is < 5 or > 125)
            {
                return new ProviderProbeResult("Invalid reading", "Bridge data malformed", false);
            }

            var publishedUtc = new DateTime(ticks, DateTimeKind.Utc);
            var age = DateTime.UtcNow - publishedUtc;
            if (age <= TimeSpan.FromSeconds(20))
            {
                return new ProviderProbeResult(
                    "Available",
                    $"{temperature:0.#} °C · {Math.Max(0, age.TotalSeconds):0} s old",
                    true);
            }

            return new ProviderProbeResult(
                "Stale reading",
                $"{temperature:0.#} °C · {FormatAge(age)} old",
                false);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentOutOfRangeException)
        {
            return new ProviderProbeResult("Probe failed", exception.Message, false);
        }
    }

    private static ProviderProbeResult ProbeHardwareSensorCatalog()
    {
        string path = HardwareSensorBridgeProvider.GetDefaultSnapshotPath();
        if (!File.Exists(path))
        {
            return new ProviderProbeResult(
                "Waiting",
                "Open the widget to start the protected hardware broker",
                false);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            int count = root.TryGetProperty("sensors", out JsonElement sensors) &&
                        sensors.ValueKind == JsonValueKind.Array
                ? sensors.GetArrayLength()
                : 0;
            DateTimeOffset timestamp = root.TryGetProperty("timestampUtc", out JsonElement time) &&
                                       time.TryGetDateTimeOffset(out DateTimeOffset parsed)
                ? parsed
                : DateTimeOffset.MinValue;
            TimeSpan age = DateTimeOffset.UtcNow - timestamp;
            bool live = count > 0 && age <= TimeSpan.FromSeconds(20);
            return new ProviderProbeResult(
                live ? "Available" : "Stale",
                $"{count} sensors · {Math.Max(0, age.TotalSeconds):0} s old",
                live);
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ProviderProbeResult("Invalid snapshot", exception.Message, false);
        }
    }

    private static ProviderProbeResult ProbeNetwork()
    {
        using var ping = new System.Net.NetworkInformation.Ping();
        var reply = ping.Send("1.1.1.1", 1_200);
        return reply?.Status == System.Net.NetworkInformation.IPStatus.Success
            ? new ProviderProbeResult(
                "Available",
                $"{reply.RoundtripTime} ms to 1.1.1.1",
                true)
            : new ProviderProbeResult(
                "No reply",
                reply?.Status.ToString() ?? "Probe unavailable",
                false);
    }

    private static string FormatAge(TimeSpan age) =>
        age.TotalHours >= 1
            ? $"{age.TotalHours:0.#} h"
            : age.TotalMinutes >= 1
                ? $"{age.TotalMinutes:0} min"
                : $"{Math.Max(0, age.TotalSeconds):0} s";

    private sealed record ProviderProbeResult(
        string Status,
        string Detail,
        bool IsAvailable);

    private bool FilterSensorCatalog(object item)
    {
        if (item is not SensorCatalogItem sensor ||
            string.IsNullOrWhiteSpace(SensorSearchText))
        {
            return true;
        }

        return sensor.Name.Contains(SensorSearchText, StringComparison.OrdinalIgnoreCase) ||
               sensor.Hardware.Contains(SensorSearchText, StringComparison.OrdinalIgnoreCase) ||
               sensor.SensorType.Contains(SensorSearchText, StringComparison.OrdinalIgnoreCase) ||
               sensor.ModuleLabel.Contains(SensorSearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshSensorCatalog(
        IReadOnlyList<StudioSensorPinSnapshot>? requestedPins = null)
    {
        var pins = requestedPins is null
            ? SensorCatalog.Where(item => item.IsPinned)
                .ToDictionary(item => item.MetricId, item => item.ModuleId, StringComparer.Ordinal)
            : requestedPins
                .Where(item => !string.IsNullOrWhiteSpace(item.MetricId))
                .GroupBy(item => item.MetricId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().ModuleId, StringComparer.Ordinal);

        foreach (SensorCatalogItem item in SensorCatalog)
        {
            item.PinChanged -= SensorPin_OnChanged;
        }
        SensorCatalog.Clear();

        string path = HardwareSensorBridgeProvider.GetDefaultSnapshotPath();
        if (!File.Exists(path))
        {
            SensorCatalogStatus = "No catalog yet. Open the widget; its broker will publish sensors automatically.";
            return;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using JsonDocument document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("sensors", out JsonElement sensors) ||
                sensors.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("Sensor array is missing.");
            }

            foreach (JsonElement sensor in sensors.EnumerateArray())
            {
                string identifier = JsonString(sensor, "sensorIdentifier");
                string name = JsonString(sensor, "sensorName");
                string hardware = JsonString(sensor, "hardwareName");
                string hardwareType = JsonString(sensor, "hardwareType");
                string sensorType = JsonString(sensor, "sensorType");
                if (string.IsNullOrWhiteSpace(identifier) ||
                    string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(sensorType))
                {
                    continue;
                }

                string metricId = HardwareSensorBridgeProvider.GetMetricId(identifier).Value;
                string moduleId = pins.TryGetValue(metricId, out string? pinnedModule)
                    ? NormalizeSensorModule(pinnedModule)
                    : SuggestSensorModule(hardwareType, identifier);
                double? value = sensor.TryGetProperty("value", out JsonElement valueElement) &&
                                valueElement.TryGetDouble(out double parsedValue)
                    ? parsedValue
                    : null;
                var item = new SensorCatalogItem(
                    metricId,
                    name,
                    hardware,
                    sensorType,
                    FormatSensorValue(sensorType, value),
                    moduleId,
                    pins.ContainsKey(metricId));
                item.PinChanged += SensorPin_OnChanged;
                SensorCatalog.Add(item);
            }

            SensorCatalogStatus = SensorCatalog.Count == 0
                ? "The broker is live but exposed no compatible sensors."
                : $"{SensorCatalog.Count} sensors · pin up to 3 details per module";
            SensorCatalogView.Refresh();
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            SensorCatalogStatus = $"Catalog unavailable: {exception.Message}";
        }
    }

    private void SensorPin_OnChanged(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (_isRestoring)
        {
            return;
        }

        if (sender is SensorCatalogItem { IsPinned: true } pinned &&
            SensorCatalog.Count(item =>
                item.IsPinned &&
                item.ModuleId.Equals(pinned.ModuleId, StringComparison.Ordinal)) > 3)
        {
            pinned.PinChanged -= SensorPin_OnChanged;
            pinned.IsPinned = false;
            pinned.PinChanged += SensorPin_OnChanged;
            SensorCatalogStatus = $"{pinned.ModuleLabel} already has the maximum 3 optional details.";
            return;
        }

        QueueLiveApply();
        SensorCatalogStatus = $"{SensorCatalog.Count(item => item.IsPinned)} sensor details pinned";
    }

    private static string JsonString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static string SuggestSensorModule(string hardwareType, string identifier)
    {
        string value = $"{hardwareType} {identifier}".ToLowerInvariant();
        if (value.Contains("gpu"))
        {
            return "gpu";
        }
        if (value.Contains("storage") || value.Contains("nvme"))
        {
            return "disk";
        }
        if (value.Contains("memory"))
        {
            return "ram";
        }
        return "cpu";
    }

    private static string NormalizeSensorModule(string value) =>
        value is "cpu" or "gpu" or "ram" or "disk" ? value : "cpu";

    private static string FormatSensorValue(string sensorType, double? value)
    {
        if (value is not { } actual || !double.IsFinite(actual))
        {
            return "N/A";
        }

        return sensorType.ToLowerInvariant() switch
        {
            "temperature" => $"{actual:0.#} °C",
            "load" or "level" or "control" => $"{actual:0.#}%",
            "clock" or "frequency" => $"{actual:0} MHz",
            "power" => $"{actual:0.#} W",
            "voltage" => $"{actual:0.###} V",
            "fan" => $"{actual:0} RPM",
            "data" => $"{actual:0.##} GB",
            "smalldata" => $"{actual:0.##} MB",
            "throughput" => FormatSensorRate(actual),
            _ => actual.ToString("0.##", CultureInfo.InvariantCulture)
        };
    }

    private static string FormatSensorRate(double bytesPerSecond)
    {
        var magnitude = Math.Abs(bytesPerSecond);
        return magnitude switch
        {
            >= 1_000_000_000d => $"{bytesPerSecond / 1_000_000_000d:0.##} GB/s",
            >= 1_000_000d => $"{bytesPerSecond / 1_000_000d:0.##} MB/s",
            >= 1_000d => $"{bytesPerSecond / 1_000d:0.##} KB/s",
            _ => $"{bytesPerSecond:0} B/s"
        };
    }

    private void CopyDiagnostics()
    {
        RequestCopyDiagnostics?.Invoke(this, EventArgs.Empty);
        StatusMessage = "Diagnostics copied to clipboard";
    }

    private void OpenOrRestartWidget()
    {
        FlushSettings();
        var result = WidgetProcessController.OpenOrRestart();
        StatusMessage = result.Message;
        RefreshWidgetStatus();
    }

    private void ResetDemo()
    {
        _isRestoring = true;
        BackgroundOpacity = 0.82;
        ContentOpacity = 1;
        BlurStrength = 24;
        FontScale = 1;
        Density = "Compact";
        SelectedLayout = "Pill";
        AlwaysOnTop = true;
        PositionLocked = false;
        ClickThrough = false;
        Draggable = true;
        Resizable = true;
        StartAtSignIn = true;
        WidgetScalePercent = 100;
        UpdateRate = "2 seconds";
        PerformanceMode = "Balanced";
        ReducedMotion = false;
        DemoMetrics = true;

        var defaultOrder = new[] { "cpu", "gpu", "ram", "net", "latency", "disk", "battery", "weather" };
        for (var targetIndex = 0; targetIndex < defaultOrder.Length; targetIndex++)
        {
            var module = Modules.First(item => item.Id == defaultOrder[targetIndex]);
            var currentIndex = Modules.IndexOf(module);
            if (currentIndex != targetIndex)
            {
                Modules.Move(currentIndex, targetIndex);
            }
        }

        foreach (var module in Modules)
        {
            module.IsVisible = module.Id is "cpu" or "gpu" or "ram" or "net" or "latency" or "weather";
            module.Size = module.Id is "cpu" or "gpu" ? "Large" : "Medium";
            module.Visualization = "Value + sparkline";
            module.ShowLabel = true;
            module.ShowSparkline = true;
            module.ShowTemperature = true;
            module.Precision = "Whole numbers";
            module.CustomTitle = module.Name;
            module.CustomIcon = module.Icon;
            module.UseCustomAccent = false;
            module.UseCustomCardColor = false;
            module.UseCustomBorderColor = false;
            module.UseCustomPrimaryTextColor = false;
            module.UseCustomSecondaryTextColor = false;
            module.UseCustomTrackColor = false;
            module.ShowIcon = true;
            module.ShowAccent = true;
            module.CardOpacity = 1;
            module.BorderOpacity = 1;
            module.CardCornerRadius = -1;
            module.CardBorderWidth = -1;
            module.CardGap = -1;
            module.CardPadding = -1;
            module.AccentWidth = -1;
            module.ProgressHeight = -1;
            module.ProgressCornerRadius = -1;
            module.SparklineThickness = -1;
            module.SparklineFillOpacity = -1;
            module.LabelSize = -1;
            module.SecondarySize = -1;
            module.ValueSize = -1;
            module.IconSize = -1;
            module.LabelWeight = -1;
            module.ValueWeight = -1;
        }

        foreach (var scene in Scenes)
        {
            scene.IsActive = scene.Id == "daily";
        }

        ActiveScene = "Daily driver";
        RefreshSensorCatalog([]);
        SelectedModule = Modules[0];
        ApplyTheme(Themes[0]);
        ApplyLayoutMetrics();
        _isRestoring = false;
        _undo.Clear();
        _redo.Clear();
        RaiseEditorCommandState();
        VisibleModulesView.Refresh();
        RefreshPreviewBrushes();
        StatusMessage = "Studio defaults restored";
        QueueLiveApply();
    }

    private void ApplyModuleSnapshots(IReadOnlyList<StudioModuleSnapshot>? snapshots)
    {
        if (snapshots is null || snapshots.Count == 0)
        {
            return;
        }

        var supportedIds = Modules.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var applicable = snapshots
            .Where(item => supportedIds.Contains(item.Id))
            .OrderBy(item => item.Order)
            .ToArray();
        var snapshotIds = applicable.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var snapshot in applicable)
        {
            var module = Modules.First(item =>
                item.Id.Equals(snapshot.Id, StringComparison.Ordinal));

            module.Name = snapshot.Name;
            module.Icon = snapshot.Icon;
            module.CustomTitle = snapshot.Name;
            module.CustomIcon = snapshot.Icon;
            if (!string.IsNullOrWhiteSpace(snapshot.Accent))
            {
                module.AccentHex = snapshot.Accent;
            }
            module.UseCustomAccent = snapshot.UseCustomAccent && !string.IsNullOrWhiteSpace(snapshot.Accent);
            ApplyOptionalModuleColor(snapshot.CardColor, value => module.CardHex = value);
            ApplyOptionalModuleColor(snapshot.BorderColor, value => module.BorderHex = value);
            ApplyOptionalModuleColor(snapshot.PrimaryTextColor, value => module.PrimaryTextHex = value);
            ApplyOptionalModuleColor(snapshot.SecondaryTextColor, value => module.SecondaryTextHex = value);
            ApplyOptionalModuleColor(snapshot.TrackColor, value => module.TrackHex = value);
            module.UseCustomCardColor = !string.IsNullOrWhiteSpace(snapshot.CardColor);
            module.UseCustomBorderColor = !string.IsNullOrWhiteSpace(snapshot.BorderColor);
            module.UseCustomPrimaryTextColor = !string.IsNullOrWhiteSpace(snapshot.PrimaryTextColor);
            module.UseCustomSecondaryTextColor = !string.IsNullOrWhiteSpace(snapshot.SecondaryTextColor);
            module.UseCustomTrackColor = !string.IsNullOrWhiteSpace(snapshot.TrackColor);
            module.IsVisible = snapshot.Enabled;
            module.Size = snapshot.Size;
            module.Visualization = snapshot.Visualization;
            module.ShowLabel = snapshot.ShowLabel;
            module.ShowSparkline = snapshot.ShowSparkline;
            module.ShowTemperature = snapshot.ShowTemperature;
            module.Precision = snapshot.Precision;
            module.ShowIcon = snapshot.ShowIcon;
            module.ShowAccent = snapshot.ShowAccent;
            module.CardOpacity = snapshot.CardOpacity;
            module.BorderOpacity = snapshot.BorderOpacity;
            module.CardCornerRadius = snapshot.CardCornerRadiusOverride ?? -1;
            module.CardBorderWidth = snapshot.CardBorderWidthOverride ?? -1;
            module.CardGap = snapshot.CardGapOverride ?? -1;
            module.CardPadding = snapshot.CardPaddingOverride ?? -1;
            module.AccentWidth = snapshot.AccentWidthOverride ?? -1;
            module.ProgressHeight = snapshot.ProgressHeightOverride ?? -1;
            module.ProgressCornerRadius = snapshot.ProgressCornerRadiusOverride ?? -1;
            module.SparklineThickness = snapshot.SparklineThicknessOverride ?? -1;
            module.SparklineFillOpacity = snapshot.SparklineFillOpacityOverride ?? -1;
            module.LabelSize = snapshot.LabelSizeOverride ?? -1;
            module.SecondarySize = snapshot.SecondarySizeOverride ?? -1;
            module.ValueSize = snapshot.ValueSizeOverride ?? -1;
            module.IconSize = snapshot.IconSizeOverride ?? -1;
            module.LabelWeight = snapshot.LabelWeightOverride ?? -1;
            module.ValueWeight = snapshot.ValueWeightOverride ?? -1;
        }

        var ordered = applicable
            .Select(item => Modules.First(module =>
                module.Id.Equals(item.Id, StringComparison.Ordinal)))
            .Concat(Modules.Where(module => !snapshotIds.Contains(module.Id)))
            .ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var oldIndex = Modules.IndexOf(ordered[index]);
            if (oldIndex != index)
            {
                Modules.Move(oldIndex, index);
            }
        }

        VisibleModulesView.Refresh();
    }

    private static void ApplyOptionalModuleColor(string? color, Action<string> apply)
    {
        if (!string.IsNullOrWhiteSpace(color))
        {
            apply(color);
        }
    }

    private static double RateSeconds(string rate)
        => rate switch
        {
            "0.5 seconds" => 0.5,
            "1 second" => 1,
            "5 seconds" => 5,
            "10 seconds" => 10,
            _ => 2,
        };

    private static string RateLabel(double seconds)
    {
        if (seconds <= 0.75)
        {
            return "0.5 seconds";
        }

        if (seconds <= 1.5)
        {
            return "1 second";
        }

        if (seconds >= 7.5)
        {
            return "10 seconds";
        }

        if (seconds >= 4)
        {
            return "5 seconds";
        }

        return "2 seconds";
    }

    private void OnModulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Preview-only notifications are outputs of ApplyPreviewDesign. Feeding them
        // back into that method recursively overflows the WPF dispatcher stack.
        if (e.PropertyName?.StartsWith("Preview", StringComparison.Ordinal) == true)
        {
            return;
        }

        if (e.PropertyName == nameof(ModuleItem.IsVisible))
        {
            VisibleModulesView.Refresh();
            if (!_isRestoring)
            {
                ApplyLayoutMetrics();
            }
        }

        if (e.PropertyName is nameof(ModuleItem.UseCustomAccent) or nameof(ModuleItem.AccentHex))
        {
            RefreshModuleAccentBrushes();
        }

        if (e.PropertyName is nameof(ModuleItem.UsagePercent)
            or nameof(ModuleItem.PrimaryValue)
            or nameof(ModuleItem.SecondaryValue))
        {
            return;
        }

        if (sender is ModuleItem module)
        {
            ApplyModulePreviewDesign(module);
        }

        (ResetModuleOverridesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        QueueLiveApply();
    }

    private void OnModuleEditorValueChanging(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        PushUndo();
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

        PushUndo();
        return SetProperty(ref field, value, propertyName);
    }

    private void SetAndQueue<T>(
        ref T field,
        T value,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (SetEditorProperty(ref field, value, propertyName))
        {
            QueueLiveApply();
        }
    }

    private void QueueLiveApply()
    {
        if (_isInitializing || _isRestoring)
        {
            return;
        }

        LastAppliedText = "Applying…";
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private StudioSettingsSnapshot CaptureSettings()
    {
        return new StudioSettingsSnapshot(
            ActiveScene,
            SelectedLayout,
            _designId,
            BackgroundOpacity,
            ContentOpacity,
            BlurStrength,
            Density,
            FontScale,
            AlwaysOnTop,
            PositionLocked,
            ClickThrough,
            StartAtSignIn,
            false,
            Modules.Where(item => item.IsVisible).Select(item => item.Id).ToArray(),
            Draggable,
            Resizable,
            WidgetWidth,
            WidgetHeight,
            WidgetScalePercent,
            RateSeconds(UpdateRate),
            PerformanceMode,
            true,
            ReducedMotion,
            Modules.Select((item, order) => new StudioModuleSnapshot(
                item.Id,
                string.IsNullOrWhiteSpace(item.CustomTitle) ? item.Name : item.CustomTitle,
                order,
                item.IsVisible,
                item.Size,
                item.Visualization,
                item.ShowLabel,
                item.ShowSparkline,
                item.ShowTemperature,
                item.Precision,
                item.CustomIcon,
                item.UseCustomAccent ? item.AccentHex : string.Empty)
            {
                UseCustomAccent = item.UseCustomAccent,
                CardColor = item.UseCustomCardColor ? item.CardHex : string.Empty,
                BorderColor = item.UseCustomBorderColor ? item.BorderHex : string.Empty,
                PrimaryTextColor = item.UseCustomPrimaryTextColor ? item.PrimaryTextHex : string.Empty,
                SecondaryTextColor = item.UseCustomSecondaryTextColor ? item.SecondaryTextHex : string.Empty,
                TrackColor = item.UseCustomTrackColor ? item.TrackHex : string.Empty,
                ShowIcon = item.ShowIcon,
                ShowAccent = item.ShowAccent,
                CardOpacity = item.CardOpacity,
                BorderOpacity = item.BorderOpacity,
                CardCornerRadiusOverride = OverrideValue(item.CardCornerRadius),
                CardBorderWidthOverride = OverrideValue(item.CardBorderWidth),
                CardGapOverride = OverrideValue(item.CardGap),
                CardPaddingOverride = OverrideValue(item.CardPadding),
                AccentWidthOverride = OverrideValue(item.AccentWidth),
                ProgressHeightOverride = OverrideValue(item.ProgressHeight),
                ProgressCornerRadiusOverride = OverrideValue(item.ProgressCornerRadius),
                SparklineThicknessOverride = OverrideValue(item.SparklineThickness),
                SparklineFillOpacityOverride = OverrideValue(item.SparklineFillOpacity),
                LabelSizeOverride = OverrideValue(item.LabelSize),
                SecondarySizeOverride = OverrideValue(item.SecondarySize),
                ValueSizeOverride = OverrideValue(item.ValueSize),
                IconSizeOverride = OverrideValue(item.IconSize),
                LabelWeightOverride = OverrideValue(item.LabelWeight),
                ValueWeightOverride = OverrideValue(item.ValueWeight)
            }).ToArray(),
            Designer.Capture(_designId, _designName),
            Scenes.Select(item => new StudioSceneSnapshot(
                item.Id,
                item.Name,
                item.Layout,
                item.IsActive)).ToArray(),
            null,
            DemoMetrics,
            LocalStudioSettingsSink.CurrentSchemaVersion,
            SensorCatalog.Where(item => item.IsPinned)
                .GroupBy(item => item.ModuleId, StringComparer.Ordinal)
                .SelectMany(group => group.Take(3))
                .Select(item => new StudioSensorPinSnapshot(
                    item.MetricId,
                    item.ModuleId))
                .ToArray());
    }

    private static double? OverrideValue(double value) => value < 0 ? null : value;
    private static int? OverrideValue(int value) => value < 0 ? null : value;

    private void RefreshPreviewBrushes()
    {
        const double guardActivationOpacity = 0.82;
        const double fullGuardOpacity = 0.55;
        var guardStrength = Math.Clamp(
            (guardActivationOpacity - BackgroundOpacity) /
            (guardActivationOpacity - fullGuardOpacity),
            0,
            1);
        var cardOpacity = Designer.CardOpacity;

        PreviewSurfaceBrush = OpacityBrush(
            ColorText.Parse(Designer.Surface, Colors.Black), BackgroundOpacity);
        PreviewCardBrush = OpacityBrush(
            ColorText.Parse(Designer.Card, Colors.Black), cardOpacity);
        PreviewBorderBrush = OpacityBrush(
            ColorText.Parse(Designer.Border, Colors.DimGray),
            Math.Max(BackgroundOpacity * 0.54, 0.5 * guardStrength));
        PreviewAccentBrush = new SolidColorBrush(
            ColorText.Parse(Designer.CpuAccent, Colors.Cyan));
    }

    private void CheckForUpdates()
    {
        var updateScript = Path.Combine(AppContext.BaseDirectory, "Update.ps1");
        if (!File.Exists(updateScript))
        {
            StatusMessage = "Updater is available in installed release builds";
            return;
        }

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory
            };
            start.ArgumentList.Add("-NoLogo");
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-WindowStyle");
            start.ArgumentList.Add("Hidden");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(updateScript);
            start.ArgumentList.Add("-Interactive");
            using var process = Process.Start(start);
            StatusMessage = process is null
                ? "Windows did not start the updater"
                : "Update check opened";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            IOException)
        {
            StatusMessage = $"Update check failed: {exception.Message}";
        }
    }

    private void RaiseProductionPreviewContext()
    {
        OnPropertyChanged(nameof(TextPrimaryBrush));
        OnPropertyChanged(nameof(TextSecondaryBrush));
        OnPropertyChanged(nameof(TrackBrush));
        OnPropertyChanged(nameof(WidgetFontFamily));
        OnPropertyChanged(nameof(LabelFontSize));
        OnPropertyChanged(nameof(ValueFontSize));
        OnPropertyChanged(nameof(LabelFontWeight));
        OnPropertyChanged(nameof(ValueFontWeight));
        OnPropertyChanged(nameof(UseTabularNumbers));
    }

    private static SolidColorBrush FrozenBrush(string colorText, Color fallback)
    {
        var brush = new SolidColorBrush(ColorText.Parse(colorText, fallback));
        brush.Freeze();
        return brush;
    }

    private void RefreshModuleAccentBrushes()
    {
        foreach (var module in Modules)
        {
            var color = module.UseCustomAccent
                ? module.AccentHex
                : module.Id switch
                {
                    "gpu" => Designer.GpuAccent,
                    "ram" or "disk" or "battery" => Designer.MemoryAccent,
                    "net" => Designer.NetworkAccent,
                    "latency" => Designer.LatencyAccent,
                    "weather" => Designer.WeatherAccent,
                    _ => Designer.CpuAccent
                };
            module.SetPreviewAccent(new SolidColorBrush(ColorText.Parse(color, Colors.Cyan)));
        }

    }

    private void RefreshModulePresentation()
    {
        foreach (var module in Modules)
        {
            ApplyModulePreviewDesign(module);
        }
    }

    private void ApplyModulePreviewDesign(ModuleItem module)
    {
        var maximumCardPadding = (SelectedLayout, Density) switch
        {
            ("Mini", _) => 1,
            ("Rail", "Compact") => 3,
            ("Pill", "Compact") => 5,
            _ => 12,
        };
        module.ApplyPreviewDesign(Designer, maximumCardPadding);
    }

    private static SolidColorBrush OpacityBrush(Color color, double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255),
            color.R,
            color.G,
            color.B));
        brush.Freeze();
        return brush;
    }

    private void ApplyLayoutMetrics()
    {
        var visibleModuleCount = Modules.Count(item => item.IsVisible);
        var recommendation = CalculateWidgetSize(
            SelectedLayout,
            Density,
            visibleModuleCount,
            WidgetScalePercent);
        var scale = Math.Clamp(WidgetScalePercent, 80, 160) / 100d;
        var footprintScale = scale < 1 &&
                             SelectedLayout is "Pill" or "Mini" &&
                             Density == "Compact"
            ? scale
            : Math.Max(1, scale);
        var moduleHeight = (SelectedLayout, Density) switch
        {
            ("Rail", "Compact") => 39,
            ("Rail", "Airy") => 144,
            ("Rail", _) => 104,
            ("Dock", _) => 72,
            ("Mini", "Compact") => 30,
            ("Mini", "Airy") => 46,
            ("Mini", _) => 38,
            ("Pill", "Compact") => 60,
            ("Pill", "Airy") => 144,
            _ => 104,
        };
        var moduleWidth = SelectedLayout == "Dock"
            ? 132 * footprintScale
            : Math.Max(112, recommendation.SuggestedWidth - 24);
        var cornerRadius = SelectedLayout switch
        {
            "Dock" => 24,
            "Pill" => 27,
            "Mini" => 16,
            _ => 19
        };
        _isApplyingLayoutMetrics = true;
        try
        {
            var previewModuleHeight = SelectedLayout == "Mini"
                ? Math.Max(29, moduleHeight * footprintScale)
                : moduleHeight * footprintScale;
            (WidgetWidth, WidgetHeight, PreviewModuleWidth, PreviewModuleHeight, PreviewCornerRadius) =
                (recommendation.SuggestedWidth,
                    recommendation.SuggestedHeight,
                    moduleWidth,
                    previewModuleHeight,
                    cornerRadius * footprintScale);
        }
        finally
        {
            _isApplyingLayoutMetrics = false;
        }

        OnPropertyChanged(nameof(WidgetWidth));
        OnPropertyChanged(nameof(WidgetHeight));
        RefreshModulePresentation();
    }

    internal static OpsMonitor.Core.Settings.WidgetSizeRecommendation CalculateWidgetSize(
        string layout,
        string density,
        int visibleModuleCount,
        int scalePercent)
        => OpsMonitor.Core.Settings.WidgetSizingPolicy.Calculate(
            layout switch
            {
                "Dock" => OpsMonitor.Core.Settings.WidgetDesign.Dock,
                "Rail" => OpsMonitor.Core.Settings.WidgetDesign.Rail,
                "Mini" => OpsMonitor.Core.Settings.WidgetDesign.Canvas,
                _ => OpsMonitor.Core.Settings.WidgetDesign.Pill
            },
            density switch
            {
                "Compact" => OpsMonitor.Core.Settings.WidgetDensity.Compact,
                "Airy" => OpsMonitor.Core.Settings.WidgetDensity.Comfortable,
                _ => OpsMonitor.Core.Settings.WidgetDensity.Normal
            },
            visibleModuleCount,
            scalePercent);

    private void PushUndo()
    {
        if (_isInitializing || _isRestoring)
        {
            return;
        }

        _undo.Push(CaptureEditorState());
        _redo.Clear();
        RaiseEditorCommandState();
    }

    private void RunDesignerTransaction(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        PushUndo();
        _isRestoring = true;
        try
        {
            action();
        }
        finally
        {
            _isRestoring = false;
        }

        RefreshModuleAccentBrushes();
        RefreshPreviewBrushes();
        QueueLiveApply();
    }

    private EditorState CaptureEditorState()
        => new(
            SelectedLayout,
            ActiveScene,
            SelectedTheme?.Id ?? "void",
            BackgroundOpacity,
            ContentOpacity,
            BlurStrength,
            FontScale,
            Density,
            AlwaysOnTop,
            PositionLocked,
            ClickThrough,
            Draggable,
            Resizable,
            StartAtSignIn,
            ReducedMotion,
            DemoMetrics,
            WidgetScalePercent,
            UpdateRate,
            PerformanceMode,
            WidgetWidth,
            WidgetHeight,
            Designer.Capture(_designId, _designName),
            Modules.Select(item => new ModuleState(
                item.Id,
                item.IsVisible,
                item.Size,
                item.Visualization,
                item.Precision,
                item.ShowLabel,
                item.ShowSparkline,
                item.ShowTemperature,
                item.CustomTitle,
                item.CustomIcon,
                item.AccentHex,
                item.UseCustomAccent,
                item.CardHex,
                item.BorderHex,
                item.PrimaryTextHex,
                item.SecondaryTextHex,
                item.TrackHex,
                item.UseCustomCardColor,
                item.UseCustomBorderColor,
                item.UseCustomPrimaryTextColor,
                item.UseCustomSecondaryTextColor,
                item.UseCustomTrackColor,
                item.ShowIcon,
                item.ShowAccent,
                item.CardOpacity,
                item.BorderOpacity,
                item.CardCornerRadius,
                item.CardBorderWidth,
                item.CardGap,
                item.CardPadding,
                item.AccentWidth,
                item.ProgressHeight,
                item.ProgressCornerRadius,
                item.SparklineThickness,
                item.SparklineFillOpacity,
                item.LabelSize,
                item.SecondarySize,
                item.ValueSize,
                item.IconSize,
                item.LabelWeight,
                item.ValueWeight)).ToArray());

    private void RestoreEditorState(EditorState state)
    {
        _isRestoring = true;
        try
        {
            SelectedLayout = state.Layout;
            BackgroundOpacity = state.BackgroundOpacity;
            ContentOpacity = state.ContentOpacity;
            BlurStrength = state.BlurStrength;
            FontScale = state.FontScale;
            Density = state.Density;
            AlwaysOnTop = state.AlwaysOnTop;
            PositionLocked = state.PositionLocked;
            ClickThrough = state.ClickThrough;
            Draggable = state.Draggable;
            Resizable = state.Resizable;
            StartAtSignIn = state.StartAtSignIn;
            ReducedMotion = state.ReducedMotion;
            DemoMetrics = state.DemoMetrics;
            WidgetScalePercent = state.WidgetScalePercent;
            UpdateRate = state.UpdateRate;
            PerformanceMode = state.PerformanceMode;
            var theme = Themes.FirstOrDefault(item => item.Id == state.ThemeId) ?? Themes[0];
            foreach (var item in Themes)
            {
                item.IsSelected = item == theme;
            }

            SelectedTheme = theme;
            _designId = state.DesignerTheme.Id;
            _designName = state.DesignerTheme.Name;
            Designer.Apply(state.DesignerTheme);
            ActiveScene = state.ActiveScene;
            foreach (var scene in Scenes)
            {
                scene.IsActive = scene.Name.Equals(
                    state.ActiveScene,
                    StringComparison.OrdinalIgnoreCase);
            }

            var stateById = state.Modules.ToDictionary(item => item.Id);
            foreach (var module in Modules)
            {
                if (stateById.TryGetValue(module.Id, out var moduleState))
                {
                    module.IsVisible = moduleState.Visible;
                    module.Size = moduleState.Size;
                    module.Visualization = moduleState.Visualization;
                    module.Precision = moduleState.Precision;
                    module.ShowLabel = moduleState.ShowLabel;
                    module.ShowSparkline = moduleState.ShowSparkline;
                    module.ShowTemperature = moduleState.ShowTemperature;
                    module.CustomTitle = moduleState.CustomTitle;
                    module.CustomIcon = moduleState.CustomIcon;
                    module.AccentHex = moduleState.AccentHex;
                    module.UseCustomAccent = moduleState.UseCustomAccent;
                    module.CardHex = moduleState.CardHex;
                    module.BorderHex = moduleState.BorderHex;
                    module.PrimaryTextHex = moduleState.PrimaryTextHex;
                    module.SecondaryTextHex = moduleState.SecondaryTextHex;
                    module.TrackHex = moduleState.TrackHex;
                    module.UseCustomCardColor = moduleState.UseCustomCardColor;
                    module.UseCustomBorderColor = moduleState.UseCustomBorderColor;
                    module.UseCustomPrimaryTextColor = moduleState.UseCustomPrimaryTextColor;
                    module.UseCustomSecondaryTextColor = moduleState.UseCustomSecondaryTextColor;
                    module.UseCustomTrackColor = moduleState.UseCustomTrackColor;
                    module.ShowIcon = moduleState.ShowIcon;
                    module.ShowAccent = moduleState.ShowAccent;
                    module.CardOpacity = moduleState.CardOpacity;
                    module.BorderOpacity = moduleState.BorderOpacity;
                    module.CardCornerRadius = moduleState.CardCornerRadius;
                    module.CardBorderWidth = moduleState.CardBorderWidth;
                    module.CardGap = moduleState.CardGap;
                    module.CardPadding = moduleState.CardPadding;
                    module.AccentWidth = moduleState.AccentWidth;
                    module.ProgressHeight = moduleState.ProgressHeight;
                    module.ProgressCornerRadius = moduleState.ProgressCornerRadius;
                    module.SparklineThickness = moduleState.SparklineThickness;
                    module.SparklineFillOpacity = moduleState.SparklineFillOpacity;
                    module.LabelSize = moduleState.LabelSize;
                    module.SecondarySize = moduleState.SecondarySize;
                    module.ValueSize = moduleState.ValueSize;
                    module.IconSize = moduleState.IconSize;
                    module.LabelWeight = moduleState.LabelWeight;
                    module.ValueWeight = moduleState.ValueWeight;
                }
            }

            var ordered = state.Modules
                .Select(item => Modules.FirstOrDefault(module => module.Id == item.Id))
                .Where(module => module is not null)
                .Cast<ModuleItem>()
                .Concat(Modules.Where(module => state.Modules.All(item => item.Id != module.Id)))
                .ToArray();
            for (var index = 0; index < ordered.Length; index++)
            {
                var current = Modules.IndexOf(ordered[index]);
                if (current != index)
                {
                    Modules.Move(current, index);
                }
            }

            ApplyLayoutMetrics();
            WidgetWidth = state.WidgetWidth;
            WidgetHeight = state.WidgetHeight;
        }
        finally
        {
            _isRestoring = false;
        }

        VisibleModulesView.Refresh();
        RefreshPreviewBrushes();
        QueueLiveApply();
    }

    private void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Push(CaptureEditorState());
        RestoreEditorState(_undo.Pop());
        StatusMessage = "Last layout change undone";
        RaiseEditorCommandState();
    }

    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Push(CaptureEditorState());
        RestoreEditorState(_redo.Pop());
        StatusMessage = "Layout change restored";
        RaiseEditorCommandState();
    }

    private void RaiseEditorCommandState()
    {
        (UndoCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RedoCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SelectLayoutCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ApplyThemeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ActivateSceneCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveModuleUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveModuleDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ResetModuleOverridesCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private sealed record ModuleState(
        string Id,
        bool Visible,
        string Size,
        string Visualization,
        string Precision,
        bool ShowLabel,
        bool ShowSparkline,
        bool ShowTemperature,
        string CustomTitle,
        string CustomIcon,
        string AccentHex,
        bool UseCustomAccent,
        string CardHex,
        string BorderHex,
        string PrimaryTextHex,
        string SecondaryTextHex,
        string TrackHex,
        bool UseCustomCardColor,
        bool UseCustomBorderColor,
        bool UseCustomPrimaryTextColor,
        bool UseCustomSecondaryTextColor,
        bool UseCustomTrackColor,
        bool ShowIcon,
        bool ShowAccent,
        double CardOpacity,
        double BorderOpacity,
        double CardCornerRadius,
        double CardBorderWidth,
        double CardGap,
        double CardPadding,
        double AccentWidth,
        double ProgressHeight,
        double ProgressCornerRadius,
        double SparklineThickness,
        double SparklineFillOpacity,
        double LabelSize,
        double SecondarySize,
        double ValueSize,
        double IconSize,
        int LabelWeight,
        int ValueWeight);

    private sealed record EditorState(
        string Layout,
        string ActiveScene,
        string ThemeId,
        double BackgroundOpacity,
        double ContentOpacity,
        double BlurStrength,
        double FontScale,
        string Density,
        bool AlwaysOnTop,
        bool PositionLocked,
        bool ClickThrough,
        bool Draggable,
        bool Resizable,
        bool StartAtSignIn,
        bool ReducedMotion,
        bool DemoMetrics,
        int WidgetScalePercent,
        string UpdateRate,
        string PerformanceMode,
        double WidgetWidth,
        double WidgetHeight,
        StudioThemeSnapshot DesignerTheme,
        IReadOnlyList<ModuleState> Modules);
}
