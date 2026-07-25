# Performance Pill

Performance Pill is a slim, always-on-top Windows 11 desktop monitor built with the
Windows PowerShell and WPF components already included in Windows. It has no
third-party runtime or package dependencies.

It shows:

- CPU load and temperature
- GPU load and temperature
- used and total memory
- download and upload throughput
- internet latency and rolling packet loss

The cog opens settings for always-on-top, position locking, dragging, background
opacity, refresh frequency, automatic sign-in launch, metric labels, temperature
display, and persistent size presets. The lower-right grip allows free scaling
between 80% and 150%. Background opacity ranges from 30% to 100% and applies to
both the outer Pill and every metric card while keeping the readings fully opaque.

The redesigned equal-row layout includes four ready-made sizes: **Mini (80%)**,
**Slim (90%)**, **Balanced (100%)**, and **Comfort (115%)**. Choose them from
settings or the Pill's right-click menu.

Three live designs can be switched instantly from the cog or right-click menu:

- **Pill** — the original stacked glass-card layout at 184×396
- **Rail** — a continuous, box-free vertical readout at 160×286
- **Dock** — a 540×86 horizontal strip with every metric in one row

Each design supports the same opacity, scale, locking, dragging, startup, sensor,
and refresh controls. The selected design persists between Windows sessions.

On AMD Ryzen systems that protect CPU thermal telemetry, choose **Enable** beside
**CPU sensor access** in settings. Windows asks for administrator approval once,
then installs a small scheduled sensor bridge for the current user. The visible
widget remains a standard-user process. The bridge is built locally from
`src\CpuTemperatureBridge.cs` using the .NET Framework compiler included with
Windows and reads the installed AMD Ryzen Master SDK.

Metric collection runs off the UI thread, rendering is updated only at the chosen
refresh interval, and hardware queries are cached. Sensor helper processes use
bounded timeouts and explicitly release their output streams for stable long-run
resource use.

## Start

Double-click `Launch-PerformancePill.vbs`. The launcher starts the PowerShell/WPF
window without leaving a console open.

To add a Start menu shortcut:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -Launch
```

Automatic startup can be toggled later from the cog. You can also enable it while
installing with `-EnableStartup`.

Right-click the Pill or use its notification-area icon to open settings, toggle
the position lock, restore the widget after hiding it, or exit.

## Temperature availability

NVIDIA GPU metrics use the driver-provided `nvidia-smi` utility. Other GPUs use
Windows' GPU performance counters when available. CPU temperature is read from
the elevated AMD bridge when enabled, Windows ACPI firmware data, or WMI sensor
data exposed by LibreHardwareMonitor/OpenHardwareMonitor. An unsupported
temperature is shown honestly as `N/A`; all other metrics continue working.

## Verify

Run the dependency-free smoke test:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Smoke.ps1
```

Render the live widget to a PNG without leaving it running:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\PerformancePill.ps1 -RenderPreview .\preview.png
```

Settings are stored in `%LOCALAPPDATA%\PerformancePill\settings.json`.
`Uninstall.ps1` removes the Start menu shortcut and startup entry; pass
`-RemoveSettings` to remove saved settings too.
