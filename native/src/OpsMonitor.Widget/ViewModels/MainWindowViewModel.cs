using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using OpsMonitor.Widget.Infrastructure;
using OpsMonitor.Widget.Models;
using OpsMonitor.Widget.Services;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace OpsMonitor.Widget.ViewModels;

internal sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly IReadOnlyList<ThemeDefinition> Themes =
    [
        new(
            "Void",
            Color.FromRgb(8, 11, 18),
            Color.FromRgb(15, 21, 33),
            Color.FromRgb(54, 66, 88),
            Color.FromRgb(246, 249, 255),
            Color.FromRgb(157, 171, 194),
            Color.FromRgb(72, 220, 249),
            Color.FromRgb(255, 79, 216),
            Color.FromRgb(88, 230, 178),
            Color.FromRgb(255, 195, 90)),
        new(
            "Aurora",
            Color.FromRgb(14, 10, 27),
            Color.FromRgb(25, 19, 45),
            Color.FromRgb(76, 62, 111),
            Color.FromRgb(249, 246, 255),
            Color.FromRgb(180, 169, 207),
            Color.FromRgb(86, 226, 255),
            Color.FromRgb(255, 91, 215),
            Color.FromRgb(91, 241, 190),
            Color.FromRgb(255, 184, 91)),
        new(
            "Slate",
            Color.FromRgb(18, 24, 31),
            Color.FromRgb(27, 36, 47),
            Color.FromRgb(67, 83, 101),
            Color.FromRgb(244, 249, 252),
            Color.FromRgb(164, 181, 194),
            Color.FromRgb(72, 207, 234),
            Color.FromRgb(235, 99, 207),
            Color.FromRgb(86, 221, 167),
            Color.FromRgb(244, 184, 89)),
        new(
            "Ember",
            Color.FromRgb(22, 14, 15),
            Color.FromRgb(38, 23, 25),
            Color.FromRgb(93, 56, 60),
            Color.FromRgb(255, 248, 244),
            Color.FromRgb(203, 174, 169),
            Color.FromRgb(77, 215, 239),
            Color.FromRgb(255, 93, 179),
            Color.FromRgb(94, 225, 168),
            Color.FromRgb(255, 172, 72))
    ];

    private readonly ITelemetrySource _telemetrySource;
    private readonly Dictionary<string, MetricCardViewModel> _metricIndex;
    private WidgetLayout _layout;
    private WidgetDensity _density;
    private WidgetInteractionMode _interactionMode;
    private string _themeName;
    private bool _topmost;
    private bool _draggable;
    private bool _resizable;
    private bool _showBattery;
    private bool _startAtSignIn;
    private double _updateCadenceSeconds;
    private double _surfaceOpacity;
    private double _contentOpacity;
    private bool _isSettingsOpen;
    private string _lastUpdatedText = "Connecting to telemetry…";
    private Brush _surfaceBrush = Brushes.Black;
    private Brush _cardBrush = Brushes.Black;
    private Brush _borderBrush = Brushes.DimGray;
    private Brush _textPrimaryBrush = Brushes.White;
    private Brush _textSecondaryBrush = Brushes.LightGray;
    private Brush _flyoutBrush = Brushes.Black;
    private Brush _trackBrush = Brushes.DimGray;
    private bool _disposed;

    public MainWindowViewModel(ITelemetrySource telemetrySource, WidgetSettings settings)
    {
        ArgumentNullException.ThrowIfNull(telemetrySource);
        ArgumentNullException.ThrowIfNull(settings);

        _telemetrySource = telemetrySource;
        _layout = settings.Layout;
        _density = settings.Density;
        _interactionMode = settings.InteractionMode;
        _themeName = Themes.Any(theme => theme.Name.Equals(settings.Theme, StringComparison.OrdinalIgnoreCase))
            ? Themes.First(theme => theme.Name.Equals(settings.Theme, StringComparison.OrdinalIgnoreCase)).Name
            : Themes[0].Name;
        _topmost = settings.Topmost;
        _draggable = settings.Draggable;
        _resizable = settings.Resizable;
        _showBattery = settings.ShowBattery;
        _startAtSignIn = settings.StartAtSignIn;
        _updateCadenceSeconds = NormalizeUpdateCadence(settings.UpdateCadenceSeconds);
        _surfaceOpacity = Math.Clamp(settings.SurfaceOpacity, 0.28, 0.98);
        _contentOpacity = Math.Clamp(settings.ContentOpacity, 0.65, 1);

        MetricCardViewModel[] metrics =
        [
            CreateCpuMetric(),
            CreateGpuMetric(),
            CreateMemoryMetric(),
            CreateNetworkMetric(),
            CreateStorageMetric(),
            CreateBatteryMetric()
        ];
        Metrics = new ObservableCollection<MetricCardViewModel>(metrics);
        _metricIndex = metrics.ToDictionary(metric => metric.Key, StringComparer.Ordinal);

        ApplyTheme();
        ApplyModuleConfiguration(settings.ModuleOrder, settings.EnabledModules);
        _telemetrySource.SnapshotAvailable += OnSnapshotAvailable;
    }

    public ObservableCollection<MetricCardViewModel> Metrics { get; }

    public ITelemetrySource TelemetrySource => _telemetrySource;

    public IReadOnlyList<WidgetLayout> LayoutOptions { get; } = Enum.GetValues<WidgetLayout>();

    public IReadOnlyList<WidgetDensity> DensityOptions { get; } = Enum.GetValues<WidgetDensity>();

    public IReadOnlyList<string> ThemeOptions { get; } = Themes.Select(theme => theme.Name).ToArray();

    public string SourceName => _telemetrySource.Name;

    public bool IsDemo => _telemetrySource.IsDemo;

    public string SourceBadge => IsDemo ? "DEMO DATA" : "LIVE";

    public string HotkeyHint => InteractionMode == WidgetInteractionMode.ClickThrough
        ? "CTRL+ALT+O · EDIT"
        : "DRAG · CTRL+ALT+O";

    public MetricCardViewModel BatteryMetric => _metricIndex["battery"];

    public MetricCardViewModel StorageMetric => _metricIndex["storage"];

    public WidgetLayout Layout
    {
        get => _layout;
        set => SetProperty(ref _layout, value);
    }

    public WidgetDensity Density
    {
        get => _density;
        set => SetProperty(ref _density, value);
    }

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

    public double UpdateCadenceSeconds
    {
        get => _updateCadenceSeconds;
        set => SetProperty(
            ref _updateCadenceSeconds,
            NormalizeUpdateCadence(value));
    }

    public double SurfaceOpacity
    {
        get => _surfaceOpacity;
        set
        {
            if (SetProperty(ref _surfaceOpacity, Math.Clamp(value, 0.28, 0.98)))
            {
                ApplyTheme();
            }
        }
    }

    public double ContentOpacity
    {
        get => _contentOpacity;
        set => SetProperty(ref _contentOpacity, Math.Clamp(value, 0.65, 1));
    }

    public string ThemeName
    {
        get => _themeName;
        set
        {
            if (!Themes.Any(theme => theme.Name.Equals(value, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (SetProperty(ref _themeName, value))
            {
                ApplyTheme();
            }
        }
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
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

    public void Start() => _telemetrySource.Start();

    public void ApplyModuleConfiguration(
        IEnumerable<string>? order,
        IEnumerable<string>? enabled)
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
        }

        _ = SetProperty(
            ref _showBattery,
            enabledKeys.Contains(WidgetModuleCatalog.Battery),
            nameof(ShowBattery));
    }

    public IReadOnlyList<string> GetModuleOrder() =>
        Metrics.Select(metric => metric.Key).ToArray();

    public IReadOnlyList<string> GetEnabledModules() =>
        Metrics
            .Where(metric => metric.IsVisible)
            .Select(metric => metric.Key)
            .ToArray();

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
        _telemetrySource.Dispose();
    }

    private static MetricCardViewModel CreateCpuMetric()
    {
        var metric = new MetricCardViewModel(
            "cpu",
            "CPU",
            ParseGeometry("M4,7 L4,17 L20,17 L20,7 Z M8,3 L8,7 M12,3 L12,7 M16,3 L16,7 M8,17 L8,21 M12,17 L12,21 M16,17 L16,21 M1,10 L4,10 M1,14 L4,14 M20,10 L23,10 M20,14 L23,14"),
            SemanticAccent.Cyan);
        metric.ConfigureDetails(("TEMP", true), ("CLOCK", true), ("POWER", false));
        return metric;
    }

    private static MetricCardViewModel CreateGpuMetric()
    {
        var metric = new MetricCardViewModel(
            "gpu",
            "GPU",
            ParseGeometry("M3,5 L19,5 L19,19 L3,19 Z M7,9 A5,5 0 1 0 17,9 A5,5 0 1 0 7,9 M19,9 L23,9 M19,14 L23,14"),
            SemanticAccent.Magenta);
        metric.ConfigureDetails(("TEMP", true), ("VRAM", true), ("CLOCK", false));
        return metric;
    }

    private static MetricCardViewModel CreateMemoryMetric()
    {
        var metric = new MetricCardViewModel(
            "memory",
            "RAM",
            ParseGeometry("M3,7 L21,7 L21,17 L3,17 Z M7,10 L7,14 M11,10 L11,14 M15,10 L15,14 M5,17 L5,21 M9,17 L9,21 M13,17 L13,21 M17,17 L17,21"),
            SemanticAccent.Mint);
        metric.ConfigureDetails(("COMMIT", true), ("CACHED", true), ("HEADROOM", false));
        return metric;
    }

    private static MetricCardViewModel CreateNetworkMetric()
    {
        var metric = new MetricCardViewModel(
            "network",
            "NET",
            ParseGeometry("M8,3 L8,20 M4,7 L8,3 L12,7 M17,21 L17,4 M13,17 L17,21 L21,17"),
            SemanticAccent.Cyan);
        metric.ConfigureDetails(("UPLOAD", true), ("LOSS", true), ("JITTER", false));
        return metric;
    }

    private static MetricCardViewModel CreateStorageMetric()
    {
        var metric = new MetricCardViewModel(
            "storage",
            "STORAGE",
            ParseGeometry("M4,6 C4,3 20,3 20,6 L20,18 C20,21 4,21 4,18 Z M4,6 C4,9 20,9 20,6 M4,12 C4,15 20,15 20,12"),
            SemanticAccent.Amber);
        metric.ConfigureDetails(("READ", true), ("WRITE", true), ("TEMP", false), ("HEALTH", false));
        return metric;
    }

    private static MetricCardViewModel CreateBatteryMetric()
    {
        var metric = new MetricCardViewModel(
            "battery",
            "BATTERY",
            ParseGeometry("M3,7 L19,7 L19,18 L3,18 Z M19,10 L22,10 L22,15 L19,15 M6,10 L14,10 L14,15 L6,15 Z"),
            SemanticAccent.Mint);
        metric.ConfigureDetails(("STATE", true), ("RUNTIME", true), ("DRAW", false));
        return metric;
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

        var age = DateTimeOffset.Now - snapshot.CapturedAt;
        LastUpdatedText = age.TotalSeconds < 3
            ? "Updated now"
            : $"Updated {Math.Max(1, (int)age.TotalSeconds)}s ago";
    }

    private void UpdateCpu(CpuTelemetry sample)
    {
        var metric = _metricIndex["cpu"];
        metric.PrimaryValue = $"{sample.LoadPercent:0}%";
        metric.Progress = sample.LoadPercent;
        metric.State = sample.State;
        metric.Status = sample.TemperatureCelsius is null
            ? "TEMP N/A"
            : sample.State == SensorState.Stale
                ? $"{FormatTemperature(sample.TemperatureCelsius)} stale"
                : FormatTemperature(sample.TemperatureCelsius);
        var hasClock = sample.ClockGhz > 0;
        var hasPower = sample.PackagePowerWatts > 0;
        metric.SetDetailValues(
            (FormatTemperature(sample.TemperatureCelsius), sample.TemperatureCelsius is not null),
            (hasClock ? $"{sample.ClockGhz:0.00} GHz" : "—", hasClock),
            (hasPower ? $"{sample.PackagePowerWatts:0} W" : "—", hasPower));
        metric.PushSample(sample.LoadPercent);
    }

    private void UpdateGpu(GpuTelemetry sample)
    {
        var metric = _metricIndex["gpu"];
        metric.PrimaryValue = sample.State == SensorState.Unavailable
            ? "—"
            : $"{sample.LoadPercent:0}%";
        metric.Progress = sample.LoadPercent;
        metric.State = sample.State;
        metric.Status = sample.TemperatureCelsius is null
            ? "TEMP N/A"
            : FormatTemperature(sample.TemperatureCelsius);
        var hasClock = sample.ClockGhz > 0;
        var hasVram = sample.TotalVramGigabytes > 0;
        metric.SetDetailValues(
            (FormatTemperature(sample.TemperatureCelsius), sample.TemperatureCelsius is not null),
            (hasVram
                ? $"{sample.UsedVramGigabytes:0.0}/{sample.TotalVramGigabytes:0.#} GB"
                : "—", hasVram),
            (hasClock ? $"{sample.ClockGhz:0.00} GHz" : "—", hasClock));
        metric.PushSample(sample.LoadPercent);
    }

    private void UpdateMemory(MemoryTelemetry sample)
    {
        var metric = _metricIndex["memory"];
        var usedPercent = sample.TotalGigabytes <= 0
            ? 0
            : (sample.UsedGigabytes / sample.TotalGigabytes) * 100;
        metric.PrimaryValue = $"{sample.UsedGigabytes:0.0}/{sample.TotalGigabytes:0.#} GB";
        metric.Progress = usedPercent;
        metric.State = sample.State;
        metric.Status = $"{usedPercent:0}% used";
        var hasCommit = sample.CommitGigabytes > 0;
        var hasCached = sample.CachedGigabytes > 0;
        metric.SetDetailValues(
            (hasCommit ? $"{sample.CommitGigabytes:0.0} GB" : "—", hasCommit),
            (hasCached ? $"{sample.CachedGigabytes:0.0} GB" : "—", hasCached),
            ($"{Math.Max(0, sample.TotalGigabytes - sample.UsedGigabytes):0.0} GB", true));
        metric.PushSample(usedPercent);
    }

    private void UpdateNetwork(NetworkTelemetry sample)
    {
        var metric = _metricIndex["network"];
        metric.PrimaryValue =
            $"\u2193{FormatCompactRate(sample.DownloadBytesPerSecond)}  " +
            $"\u2191{FormatCompactRate(sample.UploadBytesPerSecond)}";
        metric.Progress = Math.Clamp((Math.Log10(sample.DownloadBytesPerSecond + 1) / 8) * 100, 0, 100);
        metric.State = sample.PacketLossPercent switch
        {
            >= 5 => SensorState.Critical,
            >= 1 => SensorState.Warning,
            _ => sample.State
        };
        metric.Status =
            $"{sample.PingMilliseconds:0}ms LOSS {sample.PacketLossPercent:0.#}%";
        metric.SetDetailValues(
            ($"↑ {FormatRate(sample.UploadBytesPerSecond)}", true),
            ($"{sample.PacketLossPercent:0.0}%", true),
            ($"{sample.JitterMilliseconds:0.0} ms", true));
        metric.PushSample(sample.PingMilliseconds, 80);
    }

    private void UpdateStorage(StorageTelemetry sample)
    {
        var metric = _metricIndex["storage"];
        if (sample.State == SensorState.Unavailable)
        {
            metric.PrimaryValue = "—";
            metric.Progress = 0;
            metric.State = SensorState.Unavailable;
            metric.Status = sample.Health;
            metric.SetDetailValues(
                ("—", false),
                ("—", false),
                ("—", false),
                ("—", false));
            return;
        }

        metric.PrimaryValue = $"{sample.UsedPercent:0}% used";
        metric.Progress = sample.UsedPercent;
        metric.State = sample.State;
        metric.Status = sample.State == SensorState.Stale ? "Sample delayed" : sample.Health;
        metric.SetDetailValues(
            (FormatRate(sample.ReadBytesPerSecond), true),
            (FormatRate(sample.WriteBytesPerSecond), true),
            (FormatTemperature(sample.TemperatureCelsius), sample.TemperatureCelsius is not null),
            (sample.Health, !string.IsNullOrWhiteSpace(sample.Health)));
        metric.PushSample(sample.UsedPercent);
    }

    private void UpdateBattery(BatteryTelemetry sample)
    {
        var metric = _metricIndex["battery"];
        metric.State = sample.State;

        if (sample.ChargePercent is not { } charge)
        {
            metric.PrimaryValue = "Not present";
            metric.Progress = 0;
            metric.Status = "No battery";
            metric.SetDetailValues(
                (sample.PowerState, false),
                ("—", false),
                ("—", false));
            return;
        }

        metric.PrimaryValue = $"{charge:0}%";
        metric.Progress = charge;
        metric.Status = sample.PowerState;
        metric.SetDetailValues(
            (sample.PowerState, true),
            (FormatDuration(sample.Remaining), sample.Remaining is not null),
            (sample.DrawWatts is { } draw ? $"{draw:0.0} W" : "Unavailable", sample.DrawWatts is not null));
        metric.PushSample(charge);
    }

    private void ApplyTheme()
    {
        var theme = Themes.First(candidate =>
            candidate.Name.Equals(ThemeName, StringComparison.OrdinalIgnoreCase));

        SurfaceBrush = CreateBrush(WithOpacity(theme.Surface, SurfaceOpacity));
        CardBrush = CreateBrush(WithOpacity(theme.Card, Math.Clamp(SurfaceOpacity * 0.82, 0.22, 0.92)));
        BorderBrush = CreateBrush(WithOpacity(theme.Border, Math.Clamp(SurfaceOpacity * 0.9, 0.34, 0.92)));
        FlyoutBrush = CreateBrush(WithOpacity(theme.Card, 0.98));
        TrackBrush = CreateBrush(WithOpacity(theme.Border, 0.48));
        TextPrimaryBrush = CreateBrush(theme.TextPrimary);
        TextSecondaryBrush = CreateBrush(theme.TextSecondary);

        if (Metrics is not null)
        {
            foreach (var metric in Metrics)
            {
                metric.SetAccent(theme);
            }
        }
    }

    private static string FormatTemperature(double? value)
        => value is { } temperature ? $"{temperature:0}°C" : "Unavailable";

    private static string FormatDuration(TimeSpan? value)
        => value is { } duration
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)duration.TotalHours}h {duration.Minutes:00}m")
            : "Unavailable";

    private static string FormatRate(double bytesPerSecond)
    {
        var absolute = Math.Abs(bytesPerSecond);
        return absolute switch
        {
            >= 1_000_000_000 => $"{bytesPerSecond / 1_000_000_000:0.0} GB/s",
            >= 1_000_000 => $"{bytesPerSecond / 1_000_000:0.0} MB/s",
            >= 1_000 => $"{bytesPerSecond / 1_000:0} KB/s",
            _ => $"{bytesPerSecond:0} B/s"
        };
    }

    private static string FormatCompactRate(double bytesPerSecond)
    {
        var absolute = Math.Abs(bytesPerSecond);
        return absolute switch
        {
            >= 1_000_000_000 => $"{bytesPerSecond / 1_000_000_000:0.#}G",
            >= 1_000_000 => $"{bytesPerSecond / 1_000_000:0.#}M",
            >= 1_000 => $"{bytesPerSecond / 1_000:0}K",
            _ => $"{bytesPerSecond:0}"
        };
    }

    private static Geometry ParseGeometry(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    private static Color WithOpacity(Color color, double opacity)
        => Color.FromArgb(
            (byte)Math.Round(Math.Clamp(opacity, 0, 1) * byte.MaxValue),
            color.R,
            color.G,
            color.B);

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
