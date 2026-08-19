# Changelog

All notable changes to the `CADacombs.Rhino` plugin project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.2] - 2026-08-18

### Added
- `spb_EndBulge`: Added a "Reset All Scale and Slide Values" button to the dialog.
- `spb_EndBulge`: Added mouse scroll wheel support to text boxes for quick, tactile increment/decrement value adjustments.
- `spb_EndBulge`: Added informative hover tooltips to the Scale and Slide controls to clarify their geometric impact.
- `spb_CADacombsAbout`: Added a new administrative command to display plugin version and developer information, including a quick link to the Package Manager.

### Changed
- `spb_EndBulge`: Restructured the dialog layout to establish a strict top-down hierarchy, moving the "Adjust edges" mode toggle to the absolute top, and adding subtle horizontal dividers to separate rules, manipulation, and display settings.
- `spb_EndBulge`: Redesigned "Linked" mode to enforce strict symmetry and support true bi-directional syncing; users can now drive adjustments using controls from either the Picked or Opposite side (mimicking the native `_BlendSrf` dialog).
- `spb_EndBulge`: Adjusted the mathematical backend to strictly lock control point translation to the active continuity tier, accurately mimicking Rhino's native `_EndBulge` behavior (e.g., editing G1 tangency strictly isolates p1 and leaves p2 locked).
- `spb_EndBulge`: The dialog now dynamically disables, visually grays out (including text labels), and resets Scale and Slide controls if the chosen continuity tier does not mathematically permit their translation.
- `spb_EndBulge`: Overhauled the command-line (CLI) interface to parallel the new UI logic. Options have been renamed and reordered for consistency (e.g., `MaintainPicked` is now `PickedEnd`/`PickedEdge`), the opposite continuity prompt is dynamically hidden in "Linked" mode, and real-time bi-directional syncing is now strictly enforced during the interactive command loop.

### Fixed
- `spb_EndBulge`: The dialog now properly remembers its screen location and numeric settings between command executions.
- `spb_EndBulge`: Fixed an event execution bug that occasionally required users to double-click continuity radio buttons to apply a downgrade.
- `spb_EndBulge`: Fixed an issue where "Linked" mode would incorrectly prevent users from downgrading continuity by forcing a snap-back to the unclicked side's higher value.
- `spb_EndBulge`: Fixed an issue where the tool would fail to automatically switch to "Independent" mode if an invalid constraint limit forced an internal continuity downgrade.
- `spb_EndBulge`: Fixed a visual bug in "Independent" mode where sliders would fail to refresh and disable themselves if a control point shortage triggered an automatic continuity downgrade.

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