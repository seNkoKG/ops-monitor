namespace OpsMonitor.SensorBridge;

internal static class SensorFailure
{
    internal static bool IsExpected(Exception exception) =>
        exception is UnauthorizedAccessException or
        IOException or
        InvalidOperationException or
        NotSupportedException or
        PlatformNotSupportedException or
        TypeInitializationException or
        TypeLoadException or
        DllNotFoundException or
        FileLoadException or
        FileNotFoundException or
        BadImageFormatException or
        System.ComponentModel.Win32Exception;
}
