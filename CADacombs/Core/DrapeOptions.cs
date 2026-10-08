using System;
using Rhino;

namespace CADacombs.Core
{
    public static class DrapeOptions
    {
        public static bool HasRunBefore { get; set; } = false;
        public static uint LastDocSerialNumber { get; set; } = 0;
        public static Eto.Drawing.Point? WindowLocation { get; set; } = null;

        public static double Tolerance { get; set; } = 0.01;
        public static double SolverTimeout { get; set; } = 1.0;
        
        public static bool FlipCPlane { get; set; } = false;
        public static bool UserProvidesStartingSrf { get; set; } = false;
        public static bool FlattenStartingSrf { get; set; } = true;
        
        public static double SpanSpacing { get; set; } = 1.0; 
        public static int SpansBeyondEachSide { get; set; } = 3;
        
        // 0: Drape with skirted borders, 1: Drape with hugging borders, 2: Project Greville points, 3: Project control points
        public static int FitMethod { get; set; } = 0; 
        
        // 0: Fix to starting surface, 1: Use lowest neighbor hits, 2: Linearly extrapolate from hits
        public static int TargetMisses { get; set; } = 1;
        
        // 0: Input, 1: Current, 2: TargetObject
        public static int OutputLayer { get; set; } = 1; 
        public static bool DeleteStartingSrf { get; set; } = false;

        public static bool ShowSurface { get; set; } = true;
        public static bool ShowWireframe { get; set; } = true;
        public static bool ShowPolygon { get; set; } = false;

        public static bool Echo { get; set; } = true;
        public static bool Debug { get; set; } = false;

        public static double GetDefaultSpanSpacing(RhinoDoc doc)
        {
            if (doc.ModelUnitSystem == UnitSystem.Inches) return 1.0;
            return 25.0 * RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem);
        }
    }
}