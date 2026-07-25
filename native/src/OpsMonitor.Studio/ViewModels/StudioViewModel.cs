using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OpsMonitor.Studio.Infrastructure;
using OpsMonitor.Studio.Models;
using OpsMonitor.Studio.Services;

namespace OpsMonitor.Studio.ViewModels;

public sealed class StudioViewModel : ObservableObject, IDisposable
{
    private readonly IStudioSettingsSink _settingsSink;
    private readonly DispatcherTimer _applyTimer;
    private readonly Stack<EditorState> _undo = new();
    private readonly Stack<EditorState> _redo = new();
    private bool _isInitializing = true;
    private bool _isRestoring;
    private double _telemetryPhase;
    private NavigationItem? _selectedNavigation;
    private ModuleItem? _selectedModule;
    private ThemePreset? _selectedTheme;
    private string _selectedLayout = "Pill";
    private string _activeScene = "Daily driver";
    private string _searchText = string.Empty;
    private double _backgroundOpacity = 0.82;
    private double _contentOpacity = 1;
    private double _blurStrength = 24;
    private double _fontScale = 1;
    private string _density = "Comfortable";
    private bool _alwaysOnTop = true;
    private bool _positionLocked;
    private bool _clickThrough;
    private bool _draggable = true;
    private bool _resizable = true;
    private bool _startAtSignIn = true;
    private bool _snapToGrid = true;
    private bool _showAllDesktops;
    private bool _hideFullscreen = true;
    private bool _alertsEnabled = true;
    private bool _historyEnabled = true;
    private bool _reducedMotion;
    private bool _contrastGuard = true;
    private bool _largeTargets;
    private bool _useSystemTextScale = true;
    private bool _redundantColorCues = true;
    private bool _demoMetrics = true;
    private double _gridSize = 8;
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

