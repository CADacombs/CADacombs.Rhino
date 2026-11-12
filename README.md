# CADacombs for Rhino

A growing collection of NURBS curve and surface modeling and analysis tools for tools for Rhinoceros 3D.

---

## Included Commands

### Curve Commands

#### ccConvertCrvToArc
Converts planar curve spans or entire curves into exact arc segments within tolerance.

#### ccConvertCrvToBezier
converts multi-span NURBS curves into single-span Bezier curves.

**Key Features:**
* **Adaptive Cubic Bulge-Fitting:** Uses high-speed iterative tangent-sliding loops for degree-3 targets.
* **Fallback Engine:** Employs native rebuild fallbacks for arbitrary target degrees, enforcing strict tangency preservation and deviation limits.

#### ccConvertCrvToLine
Replaces linear curve spans with exact line segments.

#### ccConvertCrvToUniform
Converts non-uniform NURBS curves into uniform NURBS curves while strictly respecting geometric deviation tolerances.

#### ccEndBulgeCrv
An interactive, type-safe alternative to Rhino's native `_EndBulge` command, featuring dialog controls instead of graphics window grip translation for open or non-periodic curves.

**Key Differences from Native `_EndBulge`**
* **Command Line & Dialog Interfaces:**
  * **CLI:** Allows quick changes using predetermined settings directly in the command line.
  * **Dialog:** Allows real-time modifications with live previews and built-in curvature graph analysis.
* **Numeric Value Control:**
  * Adjusts the tangent vector ($p_1 - p_0$) scale relative to its starting position.
  * Adjusts the $G_2$ ($p_2$) tangential sliding scale relative to $p_2$'s starting position (where geometry allows).
  * Adjusts the $G_3$ ($p_3$) tangential sliding scale relative to $p_3$'s starting position (where geometry allows).
* **Dual-End Modification:**
  * Modify the picked end/edge and the opposite end/edge **independently (default)** or **simultaneously (Linked)**.
* **Continuity Control:**
  * Continuities to maintain for both ends are explicitly defined (up to **G3**) and selectable by the user. 
  * Strict mathematical locking ensures control points are only shifted when permitted by the active continuity tier.
  * **Dynamic Degree Upgrades:** Includes one-click buttons to instantly upgrade curve/surface degree to unlock higher mathematical continuity tiers interactively.
* **Universal NURBS Math:**
  * Utilizes exact 4D geometric reparameterization and quotient rules to maintain perfect G2 and G3 continuity on any knot structure or rational geometry, bypassing legacy Bezier limitations.
* **Geometry Modification:**
  * **Curves:** Directly translates the control points at the extreme ends of the open curve.
  * **Surfaces:** The entire natural edge side of the surface is always modified symmetrically (isocurve at domain extreme).

**Key Similarities**
* Core function restricts and modifies $p_1$ and $p_2$ locations predictably.
* Viewport analysis modes (e.g., Zebra, Draft Angle, Shaded Views) remain active and dynamically update during slider adjustments.

#### ccMatchCrv
`ccMatchCrv` is a robust curve-matching engine that aligns curve ends while actively minimizing geometric deviation and mathematically prioritizing the lowest possible degree.

**Key Features:**
* **2D Shared-CV Optimization:** Automatically detects planar constraints and calculates shared control points via tangent and curvature offset intersections. Generates ultra-lightweight Degree 2, 3, or 4 conics and Beziers for G1/G2 matches, bypassing unnecessary native degree elevations.
* **Internal Continuity Preservation:** Explicitly evaluates and protects internal multi-knot continuity up through G2 or G3 to prevent parametric degradation during endpoint deformation.
* **Smart Deviation Shootout:** Tests multiple structural candidates (e.g., Point moves, Tangent slides, End Conditions) and selects the optimal result based on ascending degree priority and lowest average deviation.
* **Inflection Control:** Built-in safeguards optionally prevent the addition of unwanted inflection points.
* **Interactive UI & CLI:** Features a comprehensive Eto dialog with real-time average/maximum deviation reporting, minimum radius tracking, and a fully scriptable Command Line Interface.

#### ccRemoveCrvKnots
`ccRemoveCrvKnots` optimizes interior knots without altering physical curve geometry.

* **Domain Normalization:** Applies $D(A) = D(R) \cdot \frac{L(A)}{L(R)}$ across fully multiple joints to equalize derivative speeds before knot deletion, eliminating control point distortion.
* **Multi-Strategy Solver:** Runs parallel brute-force evaluations (Singular/Plural, Forward/Reverse, and Multiplicity Bracketing) to return the curve with the fewest possible knots within deviation tolerance.

