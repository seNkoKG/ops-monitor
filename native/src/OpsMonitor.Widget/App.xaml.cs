using System.Diagnostics.CodeAnalysis;
using System.Windows;
using OpsMonitor.Widget.Interop;
using Application = System.Windows.Application;

namespace OpsMonitor.Widget;

[SuppressMessage(
    "Design",
    "CA1001",
    Justification = "The WPF application disposes its process-lifetime mutex in OnExit.")]
public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            @"Local\OpsMonitor.Widget.v2",
            out var isFirstInstance);
        if (!isFirstInstance)
        {
            _ = NativeMethods.SignalExistingInstance();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        base.OnExit(e);
    }
}
