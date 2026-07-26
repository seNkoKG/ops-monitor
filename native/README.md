# OPS Monitor v2

OPS Monitor is a native Windows 11 performance widget with a separate visual
configuration app. The Widget is optimized for an always-on desktop footprint;
Studio exposes layouts, themes, module visibility and presentation, opacity,
scale, update cadence, window behavior, startup, and diagnostics without
crowding the monitor itself.

## What is included

- `OpsMonitor.Widget.exe` — live Pill, Rail, Dock, and Mini overlays
- `OpsMonitor.Studio.exe` — full visual settings and diagnostics workspace
- `OpsMonitor.Core.dll` — telemetry, history, alerts, settings, and providers
- `SensorBridge\OpsMonitor.SensorBridge.exe` — optional isolated CPU sensor
  broker
- deterministic behavioral tests for the Core and sensor contracts

Metrics include CPU and NVIDIA GPU load/temperature, memory usage, network
download/upload, ping, jitter, rolling packet loss, uptime, and battery when the
hardware exposes them. Missing or stale sensors are labeled honestly instead of
being replaced with invented values.

Settings are written atomically under `%LOCALAPPDATA%\OPS Monitor`:

- `settings.json` — shared runtime configuration
- `widget-state.json` — last widget geometry and local window state
- `Studio\studio-settings.json` — Studio editor state

## Requirements

- Windows 11 x64
- For development or framework-dependent packages: .NET 10 Desktop Runtime
- For building: .NET SDK 10.0.302 or a compatible 10.0 patch

The self-contained package carries the Microsoft runtime and does not require a
separate .NET installation. Widget and Studio have no third-party runtime
packages. The optional CPU sensor broker uses LibreHardwareMonitorLib 0.9.6;
license and source details are in `THIRD-PARTY-NOTICES.md`.

## Build and verify

From the `native` directory:

```powershell
.\Build.ps1
```

That restores the solution, builds Release, and runs all behavioral tests.
If local policy blocks scripts, prefix these commands with
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File`.

Create a compact framework-dependent release:

```powershell
.\Build.ps1 -Configuration Release -Publish
```

Create a portable Windows x64 self-contained release:

```powershell
.\Build.ps1 -Configuration Release -Publish -SelfContained
```

Publish produces a combined companion-app folder and ZIP:

```text
artifacts\publish\framework-dependent\
artifacts\publish\OPS-Monitor-framework-dependent.zip
artifacts\publish\win-x64-self-contained\
artifacts\publish\OPS-Monitor-win-x64-self-contained.zip
```

Widget and Studio intentionally live in the same folder. This lets each app open
the other reliably and ensures launch-at-sign-in points at the installed Widget.

Run a development build:

```powershell
.\Run.ps1 -Application Widget -Configuration Debug
.\Run.ps1 -Application Studio -Configuration Debug
```

Useful deterministic visual-QA arguments:

```powershell
.\Run.ps1 -Application Widget -ArgumentList '--demo','--reset-ui','--layout=Rail','--density=Compact'
```

Supported layout values are `Pill`, `Rail`, `Dock`, and `Mini`; density values
are `Compact`, `Normal`, and `Detail`. `--scale=80` exercises the minimum
readable footprint. `--show-storage` and `--show-battery` enable the two
optional modules for full-width visual checks. Demo/reset launches are
ephemeral and never overwrite the user's saved settings or startup
registration.

Run the optional live provider probe:

```powershell
dotnet run --project .\tests\OpsMonitor.Tests\OpsMonitor.Tests.csproj -c Release --no-build -- --live
```

## Install for the current user

The installer uses `%LOCALAPPDATA%\Programs\OPS Monitor`, creates Start menu
shortcuts, and preserves settings during updates:

```powershell
.\Install.ps1 -SelfContained -Launch
```

Optional switches:

- `-EnableStartup` registers the installed Widget at Windows sign-in.
- `-EnableCpuTemperature` installs and validates the isolated elevated CPU
  sensor broker. The signed official PawnIO driver must already be installed.
- `-DesktopShortcut` adds a desktop shortcut.
- omit `-SelfContained` to install the smaller framework-dependent package.
- `-NoBuild` fails instead of building when the requested package is absent.

The installer stages and verifies both executables before replacing an existing
install. It refuses to overwrite an installed copy while that copy is running.

Uninstall from the Start menu, or run:

```powershell
.\Uninstall.ps1 -StopRunningApps -RemoveCpuSensor
```

User settings are retained by default. Add `-RemoveUserData` only when the saved
configuration and local history should also be deleted. Sensor cleanup preserves
PawnIO because another hardware-monitoring app may use it.

## Runtime behavior and sensor availability

- Telemetry providers run on adaptive, non-overlapping schedules.
- History is bounded and downsampled; rendering is coalesced to the selected UI
  cadence. Dragging and resizing do not restart telemetry providers.
- NVIDIA data uses NVML first and a bounded `nvidia-smi` fallback.
- CPU temperature is read from an isolated broker installed under
  `%ProgramFiles%\OPS Monitor Sensor`. Widget and Studio remain non-elevated.
  The broker selects the real AMD `Core (Tctl/Tdie)` package/control sensor,
  rejects zero or implausible values, and publishes only a value plus UTC
  timestamp to the current user's read-only `Data\<SID>` folder beneath that
  protected install. Expired samples become `TEMP N/A`; OPS Monitor never
  presents an old or invented value as live.
- Launch-at-sign-in is a per-user value named `OPS Monitor Widget` under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- `Ctrl+Alt+O` returns a locked or click-through Widget to Edit mode.

For repeatable screenshots, use `tools\Capture-Window.ps1`. For a quick steady
state CPU and memory sample, use `tools\Measure-AppImpact.ps1`.

### CPU temperature troubleshooting

Ryzen 9000 temperature access requires the separately installed, digitally
signed official PawnIO edition from <https://pawnio.eu/>. OPS Monitor never
disables Memory Integrity and never uses WinRing0. After installing PawnIO, run
**Enable CPU Temperature** from the OPS Monitor Start menu, or:

```powershell
.\Enable-CpuTemperature.ps1
```

The one-time setup requests administrator approval because it writes the broker
to Program Files and registers an on-demand Local System task. Its action and
per-user output both stay under the administrator-protected Program Files copy.
The installing user receives read/run access to the task and read-only access to
the live sample, never edit/delete access. It does not run code from Desktop,
Downloads, `%TEMP%`, or another user-writable location. Widget startup requests
the task on demand, and the broker exits automatically after the Widget closes.

`Available` in Studio means a fresh, plausible package temperature is being
published. `Stale reading` or `Not connected` means the Widget correctly shows
`TEMP N/A`. Use **Disable CPU Temperature** to remove the task and broker while
leaving PawnIO installed for any other applications.
