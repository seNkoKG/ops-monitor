using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Threading;
using OpsMonitor.Core.Diagnostics;
using OpsMonitor.Core.Platform;
using OpsMonitor.Widget.Interop;
using OpsMonitor.Widget.Services;
using Application = System.Windows.Application;

namespace OpsMonitor.Widget;

[SuppressMessage(
    "Design",
    "CA1001",
    Justification = "The WPF application disposes its process-lifetime lease in OnExit.")]
public partial class App : Application
{
    private SingleInstanceCoordinator? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!SingleInstanceCoordinator.TryAcquire(
                @"Local\OpsMonitor.Widget.v2",
                out _singleInstance))
        {
            _ = NativeMethods.SignalExistingInstance();
            Shutdown();
            return;
        }

        AppDiagnostics.Initialize("widget");
        CpuSensorBridgeLauncher.StartMonitoring();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            CpuSensorBridgeLauncher.StopMonitoring();
            DispatcherUnhandledException -= OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            AppDiagnostics.Shutdown(e.ApplicationExitCode);
            base.OnExit(e);
        }
        finally
        {
            _singleInstance?.Dispose();
            _singleInstance = null;
        }
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        _ = sender;
        AppDiagnostics.Error(
            "Unhandled Widget dispatcher exception.",
            eventArgs.Exception);
    }

    private static void OnUnhandledException(
        object sender,
        UnhandledExceptionEventArgs eventArgs)
    {
        _ = sender;
        if (eventArgs.ExceptionObject is Exception exception)
        {
            AppDiagnostics.Error(
                $"Unhandled Widget exception. terminating={eventArgs.IsTerminating}",
                exception);
        }
        else
        {
            AppDiagnostics.Error(
                $"Unhandled Widget exception object. terminating={eventArgs.IsTerminating}; value={eventArgs.ExceptionObject}");
        }
    }

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs eventArgs)
    {
        _ = sender;
        AppDiagnostics.Error(
            "Unobserved Widget task exception.",
            eventArgs.Exception);
        eventArgs.SetObserved();
    }
}
