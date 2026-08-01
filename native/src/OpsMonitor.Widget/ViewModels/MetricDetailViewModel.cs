using OpsMonitor.Widget.Infrastructure;

namespace OpsMonitor.Widget.ViewModels;

internal sealed class MetricDetailViewModel : ObservableObject
{
    private string _label;
    private string _value = "—";
    private bool _isAvailable = true;

    public MetricDetailViewModel(string label, bool isNormalVisible)
    {
        _label = label;
        IsNormalVisible = isNormalVisible;
    }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public bool IsNormalVisible { get; }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        set => SetProperty(ref _isAvailable, value);
    }
}
