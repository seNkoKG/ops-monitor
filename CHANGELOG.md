# Changelog

All notable OPS Monitor changes are documented here. Versions follow semantic
versioning.

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

[3.2.0]: https://github.com/seNkoKG/ops-monitor/releases/tag/v3.2.0
