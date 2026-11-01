using System;
using Rhino;

namespace CADacombs.Core.Curves
{
    public static class SmoothCrvJointsOptions
    {
        public static bool SmoothG2 { get; set; } = false; 
        
        public static double AngleTolDeg { get; set; } = -1.0;
        public static double CurvatureMagTolPct { get; set; } = 5.0; // NEW
        
        public static bool SkipIfAlreadyAtContinuity { get; set; } = true;
        
        public static bool LimitCrvDev { get; set; } = false;
        public static double DevTol { get; set; } = -1.0;
        
        public static bool DeleteInput { get; set; } = true;
        
        public static bool Echo { get; set; } = true; 
        public static bool Debug { get; set; } = false;

        public static void Load(Rhino.PersistentSettings settings)
        {
            SmoothG2 = settings.GetBool("SmoothJoints_SmoothG2", SmoothG2);
            
            AngleTolDeg = settings.GetDouble("SmoothJoints_AngleTolDeg", AngleTolDeg);
            if (AngleTolDeg <= 0) AngleTolDeg = 1.0 * RhinoMath.ToDegrees(RhinoDoc.ActiveDoc.ModelAngleToleranceRadians);

            CurvatureMagTolPct = settings.GetDouble("SmoothJoints_CurvatureMagTolPct", CurvatureMagTolPct);

            SkipIfAlreadyAtContinuity = settings.GetBool("SmoothJoints_SkipIfAlready", SkipIfAlreadyAtContinuity);
            LimitCrvDev = settings.GetBool("SmoothJoints_LimitCrvDev", LimitCrvDev);
            
            DevTol = settings.GetDouble("SmoothJoints_DevTol", DevTol);
            if (DevTol <= 0) DevTol = 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

            DeleteInput = settings.GetBool("SmoothJoints_DeleteInput", DeleteInput);
            Debug = settings.GetBool("SmoothJoints_Debug", Debug);
        }

        public static void Save(Rhino.PersistentSettings settings)
        {
            settings.SetBool("SmoothJoints_SmoothG2", SmoothG2);
            settings.SetDouble("SmoothJoints_AngleTolDeg", AngleTolDeg);
            settings.SetDouble("SmoothJoints_CurvatureMagTolPct", CurvatureMagTolPct);
            settings.SetBool("SmoothJoints_SkipIfAlready", SkipIfAlreadyAtContinuity);
            settings.SetBool("SmoothJoints_LimitCrvDev", LimitCrvDev);
            settings.SetDouble("SmoothJoints_DevTol", DevTol);
            settings.SetBool("SmoothJoints_DeleteInput", DeleteInput);
            settings.SetBool("SmoothJoints_Debug", Debug);
        }
    }
}