    public StudioViewModel(IStudioSettingsSink? settingsSink = null)
    {
        _settingsSink = settingsSink ?? new StudioCoreSettingsSink();
        _settingsSink.SettingsChanged += (_, snapshot) => SettingsChanged?.Invoke(this, snapshot);

        Navigation = new ObservableCollection<NavigationItem>
        {
            new("overview", "Overview", "Health and quick actions", "⌂"),
            new("widgets", "Widgets & Scenes", "Layouts and workspaces", "▦"),
            new("modules", "Modules", "Metrics and visualizations", "◫"),
            new("appearance", "Appearance", "Theme, glass and density", "✦"),
            new("window", "Window & Input", "Placement and interaction", "⌗"),
            new("alerts", "Alerts & Automation", "Rules and actions", "◌"),
            new("history", "History & Data", "Retention and export", "⌁"),
            new("providers", "Providers & Integrations", "Sensors and extensions", "◇"),
            new("accessibility", "Accessibility", "Readability and motion", "◐"),
            new("diagnostics", "Diagnostics & About", "Impact, logs and version", "ⓘ"),
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
            new("Canvas", "Canvas", "Flexible metric board", "▦"),
        };

        Themes = new ObservableCollection<ThemePreset>
        {
            new("abyss", "Abyss", "Near-black glass with mint signals", Color.FromRgb(5, 8, 13), Color.FromRgb(13, 19, 28), Color.FromRgb(42, 56, 73), Color.FromRgb(67, 231, 210)),
            new("graphite", "Graphite", "Neutral charcoal and ice-blue detail", Color.FromRgb(15, 17, 21), Color.FromRgb(27, 30, 36), Color.FromRgb(59, 65, 75), Color.FromRgb(102, 187, 255)),
            new("violet", "Ultraviolet", "Deep indigo with electric violet", Color.FromRgb(12, 8, 24), Color.FromRgb(27, 18, 46), Color.FromRgb(71, 54, 102), Color.FromRgb(191, 112, 255)),
            new("ember", "Ember", "Warm graphite with amber highlights", Color.FromRgb(17, 11, 9), Color.FromRgb(34, 23, 19), Color.FromRgb(78, 54, 43), Color.FromRgb(255, 190, 92)),
            new("frost", "Frost", "Cool navy glass with crisp cyan", Color.FromRgb(7, 15, 23), Color.FromRgb(13, 31, 44), Color.FromRgb(41, 76, 96), Color.FromRgb(91, 221, 255)),
        };

        Modules = new ObservableCollection<ModuleItem>
        {
            NewModule("cpu", "CPU", "▦", "Compute", "Load, package temperature and clock", "Windows + hardware sensor", "#43E7F5", "42%", "68° · 4.8 GHz", 42, true, "Large"),
            NewModule("gpu", "GPU", "▣", "Compute", "3D load, temperature and VRAM", "NVIDIA NVML with bounded fallback", "#F05AD6", "17%", "41° · 2.1/12 GB", 17, true, "Large"),
            NewModule("ram", "Memory", "▤", "System", "Physical memory pressure", "Windows memory status", "#43E7D2", "15.4 / 30.9 GB", "50% used", 50),
            NewModule("net", "Network", "↕", "Network", "Download and upload throughput", "Active network adapter", "#62A7FF", "937K / 27K", "KB/s  ↓ / ↑", 36),
            NewModule("latency", "Latency", "⌁", "Network", "Ping, jitter and packet loss", "ICMP health probe", "#FFC95C", "26 ms", "0% loss · 3 ms jitter", 22),
            NewModule("disk", "Storage", "◫", "Storage", "Disk activity and remaining capacity", "Optional provider", "#63E6A6", "8%", "1.2 TB free", 8, false),
            NewModule("fps", "Frame rate", "◉", "Gaming", "FPS, frame time and 1% low", "PresentMon integration", "#A88BFF", "144 FPS", "6.9 ms · 118 low", 73, false),
            NewModule("battery", "Power", "▥", "Power", "Battery, draw and remaining time", "Windows power API", "#8EEA78", "86%", "2h 48m · 17 W", 86, false),
        };

        VisibleModulesView = new ListCollectionView(Modules)
        {
            Filter = item => item is ModuleItem module && module.IsVisible,
        };
        foreach (var module in Modules)
        {
            module.PropertyChanged += OnModulePropertyChanged;
        }

        Scenes = new ObservableCollection<SceneItem>
        {
            new("daily", "Daily driver", "Pill", "Quiet desktop essentials", "Ctrl + Alt + 1", Brush("#43E7D2")) { IsActive = true },
            new("gaming", "Gaming guard", "Dock", "FPS, temperatures and frame time", "Ctrl + Alt + 2", Brush("#F05AD6")),
            new("stream", "Stream check", "Rail", "Encoder, network and dropped frames", "Ctrl + Alt + 3", Brush("#62A7FF")),
            new("debug", "Thermal audit", "Canvas", "Expanded sensors and history", "Ctrl + Alt + 4", Brush("#FFC95C")),
        };

        AlertRules = new ObservableCollection<AlertRuleItem>
        {
            new("CPU thermal headroom", "CPU package", "above", "88 °C", "for 10 seconds", "Critical", Brush("#FFFF6B81"), true),
            new("GPU sustained load", "GPU 3D", "above", "95%", "for 2 minutes", "Warning", Brush("#FFFFC95C"), true),
            new("Network quality", "Packet loss", "above", "3%", "for 15 seconds", "Warning", Brush("#FFFFC95C"), true),
            new("Memory pressure", "Memory used", "above", "90%", "for 1 minute", "Info", Brush("#FF62A7FF"), false),
        };

        Providers = new ObservableCollection<ProviderItem>
        {
            new("Windows native", "CPU, memory, adapters, uptime and power", "Enabled", "Adaptive", "Core metrics", "Built in", false, Brush("#FF63E6A6")),
            new("NVIDIA NVML", "GPU load, temperature, VRAM, power and fan", "Automatic", "Adaptive", "When supported", "Built in", false, Brush("#FF63E6A6")),
            CreateCpuTemperatureProviderItem(),
            new("Network quality", "Ping, jitter and rolling packet loss", "Enabled", "Adaptive", "3 quality metrics", "Built in", false, Brush("#FF63E6A6")),
            new("LibreHardwareMonitor", "Additional temperatures, fans and voltage", "Not connected", "—", "0 metrics", "Optional connector", true, Brush("#FF778397")),
            new("PresentMon", "FPS, frame time and 1% lows", "Not connected", "—", "0 metrics", "Roadmap connector", false, Brush("#FF778397")),
        };

        Activities = new ObservableCollection<ActivityItem>
        {
            new("Now", "Shared settings synchronized", RuntimeSettingsPath, "●", Brush("#FF43E7D2")),
            new("Now", "Adaptive polling is active", "Providers run independently and never overlap themselves", "↕", Brush("#FF63E6A6")),
            new("Local", "Configuration stored on this PC", SettingsPath, "✓", Brush("#FF62A7FF")),
        };

        SelectLayoutCommand = new RelayCommand(SelectLayout);
        NavigateCommand = new RelayCommand(Navigate);
        ApplyThemeCommand = new RelayCommand(ApplyTheme);
        ActivateSceneCommand = new RelayCommand(ActivateScene);
        MoveModuleUpCommand = new RelayCommand(parameter => MoveModule(parameter as ModuleItem, -1));
        MoveModuleDownCommand = new RelayCommand(parameter => MoveModule(parameter as ModuleItem, 1));
        AddModuleCommand = new RelayCommand(AddModule);
        DuplicateModuleCommand = new RelayCommand(_ => DuplicateSelectedModule(), _ => SelectedModule is not null);
        DeleteModuleCommand = new RelayCommand(_ => DeleteSelectedModule(), _ => SelectedModule is not null);
        EqualizeCommand = new RelayCommand(_ => EqualizeModules());
        UndoCommand = new RelayCommand(_ => Undo(), _ => _undo.Count > 0);
        RedoCommand = new RelayCommand(_ => Redo(), _ => _redo.Count > 0);
        AddAlertCommand = new RelayCommand(_ => AddAlert());
        TestProviderCommand = new RelayCommand(TestProvider);
        CopyDiagnosticsCommand = new RelayCommand(_ => CopyDiagnostics());
        ResetDemoCommand = new RelayCommand(_ => ResetDemo());
        SaveCommand = new RelayCommand(_ => SaveSettings());
        ReloadCommand = new RelayCommand(_ => ReloadSettings());
        OpenOrRestartWidgetCommand = new RelayCommand(_ => OpenOrRestartWidget());

        SelectedNavigation = Navigation[0];
        SelectedModule = Modules[0];
        SelectedTheme = Themes[0];
        SelectedTheme.IsSelected = true;
        ApplyLayoutMetrics();
        RefreshPreviewBrushes();

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
        QueueLiveApply();
    }

