using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;
using CADacombs.Core.Curves;

namespace CADacombs.Commands.Analysis.Curves
{
    public class ReportCurveContinuitiesCommand : Command
    {
        public ReportCurveContinuitiesCommand() { Instance = this; }
        public static ReportCurveContinuitiesCommand Instance { get; private set; }
        public override string EnglishName => "ccCrvContinuities";

        // Sticky Options
        private double _distTol = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
        private double _g1AngleTolDeg = RhinoDoc.ActiveDoc.ModelAngleToleranceDegrees;
        private double _g2PlusAngleTolDeg = 2.0;
        private double _vectMagTolPct = 5.0;
        private int _dotHeight = 12;
        private bool _addDots = false;

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // --- STEP 1: Select Curves ---
            var go = new GetObject();
            go.SetCommandPrompt("Select curves to report continuities");
            go.GeometryFilter = ObjectType.Curve;
            go.GroupSelect = true;
            go.SubObjectSelect = false;
            
            var goRes = go.GetMultiple(1, 0);
            if (goRes != Rhino.Input.GetResult.Object) return Result.Cancel;
            
            List<Curve> inputCurves = new List<Curve>();
            foreach (var objRef in go.Objects())
            {
                Curve c = objRef.Curve();
                if (c != null) inputCurves.Add(c);
            }
            
            doc.Objects.UnselectAll();

            // --- STEP 2: Preview Loop & Options ---
            OptionToggle optValAddDots = new OptionToggle(_addDots, "No", "Yes");
            OptionDouble optValDist = new OptionDouble(_distTol);
            OptionDouble optValG1Ang = new OptionDouble(_g1AngleTolDeg);
            OptionDouble optValG2Ang = new OptionDouble(_g2PlusAngleTolDeg);
            OptionDouble optValVectMagPct = new OptionDouble(_vectMagTolPct);
            OptionInteger optValHeight = new OptionInteger(_dotHeight, 8, 72);

            var previewConduit = new ReportPreviewConduit();
            previewConduit.Enabled = true;

            var goOpt = new GetOption();
            goOpt.AcceptNothing(true); 

            int totalDots = 0;
            string lastSummary = null;

            while (true)
            {
                // Calculate dots based on current options
                previewConduit.Dots.Clear();
                int cGap = 0, c0 = 0, c1 = 0, c2 = 0, c3 = 0, cInf = 0;

                foreach (var crv in inputCurves)
                {
                    EvaluateCurve(crv, previewConduit.Dots, ref cGap, ref c0, ref c1, ref c2, ref c3, ref cInf);
                }

                totalDots = previewConduit.Dots.Count;
                string countSummary = $"Totals - G0:{c0} G1:{c1} G2:{c2} G3+:{c3} G∞:{cInf} Gaps:{cGap}";
                
                // Only print the summary to the command line if the counts actually changed
                if (countSummary != lastSummary)
                {
                    RhinoApp.WriteLine(countSummary);
                    lastSummary = countSummary;
                }
                
                doc.Views.Redraw();

                goOpt.ClearCommandOptions();
                goOpt.SetCommandPrompt("Set options");
                goOpt.SetDefaultString("Finalize");
                
                int optAdd = goOpt.AddOptionToggle("AddDots", ref optValAddDots);
                int optDist = goOpt.AddOptionDouble("DistTol", ref optValDist);
                int optG1 = goOpt.AddOptionDouble("G1AngleTol", ref optValG1Ang);
                int optG2 = goOpt.AddOptionDouble("G2PlusAngleTol", ref optValG2Ang);
                int optMag = goOpt.AddOptionDouble("VectMagTolPct", ref optValVectMagPct);
                int optH = goOpt.AddOptionInteger("TextHeight", ref optValHeight);

                var res = goOpt.Get();

                if (res == Rhino.Input.GetResult.Cancel) 
                {
                    previewConduit.Enabled = false;
                    doc.Views.Redraw();
                    return Result.Cancel;
                }

                // If user hits Enter (Nothing) or types Finalize (String)
                if (res == Rhino.Input.GetResult.Nothing || 
                   (res == Rhino.Input.GetResult.String && goOpt.StringResult().Equals("Finalize", StringComparison.OrdinalIgnoreCase)))
                {
                    break; 
                }

                if (res == Rhino.Input.GetResult.Option)
                {
                    _addDots = optValAddDots.CurrentValue;
                    _distTol = optValDist.CurrentValue;
                    _g1AngleTolDeg = optValG1Ang.CurrentValue;
                    _g2PlusAngleTolDeg = optValG2Ang.CurrentValue;
                    _vectMagTolPct = optValVectMagPct.CurrentValue;
                    _dotHeight = optValHeight.CurrentValue;
                }
            }

            // --- STEP 3: Bake the final dots (if requested) ---
            previewConduit.Enabled = false;
            
            if (_addDots)
            {
                var newDots = new List<Guid>();
                foreach (var dotData in previewConduit.Dots)
                {
                    TextDot dot = new TextDot(dotData.Label, dotData.Pt) { FontHeight = _dotHeight };
                    newDots.Add(doc.Objects.AddTextDot(dot));
                }

                foreach (Guid id in newDots)
                {
                    if (id != Guid.Empty) doc.Objects.Select(id);
                }
                RhinoApp.WriteLine($"Added {totalDots} continuity TextDots.");
            }

