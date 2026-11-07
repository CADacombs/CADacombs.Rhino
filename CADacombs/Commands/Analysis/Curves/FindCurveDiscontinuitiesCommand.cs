using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;
using CADacombs.Core.Curves;
using CADacombs.Core.Reporting;

namespace CADacombs.Commands.Analysis.Curves
{
    public class FindCurveDiscontinuitiesCommand : Command
    {
        public FindCurveDiscontinuitiesCommand() { Instance = this; }
        public static FindCurveDiscontinuitiesCommand Instance { get; private set; }
        
        public override string EnglishName => "ccCrvDiscontinuities";

        // Sticky Options (Defaults updated to false for finalize actions)
        private int _evalG = 2; // 1 for G1, 2 for G2, 3 for G3
        private double _vectAngleTolDeg = RhinoDoc.ActiveDoc.ModelAngleToleranceDegrees;
        private double _vectMagTolPct = 5.0;
        private bool _addPts = false;
        private bool _addDots = false;
        private bool _splitCrv = false;
        private bool _echo = true;
        private bool _debug = false;

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // --- STEP 1: Select Curves ---
            var go = new GetObject();
            go.SetCommandPrompt("Select curves to find discontinuities");
            go.GeometryFilter = ObjectType.Curve;
            go.GroupSelect = true;
            go.SubObjectSelect = false;

            if (go.GetMultiple(1, 0) != Rhino.Input.GetResult.Object) return Result.Cancel;
            
            List<ObjRef> inputRefs = new List<ObjRef>(go.Objects());
            doc.Objects.UnselectAll();

            // --- STEP 2: Preview Loop & Options ---
            OptionToggle optValAddPts = new OptionToggle(_addPts, "No", "Yes");
            OptionToggle optValAddDots = new OptionToggle(_addDots, "No", "Yes");
            OptionToggle optValSplit = new OptionToggle(_splitCrv, "No", "Yes");
            OptionToggle optValEcho = new OptionToggle(_echo, "No", "Yes");
            OptionToggle optValDebug = new OptionToggle(_debug, "No", "Yes");

            OptionDouble optValAngDeg = new OptionDouble(_vectAngleTolDeg);
            OptionDouble optValMagPct = new OptionDouble(_vectMagTolPct);
            string[] gList = { "1", "2", "3" };

            var previewConduit = new FindDiscontinuitiesPreviewConduit();
            previewConduit.Enabled = true;

            var goOpt = new GetOption();
            goOpt.AcceptNothing(true);
            goOpt.AcceptNumber(true, true); 

            int totalDiscontinuities = 0;
            string lastSummary = null;
            string breakdownStr = "";
            
            // Caches parameters to bake when Finalize is triggered
            List<(Curve Crv, List<double> SplitParams, ObjRef Ref)> curvesToProcess = new List<(Curve, List<double>, ObjRef)>();