    public event EventHandler<StudioSettingsSnapshot>? SettingsChanged;
    public event EventHandler? RequestCopyDiagnostics;

    public ObservableCollection<NavigationItem> Navigation { get; }
    public ICollectionView NavigationView { get; }
    public ObservableCollection<LayoutPreset> Layouts { get; }
    public ObservableCollection<ThemePreset> Themes { get; }
    public ObservableCollection<ModuleItem> Modules { get; }
    public ListCollectionView VisibleModulesView { get; }
    public ObservableCollection<SceneItem> Scenes { get; }
    public ObservableCollection<AlertRuleItem> AlertRules { get; }
    public ObservableCollection<ProviderItem> Providers { get; }
    public ObservableCollection<ActivityItem> Activities { get; }
    public IReadOnlyList<string> ModuleSizes { get; } = ["Small", "Medium", "Large"];
    public IReadOnlyList<string> Visualizations { get; } = ["Number only", "Bar", "Sparkline", "Bar + sparkline", "Dial"];
    public IReadOnlyList<string> PrecisionOptions { get; } = ["Whole numbers", "1 decimal", "2 decimals", "Adaptive"];
    public IReadOnlyList<string> DensityOptions { get; } = ["Compact", "Comfortable", "Airy"];
    public IReadOnlyList<string> MonitorOptions { get; } = ["Primary display", "Display 1 · 2560 × 1440", "Display 2 · 1920 × 1080", "Follow active display"];
    public IReadOnlyList<string> AnchorOptions { get; } = ["Remember exact position", "Top right", "Top left", "Bottom right", "Bottom left", "Screen edge"];
    public IReadOnlyList<string> RetentionOptions { get; } = ["Off", "1 hour", "24 hours", "7 days", "30 days"];
    public IReadOnlyList<string> UpdateRates { get; } = ["0.5 seconds", "1 second", "2 seconds", "5 seconds", "Adaptive"];
    public IReadOnlyList<string> PerformanceModes { get; } = ["Performance", "Balanced", "Efficiency"];
    public string SettingsPath => _settingsSink.SettingsPath;
    public string RuntimeSettingsPath => _settingsSink.RuntimeSettingsPath;
    public string WidgetExecutablePath
    {
        get => _widgetExecutablePath;
        private set => SetProperty(ref _widgetExecutablePath, value);
    }