#### ccSimplifyCrv
`ccSimplifyCrv` performs greedy span evaluation along curve sub-domains to replace qualified smooth sections with lines, arcs, Beziers, or uniform NURBS curves. 
* **Granular Control:** Utilizes strict, independent thresholds for `Max crv dev`, `Max angle dev`, `Min seg len`, and `Min arc bulge`.
* **Seam Preservation:** Mathematically locks the domain structure and parameterization of closed curves during extraction to guarantee zero geometric drifting or `_SelDup` failures.
* **Interactive UI:** Features a dynamic dialog with live-updating previews and pre/post conversion segment reporting.
* **Integrated Joint Smoothing:** Optionally heals internal joints up to G1 or G2 continuity. Employs a fail-safe sandbox that only commits control point modifications if the resulting blend stays within the `Max crv dev` tolerance, entirely avoiding unnecessary knot injection.
* **Ground-Truth Verification:** The dialog's interactive `OK` button mathematically verifies that geometric changes actually occurred before allowing the user to commit, safely intercepting "no-op" executions.

#### ccSmoothPolycrvJoints
`ccSmoothPolycrvJoints` is a dedicated CLI tool to evaluate and heal the internal joints of complex PolyCurves without rebuilding the entire curve.
* **Targeted Continuity:** Choose between G1, G2-Only, or a smart G2-with-G1-fallback mode that attempts curvature continuity but seamlessly degrades to tangency if the control points are too constrained.
* **Smart Bypassing:** Automatically scans and skips joints that already meet strict mathematical tolerances (`SkipAngleTolDeg`, `SkipCurvTolPct`) to prevent redundant parameterization shifts.
* **Deviation Bounding:** Optionally enforces a strict `Max crv dev` tolerance, discarding any smoothing attempts that pull the curve too far from its original shape.
* **Robust Reporting:** Outputs detailed command-line metrics comparing the pre- and post-operation inflection point counts, minimum radius changes, and maximum geometric deviation.

---

### Surface Commands

#### ccDrape
`ccDrape` is an advanced alternative to Rhino's native `_Drape` command, utilizing iterative elevation algorithms to fit open, degree-3 NURBS surfaces precisely over target Breps, Meshes, and SubDs.

**Key Features:**
* **Interactive Eto Dialog & CLI:** Features a modern, stateless UI with real-time cached preview updates, robust Add/Remove target toggling, and granular numeric control. Features full Command-Line Interface (CLI) support for rapid, macro-friendly execution.
* **Selectable Fit Methods:** Choose between distinct algorithmic solvers depending on the target geometry:
  * **Drape with skirted borders:** Employs an iterative High-to-Low elevation sorting algorithm. Quarantines border control points to ensure absolute zero clipping, ideal for vertical walls and architectural blocks.
  * **Drape with hugging borders:** Modifies the gravity drape to allow border control points to participate in neighbor translations, allowing the edges to hug meshes tighter.
* **Starting Surface Flexibility:** Automatically generates a starting surface based on the bounding box and span spacing of the target objects, or allows you to select your own custom starting surfaces.
* **Advanced Miss Handling:** Handles "missed" target projections with customizable resolution strategies: fix to the starting surface, use the lowest hit neighbor, or linearly extrapolate from the nearest hits.
* **Non-Destructive Replacement:** The `Delete input` option uses native UUID replacement, perfectly preserving the original object's `ColorSource`, `ObjectColor`, and layer assignments.
* **Flattening Options:** Automatically flattens the generated surface above the target bounding box to guarantee absolute hits, but can be toggled via the `Flatten starting surface` option for true 3D elevation drops.
* **Interruptible Solvers:** Heavy mathematical computations can be safely aborted at any time via the UI's dynamic "Stop" button or the native `Esc` key.

#### ccEdgeSrf
`ccEdgeSrf` is a high-precision alternative to Rhino's native `_EdgeSrf`, capable of establishing face-face continuity (G1 and G2) directly during surface generation, similar to running a restricted `_MatchSrf` immediately after surface creation.

**Key Features:**
* **Flexible Input:** Creates a surface from 2, 3, or 4 open curves.
* **Smart Topology Handling:** 
  * If 3 curves are provided, it automatically calculates the missing 4th curve to close the loop.
  * If 2 curves are provided, it calculates missing bridges, acting like `_ExtrudeCrvAlongCrv` (for adjacent curves via `SumSurface`) or `_BlendEdge` (for opposite curves).
  * Automatically detects overlapping curve intersections and trims them to the exact inner boundaries.
* **Independent Continuity:** Target continuities (G0, G1, G2) can be applied globally to all boundaries or assigned individually per curve.
* **Corner Averaging:** Intelligently averages intersecting G1/G2 adjustments at corners so boundaries flow together seamlessly.
* **NURBS Precision:** The output surface may contain more knot vectors than native `_EdgeSrf` to ensure absolute mathematical compliance with the requested continuity.

#### ccEndBulgeSrf
An interactive, type-safe alternative to Rhino's native `_EndBulge` command, featuring dialog controls instead of graphics window grip translation for open or non-periodic, untrimmed surface edges.

**See `ccEndBulgeCrv` under **Curve Commands** for **Key Differences from Native `_EndBulge`**.

#### ccMatchSrf
A precise complement to Rhino's native `_MatchSrf`, designed to strictly preserve input knot and control point structures when matching untrimmed surface edges.

