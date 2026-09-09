# Changelog

All notable changes to the `CADacombs.Rhino` plugin project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Note: Legacy spb_ prefixes were standardized to cc_ during the C# suite refactor.

## [0.2.6-alpha] - 2026-09-09

### Added
- `ccRemoveCrvKnots`: Implemented smart knot removal using domain normalization ($D(A) = D(R) \cdot \frac{L(A)}{L(R)}$) across fully multiple joints to equalize derivative speeds, paired with a multi-strategy brute-force removal solver.
- `ccConvertCrvToBezier`: Added single-span Bezier conversion using adaptive cubic bulge-fitting for degree 3 targets and rebuild fallbacks for arbitrary target degrees.
- `ccGCon`, `ccCrvContinuities`, `ccCrvDiscontinuities`: Added unified geometric continuity analysis tools with $G^\infty$ detection for Degree 2 curves.
- `ccConvertCrvToArc`, `ccConvertCrvToLine`, `ccSimplifyCrv`: Added curve conversion and greedy span simplification tools.
- **UI / Toolbars:** Refactored `CADacombs.rui` into separate `CADacombs Curves` and `CADacombs Surfaces` toolbars arranged in Creation $\rightarrow$ Modification $\rightarrow$ Analysis order, with button labels abbreviated to 8 characters or fewer.

## [0.2.5-alpha] - 2026-09-01

### Added
- `ccDrape`: Migrated the legacy Python script into the native C# CADacombs plugin architecture. It features a High-to-Low iterative NURBS fitting algorithm, dynamic Z-axis raycasting over Breps and Meshes, and complex neighbor-extrapolation logic to gracefully resolve raycast misses.
- UI/UX: Added a custom, dark-mode compatible SVG toolbar icon for `ccDrape` depicting the mechanical projection of a surface over a stepped target.## [0.2.4] - 2026-08-30

### Added
- `ccMatchSrf`: Migrated the legacy Python script into the native C# CADacombs plugin architecture.
- `ccEdgeSrf`: Migrated the legacy Python script into C#. It now supports 2, 3, and 4-curve surface generation with mathematically precise NURBS extraction (SumSurfaces and Blends) before applying Coons patch logic.
- **Core Math:** Added `NurbsMatchMath` to handle explicit NURBS geometry manipulation (knot transfer, degree matching, algebraic vector scaling) for true Class A G0, G1, and G2 surface matching without control point refitting.
- **Core Math:** Expanded topological solvers within `NurbsMatchMath` to include automatic curve intersection trimming, closed-loop detection, and a `CornerAdjustment` struct for precise G1/G2 corner averaging.

### Removed
- `ccMatchSrf`: Removed the `UseUnderlyingIsoCrvs` option from the command line prompt to streamline the interface. The tool now automatically and safely extracts underlying `IsoCurves` rather than visual `BrepEdges` to prevent surface generation failures with complex PolyCurves.

## [0.2.3] - 2026-08-20

### Added
- **About Dialog:** Added `Ctrl+C` keyboard shortcut support to quickly copy version, license, and support information to the clipboard.

### Changed
- **Class-A Defaults:** The default continuity constraint for both the picked and opposite ends has been upgraded from G2 to G3 to better support high-end surfacing workflows out of the box.
- **Safer UX:** The default `Linked Ends` state is now set to `Independent` to provide a more predictable and Rhino-idiomatic baseline when selecting asymmetrical geometry.
- **About Dialog:** Redesigned the layout for a more compact UI and added dedicated, clickable links for bug reports and direct forum PMs.

### Fixed
- **UI Sliders:** Resolved an issue where the G2 and G3 jog sliders could become permanently stuck if manipulated via keyboard arrow keys or if the mouse button was released outside the bounds of the dialog.

## [0.2.2] - 2026-08-18

### Added
- `ccEndBulge`: Added a "Reset All Scale and Slide Values" button to the dialog.
- `ccEndBulge`: Added mouse scroll wheel support to text boxes for quick, tactile increment/decrement value adjustments.
- `ccEndBulge`: Added informative hover tooltips to the Scale and Slide controls to clarify their geometric impact.
- `ccCADacombsAbout`: Added a new administrative command to display plugin version and developer information, including a quick link to the Package Manager.

### Changed
- `ccEndBulge`: Restructured the dialog layout to establish a strict top-down hierarchy, moving the "Adjust edges" mode toggle to the absolute top, and adding subtle horizontal dividers to separate rules, manipulation, and display settings.
- `ccEndBulge`: Redesigned "Linked" mode to enforce strict symmetry and support true bi-directional syncing; users can now drive adjustments using controls from either the Picked or Opposite side (mimicking the native `_BlendSrf` dialog).
- `ccEndBulge`: Adjusted the mathematical backend to strictly lock control point translation to the active continuity tier, accurately mimicking Rhino's native `_EndBulge` behavior (e.g., editing G1 tangency strictly isolates p1 and leaves p2 locked).
- `ccEndBulge`: The dialog now dynamically disables, visually grays out (including text labels), and resets Scale and Slide controls if the chosen continuity tier does not mathematically permit their translation.
- `ccEndBulge`: Overhauled the command-line (CLI) interface to parallel the new UI logic. Options have been renamed and reordered for consistency (e.g., `MaintainPicked` is now `PickedEnd`/`PickedEdge`), the opposite continuity prompt is dynamically hidden in "Linked" mode, and real-time bi-directional syncing is now strictly enforced during the interactive command loop.

### Fixed
- `ccEndBulge`: The dialog now properly remembers its screen location and numeric settings between command executions.
- `ccEndBulge`: Fixed an event execution bug that occasionally required users to double-click continuity radio buttons to apply a downgrade.
- `ccEndBulge`: Fixed an issue where "Linked" mode would incorrectly prevent users from downgrading continuity by forcing a snap-back to the unclicked side's higher value.
- `ccEndBulge`: Fixed an issue where the tool would fail to automatically switch to "Independent" mode if an invalid constraint limit forced an internal continuity downgrade.
- `ccEndBulge`: Fixed a visual bug in "Independent" mode where sliders would fail to refresh and disable themselves if a control point shortage triggered an automatic continuity downgrade.

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
- **Plugin Preparation:** Refactored `ccEndBulge_Crv.py`, `ccEndBulge_Srf.py`, and `ccEndBulge.py` to be plugin-friendly.
- **Kernel Extraction:** Created `ccEndBulge_Kernel.py` by extracting shared calculation logic from curve and surface handlers (2026-07-12 – 2026-07-20).
- **Surface Support:** Created `ccEndBulge_Srf.py` to bring dynamic dialog control to natural surface edge isocurves (2026-07-12 – 2026-07-18).
- **Master Command:** Created `ccEndBulge.py` as the main command router (2026-07-20).

### Pre-Release Iterations (2026)
- **2026-07-09 – 2026-07-18:** Added dynamic dialog interface, viewport graphic preview, and refactored core curve logic.
- **2026-04-20 – 2026-04-25:** Initial development of optional dialog interface and live previews for curve manipulation.

### Initial Creation (2021)
- **2021-03-03 – 2021-03-07:** Created original `ccEndBulge_Crv.py` script and core $p_1 / p_2$ mathematical algorithms.