using System.Collections.ObjectModel;
using System.Windows.Media;
using OpsMonitor.Widget.Infrastructure;
using OpsMonitor.Widget.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace OpsMonitor.Widget.ViewModels;

internal sealed class MetricCardViewModel : ObservableObject
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

    public MetricCardViewModel(
        string key,
        string title,
        Geometry iconData,
        SemanticAccent semanticAccent)
    {
        Key = key;
        Title = title;
        IconData = iconData;
        _semanticAccent = semanticAccent;
    }

    public string Key { get; }

    public string Title { get; }

    public Geometry IconData { get; }

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

    public void SetAccent(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var color = _semanticAccent switch
        {
            SemanticAccent.Gpu => theme.GpuAccent,
            SemanticAccent.Memory => theme.MemoryAccent,
            SemanticAccent.Network => theme.NetworkAccent,
            SemanticAccent.Latency => theme.Warning,
            SemanticAccent.Weather => theme.NetworkAccent,
            _ => theme.CpuAccent
        };

        _warningColor = theme.Warning;
        _criticalColor = theme.Critical;
        AccentBrush = CreateBrush(color);
        HistoryFillBrush = CreateBrush(Color.FromArgb(42, color.R, color.G, color.B));
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
