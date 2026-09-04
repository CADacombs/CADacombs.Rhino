using Rhino;

namespace CADacombs.Core.Curves
{
    public static class ConvertToArcOptions
    {
        public static bool TolByRatio { get; set; } = false;
        public static double TolRatio { get; set; } = 1000.0;
        public static double DevTol { get; set; } = 1e-6;
        public static double TanTol { get; set; } = RhinoDoc.ActiveDoc.ModelAngleToleranceDegrees;
        public static double MinNewCrvLen { get; set; } = 100.0 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
        public static double MaxRadius { get; set; } = 1e5;
        public static bool Replace { get; set; } = true;
        public static bool Echo { get; set; } = true;
        public static bool Debug { get; set; } = false;
    }
}