            doc.Views.Redraw();
            return Result.Success;
        }

        private void EvaluateCurve(Curve crv, List<(Point3d, string, Color)> outDots, 
            ref int cGap, ref int c0, ref int c1, ref int c2, ref int c3, ref int cInf)
        {
            if (crv is PolylineCurve plc)
            {
                for (int i = 1; i < plc.PointCount - 1; i++) AddDotRecord(outDots, plc.Point(i), "G0", ref cGap, ref c0, ref c1, ref c2, ref c3, ref cInf);
            }
            else if (crv is PolyCurve pc)
            {
                for (int i = 1; i < pc.SegmentCount; i++)
                {
                    Curve segB = pc.SegmentCurve(i - 1);
                    Curve segA = pc.SegmentCurve(i);
                    ProcessJoin(segB, segB.Domain.T1, segA, segA.Domain.T0, outDots, ref cGap, ref c0, ref c1, ref c2, ref c3, ref cInf);
                }
                
                for (int i = 0; i < pc.SegmentCount; i++)
                {
                    if (pc.SegmentCurve(i) is PolylineCurve subPlc)
                    {
                        for (int j = 1; j < subPlc.PointCount - 1; j++)
                            AddDotRecord(outDots, subPlc.Point(j), "G0", ref cGap, ref c0, ref c1, ref c2, ref c3, ref cInf);
                    }
                    else
                    {
                        EvaluateInnerKnots(pc.SegmentCurve(i), outDots, ref cGap, ref c0, ref c1, ref c2, ref c3, ref cInf);
                    }
                }
            }
            else
            {
                EvaluateInnerKnots(crv, outDots, ref cGap, ref c0, ref c1, ref c2, ref c3, ref cInf);
            }

            if (crv.IsClosed)
            {
                ProcessJoin(crv, crv.Domain.Max, crv, crv.Domain.Min, outDots, ref cGap, ref c0, ref c1, ref c2, ref c3, ref cInf);
            }
        }

        private void EvaluateInnerKnots(Curve crv, List<(Point3d, string, Color)> outDots, 
            ref int cGap, ref int c0, ref int c1, ref int c2, ref int c3, ref int cInf)
        {
            if (crv is PolylineCurve || crv is LineCurve || crv is ArcCurve) return;

            NurbsCurve nc = crv.ToNurbsCurve();
            if (nc == null) return;

            double[] spans = nc.SpanVector();
            if (spans == null || spans.Length <= 2) return;

            for (int i = 1; i < spans.Length - 1; i++)
            {
                ProcessJoin(nc, spans[i], nc, spans[i], outDots, ref cGap, ref c0, ref c1, ref c2, ref c3, ref cInf);
            }
        }

        private void ProcessJoin(Curve cB, double tB, Curve cA, double tA, List<(Point3d, string, Color)> outDots,
            ref int cGap, ref int c0, ref int c1, ref int c2, ref int c3, ref int cInf)
        {
            NurbsCurve ncB = cB.ToNurbsCurve();
            NurbsCurve ncA = cA.ToNurbsCurve();
            if (ncB == null || ncA == null) return;

            // Check True Geometric Infinity (Domain-agnostic)
            bool isGInf = ContinuityUtils.IsGInfinity(ncA, tA, CurveEvaluationSide.Above, ncB, tB, CurveEvaluationSide.Below, _g1AngleTolDeg);
            
            var vB = ContinuityUtils.GetContinuityVectorsAt(ncB, tB, CurveEvaluationSide.Below);
            var vA = ContinuityUtils.GetContinuityVectorsAt(ncA, tA, CurveEvaluationSide.Above);

            if (isGInf)
            {
                AddDotRecord(outDots, vA.Pt, "G∞", ref cGap, ref c0, ref c1, ref c2, ref c3, ref cInf);
                return;
            }

            int? gLevel = ContinuityUtils.GetContinuityLevel(vB, vA, _distTol, _g1AngleTolDeg, _g2PlusAngleTolDeg, _vectMagTolPct);
            AddDotRecord(outDots, vA.Pt, ContinuityUtils.FormatContinuityString(gLevel), ref cGap, ref c0, ref c1, ref c2, ref c3, ref cInf);
        }

        private void AddDotRecord(List<(Point3d, string, Color)> outDots, Point3d pt, string label,
            ref int cGap, ref int c0, ref int c1, ref int c2, ref int c3, ref int cInf)
        {
            Color color = Color.Gray;
            if (label == "Gap") { color = Color.DarkRed; cGap++; }
            else if (label == "G0") { color = Color.Red; c0++; }
            else if (label == "G1") { color = Color.Gold; c1++; }
            else if (label == "G2") { color = Color.YellowGreen; c2++; }
            else if (label == "G3+") { color = Color.LimeGreen; c3++; }
            else if (label == "G∞") { color = Color.Cyan; cInf++; }

            outDots.Add((pt, label, color));
        }
    }

    class ReportPreviewConduit : Rhino.Display.DisplayConduit
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