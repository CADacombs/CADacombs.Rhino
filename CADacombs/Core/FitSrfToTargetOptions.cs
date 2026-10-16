using System;
using Rhino;

namespace CADacombs.Core
{
    public static class FitSrfToTargetOptions
    {
        public static bool HasRunBefore { get; set; } = false;
        public static uint LastDocSerialNumber { get; set; } = 0;
        public static Eto.Drawing.Point? WindowLocation { get; set; } = null;

        // General
        public static double Tolerance { get; set; } = 0.01;
        public static double SolverTimeout { get; set; } = 2.0;
        
        // 0: Closest Point (Pull), 1: Z-Axis (Raycast Projection)
        public static int MappingDirection { get; set; } = 0; 
        public static bool FlipDirection { get; set; } = false;
        
        // 0: Direct Control Point, 1: Direct Greville, 2: Python Legacy, 3: Weighted / Relaxed
        public static int SolverType { get; set; } = 3; 

        // Advanced Options
        public static double SamplingLength { get; set; } = 2.0;
        public static int SamplingMode { get; set; } = 0; // 0: Distributed, 1: NearestPoint
        public static int WeightCurve { get; set; } = 2;  // 0: Linear, 1: Hyperbolic, 2: Gaussian
        public static double PercentOfDomain { get; set; } = 5.0;
        
        // Output Settings
        public static int OutputLayer { get; set; } = 1; // 0: Input, 1: Current, 2: TargetObject
        public static bool DeleteStartingSrf { get; set; } = false;

        // Display
        public static bool ShowSurface { get; set; } = true;
        public static bool ShowWireframe { get; set; } = true;
        public static bool ShowPolygon { get; set; } = false;

        public static bool Echo { get; set; } = true;
        public static bool Debug { get; set; } = false;
    }
}