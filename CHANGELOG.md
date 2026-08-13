# Changelog

All notable OPS Monitor changes are documented here. Versions follow semantic
versioning.

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

[3.3.0]: https://github.com/seNkoKG/ops-monitor/releases/tag/v3.3.0
[3.2.0]: https://github.com/seNkoKG/ops-monitor/releases/tag/v3.2.0
