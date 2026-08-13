using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpsMonitor.Widget.Models;
using OpsMonitor.Widget.Services;
using Drawing = System.Drawing;

namespace OpsMonitor.Widget;

[SuppressMessage(
    "Design",
    "CA1001",
    Justification = "WPF window lifetime disposes the search cancellation source in Window_OnClosed.")]
public partial class WeatherWindow : Window, INotifyPropertyChanged
{
    private readonly WeatherService _weatherService;
    private readonly Func<WeatherLocation, Task> _locationChanged;
    private readonly DispatcherTimer _radarTimer;
    private readonly List<BitmapSource> _radarFrames = [];
    private CancellationTokenSource? _searchCancellation;
    private WeatherSnapshot? _current;
    private string _locationQuery = "Celje";
    private string _statusText = "Loading local weather…";
    private string _radarStatus = "Open Live Radar to load the official ARSO composite";
    private int _radarFrameIndex;
    private bool _radarPlaying = true;
    private double _radarZoom = 0.64;
    private bool _radarLoaded;
    private bool _isSearchOpen;

    internal WeatherWindow(
        WeatherService weatherService,
        Func<WeatherLocation, Task> locationChanged)
    {
        InitializeComponent();
        _weatherService = weatherService;
        _locationChanged = locationChanged;
        _locationQuery = weatherService.Location.Name;
        _radarTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(560)
        };
        _radarTimer.Tick += RadarTimer_OnTick;
        DataContext = this;
        ApplySnapshot(weatherService.Current);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<WeatherHour> Hourly { get; } = [];

    public ObservableCollection<WeatherMinute> Nowcast { get; } = [];

    public ObservableCollection<WeatherDay> Daily { get; } = [];

    public ObservableCollection<WeatherLocation> SearchResults { get; } = [];

