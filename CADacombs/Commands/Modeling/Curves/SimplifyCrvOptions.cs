using Eto.Drawing;

namespace CADacombs.Commands.Modeling.Curves
{
    public static class SimplifyCrvOptions
    {
        public static Point? WindowLocation { get; set; } = null;
        
        public static bool ConvertLines { get; set; } = true;
        public static bool ConvertArcs { get; set; } = true;
        public static bool MergePolylines { get; set; } = true;
        
        public static bool SplitAllKnots { get; set; } = false;
        public static bool SplitFullyMultiple { get; set; } = true;
        public static bool AdjustG1 { get; set; } = false;
    }
}