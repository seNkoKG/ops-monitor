using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using OpsMonitor.Core.Platform;
using OpsMonitor.Widget.Interop;
using OpsMonitor.Widget.Controls;
using OpsMonitor.Widget.Models;
using OpsMonitor.Widget.Services;
using OpsMonitor.Widget.ViewModels;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Drawing = System.Drawing;
using Ellipse = System.Windows.Shapes.Ellipse;
using Forms = System.Windows.Forms;
using JsonSettingsRepository = OpsMonitor.Core.Settings.JsonSettingsRepository;
using MessageBox = System.Windows.MessageBox;

namespace OpsMonitor.Widget;

[SuppressMessage(
    "Design",
    "CA1001",
    Justification = "WPF Window lifetime owns and disposes tray and telemetry resources in OnClosed.")]
public partial class MainWindow : Window
{
    private static readonly HashSet<string> PersistedViewModelProperties =
    [
        nameof(MainWindowViewModel.Layout),
        nameof(MainWindowViewModel.Density),
        nameof(MainWindowViewModel.InteractionMode),
        nameof(MainWindowViewModel.ThemeName),
        nameof(MainWindowViewModel.Topmost),
        nameof(MainWindowViewModel.Draggable),
        nameof(MainWindowViewModel.Resizable),
        nameof(MainWindowViewModel.ShowBattery),
        nameof(MainWindowViewModel.ShowWeather),
        nameof(MainWindowViewModel.WeatherLocation),
        nameof(MainWindowViewModel.StartAtSignIn),
        nameof(MainWindowViewModel.ScalePercent),
        nameof(MainWindowViewModel.UpdateCadenceSeconds),
        nameof(MainWindowViewModel.SurfaceOpacity),
        nameof(MainWindowViewModel.ContentOpacity)
    ];

    private readonly WidgetSettings _startupSettings;
    private readonly MainWindowViewModel _viewModel;
    private readonly DispatcherTimer _saveTimer;
    private readonly DebouncedSettingsFileWatcher _settingsWatcher;
    private readonly bool _isEphemeralSession;
    private readonly WindowsStartupRegistration _startupRegistration =
        new("OPS Monitor Widget");
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private Forms.ToolStripMenuItem? _topmostTrayItem;
    private Forms.ToolStripMenuItem? _lockedTrayItem;
    private Forms.ToolStripMenuItem? _clickThroughTrayItem;
    private HwndSource? _windowSource;
    private WeatherWindow? _weatherWindow;
    private HwndSourceHook? _hotkeyHook;
    private nint _windowHandle;
    private bool _isLoaded;
    private bool _isRestoringGeometry;
    private bool _isApplyingExternalSettings;
    private bool _isClosing;
    private bool _hotkeyRegistered;
    private bool _sessionEventsSubscribed;
    private double _lastRuntimeCadenceSeconds;

