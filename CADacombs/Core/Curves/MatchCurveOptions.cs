using System;
using Eto.Drawing;
using Rhino;

namespace CADacombs.Core.Curves
{
    public static class MatchCrvOptions
    {
        // Derived from Math.Acos(1.0 - Math.Pow(2, -52)) where 2^-52 is double-precision machine epsilon.
        public const double DefaultAngleTolDeg = 1.2074182697257333e-06;
        public const double DefaultCurvatureMagTolPct = 1e-8;

        public static Point? WindowLocation { get; set; }

        // 0 = Position, 1 = Tangency, 2 = Curvature
        public static int Continuity { get; set; } = 1;

        public static double PosTol { get; set; } = -1.0;
        public static double AngleTolDeg { get; set; } = -1.0;
        public static double CurvatureMagTolPct { get; set; } = DefaultCurvatureMagTolPct;
        public static bool SkipIfAlreadyAtContinuity { get; set; } = true;

        // 0 = None, 1 = Position, 2 = Tangency, 3 = Curvature
        public static int PreserveOtherEnd { get; set; } = 2;

        // 0 = First curve, 1 = Average curves, 2 = Minimize deviation (Either/Both)
        public static int ModifyMode { get; set; } = 2;

        public static bool LimitCrvDev { get; set; } = false;
        public static double DevTol { get; set; } = -1.0;

        // 0 = Lock, 1 = Keep as line / Keep as arc, 2 = Convert to NURBS
        public static int LineMode { get; set; } = 1;
        public static int ArcMode { get; set; } = 1;

        // 0 = Lock, 1 = Extend/Trim, 2 = Deform
        public static int NurbsMode { get; set; } = 2;

        // 0 = Deg, 1 = Span ct, 2 = None
        public static int IncrAsNeeded { get; set; } = 0;

        public static bool PreventExtraInflections { get; set; } = true;
        public static bool PreserveInternalContinuity { get; set; } = true;

        // 2 = G2, 3 = G3
        public static int MaxInternalContinuity { get; set; } = 2;

        public static bool DeleteInput { get; set; } = true;
        public static bool Echo { get; set; } = true; // Excluded from Load/Save so it resets default for script runners
        public static bool Debug { get; set; } = false;

        public static double GetDefaultPosTol() => RhinoMath.ZeroTolerance;
        public static double GetDefaultAngleTolDeg() => DefaultAngleTolDeg;
        public static double GetDefaultCurvMagTolPct() => DefaultCurvatureMagTolPct;
        public static double GetDefaultDevTol() =>
            RhinoDoc.ActiveDoc != null ? 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance : 1e-4;

        public static void Load(Rhino.PersistentSettings settings)
        {
            Continuity = Math.Max(0, Math.Min(2, settings.GetInteger("MatchCrv_Continuity", Continuity)));

            PosTol = settings.GetDouble("MatchCrv_PosTol", PosTol);
            if (PosTol <= 0) PosTol = GetDefaultPosTol();

            AngleTolDeg = settings.GetDouble("MatchCrv_AngleTolDeg", AngleTolDeg);
            if (AngleTolDeg <= 0) AngleTolDeg = GetDefaultAngleTolDeg();

            CurvatureMagTolPct = settings.GetDouble("MatchCrv_CurvatureMagTolPct", CurvatureMagTolPct);
            if (CurvatureMagTolPct <= 0) CurvatureMagTolPct = GetDefaultCurvMagTolPct();

            SkipIfAlreadyAtContinuity = settings.GetBool("MatchCrv_SkipIfAlreadyAtContinuity", SkipIfAlreadyAtContinuity);
            PreserveOtherEnd = Math.Max(0, Math.Min(3, settings.GetInteger("MatchCrv_PreserveOtherEnd", PreserveOtherEnd)));
            ModifyMode = Math.Max(0, Math.Min(2, settings.GetInteger("MatchCrv_ModifyMode", ModifyMode)));

            LimitCrvDev = settings.GetBool("MatchCrv_LimitCrvDev", LimitCrvDev);
            DevTol = settings.GetDouble("MatchCrv_DevTol", DevTol);
            if (DevTol <= 0) DevTol = GetDefaultDevTol();

            LineMode = Math.Max(0, Math.Min(2, settings.GetInteger("MatchCrv_LineMode", LineMode)));
            ArcMode = Math.Max(0, Math.Min(2, settings.GetInteger("MatchCrv_ArcMode", ArcMode)));
            NurbsMode = Math.Max(0, Math.Min(2, settings.GetInteger("MatchCrv_NurbsMode", NurbsMode)));
            IncrAsNeeded = Math.Max(0, Math.Min(2, settings.GetInteger("MatchCrv_IncrAsNeeded", IncrAsNeeded)));
            PreventExtraInflections = settings.GetBool("MatchCrv_PreventExtraInflections", PreventExtraInflections);
            PreserveInternalContinuity = settings.GetBool("MatchCrv_PreserveInternalContinuity", PreserveInternalContinuity);
            MaxInternalContinuity = settings.GetInteger("MatchCrv_MaxInternalContinuity", MaxInternalContinuity) == 3 ? 3 : 2;

            DeleteInput = settings.GetBool("MatchCrv_DeleteInput", DeleteInput);
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

            settings.SetInteger("MatchCrv_LineMode", LineMode);
            settings.SetInteger("MatchCrv_ArcMode", ArcMode);
            settings.SetInteger("MatchCrv_NurbsMode", NurbsMode);
            settings.SetInteger("MatchCrv_IncrAsNeeded", IncrAsNeeded);
            settings.SetBool("MatchCrv_PreventExtraInflections", PreventExtraInflections);
            settings.SetBool("MatchCrv_PreserveInternalContinuity", PreserveInternalContinuity);
            settings.SetInteger("MatchCrv_MaxInternalContinuity", MaxInternalContinuity);

            settings.SetBool("MatchCrv_DeleteInput", DeleteInput);
            settings.SetBool("MatchCrv_Debug", Debug);

            if (WindowLocation.HasValue)
            {
                settings.SetInteger("MatchCrv_WindowX", WindowLocation.Value.X);
                settings.SetInteger("MatchCrv_WindowY", WindowLocation.Value.Y);
            }
        }
    }
}