**Key Features:**
* **Structural Fidelity:** Produces surfaces that follow the input knot and point structures as closely as mathematically possible, rather than refitting the entire surface.
* **Continuity Priority:** Strictly targets continuity in order of G0, then G1, then G2, increasing degree or adding spans only when mathematically necessary.
* **Pick-Point Alignment:** Matches parameterization directions based on exactly where you click the edges, rather than attempting automatic alignments.
* **Smart Upgrading:** Automatically transfers unique knots and safely handles matching to reference surfaces of lesser or greater degrees.
* **Non-Destructive Options:** Includes toggles to replace the original surface or add a new one, maintain degree (by adding spans instead), and an echo mode for detailed command-line reporting.

#### ccProjectSrf & ccPullSrf
`ccProjectSrf` and `ccPullSrf` are powerful surface deformation tools that utilize the unified `ConformSrf` physics engine to conform existing NURBS surfaces to target Breps, Meshes, and SubDs.

**Key Features:**
* **Interactive Eto Dialog & CLI:** Both commands share a dynamic, stateless UI with pre-configured defaults depending on the command executed. Full Command-Line Interface (CLI) support exposes solver-specific options for headless scripting.
* **Advanced Solvers:** 
  * **Direct control point:** A fast, direct 1:1 control point projection.
  * **Direct Greville:** A direct point-relaxation solver that produces smooth, low-tension surfaces.
  * **Weighted / relaxed:** A spring-relaxation physics engine with user-defined sampling modes, weight curves (Linear, Hyperbolic, Gaussian), and an automated stagnation check that stops the solver if accuracy fails to improve.
  * **Global least squares & Energy minimization:** Matrix solvers that evaluate the entire surface as a linked mathematical web, optionally utilizing an explicit stiffness exponent for highly rigid conforms.
* **Directional Mapping:** Choose to map geometry via closest-point Pull, Project down (-Z), or Project up (+Z).
* **1D Constraints:** The `Constrain to 1D displacement` option locks lateral control point shifting (X and Y) during Z-axis Matrix projections, causing the solver to act as a strict vertical press and preventing overall surface shrinkage.
* **Robust Feedback:** Comprehensive command-line reporting details exactly why a solver halted (e.g., "Converged", "Timed out", "Geometrically constrained", or "No improvement").

---

### Analysis Commands

#### ccCrvContinuities
Reports geometric continuity levels across all internal span joints of selected curves.
  * **Interactive Preview:** Features a live display conduit that visualizes discontinuities with color-coded text dots before finalizing actions.
  * **Detailed Reporting:** Breaks down discovered discontinuities by their specific sub-tier levels in the command history and displays exact angular deviations (in degrees) for G1 failures directly in the viewport.
  * **Actionable Outputs:** Allows you to automatically place point objects, bake text dots, or physically split the curves at the exact parameters where the target continuity fails.

#### ccCrvDiscontinuities
Locates, marks, and processes curve parameters where continuity drops below a requested target tier.
  * **Interactive Preview:** Features a live display conduit that visualizes discontinuities with color-coded text dots before finalizing actions.
  * **Detailed Reporting:** Breaks down discovered discontinuities by their specific sub-tier levels in the command history and displays exact angular deviations (in degrees) for G1 failures directly in the viewport.
  * **Actionable Outputs:** Allows you to automatically place point objects, bake text dots, or physically split the curves at the exact parameters where the target continuity fails.

#### ccGCon
`ccGCon` measures geometric continuity ($G0$, $G1$, $G2$, $G3$, $G^\infty$) between two curves at a junction using normalized domain scaling and $G3$ geometric matching.

#### ccInflectionPts
`ccInflectionPts` analyzes curves to locate and process points of inflection (parameters where the curve's concavity changes sign). Essential for detecting unwanted structural wobbles or "S-curves" before generating surfaces.
* **Actionable Outputs:** Extracted inflection points can be automatically marked with Point objects, or the curves can be physically split at the exact inflection parameters.
* **Batch Processing:** Handles multiple curve selections simultaneously and includes a `DeleteInput` toggle to keep the document clean after splitting.

#### ccRadiusMinima
`ccRadiusMinima` evaluates curves to calculate and mark the exact points of minimum radius (maximum curvature). Highly useful for identifying manufacturing bottlenecks, checking tooling limits, or finding sharp parametric pinches.
* **Granular Filtering:** Filter results by defining strict Minimum and Maximum radius thresholds to report.
* **Flexible Marking:** Choose to mark all discovered minima, only the absolute minimum per curve, or the single global minimum across all selected curves. 
* **Visual Output:** Generates standard Point objects or readable Text Dots displaying the exact radius value.
* **Topology Support:** Safely handles complex PolyCurves and multi-span NURBS curves, and can optionally extract constant-radius values from internal arc segments.

---

### Selection Commands

#### ccSelUniformNurbsCrv
Selects all mathematically uniform NURBS curves in the active document or within a pre-selected subset of curves.

---

### Utility Commands

#### ccAboutCADacombs
Displays plugin version information, developer credits, and license details.

---

## Support & Service

For bug reports, feature requests, or custom script development, contact **@spb** on the [McNeel Discourse Forum](https://discourse.mcneel.com/).

*Licensed under GNU LGPLv3.*