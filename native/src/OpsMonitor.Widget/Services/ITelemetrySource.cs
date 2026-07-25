using OpsMonitor.Widget.Models;

namespace OpsMonitor.Widget.Services;

public interface ITelemetrySource : IDisposable
{
    string Name { get; }

    bool IsDemo { get; }

    event EventHandler<TelemetrySnapshot>? SnapshotAvailable;

    void Start();
}
