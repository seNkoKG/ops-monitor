# OPS Monitor

OPS Monitor is a lightweight native Windows 11 performance widget with a
separate configuration Studio. The current production implementation lives in
[`native`](native/README.md); the older PowerShell prototype remains in the
repository for reference.

The Widget provides four persistent layouts:

- **Pill** — the original 184×396 stacked glass design
- **Rail** — a borderless 184×286 vertical readout
- **Dock** — an 84-pixel-tall one-row desktop strip
- **Mini** — a 176×220 capsule with a 176×176 minimum preset

All layouts show separate CPU, GPU, RAM, network throughput, and
ping/packet-loss modules. Settings cover layout, density, theme, module
visibility and presentation, opacity, scale, update cadence, always-on-top,
dragging, resizing, locking, click-through, and Windows sign-in startup.

## Build, test, and run

```powershell
cd .\native
.\Build.ps1
.\Run.ps1 -Application Widget -Configuration Release
.\Run.ps1 -Application Studio -Configuration Release
```

Create and install the current-user release:

```powershell
.\Build.ps1 -Configuration Release -Publish
.\Install.ps1 -NoBuild -EnableStartup -Launch
```

See [`native/README.md`](native/README.md) for packaging, sensor availability,
visual-QA arguments, troubleshooting, and uninstall instructions.
