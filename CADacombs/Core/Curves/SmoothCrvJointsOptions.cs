using System;
using Rhino;

namespace CADacombs.Core.Curves
{
    public static class SmoothCrvJointsOptions
    {
        public static double AngleTolDeg { get; set; } = -1.0;
        public static bool SkipIfAlreadyAtContinuity { get; set; } = true;
        
        public static bool LimitCrvDev { get; set; } = false;
        public static double DevTol { get; set; } = -1.0;
        
        public static bool ReplaceInput { get; set; } = true;
        
        public static bool Echo { get; set; } = true; 
        public static bool Debug { get; set; } = false;

        public static void Load(Rhino.PersistentSettings settings)
        {
            AngleTolDeg = settings.GetDouble("SmoothJoints_AngleTolDeg", AngleTolDeg);
            if (AngleTolDeg <= 0) AngleTolDeg = 0.1 * RhinoMath.ToDegrees(RhinoDoc.ActiveDoc.ModelAngleToleranceRadians);

            SkipIfAlreadyAtContinuity = settings.GetBool("SmoothJoints_SkipIfAlready", SkipIfAlreadyAtContinuity);
            LimitCrvDev = settings.GetBool("SmoothJoints_LimitCrvDev", LimitCrvDev);
            
            DevTol = settings.GetDouble("SmoothJoints_DevTol", DevTol);
            if (DevTol <= 0) DevTol = 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

            ReplaceInput = settings.GetBool("SmoothJoints_ReplaceInput", ReplaceInput);
            Debug = settings.GetBool("SmoothJoints_Debug", Debug);
        }

        public static void Save(Rhino.PersistentSettings settings)
        {
            settings.SetDouble("SmoothJoints_AngleTolDeg", AngleTolDeg);
            settings.SetBool("SmoothJoints_SkipIfAlready", SkipIfAlreadyAtContinuity);
            settings.SetBool("SmoothJoints_LimitCrvDev", LimitCrvDev);
            settings.SetDouble("SmoothJoints_DevTol", DevTol);
            settings.SetBool("SmoothJoints_ReplaceInput", ReplaceInput);
            settings.SetBool("SmoothJoints_Debug", Debug);
        }
    }
}