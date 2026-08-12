using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace OpsMonitor.Studio.Controls;

public sealed class StudioSettingSlider : Control
{
    static StudioSettingSlider()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StudioSettingSlider),
            new FrameworkPropertyMetadata(typeof(StudioSettingSlider)));
    }

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(StudioSettingSlider), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(StudioSettingSlider), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(StudioSettingSlider),
        new FrameworkPropertyMetadata(
            0d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnDisplayValueChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(StudioSettingSlider), new PropertyMetadata(0d, OnDisplayValueChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(StudioSettingSlider), new PropertyMetadata(100d));

    public static readonly DependencyProperty SmallChangeProperty = DependencyProperty.Register(
        nameof(SmallChange), typeof(double), typeof(StudioSettingSlider), new PropertyMetadata(1d));

    public static readonly DependencyProperty LargeChangeProperty = DependencyProperty.Register(
        nameof(LargeChange), typeof(double), typeof(StudioSettingSlider), new PropertyMetadata(10d));

    public static readonly DependencyProperty TickFrequencyProperty = DependencyProperty.Register(
        nameof(TickFrequency), typeof(double), typeof(StudioSettingSlider), new PropertyMetadata(1d));

    public static readonly DependencyProperty IsSnapToTickEnabledProperty = DependencyProperty.Register(
        nameof(IsSnapToTickEnabled), typeof(bool), typeof(StudioSettingSlider), new PropertyMetadata(false));

    public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
        nameof(ValueFormat), typeof(string), typeof(StudioSettingSlider), new PropertyMetadata("0", OnDisplayValueChanged));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(StudioSettingSlider), new PropertyMetadata(string.Empty, OnDisplayValueChanged));

    public static readonly DependencyProperty InheritAtMinimumProperty = DependencyProperty.Register(
        nameof(InheritAtMinimum), typeof(bool), typeof(StudioSettingSlider), new PropertyMetadata(false, OnDisplayValueChanged));

    private static readonly DependencyPropertyKey DisplayValuePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayValue), typeof(string), typeof(StudioSettingSlider), new PropertyMetadata("0"));

    public static readonly DependencyProperty DisplayValueProperty = DisplayValuePropertyKey.DependencyProperty;

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double SmallChange { get => (double)GetValue(SmallChangeProperty); set => SetValue(SmallChangeProperty, value); }
    public double LargeChange { get => (double)GetValue(LargeChangeProperty); set => SetValue(LargeChangeProperty, value); }
    public double TickFrequency { get => (double)GetValue(TickFrequencyProperty); set => SetValue(TickFrequencyProperty, value); }
    public bool IsSnapToTickEnabled { get => (bool)GetValue(IsSnapToTickEnabledProperty); set => SetValue(IsSnapToTickEnabledProperty, value); }
    public string ValueFormat { get => (string)GetValue(ValueFormatProperty); set => SetValue(ValueFormatProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public bool InheritAtMinimum { get => (bool)GetValue(InheritAtMinimumProperty); set => SetValue(InheritAtMinimumProperty, value); }

    public string DisplayValue => (string)GetValue(DisplayValueProperty);

    private static void OnDisplayValueChanged(DependencyObject target, DependencyPropertyChangedEventArgs eventArgs)
    {
        _ = eventArgs;
        var slider = (StudioSettingSlider)target;
        slider.SetValue(
            DisplayValuePropertyKey,
            slider.InheritAtMinimum && slider.Value <= slider.Minimum
                ? "INHERIT"
                : string.Concat(slider.Value.ToString(slider.ValueFormat, CultureInfo.InvariantCulture), slider.Unit));
    }
}
