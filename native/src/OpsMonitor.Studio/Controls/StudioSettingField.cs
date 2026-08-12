using System.Windows;
using System.Windows.Controls;

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

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public int MaxLength { get => (int)GetValue(MaxLengthProperty); set => SetValue(MaxLengthProperty, value); }
}
