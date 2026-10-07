using System;
using Rhino;

namespace CADacombs.Core
{
    public static class DrapeOptions
    {
        public static bool HasRunBefore { get; set; } = false;
        public static Eto.Drawing.Point? WindowLocation { get; set; } = null;

        public static double Tolerance { get; set; } = 10.0 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
        public static double SolverTimeout { get; set; } = 1.0;
        
        public static bool FlipCPlane { get; set; } = false;
        public static bool UserProvidesStartingSrf { get; set; } = false;
        
        public static double SpanSpacing { get; set; } = 1.0; 
        public static int SpansBeyondEachSide { get; set; } = 3;
        
        public static int FitMethod { get; set; } = 0; 
        public static int TargetMisses { get; set; } = 1;
        
        public static bool ShowSurface { get; set; } = true;
        public static bool ShowWireframe { get; set; } = true;
        public static bool ShowPolygon { get; set; } = false;

        public static bool DeleteStartingSrf { get; set; } = false;
        public static bool Echo { get; set; } = true;
        public static bool Debug { get; set; } = false;
    }
}