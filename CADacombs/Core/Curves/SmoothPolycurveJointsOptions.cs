using System;
using Rhino;

namespace CADacombs.Core.Curves
{
    public static class SmoothCrvJointsOptions
    {
        // 0: G1, 1: G2WithG1Fallback, 2: G2Only
        public static int ContinuityMode { get; set; } = 0; 
        
        public static double MinKinkAngle { get; set; } = 5.0;
        
        public static bool SkipIfAlreadyAtContinuity { get; set; } = true;
        
        // Lower limits for geometric perfection
        public static double SkipAngleTolDeg { get; set; } = 1.2074182697257333e-06; 
        public static double SkipCurvTolPct { get; set; } = 1e-8; 
        
        public static bool LimitCrvDev { get; set; } = false;
        public static double DevTol { get; set; } = -1.0;
        
        public static bool DeleteInput { get; set; } = true;
        
        public static bool Echo { get; set; } = true; 
        public static bool Debug { get; set; } = false;

        public static void Load(Rhino.PersistentSettings settings)
        {
            ContinuityMode = settings.GetInteger("SmoothJoints_ContinuityMode", ContinuityMode);
            
            MinKinkAngle = settings.GetDouble("SmoothJoints_MinKinkAngle", MinKinkAngle);
            if (MinKinkAngle <= 0) MinKinkAngle = 5.0;

            SkipIfAlreadyAtContinuity = settings.GetBool("SmoothJoints_SkipIfAlready", SkipIfAlreadyAtContinuity);
            
            SkipAngleTolDeg = settings.GetDouble("SmoothJoints_SkipAngleTolDeg", SkipAngleTolDeg);
            if (SkipAngleTolDeg <= 0) SkipAngleTolDeg = 1.2074182697257333e-06;
            
            SkipCurvTolPct = settings.GetDouble("SmoothJoints_SkipCurvTolPct", SkipCurvTolPct);
            if (SkipCurvTolPct <= 0) SkipCurvTolPct = 1e-8;

            LimitCrvDev = settings.GetBool("SmoothJoints_LimitCrvDev", LimitCrvDev);
            
            DevTol = settings.GetDouble("SmoothJoints_DevTol", DevTol);
            if (DevTol <= 0) DevTol = 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

            DeleteInput = settings.GetBool("SmoothJoints_DeleteInput", DeleteInput);
            Debug = settings.GetBool("SmoothJoints_Debug", Debug);
        }

        public static void Save(Rhino.PersistentSettings settings)
        {
            settings.SetInteger("SmoothJoints_ContinuityMode", ContinuityMode);
            settings.SetDouble("SmoothJoints_MinKinkAngle", MinKinkAngle);
            settings.SetBool("SmoothJoints_SkipIfAlready", SkipIfAlreadyAtContinuity);
            settings.SetDouble("SmoothJoints_SkipAngleTolDeg", SkipAngleTolDeg);
            settings.SetDouble("SmoothJoints_SkipCurvTolPct", SkipCurvTolPct);
            settings.SetBool("SmoothJoints_LimitCrvDev", LimitCrvDev);
            settings.SetDouble("SmoothJoints_DevTol", DevTol);
            settings.SetBool("SmoothJoints_DeleteInput", DeleteInput);
            settings.SetBool("SmoothJoints_Debug", Debug);
        }
    }
}