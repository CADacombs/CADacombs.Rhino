using System;
using Eto.Drawing;
using Rhino;

namespace CADacombs.Core.Curves
{
    public static class MatchCrvOptions
    {
        public static Point? WindowLocation { get; set; }

        public static int Continuity { get; set; } = 1; 
        public static double PosTol { get; set; } = -1.0; 
        public static double AngleTolDeg { get; set; } = -1.0; 
        public static double CurvatureMagTolPct { get; set; } = 5.0; 
        public static bool SkipIfAlreadyAtContinuity { get; set; } = true;
        
        public static int PreserveOtherEnd { get; set; } = 2; 
        public static int ModifyMode { get; set; } = 2; 

        public static bool LimitCrvDev { get; set; } = true;
        public static double DevTol { get; set; } = -1.0;
        public static bool DeformLines { get; set; } = false;
        
        public static bool RebuildRationals { get; set; } = false;
        public static bool KeepArcs { get; set; } = true;

        public static bool DeleteInput { get; set; } = true;
        public static bool Echo { get; set; } = true;
        public static bool Debug { get; set; } = false;

        public static void Load(Rhino.PersistentSettings settings)
        {
            Continuity = settings.GetInteger("MatchCrv_Continuity", Continuity);
            
            PosTol = settings.GetDouble("MatchCrv_PosTol", PosTol);
            if (PosTol <= 0) PosTol = 0.0001 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

            AngleTolDeg = settings.GetDouble("MatchCrv_AngleTolDeg", AngleTolDeg);
            if (AngleTolDeg <= 0) AngleTolDeg = RhinoMath.ToDegrees(RhinoDoc.ActiveDoc.ModelAngleToleranceRadians);

            CurvatureMagTolPct = settings.GetDouble("MatchCrv_CurvatureMagTolPct", CurvatureMagTolPct);

            SkipIfAlreadyAtContinuity = settings.GetBool("MatchCrv_SkipIfAlreadyAtContinuity", SkipIfAlreadyAtContinuity);
            PreserveOtherEnd = settings.GetInteger("MatchCrv_PreserveOtherEnd", PreserveOtherEnd);
            ModifyMode = settings.GetInteger("MatchCrv_ModifyMode", ModifyMode);
            
            LimitCrvDev = settings.GetBool("MatchCrv_LimitCrvDev", LimitCrvDev);
            DevTol = settings.GetDouble("MatchCrv_DevTol", DevTol);
            if (DevTol <= 0) DevTol = 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

            DeformLines = settings.GetBool("MatchCrv_DeformLines", DeformLines);
            RebuildRationals = settings.GetBool("MatchCrv_RebuildRationals", RebuildRationals);
            KeepArcs = settings.GetBool("MatchCrv_KeepArcs", KeepArcs);

            DeleteInput = settings.GetBool("MatchCrv_DeleteInput", DeleteInput);
            Echo = settings.GetBool("MatchCrv_Echo", Echo);
            Debug = settings.GetBool("MatchCrv_Debug", Debug);
            
            int x = settings.GetInteger("MatchCrv_WindowX", -1);
            int y = settings.GetInteger("MatchCrv_WindowY", -1);
            if (x != -1 && y != -1) WindowLocation = new Point(x, y);
        }

        public static void Save(Rhino.PersistentSettings settings)
        {
            settings.SetInteger("MatchCrv_Continuity", Continuity);
            settings.SetDouble("MatchCrv_PosTol", PosTol);
            settings.SetDouble("MatchCrv_AngleTolDeg", AngleTolDeg);
            settings.SetDouble("MatchCrv_CurvatureMagTolPct", CurvatureMagTolPct);
            settings.SetBool("MatchCrv_SkipIfAlreadyAtContinuity", SkipIfAlreadyAtContinuity);
            settings.SetInteger("MatchCrv_PreserveOtherEnd", PreserveOtherEnd);
            settings.SetInteger("MatchCrv_ModifyMode", ModifyMode);
            
            settings.SetBool("MatchCrv_LimitCrvDev", LimitCrvDev);
            settings.SetDouble("MatchCrv_DevTol", DevTol);
            
            settings.SetBool("MatchCrv_DeformLines", DeformLines);
            settings.SetBool("MatchCrv_RebuildRationals", RebuildRationals);
            settings.SetBool("MatchCrv_KeepArcs", KeepArcs);

            settings.SetBool("MatchCrv_DeleteInput", DeleteInput);
            settings.SetBool("MatchCrv_Echo", Echo);
            settings.SetBool("MatchCrv_Debug", Debug);

            if (WindowLocation.HasValue)
            {
                settings.SetInteger("MatchCrv_WindowX", WindowLocation.Value.X);
                settings.SetInteger("MatchCrv_WindowY", WindowLocation.Value.Y);
            }
        }
    }
}