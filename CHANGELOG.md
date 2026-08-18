# Changelog

All notable changes to the `CADacombs.Rhino` plugin project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.2] - 2026-08-17

### Added
- `spb_EndBulge`: Added a "Reset All Scale and Slide Values" button to the dialog.
- `spb_CADacombsAbout`: Added a new administrative command to display plugin version and developer information, including a quick link to the Package Manager.

### Changed
- `spb_EndBulge`: Refined the dialog layout for better control alignment, justification, and consistent row widths.
- `spb_EndBulge`: Redesigned "Linked" mode to enforce strict symmetry. It now automatically resolves conflicting continuity constraints by downgrading to the highest valid shared value and properly restricts sliders when available control points cannot be divided equally.

### Fixed
- `spb_EndBulge`: The dialog now properly remembers its screen location and numeric settings between command executions.
- `spb_EndBulge`: Fixed an event execution bug that occasionally required users to double-click continuity radio buttons to apply a downgrade.
- `spb_EndBulge`: Fixed an issue where the tool would fail to automatically switch to "Independent" mode if a constraint limit forced an internal continuity downgrade.

## [0.2.1] - 2026-08-16

### Fixed
- Package Manager: Resolved a display bug where the version string was bloated by .NET 8 auto-injecting Git commit hashes.

## [0.2.0] - 2026-08-16

### Added
- Complete architecture migration from IronPython scripts to C# (`.cs`) with dynamic C# script (`.csx`) hot-loading support.
- Live-reloading test pipeline enabling fast iteration without recompiling binaries or restarting Rhino.
- Multi-file C# structure separating Core logic (`EndBulgeMath`, `EndBulgeOptions`, `EndBulgeConduit`, `EndBulgeDialog`) from Command logic (`EndBulgeCurveLogic`, `EndBulgeSurfaceLogic`, etc.).
- Integrated `Eto.Forms` and `Eto.Drawing` support across C# assemblies.

---

## Legacy History (Python Prototype)

### [0.1.0] - 2026-07-24
- **Plugin Preparation:** Refactored `spb_EndBulge_Crv.py`, `spb_EndBulge_Srf.py`, and `spb_EndBulge.py` to be plugin-friendly.
- **Kernel Extraction:** Created `spb_EndBulge_Kernel.py` by extracting shared calculation logic from curve and surface handlers (2026-07-12 – 2026-07-20).
- **Surface Support:** Created `spb_EndBulge_Srf.py` to bring dynamic dialog control to natural surface edge isocurves (2026-07-12 – 2026-07-18).
- **Master Command:** Created `spb_EndBulge.py` as the main command router (2026-07-20).

### Pre-Release Iterations (2026)
- **2026-07-09 – 2026-07-18:** Added dynamic dialog interface, viewport graphic preview, and refactored core curve logic.
- **2026-04-20 – 2026-04-25:** Initial development of optional dialog interface and live previews for curve manipulation.

### Initial Creation (2021)
- **2021-03-03 – 2021-03-07:** Created original `spb_EndBulge_Crv.py` script and core $p_1 / p_2$ mathematical algorithms.