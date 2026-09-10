using System.Collections.Generic;
using Eto.Drawing;

namespace CADacombs.Commands.Modeling.Curves
{
    public static class SimplifyCrvOptions
    {
        public static Point? WindowLocation { get; set; } = null;
        
        public static double DistanceTolerance { get; set; } = -1.0;
        public static double AngleTolerance { get; set; } = -1.0;
        
        // Global allowed degrees for conversion/output
        public static List<int> TargetDegrees { get; set; } = new List<int> { 2, 3, 5 }; 
        
        public static bool ConvertLines { get; set; } = true;
        public static bool ConvertArcs { get; set; } = true;
        public static bool AdjustG1 { get; set; } = true;
        public static bool ConvertBeziers { get; set; } = true; // Enabled by default
        
        public static bool SplitAllKnots { get; set; } = false;
        public static bool SplitFullyMultiple { get; set; } = true;
        
        public static bool MergePolylines { get; set; } = true;
    }
}