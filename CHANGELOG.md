# Changelog

All notable OPS Monitor changes are documented here. Versions follow semantic
versioning.

## [3.4.6] — 2026-08-17

### Changed

- Rebuilt Weather Station from scratch around an opaque, native station shell
  with left-rail navigation for Now, Forecast, Radar, and Environment.
- Replaced repeated glass cards and duplicate tab navigation with stronger
  hierarchy, larger readable values, calmer data grouping, and dedicated
  station/source context.

### Fixed

- Removed old weather-window emoji bindings and emoji-font presentation.
- Forecast horizontal-wheel routing now follows the active parent page after
  the split station layout.

## [3.4.5] — 2026-08-17

### Changed

- Rebuilt Weather Station UI around four focused surfaces: Now, Forecast,
  Radar, and Environment.
- Current conditions now lead with station freshness, confidence, live
  measurements, and a dedicated rain track instead of one long dashboard.
- Hourly and daily forecast layouts now use clearer hierarchy, calmer cards,
  larger visual weather states, and fewer fixed-width columns.
- Radar and environment views now use the same station shell and visual tokens.

### Fixed

- Forecast wheel routing now scrolls whichever parent page contains the active
  horizontal strip, not only the old overview page.
- Weather station shell now has a dedicated rendering smoke test and no longer
  relies on emoji fonts or weather emoji bindings.

## [3.4.4] — 2026-08-17

### Added

- Animated, colorful vector weather icons replace the static emoji across the
  whole weather suite. Clear days spin their sun rays, nights twinkle, clouds
  drift, rain falls, snow sways, fog slides, and storms flash — all drawn
  locally with no external assets, fonts, or network dependency.
- A gentle breathing glow behind the current-conditions hero and the title-bar
  weather mark. Icons and ambient motion stop automatically when Windows
  animations are disabled or the user's theme disables motion.

### Changed

- The Nowcast and Hourly forecast strips hide their horizontal scrollbars so
  the layout stays clean; chevron buttons, Shift+wheel, and touchpad panning
  still reach every card.

### Fixed

- Small forecast cards now animate lightly (a single drifting or falling
  element) so dozens of visible cards stay cheap on the render thread.

## [3.4.3] — 2026-08-17

### Added

- Weather current conditions now include UV index, snow depth, snowfall,
  freezing level, and soil temperature, and the condition icon respects the
  reported day/night state instead of the wall clock.
- Hourly forecast cards show pressure, UV index, wind direction (compass),
  snowfall, and freezing level per hour.
- The eight-day outlook adds precipitation hours, sunshine hours, snowfall
  totals, dominant wind direction, and apparent (feels-like) minimum/maximum.
- The atmosphere view adds NO₂, O₃, SO₂, and CO alongside PM2.5/PM10, plus
  alder, olive, and ragweed pollen.

### Fixed

- The weather page's Nowcast and Hourly strips no longer swallow mouse wheel
  input, so the page keeps scrolling vertically over them.
- Hidden forecast content to the left and right is now reachable: chevron
  buttons page the strips, the strip scrollbars are visible, and Shift+wheel
  scrolls a strip horizontally while a trackpad can pan it directly.

## [3.4.2] — 2026-08-14

### Fixed

- The always-on-top widget can no longer become the foreground window from a
  pointer click, preventing it from taking keyboard or raw-mouse focus from a
  running game.
- Borderless and full-screen applications now enable a temporary native
  click-through guard. The user's normal interaction mode returns automatically
  after leaving full screen.
- Widget startup no longer activates over the application the user is already
  using.

### Added

- Automated style-policy coverage and a real HWND full-screen transition probe
  for the no-activate and input pass-through contracts.

## [3.4.1] — 2026-08-14

### Added

- A self-contained Windows 11 setup executable with a standard install wizard,
  current-user install, Start menu entries, optional desktop and sign-in
  shortcuts, Add/Remove Programs registration, clean uninstall, and in-place
  upgrade support that preserves settings.
- Direct installer downloads on the project website and README, plus a
  versioned SHA-256 checksum beside every setup executable.

### Changed

- The installed updater now prefers the verified Windows installer when a
  release provides one, while retaining ZIP compatibility for older releases.
- GitHub releases and CI artifacts now publish the installer alongside the two
  portable ZIP variants.

## [3.4.0] — 2026-08-13

### Added

- A local-first weather confidence layer that compares Open-Meteo Best Match,
  ECMWF IFS, and DWD ICON-EU guidance for Celje and other saved locations.
- Fifteen-minute precipitation nowcast, model-agreement scores, richer hourly
  humidity/gust/visibility/cloud detail, and precipitation totals in the
  multi-day forecast.
- Official ARSO regional outlooks alongside nearest-station observations,
  Slovenian warnings, air quality, and the animated national radar.
