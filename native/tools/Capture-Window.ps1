[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int]$TargetProcessId,

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [ValidateRange(0, 64)]
    [int]$Padding = 12
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class OpsCaptureNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr window, out Rect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr state);

    public static IntPtr FindVisibleWindow(uint targetProcessId)
    {
        IntPtr match = IntPtr.Zero;
        EnumWindows(delegate(IntPtr window, IntPtr state)
        {
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (processId == targetProcessId && IsWindowVisible(window))
            {
                match = window;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return match;
    }
}
'@

$process = Get-Process -Id $TargetProcessId -ErrorAction Stop
for ($attempt = 0; $attempt -lt 40 -and $process.MainWindowHandle -eq [IntPtr]::Zero; $attempt++) {
    Start-Sleep -Milliseconds 100
    $process.Refresh()
}

$handle = $process.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) {
    $handle = [OpsCaptureNative]::FindVisibleWindow([uint32]$TargetProcessId)
}
if ($handle -eq [IntPtr]::Zero) {
    throw "Process $TargetProcessId has no visible main window."
}

[void][OpsCaptureNative]::SetForegroundWindow($handle)
Start-Sleep -Milliseconds 250

$rectangle = [OpsCaptureNative+Rect]::new()
if (-not [OpsCaptureNative]::GetWindowRect($handle, [ref]$rectangle)) {
    throw 'GetWindowRect failed.'
}

$left = [Math]::Max(0, $rectangle.Left - $Padding)
$top = [Math]::Max(0, $rectangle.Top - $Padding)
$width = ($rectangle.Right - $rectangle.Left) + (2 * $Padding)
$height = ($rectangle.Bottom - $rectangle.Top) + (2 * $Padding)

if ($width -le 0 -or $height -le 0) {
    throw "Invalid window rectangle: $width x $height."
}

$absoluteOutput = [IO.Path]::GetFullPath($OutputPath)
$directory = Split-Path -Parent $absoluteOutput
if (-not (Test-Path -LiteralPath $directory)) {
    [void](New-Item -ItemType Directory -Path $directory -Force)
}

$bitmap = [Drawing.Bitmap]::new($width, $height)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CopyFromScreen(
        $left,
        $top,
        0,
        0,
        [Drawing.Size]::new($width, $height),
        [Drawing.CopyPixelOperation]::SourceCopy)
    $bitmap.Save($absoluteOutput, [Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}

Write-Output $absoluteOutput
