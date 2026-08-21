# CADacombs for Rhino

A growing collection of NURBS curve and surface modeling tools for Rhinoceros 3D.

---

## Included Commands

### 1. spb_EndBulge
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

### 2. spb_CADacombsAbout
Displays the current plugin version, author credits, and license information.

---

## Support & Service

For bug reports, feature requests, or custom script development, contact **@spb** on the [McNeel Discourse Forum](https://discourse.mcneel.com/).

*Licensed under GNU LGPLv3.*