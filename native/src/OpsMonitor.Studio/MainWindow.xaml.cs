using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OpsMonitor.Studio.ViewModels;

namespace OpsMonitor.Studio;

public partial class MainWindow : Window, IDisposable
{
    private readonly StudioViewModel _viewModel;
    private readonly DispatcherTimer _telemetryTimer;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new StudioViewModel();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.RequestCopyDiagnostics += OnRequestCopyDiagnostics;

        _telemetryTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _telemetryTimer.Tick += (_, _) => _viewModel.AdvanceTelemetry();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdatePageTemplate(animate: false);
        UpdateResponsiveColumns(ActualWidth);
        _viewModel.RefreshWidgetStatus();
        _viewModel.AdvanceTelemetry();
        _telemetryTimer.Start();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _telemetryTimer.Stop();
        _viewModel.FlushSettings();
        Dispose();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudioViewModel.CurrentPageId))
        {
            UpdatePageTemplate(animate: true);
        }
    }

    private void UpdatePageTemplate(bool animate)
    {
        var key = _viewModel.CurrentPageId switch
        {
            "widgets" => "Page.Widgets",
            "modules" => "Page.Modules",
            "appearance" => "Page.Appearance",
            "window" => "Page.Window",
            "alerts" => "Page.Alerts",
            "history" => "Page.History",
            "providers" => "Page.Providers",
            "accessibility" => "Page.Accessibility",
            "diagnostics" => "Page.Diagnostics",
            _ => "Page.Overview",
        };

        PageHost.ContentTemplate = (DataTemplate)FindResource(key);

        if (!animate || _viewModel.ReducedMotion)
        {
            PageHost.Opacity = 1;
            PageTranslate.Y = 0;
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(145));
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
        PageTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(9, 0, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void OnNavigationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is null && listBox.Items.Count > 0)
        {
            listBox.SelectedIndex = 0;
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        var control = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (control && e.Key == Key.K)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (control && e.Key == Key.Z && _viewModel.UndoCommand.CanExecute(null))
        {
            _viewModel.UndoCommand.Execute(null);
            e.Handled = true;
        }
        else if (control && e.Key == Key.Y && _viewModel.RedoCommand.CanExecute(null))
        {
            _viewModel.RedoCommand.Execute(null);
            e.Handled = true;
        }
        else if (control && e.Key == Key.S)
        {
            _viewModel.SaveCommand.Execute(null);
            e.Handled = true;
        }
        else if (control && e.Key == Key.L)
        {
            _viewModel.PositionLocked = !_viewModel.PositionLocked;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocusWithin)
        {
            _viewModel.SearchText = string.Empty;
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void OnRequestCopyDiagnostics(object? sender, EventArgs e)
    {
        var text = new StringBuilder()
            .AppendLine(_viewModel.AppVersion)
            .Append("Scene: ").AppendLine(_viewModel.ActiveScene)
            .Append("Layout: ").AppendLine(_viewModel.SelectedLayout)
            .Append("Visible modules: ").AppendLine(_viewModel.VisibleModulesView.Count.ToString(CultureInfo.InvariantCulture))
            .Append("CPU impact: ").AppendLine(_viewModel.ResourceCpu)
            .Append("Memory: ").AppendLine(_viewModel.ResourceMemory)
            .Append("Editor settings: ").AppendLine(_viewModel.SettingsPath)
            .Append("Runtime settings: ").AppendLine(_viewModel.RuntimeSettingsPath)
            .Append("Widget executable: ").AppendLine(_viewModel.WidgetExecutablePath)
            .ToString();

        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard contention should never interrupt monitoring or settings.
        }
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateResponsiveColumns(e.NewSize.Width);

    private void UpdateResponsiveColumns(double width)
    {
        if (width < 1220)
        {
            SidebarColumn.Width = new GridLength(196);
            PreviewColumn.Width = new GridLength(332);
        }
        else if (width > 1550)
        {
            SidebarColumn.Width = new GridLength(244);
            PreviewColumn.Width = new GridLength(448);
        }
        else
        {
            SidebarColumn.Width = new GridLength(232);
            PreviewColumn.Width = new GridLength(410);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.Dispose();
        GC.SuppressFinalize(this);
    }
}