    public WeatherSnapshot? Current
    {
        get => _current;
        private set
        {
            if (ReferenceEquals(_current, value))
            {
                return;
            }

            _current = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AlertVisibility));
            OnPropertyChanged(nameof(OfficialOutlookVisibility));
            OnPropertyChanged(nameof(DaylightLabel));
        }
    }

    public string LocationQuery
    {
        get => _locationQuery;
        set
        {
            if (_locationQuery == value)
            {
                return;
            }

            _locationQuery = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsSearchOpen
    {
        get => _isSearchOpen;
        set
        {
            _isSearchOpen = value;
            OnPropertyChanged();
        }
    }

    public Visibility AlertVisibility => Current?.Alert is { IsActive: true }
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility OfficialOutlookVisibility => Current?.OfficialOutlook is not null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string DaylightLabel
    {
        get
        {
            WeatherDay? today = Daily.FirstOrDefault();
            if (today?.Sunrise is not { } sunrise || today.Sunset is not { } sunset)
            {
                return "N/A";
            }

            TimeSpan daylight = sunset - sunrise;
            return $"{(int)daylight.TotalHours}h {daylight.Minutes:00}m";
        }
    }

    public double RadarZoom
    {
        get => _radarZoom;
        set
        {
            _radarZoom = Math.Clamp(value, 0.6, 2.2);
            OnPropertyChanged();
        }
    }

    public string RadarStatus
    {
        get => _radarStatus;
        private set
        {
            _radarStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RadarStatusVisibility));
        }
    }

    public Visibility RadarStatusVisibility => string.IsNullOrWhiteSpace(RadarStatus)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string RadarPlayLabel => _radarPlaying ? "❚❚  PAUSE" : "▶  PLAY";

    public string RadarFrameLabel => _radarFrames.Count == 0
        ? "Waiting for radar"
        : $"{_radarFrameIndex + 1} / {_radarFrames.Count} · 5-minute frames";

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _weatherService.SnapshotAvailable += WeatherService_OnSnapshotAvailable;
        await RefreshWeatherAsync().ConfigureAwait(true);
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _weatherService.SnapshotAvailable -= WeatherService_OnSnapshotAvailable;
        _radarTimer.Stop();
        RadarImage.Source = null;
        _radarFrames.Clear();
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
    }

    private void WeatherService_OnSnapshotAvailable(object? sender, WeatherSnapshot snapshot)
    {
        _ = sender;
        _ = Dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(WeatherSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        Current = snapshot;
        LocationQuery = snapshot.Location.Name;
        Nowcast.Clear();
        foreach (WeatherMinute minute in snapshot.Nowcast)
        {
            Nowcast.Add(minute);
        }

        Hourly.Clear();
        foreach (WeatherHour hour in snapshot.Hourly)
        {
            Hourly.Add(hour);
        }

        Daily.Clear();
        foreach (WeatherDay day in snapshot.Daily)
        {
            Daily.Add(day);
        }

        OnPropertyChanged(nameof(DaylightLabel));
        StatusText = $"{snapshot.FreshnessLabel} · {snapshot.ObservationSource} · {snapshot.Confidence.Label}";
    }

    private async Task RefreshWeatherAsync()
    {
        StatusText = "Refreshing ARSO observations, radar intelligence and 3-model forecast…";
        await _weatherService.RefreshNowAsync().ConfigureAwait(true);
        ApplySnapshot(_weatherService.Current);
        if (_weatherService.Current is null)
        {
            StatusText = "Weather data is temporarily unavailable. The app will retry automatically.";
        }
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await RefreshWeatherAsync().ConfigureAwait(true);
        if (_radarLoaded)
        {
            await LoadRadarAsync(force: true).ConfigureAwait(true);
        }
    }

    private async void SearchButton_OnClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await SearchAsync().ConfigureAwait(true);
    }

    private async void LocationSearchBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        _ = sender;
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await SearchAsync().ConfigureAwait(true);
    }

    private async Task SearchAsync()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        SearchResults.Clear();
        IsSearchOpen = false;
        if (string.IsNullOrWhiteSpace(LocationQuery))
        {
            return;
        }

        StatusText = $"Finding {LocationQuery.Trim()}…";
        try
        {
            IReadOnlyList<WeatherLocation> results = await _weatherService.SearchLocationsAsync(
                LocationQuery,
                _searchCancellation.Token).ConfigureAwait(true);
            foreach (WeatherLocation result in results)
            {
                SearchResults.Add(result);
            }

            IsSearchOpen = results.Count > 0;

            StatusText = results.Count == 0
                ? "No matching locations found. Try a town, municipality, or postcode."
                : "Choose the precise location below.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpRequestException)
        {
            StatusText = "Location search is temporarily unavailable.";
        }
    }

    private async void LocationResult_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WeatherLocation location })
        {
            return;
        }

        SearchResults.Clear();
        IsSearchOpen = false;
        LocationQuery = location.Name;
        StatusText = $"Switching local weather to {location.DisplayName}…";
        await _locationChanged(location).ConfigureAwait(true);
        ApplySnapshot(_weatherService.Current);
    }

    private async void WeatherTabs_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _ = e;
        if (sender is System.Windows.Controls.TabControl tabControl &&
            ReferenceEquals(tabControl.SelectedItem, RadarTab) &&
            !_radarLoaded)
        {
            await LoadRadarAsync(force: false).ConfigureAwait(true);
        }
    }

    private async Task LoadRadarAsync(bool force)
    {
        if (_radarLoaded && !force)
        {
            return;
        }

        RadarStatus = "Loading official ARSO radar…";
        try
        {
            byte[] bytes = await _weatherService.GetRadarAnimationAsync().ConfigureAwait(true);
            _radarFrames.Clear();
            using var stream = new MemoryStream(bytes, writable: false);
            using Drawing.Image gif = Drawing.Image.FromStream(stream);
            var frameDimension = new Drawing.Imaging.FrameDimension(gif.FrameDimensionsList[0]);
            int frameCount = gif.GetFrameCount(frameDimension);
            for (var index = 0; index < frameCount; index++)
            {
                _ = gif.SelectActiveFrame(frameDimension, index);
                using var composed = new Drawing.Bitmap(
                    gif.Width,
                    gif.Height,
                    Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(composed))
                {
                    graphics.DrawImageUnscaled(gif, 0, 0);
                }

                using var png = new MemoryStream();
                composed.Save(png, Drawing.Imaging.ImageFormat.Png);
                png.Position = 0;
                var decoder = new PngBitmapDecoder(
                    png,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                BitmapFrame frame = BitmapFrame.Create(decoder.Frames[0]);
                frame.Freeze();
                _radarFrames.Add(frame);
            }

            _radarFrameIndex = 0;
            RadarTimeline.Maximum = Math.Max(0, _radarFrames.Count - 1);
            RadarTimeline.TickFrequency = 1;
            ShowRadarFrame(0);
            RadarStatus = string.Empty;
            _radarLoaded = true;
            if (_radarPlaying && SystemParameters.ClientAreaAnimation)
            {
                _radarTimer.Start();
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or FileFormatException)
        {
            RadarStatus = "Radar is temporarily unavailable. Press refresh to retry.";
        }
    }

    private void RadarTimer_OnTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_radarFrames.Count == 0)
        {
            return;
        }

        ShowRadarFrame((_radarFrameIndex + 1) % _radarFrames.Count);
    }

    private void ShowRadarFrame(int index)
    {
        if (_radarFrames.Count == 0)
        {
            return;
        }

        _radarFrameIndex = Math.Clamp(index, 0, _radarFrames.Count - 1);
        RadarImage.Source = _radarFrames[_radarFrameIndex];
        RadarTimeline.Value = _radarFrameIndex;
        OnPropertyChanged(nameof(RadarFrameLabel));
    }

    private void RadarTimeline_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _ = sender;
        if (_radarFrames.Count > 0 && Math.Abs(e.NewValue - _radarFrameIndex) >= 0.5)
        {
            ShowRadarFrame((int)Math.Round(e.NewValue));
        }
    }

    private void RadarPlayButton_OnClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _radarPlaying = !_radarPlaying;
        if (_radarPlaying && _radarFrames.Count > 0 && SystemParameters.ClientAreaAnimation)
        {
            _radarTimer.Start();
        }
        else
        {
            _radarTimer.Stop();
        }

        OnPropertyChanged(nameof(RadarPlayLabel));
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _ = sender;
        if (e.ChangedButton == MouseButton.Left &&
            FindVisualAncestor<System.Windows.Controls.Primitives.ButtonBase>(e.OriginalSource as DependencyObject) is null &&
            FindVisualAncestor<System.Windows.Controls.TextBox>(e.OriginalSource as DependencyObject) is null)
        {
            DragMove();
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match)
            {
                return match;
            }

            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