    public ICommand SelectLayoutCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand ApplyThemeCommand { get; }
    public ICommand ActivateSceneCommand { get; }
    public ICommand MoveModuleUpCommand { get; }
    public ICommand MoveModuleDownCommand { get; }
    public ICommand AddModuleCommand { get; }
    public ICommand DuplicateModuleCommand { get; }
    public ICommand DeleteModuleCommand { get; }
    public ICommand EqualizeCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand AddAlertCommand { get; }
    public ICommand TestProviderCommand { get; }
    public ICommand CopyDiagnosticsCommand { get; }
    public ICommand ResetDemoCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand OpenOrRestartWidgetCommand { get; }

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
                ApplyLayoutMetrics();
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

    public double BackgroundOpacity
    {
        get => _backgroundOpacity;
        set
        {
            if (SetProperty(ref _backgroundOpacity, value))
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
            if (SetProperty(ref _contentOpacity, value))
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
            if (SetProperty(ref _blurStrength, value))
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
            if (SetProperty(ref _fontScale, value))
            {
                QueueLiveApply();
            }
        }
    }

    public string Density
    {
        get => _density;
        set
        {
            if (SetProperty(ref _density, value))
            {
                ApplyLayoutMetrics();
                QueueLiveApply();
            }
        }
    }

    public bool AlwaysOnTop { get => _alwaysOnTop; set => SetAndQueue(ref _alwaysOnTop, value); }
    public bool PositionLocked { get => _positionLocked; set => SetAndQueue(ref _positionLocked, value); }
    public bool ClickThrough { get => _clickThrough; set => SetAndQueue(ref _clickThrough, value); }
    public bool Draggable { get => _draggable; set => SetAndQueue(ref _draggable, value); }
    public bool Resizable { get => _resizable; set => SetAndQueue(ref _resizable, value); }
    public bool StartAtSignIn { get => _startAtSignIn; set => SetAndQueue(ref _startAtSignIn, value); }
    public bool SnapToGrid { get => _snapToGrid; set => SetAndQueue(ref _snapToGrid, value); }
    public bool ShowAllDesktops { get => _showAllDesktops; set => SetAndQueue(ref _showAllDesktops, value); }
    public bool HideFullscreen { get => _hideFullscreen; set => SetAndQueue(ref _hideFullscreen, value); }
    public bool AlertsEnabled { get => _alertsEnabled; set => SetAndQueue(ref _alertsEnabled, value); }
    public bool HistoryEnabled { get => _historyEnabled; set => SetAndQueue(ref _historyEnabled, value); }
    public bool ReducedMotion { get => _reducedMotion; set => SetAndQueue(ref _reducedMotion, value); }
    public bool ContrastGuard { get => _contrastGuard; set => SetAndQueue(ref _contrastGuard, value); }
    public bool LargeTargets { get => _largeTargets; set => SetAndQueue(ref _largeTargets, value); }
    public bool UseSystemTextScale { get => _useSystemTextScale; set => SetAndQueue(ref _useSystemTextScale, value); }
    public bool RedundantColorCues { get => _redundantColorCues; set => SetAndQueue(ref _redundantColorCues, value); }
    public bool DemoMetrics { get => _demoMetrics; set => SetProperty(ref _demoMetrics, value); }

    public double GridSize
    {
        get => _gridSize;
        set => SetAndQueue(ref _gridSize, value);
    }

