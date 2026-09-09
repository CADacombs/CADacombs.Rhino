using System;
using System.Text;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;
using CADacombs.Core.Curves;
using CADacombs.Core.Reporting;

namespace CADacombs.Commands.Analysis.Curves
{
    public class CompareCurveContinuityCommand : Command
    {
        public CompareCurveContinuityCommand() { Instance = this; }
        public static CompareCurveContinuityCommand Instance { get; private set; }
        public override string EnglishName => "ccGCon";

        // Sticky Options
        private double _distTol = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
        private double _g1AngleTolDeg = RhinoDoc.ActiveDoc.ModelAngleToleranceDegrees;
        private double _g2PlusAngleTolDeg = 2.0;
        private double _vectMagTolPct = 5.0;
        private bool _alignDirs = true;
        private bool _addDot = false;
        private bool _showCCont = false;
        private bool _echo = true;
        private bool _debug = false;

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // --- Common Option State ---
            OptionDouble optValDist = new OptionDouble(_distTol);
            OptionDouble optValG1Ang = new OptionDouble(_g1AngleTolDeg);
            OptionDouble optValG2Ang = new OptionDouble(_g2PlusAngleTolDeg);
            OptionDouble optValVectMagPct = new OptionDouble(_vectMagTolPct);
            OptionToggle optValAlign = new OptionToggle(_alignDirs, "No", "Yes");
            OptionToggle optValDot = new OptionToggle(_addDot, "No", "Yes");
            OptionToggle optValCCont = new OptionToggle(_showCCont, "No", "Yes");
            OptionToggle optValEcho = new OptionToggle(_echo, "No", "Yes");
            OptionToggle optValDebug = new OptionToggle(_debug, "No", "Yes");

            int optDoc = -1, optTight = -1;

            void SetupOptions(GetObject go)
            {
                go.ClearCommandOptions();
                optDoc = go.AddOption("DocTols");
                optTight = go.AddOption("TightTols");
                go.AddOptionDouble("DistTol", ref optValDist);
                go.AddOptionDouble("G1AngleTol", ref optValG1Ang);
                go.AddOptionDouble("G2PlusAngleTol", ref optValG2Ang);
                go.AddOptionDouble("VectMagTolPct", ref optValVectMagPct);
                go.AddOptionToggle("AlignCrvDirs", ref optValAlign);
                go.AddOptionToggle("AddDot", ref optValDot);
                go.AddOptionToggle("ShowCCont", ref optValCCont);
                go.AddOptionToggle("Echo", ref optValEcho);
                go.AddOptionToggle("Debug", ref optValDebug);
            }

            void UpdateOptions(Rhino.Input.GetResult res, GetObject go)
            {
                if (res == Rhino.Input.GetResult.Option)
                {
                    var opt = go.Option();
                    if (opt.Index == optDoc)
                    {
                        optValDist.CurrentValue = doc.ModelAbsoluteTolerance;
                        optValG1Ang.CurrentValue = doc.ModelAngleToleranceDegrees;
                        optValG2Ang.CurrentValue = 2.0;
                    }
                    else if (opt.Index == optTight)
                    {
                        optValDist.CurrentValue = 1e-12;
                        optValG1Ang.CurrentValue = 1e-6;
                        optValG2Ang.CurrentValue = 1e-6;
                    }

                    _distTol = optValDist.CurrentValue;
                    _g1AngleTolDeg = optValG1Ang.CurrentValue;
                    _g2PlusAngleTolDeg = optValG2Ang.CurrentValue;
                    _vectMagTolPct = optValVectMagPct.CurrentValue;
                    _alignDirs = optValAlign.CurrentValue;
                    _addDot = optValDot.CurrentValue;
                    _showCCont = optValCCont.CurrentValue;
                    _echo = optValEcho.CurrentValue;
                    _debug = optValDebug.CurrentValue;
                }
            }

            // --- STEP 1: Select First Curve ---
            var go1 = new GetObject();
            go1.SetCommandPrompt("First curve - select near end");
            go1.GeometryFilter = ObjectType.Curve;
            go1.DisablePreSelect();

            ObjRef refA = null;
            while (refA == null)
            {
                SetupOptions(go1);
                var res = go1.Get();
                if (res == Rhino.Input.GetResult.Cancel) return Result.Cancel;
                if (res == Rhino.Input.GetResult.Option) { UpdateOptions(res, go1); continue; }
                if (res == Rhino.Input.GetResult.Object) refA = go1.Object(0);
            }

            doc.Objects.UnselectAll();
            doc.Views.Redraw();

            Curve crvA = refA.Curve();
            RhinoObject objA = refA.Object();
            
