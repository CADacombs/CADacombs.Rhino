# CADacombs for Rhino

A growing collection of NURBS curve and surface modeling tools for Rhinoceros 3D.

---

## Included Commands

### 1. spb_EdgeSrf
`spb_EdgeSrf` is a high-precision alternative to Rhino's native `_EdgeSrf`, capable of establishing face-face continuity (G1 and G2) directly during surface generation, similar to running a restricted `_MatchSrf` immediately after surface creation.

**Key Features:**
* **Flexible Input:** Creates a surface from 2, 3, or 4 open curves.
* **Smart Topology Handling:** 
  * If 3 curves are provided, it automatically calculates the missing 4th curve to close the loop.
  * If 2 curves are provided, it calculates missing bridges, acting like `_ExtrudeCrvAlongCrv` (for adjacent curves via `SumSurface`) or `_BlendEdge` (for opposite curves).
  * Automatically detects overlapping curve intersections and trims them to the exact inner boundaries.
* **Independent Continuity:** Target continuities (G0, G1, G2) can be applied globally to all boundaries or assigned individually per curve.
* **Corner Averaging:** Intelligently averages intersecting G1/G2 adjustments at corners so boundaries flow together seamlessly.
* **NURBS Precision:** The output surface may contain more knot vectors than native `_EdgeSrf` to ensure absolute mathematical compliance with the requested continuity.

### 2. spb_Drape
`spb_Drape` is an advanced alternative to Rhino's native `_Drape` command, utilizing Greville point locations to fit an open, degree-3 NURBS surface precisely over target Breps and Meshes.

**Key Features:**
* **Starting Surface Flexibility:** Automatically generates a starting surface based on the bounding box and span spacing of the target objects, or allows you to select your own custom starting surface.
* **Intelligent Z-Projection:** Preserves the starting surface's X and Y control point coordinates while adjusting the Z elevations to match the target objects using Z-axis raycasting.
* **Advanced Miss Handling:** Handles "missed" target projections with customizable resolution strategies: lock to the starting surface, use the lowest hit neighbor, or linearly extrapolate from the nearest hits.
* **High-To-Low Fitting:** Employs an iterative High-to-Low elevation sorting algorithm to achieve a smooth, mathematically precise drape that rests seamlessly on the target without clipping through.

### 3. spb_MatchSrf
`spb_MatchSrf` is a precise complement to Rhino's native `_MatchSrf`, designed to strictly preserve input knot and control point structures when matching untrimmed surface edges.

**Key Features:**
* **Structural Fidelity:** Produces surfaces that follow the input knot and point structures as closely as mathematically possible, rather than refitting the entire surface.
* **Continuity Priority:** Strictly targets continuity in order of G0, then G1, then G2, increasing degree or adding spans only when mathematically necessary.
* **Pick-Point Alignment:** Matches parameterization directions based on exactly where you click the edges, rather than attempting automatic alignments.
* **Smart Upgrading:** Automatically transfers unique knots and safely handles matching to reference surfaces of lesser or greater degrees.
* **Non-Destructive Options:** Includes toggles to replace the original surface or add a new one, maintain degree (by adding spans instead), and an echo mode for detailed command-line reporting.

### 4. spb_EndBulge
`spb_EndBulge` is an interactive alternative to Rhino's native `_EndBulge` command, featuring dialog controls instead of graphics window grip translation.

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
  * Continuities to maintain for both ends are explicitly defined (defaults to **G3**) and selectable by the user. 
  * Strict mathematical locking ensures control points are only shifted when permitted by the active continuity tier.
* **Surface Modification:**
  * The entire natural edge side of the surface is always modified (isocurve at domain extreme).

**Key Similarities**
* Core function restricts and modifies $p_1$ and $p_2$ locations predictably.
* Viewport analysis modes (e.g., Zebra, Draft Angle, Shaded Views) remain active and dynamically update during slider adjustments.

### 5. spb_CADacombsAbout
Displays the current plugin version, author credits, and license information.

---

## Support & Service

For bug reports, feature requests, or custom script development, contact **@spb** on the [McNeel Discourse Forum](https://discourse.mcneel.com/).

*Licensed under GNU LGPLv3.*