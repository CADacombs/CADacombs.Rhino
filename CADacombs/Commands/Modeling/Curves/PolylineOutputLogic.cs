using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace CADacombs.Commands.Modeling.Curves
{
    public static class PolylineOutputLogic
    {
        private static bool TryExtract(Curve c, bool strict, out Polyline pl)
        {
            pl = null;
            // If strict is true, reject anything that isn't explicitly a Line or Polyline
            if (strict && !(c is LineCurve || c is PolylineCurve)) return false;
            
            return c.TryGetPolyline(out pl);
        }

        /// <summary>
        /// Scans a curve for contiguous linear segments and merges them into PolylineCurves.
        /// Safely handles closed curves to prevent breaking polyline chains at the seam.
        /// </summary>
        public static Curve Execute(Curve inputCurve, bool strictLineSegmentsOnly = false)
        {
            if (inputCurve == null) return null;

            // If it's not a PolyCurve, check if it's already a single polyline representation
            if (!(inputCurve is PolyCurve pc))
            {
                if (TryExtract(inputCurve, strictLineSegmentsOnly, out Polyline pl))
                {
                    if (pl.Count == 2) return new LineCurve(pl[0], pl[1]);
                    return new PolylineCurve(pl);
                }
                
                return inputCurve;
            }

            // If the entire PolyCurve is just one continuous polyline, simplify it immediately
            if (TryExtract(pc, strictLineSegmentsOnly, out Polyline fullPl))
            {
                if (fullPl.Count == 2) return new LineCurve(fullPl[0], fullPl[1]);
                return new PolylineCurve(fullPl);
            }

            // Handle closed curves: Find a safe starting index (a non-linear segment) 
            // so we don't sever a continuous polyline at the start/end seam.
            int startIdx = 0;
            if (pc.IsClosed)
            {
                for (int i = 0; i < pc.SegmentCount; i++)
                {
                    if (!TryExtract(pc.SegmentCurve(i), strictLineSegmentsOnly, out _))
                    {
                        startIdx = i;
                        break;
                    }
                }
            }

            var newSegments = new List<Curve>();
            var currentPoints = new List<Point3d>();

            for (int count = 0; count < pc.SegmentCount; count++)
            {
                int idx = (startIdx + count) % pc.SegmentCount;
                Curve seg = pc.SegmentCurve(idx);

                if (TryExtract(seg, strictLineSegmentsOnly, out Polyline pl))
                {
                    if (currentPoints.Count == 0)
                    {
                        currentPoints.AddRange(pl);
                    }
                    else
                    {
                        // Append points, skipping the first to avoid duplicating the shared seam vertex
                        for (int p = 1; p < pl.Count; p++)
                        {
                            currentPoints.Add(pl[p]);
                        }
                    }
                }
                else
                {
                    // Flush accumulated points
                    if (currentPoints.Count == 2)
                    {
                        newSegments.Add(new LineCurve(currentPoints[0], currentPoints[1]));
                        currentPoints.Clear();
                    }
                    else if (currentPoints.Count > 2)
                    {
                        newSegments.Add(new PolylineCurve(currentPoints));
                        currentPoints.Clear();
                    }
                    
                    // Add the non-linear segment
                    newSegments.Add(seg);
                }
            }

            // Flush any remaining points (happens if the curve is open and ends on a linear segment)
            if (currentPoints.Count == 2)
            {
                newSegments.Add(new LineCurve(currentPoints[0], currentPoints[1]));
            }
            else if (currentPoints.Count > 2)
            {
                newSegments.Add(new PolylineCurve(currentPoints));
            }

            // Reconstruct the curve
            if (newSegments.Count == 1)
            {
                return newSegments[0];
            }

            var resultPolyCurve = new PolyCurve();
            foreach (var segment in newSegments)
            {
                resultPolyCurve.Append(segment);
            }

            return resultPolyCurve;
        }
    }
}