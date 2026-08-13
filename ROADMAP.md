# OPS Monitor roadmap

OPS Monitor follows small, auditable releases. Features move into a release
only when their UI, persistence, runtime behavior, and regression tests ship
together.

## 3.2 — Production foundation

- One renderer for the live widget and Studio preview across Pill, Rail, Dock,
  and Mini layouts.
- Power-aware polling: pause while Windows is locked and back off on battery
  saver without blocking independent providers.
- Vendor-neutral GPU fallback through LibreHardwareMonitor for AMD, Intel, and
  NVIDIA hardware, while retaining native NVIDIA telemetry where available.
- Windows CI, CodeQL, release artifacts, SHA-256 checksums, updater metadata,
  performance budgets, accessibility and layout regression gates.
- Versioned self-contained and framework-dependent packages, local upgrade
  path, refreshed documentation, Pages site, and installed desktop build.

## 3.3 — Designer workbench

- Complete global token editing for palette, surfaces, geometry, typography,
  graphs, header chrome, and motion.
- Per-module colors, opacity, geometry, graph, typography, icon, title,
  precision, size, visualization, and visibility overrides.
- Production-renderer Mini parity at 80%, compact alignment fixes, plain
  weather labeling, and brief-gap stabilization for temperatures and ping.
- Schema-5 portable designs, safe migration, atomic reset/undo, contrast-aware
  compositing, and malformed-package recovery.

## 3.4 — Weather intelligence and designer reliability

- ARSO live observations and official regional outlooks combined with
  Open-Meteo Best Match, ECMWF IFS, and DWD ICON-EU forecast guidance.
- Fifteen-minute precipitation nowcast, confidence/model-agreement analysis,
  richer hourly and daily conditions, and a last-known-good offline cache.
- Fully wired compact typography, spacing, radius, icon, and progress tokens,
  with direct temperature/status controls and shared preview/runtime rules.
- Expanded Mini safety sizing and automated no-clipping checks at 80%, 100%,
  and 125% scale.

## 3.5 — Profiles and automation

- User-editable hotkeys with conflict detection.
- Scene triggers for process, full-screen app, AC/battery state, schedule, and
  manual override.
- Import/export for complete profiles, not only visual design tokens.
- Per-monitor placement profiles and safer recovery when displays change.

## 3.6 — Telemetry depth

- Multi-GPU selection and per-device cards.
- Per-core CPU, disk volume, fan curve, motherboard, and process telemetry.
- Optional bounded on-disk history with explicit retention and export.
- Sensor provenance, confidence, age, and fallback diagnostics in Studio.

## 4.0 — Extensible instruments

- Sandboxed local provider SDK with versioned contracts.
- User-created formulas and composite metrics.
- Signed community theme/profile gallery with offline-first import.
- Optional local-only HTTP API, disabled by default and protected by an
  explicit access token.

## Release quality bar

Every stable release must pass:

1. zero-warning Release build and all behavior tests;
2. production-renderer checks for every layout at 80%, 100%, and 125%;
3. install, update, startup, sensor-broker, and uninstall smoke tests;
4. keyboard/focus, readable contrast, reduced-motion, and screen-scaling QA;
5. measured idle CPU/memory budgets and bounded logs/history;
6. checksum verification, GitHub release, Pages deployment, and live-link QA.
