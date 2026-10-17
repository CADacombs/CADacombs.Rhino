using System;
using Rhino;

namespace CADacombs.Core
{
    public static class FitSrfToTargetOptions
    {
        public static bool HasRunBefore { get; set; } = false;
        public static uint LastDocSerialNumber { get; set; } = 0;
        public static Eto.Drawing.Point? WindowLocation { get; set; } = null;

        public static double Tolerance { get; set; } = 0.01;
        public static double SolverTimeout { get; set; } = 2.0;
        
        public static int MappingDirection { get; set; } = 0; 
        public static bool FlipDirection { get; set; } = false;
        
        public static int SolverType { get; set; } = 2; 

        public static int SamplingMode { get; set; } = 0; 
        public static int WeightCurve { get; set; } = 2; 
        public static double PercentOfDomain { get; set; } = 5.0;
        public static int EnergyStiffnessExponent { get; set; } = -2; 
        public static bool Constrain1D { get; set; } = true; 
        
        public static int OutputLayer { get; set; } = 1; 
        public static bool DeleteStartingSrf { get; set; } = false;
        public static bool AddErrorLines { get; set; } = false; 

        public static bool ShowSurface { get; set; } = true;
        public static bool ShowWireframe { get; set; } = true;
        public static bool ShowPolygon { get; set; } = false;
        public static bool ShowErrorLines { get; set; } = false; 

        public static bool Echo { get; set; } = true;
        public static bool Debug { get; set; } = false;
    }
}