            while (true)
            {
                previewConduit.Dots.Clear();
                curvesToProcess.Clear();
                totalDiscontinuities = 0;
                int cGap = 0, c0 = 0, c1 = 0, c2 = 0;

                foreach (var objRef in inputRefs)
                {
                    Curve crv = objRef.Curve();
                    if (crv == null) continue;

                    NurbsCurve nc = crv.ToNurbsCurve();
                    List<double> splitParams = new List<double>();

                    int iK = (nc.IsClosed && !nc.IsPeriodic) ? 0 : nc.Degree;
                    int iK_Stop = nc.Knots.Count - nc.Degree;

                    while (iK < iK_Stop)
                    {
                        int m = nc.Knots.KnotMultiplicity(iK);

                        if (m <= nc.Degree - _evalG)
                        {
                            iK += m;
                            continue;
                        }

                        double tEval = nc.Knots[iK];
                        var vecsB = (iK == 0) ? 
                            ContinuityUtils.GetContinuityVectorsAt(nc, nc.Knots[nc.Knots.Count - 1], CurveEvaluationSide.Below) :
                            ContinuityUtils.GetContinuityVectorsAt(nc, tEval, CurveEvaluationSide.Below);
                            
                        var vecsA = ContinuityUtils.GetContinuityVectorsAt(nc, tEval, CurveEvaluationSide.Above);

                        int? actualGLevel = ContinuityUtils.GetContinuityLevel(
                            vecsB, vecsA, 
                            doc.ModelAbsoluteTolerance, 
                            _vectAngleTolDeg,
                            _vectAngleTolDeg,
                            _vectMagTolPct);

                        if (actualGLevel == null || actualGLevel.Value < _evalG)
                        {
                            splitParams.Add(tEval);

                            if (actualGLevel == null) cGap++;
                            else if (actualGLevel.Value == 0) c0++;
                            else if (actualGLevel.Value == 1) c1++;
                            else if (actualGLevel.Value == 2) c2++;
                            
                            string label = ContinuityUtils.FormatContinuityString(actualGLevel);
                            
                            // Include angular difference if G-order is 1
                            if (_evalG == 1 && !vecsB.Tangent.IsTiny() && !vecsA.Tangent.IsTiny())
                            {
                                double angle = RhinoMath.ToDegrees(Vector3d.VectorAngle(vecsB.Tangent, vecsA.Tangent));
                                int prec = 2;
                                label += $"\n{Math.Round(angle, prec).ToString($"F{prec}")}°";
                            }

                            Color color = Color.Gray;
                            if (actualGLevel == null) color = Color.DarkRed;
                            else if (actualGLevel == 0) color = Color.Red;
                            else if (actualGLevel == 1) color = Color.Gold;
                            else if (actualGLevel == 2) color = Color.YellowGreen;

                            previewConduit.Dots.Add((vecsA.Pt, label, color));
                        }
                        iK += m;
                    }

                    if (splitParams.Count > 0)
                    {
                        totalDiscontinuities += splitParams.Count;
                        curvesToProcess.Add((nc, splitParams, objRef));
                    }
                }

                List<string> breakdown = new List<string>();
                if (cGap > 0) breakdown.Add($"{cGap} Gap");
                if (c0 > 0) breakdown.Add($"{c0} G0");
                if (c1 > 0) breakdown.Add($"{c1} G1");
                if (c2 > 0) breakdown.Add($"{c2} G2");

                breakdownStr = breakdown.Count > 0 ? ": " + string.Join(", ", breakdown) : "";

                string countSummary = $"Found {totalDiscontinuities} G{_evalG} discontinuities{breakdownStr}."; 

                if (countSummary != lastSummary)
                {
                    RhinoApp.WriteLine(countSummary);
                    lastSummary = countSummary;
                }
                
                doc.Views.Redraw();

                goOpt.ClearCommandOptions();
                goOpt.SetCommandPrompt("Set options");
                goOpt.SetDefaultString("Finalize");
                
                int optG = goOpt.AddOptionList("G", gList, _evalG - 1);
                int optG1 = goOpt.AddOptionDouble("VectAngleDiffTol", ref optValAngDeg);
                
                int optMag = -1;
                if (_evalG >= 2)
                {
                    optMag = goOpt.AddOptionDouble("VectMagRelDiffPct", ref optValMagPct);
                }

                int optAddPts = goOpt.AddOptionToggle("AddPts", ref optValAddPts);
                int optAddDots = goOpt.AddOptionToggle("AddDots", ref optValAddDots);
                int optSplit = goOpt.AddOptionToggle("SplitCrv", ref optValSplit);
                int optEcho = goOpt.AddOptionToggle("Echo", ref optValEcho);
                int optDebug = goOpt.AddOptionToggle("Debug", ref optValDebug);

                var res = goOpt.Get();

                if (res == Rhino.Input.GetResult.Cancel) 
                {
                    previewConduit.Enabled = false;
                    doc.Views.Redraw();
                    return Result.Cancel;
                }

                if (res == Rhino.Input.GetResult.Nothing || 
                   (res == Rhino.Input.GetResult.String && goOpt.StringResult().Equals("Finalize", StringComparison.OrdinalIgnoreCase)))
                {
                    break; 
                }

                if (res == Rhino.Input.GetResult.Number)
                {
                    int val = (int)goOpt.Number();
                    if (val >= 1 && val <= 3)
                    {
                        _evalG = val; // The list will automatically update on the next loop iteration
                    }
                    else RhinoApp.WriteLine("Numeric input must be 1, 2, or 3.");
                    continue;
                }

                if (res == Rhino.Input.GetResult.Option)
                {
                    var opt = goOpt.Option();
                    if (opt.Index == optG) _evalG = opt.CurrentListOptionIndex + 1;
                    else if (opt.Index == optG1) _vectAngleTolDeg = optValAngDeg.CurrentValue;
                    else if (_evalG >= 2 && opt.Index == optMag) _vectMagTolPct = optValMagPct.CurrentValue;
                    else if (opt.Index == optAddPts) _addPts = optValAddPts.CurrentValue;
                    else if (opt.Index == optAddDots) _addDots = optValAddDots.CurrentValue;
                    else if (opt.Index == optSplit) _splitCrv = optValSplit.CurrentValue;
                    else if (opt.Index == optEcho) _echo = optValEcho.CurrentValue;
                    else if (opt.Index == optDebug) _debug = optValDebug.CurrentValue;
                }
            }

