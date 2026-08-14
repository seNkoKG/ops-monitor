<p align="center">
  <img src="docs/assets/ops-monitor-v3-social.png" width="100%" alt="OPS Monitor v3 — native Windows telemetry and pixel-level widget design">
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2011-38C9FF?style=flat-square">
  <img alt="Framework" src="https://img.shields.io/badge/.NET-10.0-7655D9?style=flat-square">
  <img alt="UI" src="https://img.shields.io/badge/UI-Native%20WPF-63F6D8?style=flat-square&labelColor=111827">
  <img alt="Version" src="https://img.shields.io/badge/version-3.4.2-F24DE7?style=flat-square&labelColor=17111F">
</p>

<p align="center">
  A slim, local-first Windows 11 performance widget with serious telemetry,<br>
  a full visual Studio, and a weather suite built for Slovenia.
</p>

<p align="center">
  <a href="https://senkokg.github.io/ops-monitor/"><strong>Explore the website</strong></a>
  ·
  <a href="https://github.com/seNkoKG/ops-monitor/releases/download/v3.4.2/OPS-Monitor-v3.4.2-Setup.exe"><strong>Download Windows installer</strong></a>
</p>

<p align="center">
  <a href="https://github.com/seNkoKG/ops-monitor/releases/download/v3.4.2/OPS-Monitor-v3.4.2-Setup.exe">
    <img alt="Download OPS Monitor 3.4.2 for Windows 11" src="https://img.shields.io/badge/DOWNLOAD-WINDOWS%2011%20INSTALLER-48DCF9?style=for-the-badge&labelColor=071018">
  </a>
</p>

## The desktop, without the dashboard clutter

OPS Monitor keeps the numbers that matter within reach while staying visually
quiet. Its native always-on-top Widget tracks hardware, connectivity, storage,
battery, and local weather. The separate Studio handles deep customization so
the desktop overlay remains clean.

<p align="center">
  <img src="docs/assets/screenshots/studio-v3.4.png" width="100%" alt="OPS Monitor 3.4 Studio with readable temperature controls and production-renderer preview">
</p>

### Built to disappear until you need it

- **Four responsive layouts** — Pill, Rail, Dock, and an ultra-small Mini.
- **Real hardware telemetry** — CPU, GPU, RAM, VRAM, disks, fans, clocks,
  power, temperatures, battery, uptime, ping, jitter, and packet loss.
- **Local weather intelligence** — ARSO live observations and official regional
  outlooks, three-model forecast confidence, 15-minute rain nowcast, hourly and
  eight-day guidance, air quality, UV, pollen, warnings, and animated radar.
- **A real customization surface** — eight presets plus independent palette,
  surface, geometry, graph, typography, motion, opacity, scale, density,
  sensor, refresh, startup, lock, click-through, and window controls. Every
  module can override its own colors, spacing, type, icon, and visualization.
- **Portable designs** — export or import a versioned `.opsdesign` package;
  live apply, contrast checking, and atomic undo keep experiments recoverable.
- **Desktop-native behavior** — draggable, resizable, per-monitor DPI aware,
  always-on-top, sign-in startup, and keyboard recovery for locked overlays.
- **Stable, honest sensor states** — brief provider gaps retain the last valid
  temperature or network reading as stale; persistent outages become
  unavailable. OPS Monitor never invents a value.

## One widget, multiple moods

The Widget can be a narrow ambient rail, the original glass Pill, a single-row
Dock, or a compact Mini. Every layout is clamped to readable dimensions and
persists its position and scale.

<table>
  <tr>
    <td width="34%" align="center">
      <img src="docs/assets/screenshots/widget-mini-v3.3.png" width="245" alt="OPS Monitor Mini with readable temperatures, packet loss, and weather">
    </td>
    <td>
      <h3>Mini, but still useful</h3>
      <p>CPU and GPU temperatures, memory, live throughput, ping, packet loss,
      and weather fit into a tiny dark glass surface. The green status dot
      pulses when fresh data arrives.</p>
      <p>Open Studio when you want control; close it when you want calm.</p>
    </td>
  </tr>
</table>

## Weather that understands place

The default location is Celje, Slovenia, and any location or exact coordinate
can be searched and saved. OPS Monitor prioritizes the nearest official ARSO
station, adds ARSO's regional outlook, warnings, and radar, then compares
Open-Meteo Best Match, ECMWF IFS, and DWD ICON-EU guidance. The suite explains
model agreement, includes a 15-minute precipitation nowcast, and keeps a
six-hour last-known-good cache. Refresh work never overlaps and radar data is
downloaded only when its view opens.

<p align="center">
  <img src="docs/assets/screenshots/weather-overview.png" width="100%" alt="OPS Weather overview for Celje, Slovenia">
</p>

<table>
  <tr>
    <td width="50%"><img src="docs/assets/screenshots/weather-radar.png" alt="Animated ARSO radar over Slovenia"></td>
    <td width="50%"><img src="docs/assets/screenshots/weather-air-quality.png" alt="OPS Weather air quality, pollen, UV, and daylight view"></td>
  </tr>
  <tr>
    <td align="center"><sub><strong>Official ARSO radar</strong> with playback, timeline, and zoom</sub></td>
    <td align="center"><sub><strong>Atmosphere view</strong> with AQI, pollutants, pollen, UV, and daylight</sub></td>
  </tr>
