using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace CADacombs.Commands.Modeling.Curves
{
    public static class PolylineOutputLogic
    {
        /// <summary>
        /// Scans a curve for contiguous linear segments and merges them into PolylineCurves.
        /// Safely handles closed curves to prevent breaking polyline chains at the seam.
        /// </summary>
        public static Curve Execute(Curve inputCurve)
        {
            if (inputCurve == null) return null;

            // If it's not a PolyCurve, check if it's already a single polyline representation
            if (!(inputCurve is PolyCurve pc))
            {
                if (!(inputCurve is PolylineCurve) && !(inputCurve is LineCurve) && inputCurve.TryGetPolyline(out Polyline pl))
                    return new PolylineCurve(pl);
                
                return inputCurve;
            }

            // If the entire PolyCurve is just one continuous polyline, simplify it immediately
            if (pc.TryGetPolyline(out Polyline fullPl))
            {
                return new PolylineCurve(fullPl);
            }

            // Handle closed curves: Find a safe starting index (a non-linear segment) 
            // so we don't sever a continuous polyline at the start/end seam.
            int startIdx = 0;
            if (pc.IsClosed)
            {
                for (int i = 0; i < pc.SegmentCount; i++)
                {
                    if (!pc.SegmentCurve(i).TryGetPolyline(out _))
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

                if (seg.TryGetPolyline(out Polyline pl))
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
                    // Flush accumulated points to a single PolylineCurve
                    if (currentPoints.Count > 1)
                    {
                        newSegments.Add(new PolylineCurve(currentPoints));
                        currentPoints.Clear();
                    }
                    
                    // Add the non-linear segment
                    newSegments.Add(seg);
                }
            }

            // Flush any remaining points (happens if the curve is open and ends on a linear segment)
            if (currentPoints.Count > 1)
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