    public int WidgetScalePercent
    {
        get => _widgetScalePercent;
        set => SetAndQueue(ref _widgetScalePercent, Math.Clamp(value, 80, 160));
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
    public double WidgetWidth
    {
        get => _previewWidth;
        set
        {
            if (SetProperty(ref _previewWidth, Math.Clamp(value, 112, 1_600), nameof(WidgetWidth)))
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
            if (SetProperty(ref _previewHeight, Math.Clamp(value, 140, 1_200), nameof(WidgetHeight)))
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
    public string AppVersion { get; } = "OPS Monitor Studio · v2.0.0";
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
        UpdateModule("cpu", 43 + Math.Sin(_telemetryPhase) * 9, value => $"{value:0}%", value => $"{62 + value * 0.13:0}° · 4.8 GHz");
        UpdateModule("gpu", 24 + Math.Sin(_telemetryPhase * 0.72 + 1.1) * 15, value => $"{value:0}%", value => $"{39 + value * 0.18:0}° · 2.1/12 GB");
        UpdateModule("ram", 50 + Math.Sin(_telemetryPhase * 0.23) * 2, value => $"{30.9 * value / 100:0.0} / 30.9 GB", value => $"{value:0}% used");
        UpdateModule("net", 38 + Math.Sin(_telemetryPhase * 1.42) * 27, value => $"{Math.Max(44, 980 + Math.Sin(_telemetryPhase) * 420):0}K / {Math.Max(8, 31 + Math.Cos(_telemetryPhase * 1.4) * 14):0}K", _ => "KB/s  ↓ / ↑");
        UpdateModule("latency", 22 + Math.Sin(_telemetryPhase * 0.87) * 6, value => $"{value:0} ms", _ => "0% loss · 3 ms jitter");
        UpdateModule("disk", 12 + Math.Sin(_telemetryPhase * 1.9) * 8, value => $"{value:0}%", _ => "1.2 TB free");
        UpdateModule("fps", 72 + Math.Sin(_telemetryPhase * 0.8) * 10, value => $"{value * 2:0} FPS", value => $"{1000 / Math.Max(1, value * 2):0.0} ms · 118 low");
        UpdateModule("battery", 86 - (_telemetryPhase % 8) * 0.05, value => $"{value:0}%", _ => "2h 48m · 17 W");
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
        _applyTimer.Stop();
        _settingsSink.Dispose();
        GC.SuppressFinalize(this);
    }

    public void ReloadSettings()
    {
        var snapshot = _settingsSink.Reload();
        if (snapshot is null)
        {
            StatusMessage = "Using polished defaults";
            LastAppliedText = "Defaults ready";
            return;
        }

        _isRestoring = true;
        SelectedLayout = Layouts.Any(layout => layout.Id == snapshot.Layout) ? snapshot.Layout : "Pill";
        ActiveScene = snapshot.Scene;
        BackgroundOpacity = Math.Clamp(snapshot.BackgroundOpacity, 0.3, 1);
        ContentOpacity = Math.Clamp(snapshot.ContentOpacity, 0.72, 1);
        BlurStrength = Math.Clamp(snapshot.BlurStrength, 0, 40);
        Density = DensityOptions.Contains(snapshot.Density) ? snapshot.Density : "Comfortable";
        FontScale = Math.Clamp(snapshot.FontScale, 0.9, 1.35);
        AlwaysOnTop = snapshot.AlwaysOnTop;
        PositionLocked = snapshot.PositionLocked;
        ClickThrough = snapshot.ClickThrough;
        StartAtSignIn = snapshot.StartAtSignIn;
        SnapToGrid = snapshot.SnapToGrid;
        Draggable = snapshot.Draggable;
        Resizable = snapshot.Resizable;
        WidgetWidth = snapshot.WidgetWidth;
        WidgetHeight = snapshot.WidgetHeight;
        WidgetScalePercent = snapshot.WidgetScalePercent;
        UpdateRate = RateLabel(snapshot.UpdateCadenceSeconds);
        PerformanceMode = PerformanceModes.Contains(snapshot.PerformanceMode)
            ? snapshot.PerformanceMode
            : "Balanced";
        AlertsEnabled = snapshot.AlertsEnabled;
        ReducedMotion = snapshot.ReducedMotion;

        var theme = Themes.FirstOrDefault(item => item.Id == snapshot.Theme) ?? Themes[0];
        foreach (var item in Themes)
        {
            item.IsSelected = item == theme;
        }
        SelectedTheme = theme;

        foreach (var module in Modules)
        {
            module.IsVisible = snapshot.VisibleModules.Contains(module.Id);
        }
        ApplyModuleSnapshots(snapshot.Modules);

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

        if (snapshot.Alerts is not null)
        {
            foreach (var alert in AlertRules)
            {
                var mapped = snapshot.Alerts.FirstOrDefault(item =>
                    item.Name.Equals(alert.Name, StringComparison.OrdinalIgnoreCase));
                if (mapped is not null)
                {
                    alert.IsEnabled = mapped.Enabled;
                }
            }
        }

        _isRestoring = false;
        VisibleModulesView.Refresh();
        RefreshPreviewBrushes();
        StatusMessage = string.IsNullOrWhiteSpace(_settingsSink.LastWarning)
            ? "Editor and shared runtime settings reloaded"
            : _settingsSink.LastWarning;
        LastAppliedText = "Loaded from disk";
    }

    public void SaveSettings()
    {
        if (_isInitializing || _isRestoring)
        {
            return;
        }

        try
        {
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
            "CPU temperature bridge",
            "AMD package temperature from an isolated elevated reader",
            probe.Status,
            probe.Detail,
            probe.IsAvailable ? "1 metric" : "0 live metrics",
            "Optional bridge",
            true,
            Brush(probe.IsAvailable ? "#FF63E6A6" : "#FFFFC95C"));
    }

    private static SolidColorBrush Brush(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
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

    private void ApplyTheme(object? parameter)
    {
        if (parameter is not ThemePreset theme || theme == SelectedTheme)
        {
            return;
        }

        PushUndo();
        foreach (var item in Themes)
        {
            item.IsSelected = item == theme;
        }

        SelectedTheme = theme;
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

        PushUndo();
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
    }

    private void DuplicateSelectedModule()
    {
        if (SelectedModule is null)
        {
            return;
        }

        PushUndo();
        var clone = SelectedModule.Clone((Modules.Count + 1).ToString(CultureInfo.InvariantCulture));
        clone.PropertyChanged += OnModulePropertyChanged;
        Modules.Insert(Math.Min(Modules.IndexOf(SelectedModule) + 1, Modules.Count), clone);
        SelectedModule = clone;
        VisibleModulesView.Refresh();
        StatusMessage = $"{clone.Name} created";
        QueueLiveApply();
    }

    private void DeleteSelectedModule()
    {
        if (SelectedModule is null)
        {
            return;
        }

        PushUndo();
        var index = Modules.IndexOf(SelectedModule);
        var name = SelectedModule.Name;
        SelectedModule.PropertyChanged -= OnModulePropertyChanged;
        Modules.Remove(SelectedModule);
        SelectedModule = Modules.Count == 0 ? null : Modules[Math.Clamp(index, 0, Modules.Count - 1)];
        VisibleModulesView.Refresh();
        StatusMessage = $"{name} removed";
        QueueLiveApply();
    }

    private void EqualizeModules()
    {
        PushUndo();
        foreach (var module in Modules.Where(item => item.IsVisible))
        {
            module.Size = "Medium";
        }

        StatusMessage = "Visible modules equalized";
        QueueLiveApply();
    }

    private void AddAlert()
    {
        AlertRules.Insert(0, new AlertRuleItem(
            "New metric guardrail",
            "Choose metric",
            "above",
            "80%",
            "for 30 seconds",
            "Info",
            Brush("#FF62A7FF"),
            true));
        StatusMessage = "Alert rule added";
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
            "CPU temperature bridge" => ProbeCpuTemperatureBridge(),
            "Network quality" => ProbeNetwork(),
            "LibreHardwareMonitor" => ProbeOptionalProcess("LibreHardwareMonitor"),
            "PresentMon" => ProbeOptionalProcess("PresentMon"),
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
        var readingPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PerformancePill",
            "cpu-temperature.txt");
        if (!File.Exists(readingPath))
        {
            return new ProviderProbeResult("Not connected", "No reading published", false);
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
                    out var ticks))
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

    private static ProviderProbeResult ProbeOptionalProcess(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0
                ? new ProviderProbeResult("Detected", "Connector not enabled", false)
                : new ProviderProbeResult("Not connected", "Process not detected", false);
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
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
        SelectedLayout = "Pill";
        BackgroundOpacity = 0.82;
        ContentOpacity = 1;
        BlurStrength = 24;
        FontScale = 1;
        Density = "Comfortable";
        AlwaysOnTop = true;
        PositionLocked = false;
        ClickThrough = false;
        SnapToGrid = true;
        Draggable = true;
        Resizable = true;
        WidgetScalePercent = 100;
        UpdateRate = "2 seconds";
        PerformanceMode = "Balanced";
        foreach (var module in Modules)
        {
            module.IsVisible = module.Id is "cpu" or "gpu" or "ram" or "net" or "latency";
            module.Size = module.Id is "cpu" or "gpu" ? "Large" : "Medium";
        }
        ApplyTheme(Themes[0]);
        _isRestoring = false;
        VisibleModulesView.Refresh();
        RefreshPreviewBrushes();
        StatusMessage = "Demo defaults restored";
        QueueLiveApply();
    }

    private void ApplyModuleSnapshots(IReadOnlyList<StudioModuleSnapshot>? snapshots)
    {
        if (snapshots is null || snapshots.Count == 0)
        {
            return;
        }

        var builtInIds = new HashSet<string>(
            ["cpu", "gpu", "ram", "net", "latency", "disk", "fps", "battery"],
            StringComparer.Ordinal);
        var snapshotIds = snapshots.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var staleClone in Modules.Where(item =>
                     !builtInIds.Contains(item.Id) && !snapshotIds.Contains(item.Id)).ToArray())
        {
            staleClone.PropertyChanged -= OnModulePropertyChanged;
            Modules.Remove(staleClone);
        }

        foreach (var snapshot in snapshots.OrderBy(item => item.Order))
        {
            var module = Modules.FirstOrDefault(item =>
                item.Id.Equals(snapshot.Id, StringComparison.Ordinal));
            if (module is null)
            {
                var baseId = snapshot.Id.Split('-', 2)[0];
                var template = Modules.FirstOrDefault(item =>
                                   item.Id.Equals(baseId, StringComparison.Ordinal))
                               ?? Modules[0];
                module = new ModuleItem(
                    snapshot.Id,
                    snapshot.Name,
                    snapshot.Icon,
                    template.Category,
                    template.Description,
                    template.Source,
                    Brush(snapshot.Accent),
                    template.PrimaryValue,
                    template.SecondaryValue,
                    template.UsagePercent,
                    snapshot.Enabled,
                    snapshot.Size);
                module.PropertyChanged += OnModulePropertyChanged;
                Modules.Add(module);
            }

            module.Name = snapshot.Name;
            module.Icon = snapshot.Icon;
            module.Accent = Brush(snapshot.Accent);
            module.IsVisible = snapshot.Enabled;
            module.Size = snapshot.Size;
            module.Visualization = snapshot.Visualization;
            module.ShowLabel = snapshot.ShowLabel;
            module.ShowSparkline = snapshot.ShowSparkline;
            module.ShowTemperature = snapshot.ShowTemperature;
            module.Precision = snapshot.Precision;
        }

        var ordered = snapshots.OrderBy(item => item.Order)
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

    private static string BrushHex(Brush brush)
        => brush is SolidColorBrush solidColor
            ? solidColor.Color.ToString(CultureInfo.InvariantCulture)
            : "#FF43E7D2";

    private static double RateSeconds(string rate)
        => rate switch
        {
            "0.5 seconds" => 0.5,
            "1 second" => 1,
            "5 seconds" => 5,
            "Adaptive" => 2,
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

        if (seconds >= 4)
        {
            return "5 seconds";
        }

        return "2 seconds";
    }

    private void OnModulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModuleItem.IsVisible))
        {
            VisibleModulesView.Refresh();
        }