            // Robust physical distance check to avoid parameter domain bugs
            double distStartA = crvA.PointAtStart.DistanceTo(refA.SelectionPoint());
            double distEndA = crvA.PointAtEnd.DistanceTo(refA.SelectionPoint());
            bool evalEndA = distEndA < distStartA;
            Point3d ptA = evalEndA ? crvA.PointAtEnd : crvA.PointAtStart;

            // Highlight the first curve natively so it stays visible during step 2
            if (objA != null) objA.Highlight(true);

            // --- STEP 2: Select Second Curve (With Dynamic Point Preview) ---
            var go2 = new GetObject();
            go2.SetCommandPrompt("Second curve - select near end");
            go2.GeometryFilter = ObjectType.Curve;
            go2.DisablePreSelect();
            go2.EnablePreSelect(false, true);

            // Start the custom preview conduit for the red 'X'
            var previewConduit = new ComparePreviewConduit { PtA = ptA };
            previewConduit.Enabled = true;
            doc.Views.Redraw();

            ObjRef refB = null;
            while (refB == null)
            {
                SetupOptions(go2);
                var res = go2.Get();
                if (res == Rhino.Input.GetResult.Cancel) 
                {
                    previewConduit.Enabled = false;
                    if (objA != null) objA.Highlight(false);
                    doc.Views.Redraw();
                    return Result.Cancel;
                }
                if (res == Rhino.Input.GetResult.Option) { UpdateOptions(res, go2); continue; }
                if (res == Rhino.Input.GetResult.Object) refB = go2.Object(0);
            }

            // Turn off preview and highlight once selection is complete
            previewConduit.Enabled = false;
            if (objA != null) objA.Highlight(false);

            Curve crvB = refB.Curve();
            double distStartB = crvB.PointAtStart.DistanceTo(refB.SelectionPoint());
            double distEndB = crvB.PointAtEnd.DistanceTo(refB.SelectionPoint());
            bool evalEndB = distEndB < distStartB;

            if (_alignDirs && evalEndA == evalEndB)
            {
                crvB = crvB.DuplicateCurve();
                crvB.Reverse();
                evalEndB = !evalEndB;
                if (_debug) RhinoApp.WriteLine("Curve B direction was reversed to match Curve A.");
            }

            NurbsCurve ncA = crvA.ToNurbsCurve();
            NurbsCurve ncB = crvB.ToNurbsCurve();

            double evalTA = evalEndA ? ncA.Domain.T1 : ncA.Domain.T0;
            var sideA = evalEndA ? CurveEvaluationSide.Below : CurveEvaluationSide.Above;

            double evalTB = evalEndB ? ncB.Domain.T1 : ncB.Domain.T0;
            var sideB = evalEndB ? CurveEvaluationSide.Below : CurveEvaluationSide.Above;

            int maxDeg = Math.Max(3, Math.Max(ncA.Degree, ncB.Degree));
            Vector3d[] dA = ncA.DerivativeAt(evalTA, maxDeg, sideA);
            Vector3d[] dB = ncB.DerivativeAt(evalTB, maxDeg, sideB);

            var vecsA = ContinuityUtils.GetContinuityVectorsAt(ncA, evalTA, sideA);
            var vecsB = ContinuityUtils.GetContinuityVectorsAt(ncB, evalTB, sideB);

            // Process true continuities using the central engine
            int? gCont = ContinuityUtils.GetContinuityLevel(vecsA, vecsB, _distTol, _g1AngleTolDeg, _g2PlusAngleTolDeg, _vectMagTolPct);
            int cCont = ContinuityUtils.GetParametricContinuity(ncA, evalTA, sideA, ncB, evalTB, sideB);
            bool isGInf = ContinuityUtils.IsGInfinity(ncA, evalTA, sideA, ncB, evalTB, sideB, _g1AngleTolDeg);

            if (_echo) PrintReport(vecsA, vecsB, dA, dB, gCont, cCont, isGInf, doc);

            if (_addDot && gCont.HasValue)
            {
                string gStr = isGInf ? "G∞" : ContinuityUtils.FormatContinuityString(gCont);
                string cStr = cCont == int.MaxValue ? "∞" : cCont.ToString();
                string dotText = _showCCont ? $"{gStr}/C{cStr}" : gStr;
                
                TextDot dot = new TextDot(dotText, vecsA.Pt) { FontHeight = 11 };
                doc.Objects.AddTextDot(dot);
            }

            doc.Views.Redraw();
            return Result.Success;
        }

