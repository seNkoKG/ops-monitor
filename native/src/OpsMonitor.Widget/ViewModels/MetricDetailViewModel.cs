using OpsMonitor.Widget.Infrastructure;

namespace OpsMonitor.Widget.ViewModels;

internal sealed class MetricDetailViewModel : ObservableObject
{
    private string _value = "—";
    private bool _isAvailable = true;

    public MetricDetailViewModel(string label, bool isNormalVisible)
    {
        Label = label;
        IsNormalVisible = isNormalVisible;
    }

    public string Label { get; }

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