        if (e.PropertyName is nameof(ModuleItem.UsagePercent)
            or nameof(ModuleItem.PrimaryValue)
            or nameof(ModuleItem.SecondaryValue))
        {
            return;
        }

        QueueLiveApply();
    }

    private void SetAndQueue<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
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
        var selectedTheme = SelectedTheme ?? Themes[0];
        return new StudioSettingsSnapshot(
            ActiveScene,
            SelectedLayout,
            selectedTheme.Id,
            BackgroundOpacity,
            ContentOpacity,
            BlurStrength,
            Density,
            FontScale,
            AlwaysOnTop,
            PositionLocked,
            ClickThrough,
            StartAtSignIn,
            SnapToGrid,
            Modules.Where(item => item.IsVisible).Select(item => item.Id).ToArray(),
            Draggable,
            Resizable,
            WidgetWidth,
            WidgetHeight,
            WidgetScalePercent,
            RateSeconds(UpdateRate),
            PerformanceMode,
            AlertsEnabled,
            ReducedMotion,
            Modules.Select((item, order) => new StudioModuleSnapshot(
                item.Id,
                item.Name,
                order,
                item.IsVisible,
                item.Size,
                item.Visualization,
                item.ShowLabel,
                item.ShowSparkline,
                item.ShowTemperature,
                item.Precision,
                item.Icon,
                BrushHex(item.Accent))).ToArray(),
            new StudioThemeSnapshot(
                selectedTheme.Id,
                selectedTheme.Name,
                selectedTheme.Surface.ToString(CultureInfo.InvariantCulture),
                selectedTheme.Card.ToString(CultureInfo.InvariantCulture),
                selectedTheme.Border.ToString(CultureInfo.InvariantCulture),
                selectedTheme.Accent.ToString(CultureInfo.InvariantCulture)),
            Scenes.Select(item => new StudioSceneSnapshot(
                item.Id,
                item.Name,
                item.Layout,
                item.IsActive)).ToArray(),
            AlertRules.Select((item, index) => new StudioAlertSnapshot(
                $"rule-{index.ToString(CultureInfo.InvariantCulture)}",
                item.Name,
                item.Metric,
                item.Condition,
                item.Threshold,
                item.Duration,
                item.Severity,
                item.IsEnabled)).ToArray());
    }

    private void RefreshPreviewBrushes()
    {
        var theme = SelectedTheme ?? Themes.FirstOrDefault();
        if (theme is null)
        {
            return;
        }

        PreviewSurfaceBrush = OpacityBrush(theme.Surface, BackgroundOpacity);
        PreviewCardBrush = OpacityBrush(theme.Card, Math.Min(1, BackgroundOpacity + 0.08));
        PreviewBorderBrush = OpacityBrush(theme.Border, Math.Min(1, BackgroundOpacity + 0.16));
        PreviewAccentBrush = new SolidColorBrush(theme.Accent);
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
        (WidgetWidth, WidgetHeight, PreviewModuleWidth, PreviewModuleHeight, PreviewCornerRadius) =
            (SelectedLayout, Density) switch
        {
            ("Rail", "Compact") => (204, 326, 164, 48, 25),
            ("Rail", "Airy") => (276, 820, 236, 144, 29),
            ("Rail", _) => (250, 660, 210, 104, 28),
            ("Dock", "Compact") => (880, 118, 160, 64, 25),
            ("Dock", "Airy") => (1_100, 292, 180, 144, 30),
            ("Dock", _) => (940, 238, 180, 104, 30),
            ("Canvas", _) => (540, 348, 154, 94, 24),
            ("Pill", "Compact") => (240, 420, 200, 64, 25),
            ("Pill", "Airy") => (320, 820, 280, 144, 34),
            _ => (290, 660, 250, 104, 32),
        };
        OnPropertyChanged(nameof(WidgetWidth));
        OnPropertyChanged(nameof(WidgetHeight));
    }

    private void PushUndo()
    {
        if (_isRestoring)
        {
            return;
        }

        _undo.Push(CaptureEditorState());
        _redo.Clear();
        RaiseEditorCommandState();
    }

    private EditorState CaptureEditorState()
        => new(
            SelectedLayout,
            SelectedTheme?.Id ?? "abyss",
            Modules.Select(item => new ModuleState(item.Id, item.IsVisible, item.Size)).ToArray());

    private void RestoreEditorState(EditorState state)
    {
        _isRestoring = true;
        SelectedLayout = state.Layout;
        var theme = Themes.FirstOrDefault(item => item.Id == state.ThemeId) ?? Themes[0];
        foreach (var item in Themes)
        {
            item.IsSelected = item == theme;
        }
        SelectedTheme = theme;

        var stateById = state.Modules.ToDictionary(item => item.Id);
        foreach (var module in Modules)
        {
            if (stateById.TryGetValue(module.Id, out var moduleState))
            {
                module.IsVisible = moduleState.Visible;
                module.Size = moduleState.Size;
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

        _isRestoring = false;
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
        (DuplicateModuleCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteModuleCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private sealed record ModuleState(string Id, bool Visible, string Size);
    private sealed record EditorState(string Layout, string ThemeId, IReadOnlyList<ModuleState> Modules);
}
