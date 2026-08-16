# Changelog

All notable changes to the `CADacombs.Rhino` plugin project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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