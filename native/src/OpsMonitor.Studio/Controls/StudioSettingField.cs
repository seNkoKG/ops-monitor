using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpsMonitor.Studio.Controls;

public sealed class StudioSettingField : Control
{
    static StudioSettingField()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StudioSettingField),
            new FrameworkPropertyMetadata(typeof(StudioSettingField)));
    }

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(StudioSettingField), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(StudioSettingField), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StudioSettingField),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty MaxLengthProperty = DependencyProperty.Register(
        nameof(MaxLength), typeof(int), typeof(StudioSettingField), new PropertyMetadata(128));

    public static readonly DependencyProperty ShowSwatchProperty = DependencyProperty.Register(
        nameof(ShowSwatch), typeof(bool), typeof(StudioSettingField), new PropertyMetadata(false));

    public static readonly DependencyProperty SwatchBrushProperty = DependencyProperty.Register(
        nameof(SwatchBrush), typeof(Brush), typeof(StudioSettingField), new PropertyMetadata(Brushes.Transparent));

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public int MaxLength { get => (int)GetValue(MaxLengthProperty); set => SetValue(MaxLengthProperty, value); }
    public bool ShowSwatch { get => (bool)GetValue(ShowSwatchProperty); set => SetValue(ShowSwatchProperty, value); }
    public Brush SwatchBrush { get => (Brush)GetValue(SwatchBrushProperty); set => SetValue(SwatchBrushProperty, value); }
}
