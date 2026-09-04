using Rhino;

namespace CADacombs.Core.Curves
{
    public static class ConvertToLineOptions
    {
        public static bool TolByRatio { get; set; } = false;
        public static double TolRatio { get; set; } = 100.0;
        public static double DevTol { get; set; } = 1e-6;
        public static double MinNewCrvLen { get; set; } = 100.0 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
        public static bool ProcessArcs { get; set; } = false;
        public static bool Process2PtNurbs { get; set; } = true;
        public static bool Replace { get; set; } = true;
        public static bool Echo { get; set; } = true;
        public static bool Debug { get; set; } = false;
    }
}