</table>

## Telemetry architecture

```mermaid
flowchart LR
    A["Windows + NVML/LHM providers"] --> D["Adaptive metric scheduler"]
    B["Optional isolated sensor bridge"] --> D
    C["ARSO + Open-Meteo weather"] --> E["Cached weather service"]
    D --> F["Bounded history + alerts"]
    F --> G["OPS Widget"]
    E --> G
    F --> H["OPS Studio"]
    H -. "atomic settings" .-> G
```

Polling is adaptive and non-overlapping. History is bounded and downsampled,
UI updates are coalesced to the chosen cadence, and dragging or resizing never
restarts telemetry providers. Widget and Studio have no third-party runtime
packages; the optional low-level sensor bridge is isolated from both apps.

## Quick start

### Requirements

- Windows 11 x64
- .NET 10 Desktop Runtime for a framework-dependent build
- .NET SDK 10.0.302 or a compatible 10.0 patch to build from source

### Build, test, and run

```powershell
git clone https://github.com/seNkoKG/ops-monitor.git
cd .\ops-monitor\native
.\Build.ps1
.\Run.ps1 -Application Widget -Configuration Release
.\Run.ps1 -Application Studio -Configuration Release
```

`Build.ps1` restores the solution, builds Release, and runs the deterministic
behavioral test suites.

Version 3.4.2 makes the overlay game-safe: it never activates from a pointer
click and automatically passes input through while a borderless or full-screen
application owns the display. See the [changelog](CHANGELOG.md) and
[roadmap](ROADMAP.md) for the complete release contract.

### Install for the current user

Download and run
[`OPS-Monitor-v3.4.2-Setup.exe`](https://github.com/seNkoKG/ops-monitor/releases/download/v3.4.2/OPS-Monitor-v3.4.2-Setup.exe).
It is self-contained, needs no separate .NET installation, and installs without
administrator rights. The wizard offers sign-in startup and a desktop shortcut;
Windows Settings can remove it normally while saved designs and history remain.
The current community build is not code-signed, so Windows may show an Unknown
Publisher warning; the release includes a matching SHA-256 checksum for
verification.

For a source build or portable deployment:

```powershell
cd .\native
.\Build.ps1 -Configuration Release -Publish -SelfContained
.\Install.ps1 -SelfContained -EnableStartup -DesktopShortcut -Launch
```

Both install paths use `%LOCALAPPDATA%\Programs\OPS Monitor`, create verified
shortcuts, preserve settings during upgrades, and can install without admin
rights. See the [native build and operations guide](native/README.md) for
package variants, visual-QA flags, CPU temperature setup, and clean uninstall.

Installed builds also add **Check for OPS Monitor updates** to the Start menu.
The updater preserves the installed package type, verifies the downloaded ZIP
against the release manifest's SHA-256 digest, stages the replacement, and
keeps startup and desktop-shortcut preferences.

## CPU temperature and advanced sensors

Windows does not expose reliable package temperature data on every system.
OPS Monitor therefore keeps privileged hardware access out of the desktop apps:
an optional broker reads supported sensors, validates the values, writes a
versioned snapshot, and exits when the Widget closes.

On supported Ryzen systems this path requires the signed official PawnIO driver.
OPS Monitor never disables Memory Integrity and never falls back to WinRing0.
If a fresh plausible reading is unavailable, the UI correctly shows `TEMP N/A`.
Full setup and recovery steps are in [native/README.md](native/README.md#cpu-temperature-troubleshooting).

## Privacy and data sources

- Hardware metrics, history, alerts, and settings remain on the PC under
  `%LOCALAPPDATA%\OPS Monitor`.
- There is no account, advertising SDK, analytics SDK, or telemetry upload.
- Enabling Weather sends the selected coordinates to the weather providers.
- Weather data: [ARSO](https://meteo.arso.gov.si/),
  [Open-Meteo](https://open-meteo.com/), and the
  [CAMS European atmospheric model](https://atmosphere.copernicus.eu/).
- Third-party notices are documented in
  [native/THIRD-PARTY-NOTICES.md](native/THIRD-PARTY-NOTICES.md).

## Repository map

```text
native/
├─ src/OpsMonitor.Core/          telemetry, settings, alerts, history
├─ src/OpsMonitor.Widget/        always-on desktop overlay + weather suite
├─ src/OpsMonitor.Studio/        visual configuration and diagnostics
├─ src/OpsMonitor.SensorBridge/  optional isolated hardware sensor broker
├─ tests/                        deterministic behavioral test executables
└─ tools/                        screenshot and resource-impact utilities
```

The root-level PowerShell implementation is the original prototype and remains
for reference. Active production development lives in `native/`.

## Project status

OPS Monitor v3 is actively developed and currently passes **45 Core behavior
checks** plus **11 Sensor Bridge checks** in Release. The native applications
build with warnings treated as errors.

OPS Monitor is released under the [MIT License](LICENSE). Security reports use
the private process in [SECURITY.md](SECURITY.md).

---

<p align="center">
  <img src="docs/assets/ops-monitor-mark.svg" width="64" alt="OPS Monitor mark"><br>
  <sub>Designed for information density without visual noise.</sub>
</p>
