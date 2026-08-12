using System.Windows;
using System.Windows.Controls;
using OpsMonitor.Widget.Models;
using UserControl = System.Windows.Controls.UserControl;

namespace OpsMonitor.Widget.Controls;

public partial class MetricCard : UserControl
{
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout),
        typeof(WidgetLayout),
        typeof(MetricCard),
        new PropertyMetadata(WidgetLayout.Rail));

    public static readonly DependencyProperty DensityProperty = DependencyProperty.Register(
        nameof(Density),
        typeof(WidgetDensity),
        typeof(MetricCard),
        new PropertyMetadata(WidgetDensity.Compact));

    public static readonly DependencyProperty ContentOpacityProperty = DependencyProperty.Register(
        nameof(ContentOpacity),
        typeof(double),
        typeof(MetricCard),
        new PropertyMetadata(1d));

    public static readonly DependencyProperty ThemeContextProperty = DependencyProperty.Register(
        nameof(ThemeContext),
        typeof(object),
        typeof(MetricCard),
        new PropertyMetadata(null));

    public MetricCard()
    {
        InitializeComponent();
    }

    public WidgetLayout Layout
    {
        get => (WidgetLayout)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public WidgetDensity Density
    {
        get => (WidgetDensity)GetValue(DensityProperty);
        set => SetValue(DensityProperty, value);
    }

    public double ContentOpacity
    {
        get => (double)GetValue(ContentOpacityProperty);
        set => SetValue(ContentOpacityProperty, value);
    }

    public object? ThemeContext
    {
        get => GetValue(ThemeContextProperty);
        set => SetValue(ThemeContextProperty, value);
    }
}