        private void PrintReport(
            (Point3d Pt, Vector3d Tangent, Vector3d Curvature, Vector3d Torsion) vA, 
            (Point3d Pt, Vector3d Tangent, Vector3d Curvature, Vector3d Torsion) vB,
            Vector3d[] dA, Vector3d[] dB,
            int? gCont, int cCont, bool isGInf, RhinoDoc doc)
        {
            StringBuilder sb = new StringBuilder();
            double zeroTol = 1e-9;
            string units = doc.ModelUnitSystem.ToString().ToLower();
            int prec = doc.ModelDistanceDisplayPrecision;

            sb.Append("Torsion difference: ");
            if (vA.Torsion.Length <= zeroTol && vB.Torsion.Length <= zeroTol)
            {
                sb.AppendLine("(Both curves have no change in curvature at evaluated ends.)");
            }
            else if (vA.Torsion.Length <= zeroTol || vB.Torsion.Length <= zeroTol)
            {
                sb.AppendLine("(One curve has no change in curvature at evaluated end.)");
            }
            else
            {
                double angleTors = RhinoMath.ToDegrees(Vector3d.VectorAngle(vA.Torsion, vB.Torsion));
                double magTors = Math.Abs(vA.Torsion.Length - vB.Torsion.Length);
                sb.AppendLine($"{Math.Round(angleTors, prec).ToString($"F{prec}")} degrees, {Math.Round(magTors, prec).ToString($"F{prec}")} magnitude");
                
                if (_showCCont)
                {
                    sb.Append("  3rd derivative vector differences: ");
                    double angleD3 = RhinoMath.ToDegrees(Vector3d.VectorAngle(dA[3], dB[3]));
                    double magD3 = Math.Abs(dA[3].Length - dB[3].Length);
                    sb.AppendLine($"{Math.Round(angleD3, prec).ToString($"F{prec}")} degrees, {FormatUtils.FormatDistance(magD3, prec)} magnitude");
                }
            }

            sb.Append("Curvature difference: ");
            if (vA.Curvature.Length <= zeroTol && vB.Curvature.Length <= zeroTol)
            {
                sb.AppendLine("(Both curves are linear at evaluated ends.)");
            }
            else if (vA.Curvature.Length <= zeroTol || vB.Curvature.Length <= zeroTol)
            {
                sb.AppendLine("(One curve is linear at evaluated end.)");
            }
            else
            {
                double angleCrv = RhinoMath.ToDegrees(Vector3d.VectorAngle(vA.Curvature, vB.Curvature));
                double rDiff = Math.Abs((1.0 / vA.Curvature.Length) - (1.0 / vB.Curvature.Length));
                sb.AppendLine($"{Math.Round(angleCrv, prec).ToString($"F{prec}")} degrees, R{Math.Round(rDiff, prec).ToString($"F{prec}")} {units}");
                
                if (_showCCont)
                {
                    sb.Append("  2nd derivative vector differences: ");
                    double angleD2 = RhinoMath.ToDegrees(Vector3d.VectorAngle(dA[2], dB[2]));
                    double magD2 = Math.Abs(dA[2].Length - dB[2].Length);
                    sb.AppendLine($"{Math.Round(angleD2, prec).ToString($"F{prec}")} degrees, {FormatUtils.FormatDistance(magD2, prec)} magnitude");
                }
            }

            double angleTan = RhinoMath.ToDegrees(Vector3d.VectorAngle(vA.Tangent, vB.Tangent));
            sb.AppendLine($"Tangent difference: {Math.Round(angleTan, prec).ToString($"F{prec}")} degrees");
            
            if (_showCCont)
            {
                sb.Append("  1st derivative vector differences: ");
                double angleD1 = RhinoMath.ToDegrees(Vector3d.VectorAngle(dA[1], dB[1]));
                double magD1 = Math.Abs(dA[1].Length - dB[1].Length);
                sb.AppendLine($"{Math.Round(angleD1, prec).ToString($"F{prec}")} degrees, {FormatUtils.FormatDistance(magD1, prec)} magnitude");
            }

            double dist = vA.Pt.DistanceTo(vB.Pt);
            sb.AppendLine($"Curve end difference = {Math.Round(dist, prec).ToString($"F{prec}")} {units}");

            if (!gCont.HasValue)
            {
                sb.AppendLine("No continuity between the curves.");
            }
            else
            {
                string gStr = isGInf ? "G∞" : ContinuityUtils.FormatContinuityString(gCont);
                
                if (_showCCont)
                {
                    string cStr = cCont == int.MaxValue ? "∞" : cCont.ToString();
                    sb.AppendLine($"Continuities at curves' ends are {gStr} and C{cStr}.");
                }
                else
                {
                    sb.AppendLine($"Continuities at curves' ends are {gStr}.");
                }
            }

            RhinoApp.WriteLine(sb.ToString().TrimEnd());
        }
    }

    class ComparePreviewConduit : Rhino.Display.DisplayConduit
    {
        public Point3d PtA { get; set; }

        protected override void DrawForeground(Rhino.Display.DrawEventArgs e)
        {
            System.Drawing.Color feedbackColor = Rhino.ApplicationSettings.AppearanceSettings.FeedbackColor;
            e.Display.DrawPoint(PtA, Rhino.Display.PointStyle.X, 6, feedbackColor);
        }
    }
}