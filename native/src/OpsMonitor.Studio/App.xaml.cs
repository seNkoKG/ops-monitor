using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using OpsMonitor.Core.Diagnostics;
using OpsMonitor.Core.Platform;
using Application = System.Windows.Application;

namespace OpsMonitor.Studio;

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
                @"Local\OpsMonitor.Studio.v2",
                out _singleInstance))
        {
            _ = NativeMethods.ActivateExistingStudio();
            Shutdown();
            return;
        }

        AppDiagnostics.Initialize("studio");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
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
            "Unhandled Studio dispatcher exception.",
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
                $"Unhandled Studio exception. terminating={eventArgs.IsTerminating}",
                exception);
        }
        else
        {
            AppDiagnostics.Error(
                $"Unhandled Studio exception object. terminating={eventArgs.IsTerminating}; value={eventArgs.ExceptionObject}");
        }
    }

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs eventArgs)
    {
        _ = sender;
        AppDiagnostics.Error(
            "Unobserved Studio task exception.",
            eventArgs.Exception);
        eventArgs.SetObserved();
    }

    private static class NativeMethods
    {
        private const int ShowWindowRestore = 9;

        internal static bool ActivateExistingStudio()
        {
            var window = FindWindow(null, "OPS Monitor Studio");
            if (window == 0)
            {
                return false;
            }

            _ = ShowWindowAsync(window, ShowWindowRestore);
            return SetForegroundWindow(window);
        }

#pragma warning disable SYSLIB1054
        [DllImport(
            "user32.dll",
            EntryPoint = "FindWindowW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern nint FindWindow(string? className, string windowName);

        [DllImport("user32.dll", EntryPoint = "ShowWindowAsync")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(nint windowHandle, int command);

        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(nint windowHandle);
#pragma warning restore SYSLIB1054
    }
}