    public MainWindow()
    {
        InitializeComponent();

        var launchArguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        _isEphemeralSession = IsEphemeralLaunch(launchArguments);
        _startupSettings = LoadLaunchSettings(launchArguments);
        _lastRuntimeCadenceSeconds = _startupSettings.UpdateCadenceSeconds;
        _viewModel = new MainWindowViewModel(CreateTelemetrySource(), _startupSettings);
        DataContext = _viewModel;
        _settingsWatcher = new DebouncedSettingsFileWatcher(
            JsonSettingsRepository.GetDefaultSettingsPath());
        _settingsWatcher.ReloadRequested += SettingsWatcher_OnReloadRequested;

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        _saveTimer.Tick += SaveTimer_OnTick;

        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        _viewModel.TelemetryUpdated += ViewModel_OnTelemetryUpdated;
        Loaded += MainWindow_OnLoaded;
        LocationChanged += WindowGeometry_OnChanged;
        SizeChanged += WindowGeometry_OnChanged;

        ApplyInitialGeometry();
        UpdateWindowConstraints();
        if (!_startupSettings.Width.HasValue || !_startupSettings.Height.HasValue)
        {
            ApplySuggestedSize();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _windowHandle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(_windowHandle);
        _hotkeyHook = NativeMethods.CreateHotkeyHook(RestoreEditMode);
        _windowSource?.AddHook(_hotkeyHook);
        _hotkeyRegistered = NativeMethods.RegisterEditHotkey(_windowHandle);
        _viewModel.EditHotkeyAvailable = _hotkeyRegistered;
        ApplyInteractionMode();
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        if (_sessionEventsSubscribed)
        {
            SystemEvents.SessionSwitch -= SystemEvents_OnSessionSwitch;
            _sessionEventsSubscribed = false;
        }

        if (!_isEphemeralSession)
        {
            SaveNow();
        }
        _settingsWatcher.ReloadRequested -= SettingsWatcher_OnReloadRequested;
        _settingsWatcher.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _saveTimer.Stop();
        _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        _viewModel.TelemetryUpdated -= ViewModel_OnTelemetryUpdated;
        if (_weatherWindow is not null)
        {
            _weatherWindow.Close();
            _weatherWindow = null;
        }
        _viewModel.Dispose();

        if (_windowSource is not null && _hotkeyHook is not null)
        {
            _windowSource.RemoveHook(_hotkeyHook);
        }

        if (_hotkeyRegistered && _windowHandle != 0)
        {
            NativeMethods.UnregisterEditHotkey(_windowHandle);
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Icon?.Dispose();
            _trayIcon.Dispose();
        }

        _trayMenu?.Dispose();
        base.OnClosed(e);
    }

    [SuppressMessage(
        "Performance",
        "CA1859",
        Justification = "The interface is the deliberate seam for the production Core telemetry adapter.")]
    private static ITelemetrySource CreateTelemetrySource()
    {
        var isDemo = Environment.GetCommandLineArgs().Any(argument =>
            argument.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        return isDemo
            ? new DemoTelemetrySource()
            : new CoreTelemetrySource();
    }

    internal static WidgetSettings LoadLaunchSettings(IEnumerable<string> launchArguments)
    {
        var arguments = launchArguments.ToArray();
        var resetRequested = arguments.Any(argument =>
            argument.Equals("--reset-ui", StringComparison.OrdinalIgnoreCase));
        var settings = resetRequested ? new WidgetSettings() : WidgetSettingsStore.Reload();

        foreach (var argument in arguments)
        {
            if (TryReadArgument(argument, "--layout", out var layout) &&
                Enum.TryParse<WidgetLayout>(layout, true, out var parsedLayout))
            {
                settings.Layout = parsedLayout;
            }
            else if (TryReadArgument(argument, "--density", out var density) &&
                     Enum.TryParse<WidgetDensity>(density, true, out var parsedDensity))
            {
                settings.Density = parsedDensity;
            }
            else if (TryReadArgument(argument, "--theme", out var theme))
            {
                settings.Theme = theme;
            }
            else if (TryReadArgument(argument, "--scale", out var scale) &&
                     int.TryParse(
                         scale,
                         System.Globalization.NumberStyles.Integer,
                         System.Globalization.CultureInfo.InvariantCulture,
                         out var parsedScale))
            {
                settings.ScalePercent = Math.Clamp(parsedScale, 80, 160);
            }
            else if (argument.Equals("--show-battery", StringComparison.OrdinalIgnoreCase))
            {
                settings.ShowBattery = true;
                if (!settings.EnabledModules.Contains(
                        WidgetModuleCatalog.Battery,
                        StringComparer.Ordinal))
                {
                    settings.EnabledModules.Add(WidgetModuleCatalog.Battery);
                }
            }
            else if (argument.Equals("--show-storage", StringComparison.OrdinalIgnoreCase))
            {
                if (!settings.EnabledModules.Contains(
                        WidgetModuleCatalog.Storage,
                        StringComparer.Ordinal))
                {
                    settings.EnabledModules.Add(WidgetModuleCatalog.Storage);
                }
            }
            else if (argument.Equals("--demo", StringComparison.OrdinalIgnoreCase))
            {
                // Explicitly recognized for deterministic visual QA.
            }
        }

        return settings;
    }

    internal static bool IsEphemeralLaunch(IEnumerable<string> launchArguments)
    {
        ArgumentNullException.ThrowIfNull(launchArguments);
        return launchArguments.Any(argument =>
            argument.Equals("--demo", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("--reset-ui", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryReadArgument(string argument, string name, out string value)
    {
        var prefix = name + "=";
        if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = argument[prefix.Length..];
            return !string.IsNullOrWhiteSpace(value);
        }

        value = string.Empty;
        return false;
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        InitializeTrayIcon();
        SystemEvents.SessionSwitch += SystemEvents_OnSessionSwitch;
        _sessionEventsSubscribed = true;
        EnsureVisibleOnScreen();
        _isLoaded = true;
        _viewModel.Start();
        ApplyInteractionMode();
        if (!_isEphemeralSession)
        {
            _ = StartSettingsWatcherAsync();
            ScheduleSave();
        }
    }

    private void SystemEvents_OnSessionSwitch(
        object sender,
        SessionSwitchEventArgs eventArgs)
    {
        _ = sender;
        if (eventArgs.Reason is not (SessionSwitchReason.SessionLock or
            SessionSwitchReason.SessionUnlock))
        {
            return;
        }

        _viewModel.TelemetrySource.SetWorkstationLocked(
            eventArgs.Reason == SessionSwitchReason.SessionLock);
    }

    private void ViewModel_OnTelemetryUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        if (!_viewModel.MotionEnabled || !_viewModel.PulseStatusIndicator ||
            _viewModel.TransitionMilliseconds == 0 ||
            (_viewModel.RespectReducedMotion && !SystemParameters.ClientAreaAnimation))
        {
            return;
        }

        switch (_viewModel.Layout)
        {
            case WidgetLayout.Mini:
                PulseUpdateIndicator(
                    MiniUpdatePulseRing,
                    MiniUpdatePulseScale,
                    MiniUpdatePulseDot,
                    _viewModel.TransitionMilliseconds);
                break;
            case WidgetLayout.Dock:
                PulseUpdateIndicator(
                    DockUpdatePulseRing,
                    DockUpdatePulseScale,
                    DockUpdatePulseDot,
                    _viewModel.TransitionMilliseconds);
                break;
            default:
                PulseUpdateIndicator(
                    StandardUpdatePulseRing,
                    StandardUpdatePulseScale,
                    StandardUpdatePulseDot,
                    _viewModel.TransitionMilliseconds);
                break;
        }
    }

    private static void PulseUpdateIndicator(
        Ellipse ring,
        ScaleTransform scale,
        Ellipse dot,
        int transitionMilliseconds)
    {
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        // Keep the update acknowledgement crisp. A long animation on a
        // transparent always-on-top window forces needless desktop repaints.
        var ringDuration = new Duration(TimeSpan.FromMilliseconds(
            Math.Clamp(transitionMilliseconds, 60, 600)));

        ring.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.72, 0, ringDuration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.72, 1.48, ringDuration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.72, 1.48, ringDuration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
        dot.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(
                Math.Clamp(transitionMilliseconds / 2d, 40, 240)))
            {
                AutoReverse = true,
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void ApplyInitialGeometry()
    {
        _isRestoringGeometry = true;
        try
        {
            var suggested = WidgetSizingPolicy.Calculate(
                _viewModel.Layout,
                _viewModel.Density,
                _viewModel.VisibleModuleCount,
                _viewModel.ScalePercent);
            Width = _startupSettings.Width ?? suggested.SuggestedWidth;
            Height = _startupSettings.Height ?? suggested.SuggestedHeight;

            if (_startupSettings.Left is { } left && _startupSettings.Top is { } top)
            {
                Left = left;
                Top = top;
            }
        }
        finally
        {
            _isRestoringGeometry = false;
        }
    }

    private void EnsureVisibleOnScreen()
    {
        if (!double.IsFinite(Left) || !double.IsFinite(Top))
        {
            PlaceNearTopRight();
            return;
        }

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
        var visibleMargin = 48;

        var isOffScreen =
            Left + visibleMargin > virtualRight ||
            Top + visibleMargin > virtualBottom ||
            Left + Width - visibleMargin < virtualLeft ||
            Top + Height - visibleMargin < virtualTop;

        if (isOffScreen)
        {
            PlaceNearTopRight();
        }
    }

    private void PlaceNearTopRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = Math.Max(workArea.Left + 16, workArea.Right - Width - 24);
        Top = workArea.Top + 72;
    }

    private void InitializeTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        _trayMenu = new Forms.ContextMenuStrip();
        _trayMenu.Items.Add(CreateTrayItem("Show and edit", (_, _) => Dispatch(RestoreEditMode)));
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());

        var layoutMenu = new Forms.ToolStripMenuItem("Layout");
        foreach (var layout in Enum.GetValues<WidgetLayout>())
        {
            var layoutItem = CreateTrayItem(
                layout.ToString(),
                (_, _) => Dispatch(() => _viewModel.Layout = layout));
            layoutItem.Tag = layout;
            layoutMenu.DropDownItems.Add(layoutItem);
        }

        _trayMenu.Items.Add(layoutMenu);
        _topmostTrayItem = CreateTrayItem(
            "Always on top",
            (_, _) => Dispatch(() => _viewModel.Topmost = !_viewModel.Topmost));
        _lockedTrayItem = CreateTrayItem(
            "Lock position",
            (_, _) => Dispatch(() =>
                _viewModel.InteractionMode = _viewModel.InteractionMode == WidgetInteractionMode.Locked
                    ? WidgetInteractionMode.Edit
                    : WidgetInteractionMode.Locked));
        _clickThroughTrayItem = CreateTrayItem(
            "Click-through",
            (_, _) => Dispatch(() =>
                _viewModel.InteractionMode = _viewModel.InteractionMode == WidgetInteractionMode.ClickThrough
                    ? WidgetInteractionMode.Edit
                    : WidgetInteractionMode.ClickThrough));

        _trayMenu.Items.Add(_topmostTrayItem);
        _trayMenu.Items.Add(_lockedTrayItem);
        _trayMenu.Items.Add(_clickThroughTrayItem);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add(CreateTrayItem("Open weather", (_, _) => Dispatch(OpenWeatherWindow)));
        _trayMenu.Items.Add(CreateTrayItem("Open Studio", (_, _) => Dispatch(LaunchStudio)));
        _trayMenu.Items.Add(CreateTrayItem("Exit", (_, _) => Dispatch(Close)));
        _trayMenu.Opening += TrayMenu_OnOpening;

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = LoadApplicationIcon(),
            Text = "OPS Monitor · Ctrl+Alt+O to edit",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatch(RestoreEditMode);
    }

    private static Drawing.Icon LoadApplicationIcon()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var icon = Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (icon is not null)
            {
                return icon;
            }
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    private static Forms.ToolStripMenuItem CreateTrayItem(
        string text,
        EventHandler handler)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += handler;
        return item;
    }

    private void TrayMenu_OnOpening(object? sender, CancelEventArgs e)
    {
        _ = sender;
        _ = e;

        if (_topmostTrayItem is not null)
        {
            _topmostTrayItem.Checked = _viewModel.Topmost;
        }

        if (_lockedTrayItem is not null)
        {
            _lockedTrayItem.Checked = _viewModel.InteractionMode == WidgetInteractionMode.Locked;
        }

        if (_clickThroughTrayItem is not null)
        {
            _clickThroughTrayItem.Checked =
                _viewModel.InteractionMode == WidgetInteractionMode.ClickThrough;
        }

        if (_trayMenu?.Items.OfType<Forms.ToolStripMenuItem>()
                .FirstOrDefault(item => item.Text == "Layout") is { } layoutMenu)
        {
            foreach (var item in layoutMenu.DropDownItems.OfType<Forms.ToolStripMenuItem>())
            {
                item.Checked = item.Tag is WidgetLayout layout && layout == _viewModel.Layout;
            }
        }
    }

    private void Dispatch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = Dispatcher.BeginInvoke(action);
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;

        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.Layout):
            case nameof(MainWindowViewModel.Density):
            case nameof(MainWindowViewModel.ScalePercent):
                UpdateWindowConstraints();
                if (_isLoaded && !_isApplyingExternalSettings)
                {
                    ApplySuggestedSize();
                }