- A six-hour last-known-good weather cache so a brief provider or connection
  outage does not blank the suite.
- Direct per-module controls for temperature/status and primary-value size in
  the Structure inspector.

### Fixed

- Nullable tail values in multi-model forecast feeds no longer abort weather
  refreshes or get converted into misleading zero readings.
- Compact and Mini typography no longer ignores designer sizes. CPU/GPU
  temperatures, packet loss, and metadata remain readable at the smallest
  supported footprint.
- Secondary font weight now reaches every metric card, and the readable-minimum
  token protects labels, values, icons, and temperature/status text together.
- Compact card radius, padding, gap, progress height, and icon sizing now derive
  from their global or per-module tokens instead of fixed template constants.
- Shell padding and header geometry now use the same layout-aware rules in
  Studio and the live widget, with expanded Mini sizing to prevent a final
  weather or custom-sensor row from being cut off.
- Text and color fields apply as the user types rather than waiting for focus
  to leave the control.

### Changed

- The weather detail view now identifies its source mix and exact coordinates,
  explains forecast confidence, and keeps official observations separate from
  model guidance.
- Visual Designer labels now call out temperature/status typography explicitly
  instead of hiding it under generic “secondary” terminology.

## [3.3.0] — 2026-08-13

### Added

- A substantially expanded visual designer with editable system fonts, global
  graph and icon tokens, live color swatches, five visualization modes, and
  per-module surface, border, text, track, geometry, graph, and typography
  overrides.
- Per-module reset and override summaries, with every reset captured as one
  atomic undo step and every new token preserved by `.opsdesign` import/export.
- Brief-gap stabilization for CPU/GPU temperatures and network quality. Recent
  valid values remain visible as stale through short provider or ICMP misses;
  persistent outages still become unavailable.

### Fixed

- Mini at 80% now uses the same 28 px row contract in Studio and the live
  widget, so the last module cannot be cut off by mismatched preview sizing.
- Compact values align with their labels and icons, preview padding follows the
  production shell rules, and short status labels no longer collide or
  ellipsize in narrow widgets.
- The unexplained `WX` label is replaced with the plain `WEATHER` label.
- Transparent and frameless designs now remain visually bounded against the
  Studio canvas, while effective contrast analysis composites card opacity
  over the selected surface.
- Malformed design packages with null module entries are normalized safely.

### Changed

- Studio design persistence moves to schema 5 for the expanded theme and
  module token set while preserving schema-4 designs through migration.
- Visualization names now describe the actual output: value, bar, value + bar,
  sparkline, and value + sparkline.

## [3.2.0] — 2026-08-12

### Added

- Studio preview now hosts the production `MetricCard` renderer used by the
  live widget, with regression checks for Pill, Rail, Dock, and Mini at 80%,
  100%, and 125% scale.
- Vendor-neutral GPU fallback from LibreHardwareMonitor for AMD, Intel, and
  NVIDIA load, temperature, clock, and VRAM telemetry.
- Real workstation-lock pausing and battery-saver cadence backoff.
- Versioned framework-dependent and self-contained ZIPs, SHA-256 files,
  verified update manifest, and Start menu updater.
- Windows CI, CodeQL, dependency review, automated releases, and GitHub Pages
  deployment workflows.
- MIT license, security policy, roadmap, release-quality gates, and enforceable
  app-impact budgets.

### Fixed

- Preview/live drift caused by two independent widget implementations.
- Compact layout spacing when accent rails are hidden.
- Studio production-card hosting no longer depends on a widget `Window`
  ancestor for typography and theme bindings.
- Update frequency and provider polling now stop wasting work while Windows is
  locked.

### Changed

- Removed unimplemented local API, anonymous diagnostics, and disk-history
  settings so every exposed production setting has real behavior.
- GPU sensor catalog now enables all supported LibreHardwareMonitor GPU groups.
- Release artifacts contain package identity and version markers for safe
  in-place updates.

[3.4.6]: https://github.com/seNkoKG/ops-monitor/releases/tag/v3.4.6
[3.4.5]: https://github.com/seNkoKG/ops-monitor/releases/tag/v3.4.5
[3.4.4]: https://github.com/seNkoKG/ops-monitor/releases/tag/v3.4.4
[3.4.3]: https://github.com/seNkoKG/ops-monitor/releases/tag/v3.4.3
[3.4.2]: https://github.com/seNkoKG/ops-monitor/releases/tag/v3.4.2
[3.4.1]: https://github.com/seNkoKG/ops-monitor/releases/tag/v3.4.1
[3.4.0]: https://github.com/seNkoKG/ops-monitor/releases/tag/v3.4.0
[3.3.0]: https://github.com/seNkoKG/ops-monitor/releases/tag/v3.3.0
[3.2.0]: https://github.com/seNkoKG/ops-monitor/releases/tag/v3.2.0
