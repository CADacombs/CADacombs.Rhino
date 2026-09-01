using System;
using Rhino;

namespace CADacombs.Core
{
    public static class DrapeOptions
    {
        public static double Tolerance { get; set; } = 10.0 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
        public static bool FlipCPlane { get; set; } = false;
        public static bool UserProvidesStartingSrf { get; set; } = false;
        
        // Default to 1.0 (inches) or 25.0 (mm) based on standard unit systems, handled in Command or Logic
        public static double SpanSpacing { get; set; } = 1.0; 
        public static int SpansBeyondEachSide { get; set; } = 3;
        
        /// <summary>
        /// 0 = FixToStartingSrf, 1 = UseLowestNeighborHits, 2 = LinearlyExtrapolateFromHits
        /// </summary>
        public static int TargetMisses { get; set; } = 1;
        
        public static bool DeleteStartingSrf { get; set; } = false;
        public static bool Echo { get; set; } = true;
        public static bool Debug { get; set; } = false;
    }
}