                break;
            case nameof(MainWindowViewModel.InteractionMode):
                ApplyInteractionMode();
                break;
            case nameof(MainWindowViewModel.Topmost):
                Topmost = _viewModel.Topmost;
                break;
            case nameof(MainWindowViewModel.VisibleModuleCount):
                UpdateWindowConstraints();
                if (_isLoaded && !_isApplyingExternalSettings)
                {
                    ApplySuggestedSize();
                }

                break;
        }

        if (!_isApplyingExternalSettings &&
            e.PropertyName is not null &&
            PersistedViewModelProperties.Contains(e.PropertyName))
        {
            ScheduleSave();
        }
    }

    private void UpdateWindowConstraints()
    {
        var recommendation = WidgetSizingPolicy.Calculate(
            _viewModel.Layout,
            _viewModel.Density,
            _viewModel.VisibleModuleCount,
            _viewModel.ScalePercent);
        var workArea = SystemParameters.WorkArea;
        var maximumUsableWidth = Math.Max(
            160,
            Math.Min(MaxWidth, workArea.Width - 32));
        var maximumUsableHeight = Math.Max(
            140,
            Math.Min(MaxHeight, workArea.Height - 32));
        MinWidth = Math.Min(recommendation.MinimumWidth, maximumUsableWidth);
        MinHeight = Math.Min(recommendation.MinimumHeight, maximumUsableHeight);
    }

    private void ApplySuggestedSize()
    {
        var recommendation = WidgetSizingPolicy.Calculate(
            _viewModel.Layout,
            _viewModel.Density,
            _viewModel.VisibleModuleCount,
            _viewModel.ScalePercent);
        var workArea = SystemParameters.WorkArea;
        var maximumUsableWidth = Math.Max(
            MinWidth,
            Math.Min(MaxWidth, workArea.Width - 32));
        var maximumUsableHeight = Math.Max(
            MinHeight,
            Math.Min(MaxHeight, workArea.Height - 32));
        Width = Math.Clamp(
            recommendation.SuggestedWidth,
            MinWidth,
            maximumUsableWidth);
        Height = Math.Clamp(
            recommendation.SuggestedHeight,
            MinHeight,
            maximumUsableHeight);
        EnsureVisibleOnScreen();
    }

    private void ApplyInteractionMode()
    {
        if (_windowHandle == 0)
        {
            return;
        }

        var clickThrough = _viewModel.InteractionMode == WidgetInteractionMode.ClickThrough;
        NativeMethods.SetClickThrough(_windowHandle, clickThrough);
        if (!clickThrough)
        {
            IsHitTestVisible = true;
        }
    }

    private void RestoreEditMode()
    {
        _viewModel.InteractionMode = WidgetInteractionMode.Edit;
        NativeMethods.SetClickThrough(_windowHandle, false);
        Show();
        WindowState = WindowState.Normal;
        Activate();
        (_viewModel.Layout == WidgetLayout.Dock
            ? DockSettingsButton
            : SettingsButton).Focus();
    }

    private void DragHeader_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _ = sender;

        if (!_viewModel.CanDrag ||
            e.LeftButton != MouseButtonState.Pressed ||
            FindVisualAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Mouse capture can be lost when a popup or another window activates.
        }
    }

    private void ResizeGrip_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _ = sender;

        if (!_viewModel.CanResize || _windowHandle == 0)
        {
            return;
        }

        e.Handled = true;
        NativeMethods.BeginBottomRightResize(_windowHandle);
    }

    private static T? FindVisualAncestor<T>(DependencyObject? element)
        where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match)
            {
                return match;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is ButtonBase placementTarget)
        {
            QuickSettingsPopup.PlacementTarget = placementTarget;
        }

        _viewModel.IsSettingsOpen = !_viewModel.IsSettingsOpen;
    }

    private void OpenWeatherButton_OnClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _viewModel.IsSettingsOpen = false;
        OpenWeatherWindow();
    }

    private void MetricCard_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is MetricCard { DataContext: MetricCardViewModel metric } &&
            StringComparer.Ordinal.Equals(metric.Key, WidgetModuleCatalog.Weather))
        {
            e.Handled = true;
        }
    }

    private void MetricCard_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not MetricCard { DataContext: MetricCardViewModel metric } ||
            !StringComparer.Ordinal.Equals(metric.Key, WidgetModuleCatalog.Weather))
        {
            return;
        }

        e.Handled = true;
        OpenWeatherWindow();
    }

    private void OpenWeatherWindow()
    {
        if (_weatherWindow is null)
        {
            _weatherWindow = new WeatherWindow(
                _viewModel.WeatherService,
                async location =>
                {
                    await _viewModel.SetWeatherLocationAsync(location).ConfigureAwait(true);
                    ScheduleSave();
                });
            _weatherWindow.Closed += (_, _) => _weatherWindow = null;
        }

        _weatherWindow.Show();
        _weatherWindow.WindowState = WindowState.Normal;
        _weatherWindow.Activate();
    }

    private void CloseSettings_OnClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _viewModel.IsSettingsOpen = false;
    }

    private void EditMode_OnClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _viewModel.InteractionMode = WidgetInteractionMode.Edit;
    }

    private void LockedMode_OnClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _viewModel.InteractionMode = WidgetInteractionMode.Locked;
        _viewModel.IsSettingsOpen = false;
    }

    private void ClickThroughMode_OnClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _viewModel.IsSettingsOpen = false;
        _viewModel.InteractionMode = WidgetInteractionMode.ClickThrough;

        if (_trayIcon is not null)
        {
            _trayIcon.BalloonTipTitle = "Click-through enabled";
            _trayIcon.BalloonTipText = _hotkeyRegistered
                ? "Press Ctrl+Alt+O or use the tray menu to return to Edit mode."
                : "Use the OPS Monitor tray menu to return to Edit mode.";
            _trayIcon.ShowBalloonTip(3_500);
        }
    }

    private void LaunchStudio_OnClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        LaunchStudio();
    }

    private void LaunchStudio()
    {
        _saveTimer.Stop();
        SaveNow();

        var executable = FindStudioExecutable();
        if (executable is not null)
        {
            StartProcess(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executable)
            });
            return;
        }

        var project = FindStudioProject();
        if (project is not null)
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(project)
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(project);
            StartProcess(startInfo);
            return;
        }

        MessageBox.Show(
            this,
            "OPS Monitor Studio is not installed beside the widget yet.",
            "OPS Monitor",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static void StartProcess(ProcessStartInfo startInfo)
    {
        try
        {
            using var process = Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            // The shell or dotnet host could not start. The widget remains usable.
        }
        catch (InvalidOperationException)
        {
            // Invalid launch configuration should not take down the widget.
        }
    }

    private static string? FindStudioExecutable()
    {
        var besideWidget = Path.Combine(AppContext.BaseDirectory, "OpsMonitor.Studio.exe");
        if (File.Exists(besideWidget))
        {
            return besideWidget;
        }

        var project = FindStudioProject();
        var binDirectory = project is null
            ? null
            : Path.Combine(Path.GetDirectoryName(project)!, "bin");
        if (binDirectory is null || !Directory.Exists(binDirectory))
        {
            return null;
        }

        try
        {
            return Directory
                .EnumerateFiles(binDirectory, "OpsMonitor.Studio.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FindStudioProject()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && directory is not null; depth++, directory = directory.Parent)
        {
            if (!directory.Name.Equals("OpsMonitor.Widget", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = Path.Combine(
                directory.Parent?.FullName ?? string.Empty,
                "OpsMonitor.Studio",
                "OpsMonitor.Studio.csproj");
            return File.Exists(candidate) ? candidate : null;
        }

        return null;
    }

    private void WindowGeometry_OnChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        if (!_isRestoringGeometry)
        {
            ScheduleSave();
        }
    }

    private void ScheduleSave()
    {
        if (!_isLoaded || _isEphemeralSession)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveTimer_OnTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _saveTimer.Stop();
        SaveNow();
    }

    private async Task StartSettingsWatcherAsync()
    {
        try
        {
            await _settingsWatcher.StartAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The window closed before the watcher finished starting.
        }
        catch (IOException)
        {
            // Explicit Studio restart/apply remains available.
        }
        catch (UnauthorizedAccessException)
        {
            // A restricted profile must not take down the widget.
        }
    }

    private void SettingsWatcher_OnReloadRequested(
        object? sender,
        SettingsReloadRequestedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;

        var settings = WidgetSettingsStore.Reload();
        _ = Dispatcher.BeginInvoke(() => ApplyExternalSettings(settings));
        if (_viewModel.TelemetrySource is CoreTelemetrySource core)
        {
            core.ReloadSettings();
        }
    }

    private void ApplyExternalSettings(WidgetSettings settings)
    {
        _isApplyingExternalSettings = true;
        _isRestoringGeometry = true;
        try
        {
            _viewModel.Layout = settings.Layout;
            _viewModel.Density = settings.Density;
            _viewModel.InteractionMode = settings.InteractionMode;
            _viewModel.ApplyThemeConfiguration(
                settings.Theme,
                settings.CoreThemeId,
                settings.RuntimeThemes);
            _viewModel.Topmost = settings.Topmost;
            _viewModel.Draggable = settings.Draggable;
            _viewModel.Resizable = settings.Resizable;
            _viewModel.ApplyModuleConfiguration(
                settings.ModuleOrder,
                settings.EnabledModules,
                settings.ModulePresentation);
            if (!_viewModel.WeatherLocation.Equals(new WeatherLocation(
                    settings.WeatherLocationName,
                    settings.WeatherCountry,
                    settings.WeatherLatitude,
                    settings.WeatherLongitude,
                    settings.WeatherTimeZone,
                    settings.WeatherArsoStationCode)))
            {
                _ = _viewModel.SetWeatherLocationAsync(new WeatherLocation(
                    settings.WeatherLocationName,
                    settings.WeatherCountry,
                    settings.WeatherLatitude,
                    settings.WeatherLongitude,
                    settings.WeatherTimeZone,
                    settings.WeatherArsoStationCode));
            }
            _viewModel.StartAtSignIn = settings.StartAtSignIn;
            _viewModel.ScalePercent = settings.ScalePercent;
            _viewModel.UpdateCadenceSeconds = settings.UpdateCadenceSeconds;
            _lastRuntimeCadenceSeconds = _viewModel.UpdateCadenceSeconds;
            _viewModel.SurfaceOpacity = settings.SurfaceOpacity;
            _viewModel.ContentOpacity = settings.ContentOpacity;

            UpdateWindowConstraints();
            if (settings.Width is { } width)
            {
                Width = Math.Clamp(width, MinWidth, MaxWidth);
            }

            if (settings.Height is { } height)
            {
                Height = Math.Clamp(height, MinHeight, MaxHeight);
            }

            if (settings.Left is { } left && settings.Top is { } top)
            {
                Left = left;
                Top = top;
            }

            EnsureVisibleOnScreen();
            ApplyInteractionMode();
            SynchronizeStartupRegistration(settings.StartAtSignIn);
        }
        finally
        {
            _isRestoringGeometry = false;
            _isApplyingExternalSettings = false;
        }
    }

    private void SaveNow()
    {
        if (_isEphemeralSession)
        {
            return;
        }

        var settings = new WidgetSettings
        {
            Layout = _viewModel.Layout,
            Density = _viewModel.Density,
            InteractionMode = _viewModel.InteractionMode,
            Theme = _viewModel.ThemeName,
            CoreThemeId = _viewModel.CoreThemeId,
            Topmost = _viewModel.Topmost,
            Draggable = _viewModel.Draggable,
            Resizable = _viewModel.Resizable,
            ShowBattery = _viewModel.ShowBattery,
            ShowWeather = _viewModel.ShowWeather,
            WeatherLocationName = _viewModel.WeatherLocation.Name,
            WeatherCountry = _viewModel.WeatherLocation.Country,
            WeatherLatitude = _viewModel.WeatherLocation.Latitude,
            WeatherLongitude = _viewModel.WeatherLocation.Longitude,
            WeatherTimeZone = _viewModel.WeatherLocation.TimeZone,
            WeatherArsoStationCode = _viewModel.WeatherLocation.ArsoStationCode,
            WeatherRefreshMinutes = _viewModel.WeatherRefreshMinutes,
            ModuleOrder = [.. _viewModel.GetModuleOrder()],
            EnabledModules = [.. _viewModel.GetEnabledModules()],
            ModulePresentation = _viewModel.GetModulePresentation(),
            StartAtSignIn = _viewModel.StartAtSignIn,
            ScalePercent = _viewModel.ScalePercent,
            UpdateCadenceSeconds = _viewModel.UpdateCadenceSeconds,
            SurfaceOpacity = _viewModel.SurfaceOpacity,
            ContentOpacity = _viewModel.ContentOpacity,
            Left = RestoreBounds.Left,
            Top = RestoreBounds.Top,
            Width = RestoreBounds.Width,
            Height = RestoreBounds.Height
        };

        IDisposable? suppression = null;
        try
        {
            if (_settingsWatcher.IsRunning)
            {
                suppression = _settingsWatcher.SuppressNotifications();
            }

            if (WidgetSettingsStore.Save(settings))
            {
                SynchronizeStartupRegistration(settings.StartAtSignIn);
                if (!_isClosing &&
                    _viewModel.TelemetrySource is CoreTelemetrySource core &&
                    RuntimeCadenceChanged(
                        _lastRuntimeCadenceSeconds,
                        settings.UpdateCadenceSeconds))
                {
                    _lastRuntimeCadenceSeconds = settings.UpdateCadenceSeconds;
                    core.ReloadSettings();
                }
            }
        }
        finally
        {
            suppression?.Dispose();
        }
    }

    internal static bool RuntimeCadenceChanged(double previous, double current) =>
        !double.IsFinite(previous) ||
        !double.IsFinite(current) ||
        Math.Abs(previous - current) >= 0.001;

    private void SynchronizeStartupRegistration(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                _ = _startupRegistration.Remove();
                return;
            }

            var executablePath = FindCurrentWidgetExecutable();
            if (executablePath is null ||
                _startupRegistration.IsRegisteredFor(executablePath))
            {
                return;
            }

            _ = _startupRegistration.Register(executablePath);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            PlatformNotSupportedException)
        {
            // Startup registration is optional and must not disrupt monitoring.
        }
    }

    private static string? FindCurrentWidgetExecutable()
    {
        var processPath = Environment.ProcessPath;
        if (processPath is not null && IsWidgetExecutable(processPath))
        {
            return Path.GetFullPath(processPath);
        }

        var appHostPath = Path.ChangeExtension(
            typeof(MainWindow).Assembly.Location,
            ".exe");
        return IsWidgetExecutable(appHostPath)
            ? Path.GetFullPath(appHostPath)
            : null;
    }

    private static bool IsWidgetExecutable(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.GetFileName(path).Equals(
            "OpsMonitor.Widget.exe",
            StringComparison.OrdinalIgnoreCase) &&
        File.Exists(path);
}
