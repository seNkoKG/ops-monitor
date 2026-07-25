# OPS Monitor v2

OPS Monitor is a native Windows 11 performance widget with a separate visual
configuration app. The Widget is optimized for an always-on desktop footprint;
Studio exposes layouts, themes, module visibility, opacity, update cadence,
window behavior, startup, and alerts without crowding the monitor itself.

## What is included

- `OpsMonitor.Widget.exe` — live Pill, Rail, and Dock overlays
- `OpsMonitor.Studio.exe` — full visual settings and diagnostics workspace
- `OpsMonitor.Core.dll` — telemetry, history, alerts, settings, and providers
- dependency-free behavioral tests for the Core contracts

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
separate .NET installation. OPS Monitor itself has no third-party NuGet runtime
packages.

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

Supported layout values are `Pill`, `Rail`, and `Dock`; density values are
`Compact`, `Normal`, and `Detail`. `--show-battery` enables the battery module for
that launch.

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
- `-DesktopShortcut` adds a desktop shortcut.
- omit `-SelfContained` to install the smaller framework-dependent package.
- `-NoBuild` fails instead of building when the requested package is absent.

The installer stages and verifies both executables before replacing an existing
install. It refuses to overwrite an installed copy while that copy is running.

Uninstall from the Start menu, or run:

```powershell
.\Uninstall.ps1 -StopRunningApps
```

User settings are retained by default. Add `-RemoveUserData` only when the saved
configuration and local history should also be deleted.

## Runtime behavior and sensor availability

- Telemetry providers run on adaptive, non-overlapping schedules.
- History is bounded and downsampled; rendering is coalesced to the selected UI
  cadence.
- NVIDIA data uses NVML first and a bounded `nvidia-smi` fallback.
- CPU temperature is read from the isolated CPU sensor bridge when available.
  Every bridge sample is timestamped. Expired samples become `TEMP N/A`; OPS
  Monitor never keeps displaying an old temperature as if it were live.
  Firmware, vendor-version, and security-policy differences can make this sensor
  unavailable while the rest of the widget continues normally.
- Launch-at-sign-in is a per-user value named `OPS Monitor Widget` under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- `Ctrl+Alt+O` returns a locked or click-through Widget to Edit mode.

For repeatable screenshots, use `tools\Capture-Window.ps1`. For a quick steady
state CPU and memory sample, use `tools\Measure-AppImpact.ps1`.

### CPU temperature troubleshooting

OPS Monitor intentionally does not install or silently replace a kernel sensor
driver. On AMD systems, test **CPU temperature bridge** from Studio's
**Providers & Integrations** page. `Available` means a fresh timestamped value is
being published; `Stale reading` or `Not connected` means the widget will show
`TEMP N/A`.

If an installed Ryzen Master Monitoring SDK no longer supports the processor,
update it only from
[AMD's official developer download page](https://www.amd.com/en/developer/ryzen-master-monitoring-sdk.html),
then restart the CPU temperature bridge. Updating a system sensor driver can
require administrator approval and should remain an explicit user action.
