using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace OpsMonitor.Widget.Interop;

internal static class NativeMethods
{
    internal const int HotkeyId = 0x4F50;
    internal const int WmHotkey = 0x0312;
    internal const int WmShowWidget = 0x8000 + 0x04F0;
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int HitTestBottomRight = 17;
    private const uint ModifierAlt = 0x0001;
    private const uint ModifierControl = 0x0002;
    private const uint VirtualKeyO = 0x4F;

    internal static bool RegisterEditHotkey(nint windowHandle)
        => RegisterHotKey(
            windowHandle,
            HotkeyId,
            ModifierAlt | ModifierControl,
            VirtualKeyO);

    internal static void UnregisterEditHotkey(nint windowHandle)
        => _ = UnregisterHotKey(windowHandle, HotkeyId);

    internal static void SetClickThrough(nint windowHandle, bool enabled)
    {
        var styles = GetWindowLong(windowHandle, GwlExStyle);
        var updatedStyles = enabled
            ? styles | WsExTransparent
            : styles & ~WsExTransparent;

        if (styles != updatedStyles)
        {
            _ = SetWindowLong(windowHandle, GwlExStyle, updatedStyles);
        }
    }

    internal static void BeginBottomRightResize(nint windowHandle)
    {
        _ = ReleaseCapture();
        _ = SendMessage(windowHandle, WmNcLeftButtonDown, HitTestBottomRight, 0);
    }

    internal static bool SignalExistingInstance()
    {
        var windowHandle = FindWindow(null, "OPS Monitor");
        return windowHandle != 0 &&
               PostMessage(windowHandle, WmShowWidget, 0, 0);
    }

    internal static HwndSourceHook CreateHotkeyHook(Action restoreEditMode)
    {
        ArgumentNullException.ThrowIfNull(restoreEditMode);

        return (nint windowHandle, int message, nint wordParameter, nint longParameter, ref bool handled) =>
        {
            _ = windowHandle;
            _ = longParameter;

            if ((message == WmHotkey && wordParameter.ToInt32() == HotkeyId) ||
                message == WmShowWidget)
            {
                restoreEditMode();
                handled = true;
            }

            return 0;
        };
    }

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint windowHandle, int identifier, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint windowHandle, int identifier);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(nint windowHandle, int index, int newValue);

    [DllImport("user32.dll", EntryPoint = "ReleaseCapture")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint windowHandle, int message, int wordParameter, int longParameter);

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindow(string? className, string windowName);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter);
#pragma warning restore SYSLIB1054
}