            // --- STEP 3: Bake/Finalize Actions ---
            previewConduit.Enabled = false;

            List<Guid> splitOutputs = new List<Guid>();
            int splitCurveCount = 0;

            foreach (var processData in curvesToProcess)
            {
                if (_addPts)
                {
                    foreach (double t in processData.SplitParams) doc.Objects.AddPoint(processData.Crv.PointAt(t));
                }

                if (_splitCrv)
                {
                    Curve[] split = processData.Crv.Split(processData.SplitParams);
                    if (split != null && split.Length > 0)
                    {
                        splitCurveCount++;
                        foreach (var s in split)
                        {
                            Guid id = doc.Objects.AddCurve(s, processData.Ref.Object().Attributes);
                            if (id != Guid.Empty) splitOutputs.Add(id);
                        }
                        doc.Objects.Delete(processData.Ref.ObjectId, true);
                    }
                }
            }

            if (_addDots)
            {
                foreach (var dotData in previewConduit.Dots)
                {
                    TextDot dot = new TextDot(dotData.Label, dotData.Pt) { FontHeight = 12 };
                    doc.Objects.AddTextDot(dot);
                }
            }

            if (_echo)
            {
                if (totalDiscontinuities > 0)
                {
                    string msg = $"Finalized: Processed {totalDiscontinuities} G{_evalG} discontinuities{breakdownStr}.";
                    if (_splitCrv) msg += $" Input was split into {splitOutputs.Count} curves.";
                    if (_addDots) msg += $" Added {totalDiscontinuities} dots.";
                    RhinoApp.WriteLine(msg);
                }
                else
                {
                    RhinoApp.WriteLine($"No G{_evalG} discontinuities found.");
                }
            }

            if (_splitCrv && splitOutputs.Count > 0)
            {
                doc.Objects.UnselectAll();
                foreach (Guid id in splitOutputs) doc.Objects.Select(id); 
            }

            doc.Views.Redraw();
            return Result.Success;
        }
    }

    class FindDiscontinuitiesPreviewConduit : Rhino.Display.DisplayConduit
    {
        public List<(Point3d Pt, string Label, Color Color)> Dots { get; set; } = new List<(Point3d, string, Color)>();

        protected override void DrawForeground(Rhino.Display.DrawEventArgs e)
        {
            foreach (var dot in Dots)
            {
                e.Display.DrawDot(dot.Pt, dot.Label, dot.Color, Color.Black);
            }
        }
    }
}