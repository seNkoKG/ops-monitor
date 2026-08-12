using System.Windows;
using System.Windows.Controls;

namespace OpsMonitor.Studio.Controls;

public sealed class StudioSettingToggle : Control
{
    static StudioSettingToggle()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StudioSettingToggle),
            new FrameworkPropertyMetadata(typeof(StudioSettingToggle)));
    }

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(StudioSettingToggle), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(StudioSettingToggle), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(
        nameof(IsChecked), typeof(bool), typeof(StudioSettingToggle),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public bool IsChecked { get => (bool)GetValue(IsCheckedProperty); set => SetValue(IsCheckedProperty, value); }
}
