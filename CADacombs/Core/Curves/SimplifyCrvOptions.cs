using System;
using System.Collections.Generic;
using Eto.Drawing;
using Rhino;

namespace CADacombs.Core.Curves
{
    public static class SimplifyCrvOptions
    {
        public static Point? WindowLocation { get; set; } = null;
        
        public static double DistanceTolerance { get; set; } = -1.0;
        public static double MinKinkAngle { get; set; } = -1.0;
        public static double LineDistanceTolerance { get; set; } = -1.0;
        public static double ArcBulgeTolerance { get; set; } = -1.0;
        public static double MinSegmentLength { get; set; } = -1.0;
        
        public static List<int> TargetDegrees { get; set; } = new List<int> { 2, 3, 5 }; 
        
        public static bool ConvertLines { get; set; } = true;
        public static bool ConvertArcs { get; set; } = true;
        public static bool ConvertBeziers { get; set; } = true; 
        public static bool MakeUniform { get; set; } = true; 
        
        // 0 = Do not split, 1 = Split at geometric kinks, 2 = Split at kinks and curvature breaks
        public static int KnotSplitMode { get; set; } = 2; 
        
        public static bool SmoothJoints { get; set; } = true; 
        // 0 = G1 only, 1 = G2 w/ G1 fallback, 2 = G2 only
        public static int SmoothJointsMode { get; set; } = 1; 
        
        public static bool MergePolylines { get; set; } = true;

        public static int PreviewThickness { get; set; } = 5; 
        public static int AutoPreviewTimeout { get; set; } = 3;

        public static double GetDefaultLineTol() 
            => Math.Round(0.001 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, 12); 
            
        public static double GetDefaultArcBulgeTol() 
            => Math.Round(10.0 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, 12); 

        public static double GetDefaultDistTol() 
            => Math.Round(0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, 12);

        public static double GetDefaultMinSegLength()
            => Math.Round(100.0 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, 12);

        public static void Load(Rhino.PersistentSettings settings)
        {
            DistanceTolerance = settings.GetDouble("SimpCrv_DistTol", DistanceTolerance);
            MinKinkAngle = settings.GetDouble("SimpCrv_AngleTol", MinKinkAngle);
            LineDistanceTolerance = settings.GetDouble("SimpCrv_LineDistTol", LineDistanceTolerance);
            ArcBulgeTolerance = settings.GetDouble("SimpCrv_ArcBulgeTol", ArcBulgeTolerance);
            MinSegmentLength = settings.GetDouble("SimpCrv_MinSegLen", MinSegmentLength);
            
            ConvertLines = settings.GetBool("SimpCrv_ConvertLines", ConvertLines);
            ConvertArcs = settings.GetBool("SimpCrv_ConvertArcs", ConvertArcs);
            ConvertBeziers = settings.GetBool("SimpCrv_ConvertBeziers", ConvertBeziers);
            MakeUniform = settings.GetBool("SimpCrv_MakeUniform", MakeUniform);
            
            KnotSplitMode = settings.GetInteger("SimpCrv_KnotSplitMode", KnotSplitMode);
            
            SmoothJoints = settings.GetBool("SimpCrv_SmoothJoints", SmoothJoints);
            SmoothJointsMode = settings.GetInteger("SimpCrv_SmoothJointsMode", SmoothJointsMode);
            
            MergePolylines = settings.GetBool("SimpCrv_MergePolylines", MergePolylines);

            int x = settings.GetInteger("SimpCrv_WindowX", -1);
            int y = settings.GetInteger("SimpCrv_WindowY", -1);
            if (x != -1 && y != -1) WindowLocation = new Point(x, y);
        }

        public static void Save(Rhino.PersistentSettings settings)
        {
            settings.SetDouble("SimpCrv_DistTol", DistanceTolerance);
            settings.SetDouble("SimpCrv_AngleTol", MinKinkAngle);
            settings.SetDouble("SimpCrv_LineDistTol", LineDistanceTolerance);
            settings.SetDouble("SimpCrv_ArcBulgeTol", ArcBulgeTolerance);
            settings.SetDouble("SimpCrv_MinSegLen", MinSegmentLength);
            
            settings.SetBool("SimpCrv_ConvertLines", ConvertLines);
            settings.SetBool("SimpCrv_ConvertArcs", ConvertArcs);
            settings.SetBool("SimpCrv_ConvertBeziers", ConvertBeziers);
            settings.SetBool("SimpCrv_MakeUniform", MakeUniform);
            
            settings.SetInteger("SimpCrv_KnotSplitMode", KnotSplitMode);
            
            settings.SetBool("SimpCrv_SmoothJoints", SmoothJoints);
            settings.SetInteger("SimpCrv_SmoothJointsMode", SmoothJointsMode);
            
            settings.SetBool("SimpCrv_MergePolylines", MergePolylines);

            if (WindowLocation.HasValue)
            {
                settings.SetInteger("SimpCrv_WindowX", WindowLocation.Value.X);
                settings.SetInteger("SimpCrv_WindowY", WindowLocation.Value.Y);
            }
        }
    }
}