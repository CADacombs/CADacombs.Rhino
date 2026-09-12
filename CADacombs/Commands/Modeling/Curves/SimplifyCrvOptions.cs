using System;
using System.Collections.Generic;
using Eto.Drawing;
using Rhino;

namespace CADacombs.Commands.Modeling.Curves
{
    public static class SimplifyCrvOptions
    {
        public static Point? WindowLocation { get; set; } = null;
        
        public static double DistanceTolerance { get; set; } = -1.0;
        public static double AngleTolerance { get; set; } = -1.0;
        public static double LineDistanceTolerance { get; set; } = -1.0;
        public static double ArcBulgeTolerance { get; set; } = -1.0;
        public static double MinSegmentLength { get; set; } = -1.0;
        
        public static List<int> TargetDegrees { get; set; } = new List<int> { 2, 3, 5 }; 
        
        public static bool ConvertLines { get; set; } = true;
        public static bool ConvertArcs { get; set; } = true;
        public static bool AdjustG1 { get; set; } = false; 
        public static bool ConvertBeziers { get; set; } = true; 
        public static bool MakeUniform { get; set; } = true; 
        
        public static bool SplitAllKnots { get; set; } = false;
        public static bool SplitFullyMultiple { get; set; } = true;
        
        public static bool MergePolylines { get; set; } = true;

        public static int PreviewThickness { get; set; } = 5; 
        public static int AutoPreviewTimeout { get; set; } = 3;

        // Math.Round strips IEEE 754 floating-point garbage beyond the 12th decimal place
        public static double GetDefaultLineTol() 
            => Math.Round(0.001 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, 12); 
            
        public static double GetDefaultArcBulgeTol() 
            => Math.Round(10.0 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, 12); 

        public static double GetDefaultDistTol() 
            => Math.Round(0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, 12);

        public static double GetDefaultMinSegLength()
            => Math.Round(100.0 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, 12);
    }
}