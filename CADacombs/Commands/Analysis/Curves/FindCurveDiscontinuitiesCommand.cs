using System;
using System.Collections.Generic;
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
        
        // Changed prefix to match the new cc nomenclature
        public override string EnglishName => "ccCrvDiscontinuities";

        // Sticky Options
        private int _evalG = 2; // 1 for G1, 2 for G2, 3 for G3
        private double _g1AngleTolDeg = RhinoDoc.ActiveDoc.ModelAngleToleranceDegrees;
        private double _g2PlusAngleTolDeg = 2.0;
        private double _vectMagTolPct = 5.0;
        private bool _addPts = true;
        private bool _splitCurve = false;
        private bool _echo = true;
        private bool _debug = false;

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var go = new GetObject();
            go.SetCommandPrompt("Select curves to find discontinuities");
            go.GeometryFilter = ObjectType.Curve;
            go.AcceptNumber(true, true); 
            go.DeselectAllBeforePostSelect = false;
            go.EnableClearObjectsOnEntry(false);
            go.EnableUnselectObjectsOnExit(false);

            OptionDouble optValG1Ang = new OptionDouble(_g1AngleTolDeg);
            OptionDouble optValG2Ang = new OptionDouble(_g2PlusAngleTolDeg);
            OptionDouble optValMagPct = new OptionDouble(_vectMagTolPct);
            OptionToggle optValAddPts = new OptionToggle(_addPts, "No", "Yes");
            OptionToggle optValSplit = new OptionToggle(_splitCurve, "No", "Yes");
            OptionToggle optValEcho = new OptionToggle(_echo, "No", "Yes");
            OptionToggle optValDebug = new OptionToggle(_debug, "No", "Yes");

            string[] gList = { "1", "2", "3" };
            bool bPreselectedObjsChecked = false;

            while (true)
            {
                go.ClearCommandOptions();
                
                int optG = go.AddOptionList("G", gList, _evalG - 1);
                int optG1 = go.AddOptionDouble("G1AngleTol", ref optValG1Ang);
                
                int optG2 = -1, optMag = -1;
                if (_evalG >= 2)
                {
                    optG2 = go.AddOptionDouble("G2PlusAngleTol", ref optValG2Ang);
                    optMag = go.AddOptionDouble("VectMagTolPct", ref optValMagPct);
                }

                int optAdd = go.AddOptionToggle("AddPts", ref optValAddPts);
                int optSplit = go.AddOptionToggle("SplitCurve", ref optValSplit);
                int optEcho = go.AddOptionToggle("Echo", ref optValEcho);
                int optDebug = go.AddOptionToggle("Debug", ref optValDebug);

                var res = go.GetMultiple(1, 0);

                if (!bPreselectedObjsChecked && go.ObjectsWerePreselected)
                {
                    bPreselectedObjsChecked = true;
                    go.EnablePreSelect(false, true);
                    continue;
                }

                if (res == Rhino.Input.GetResult.Cancel) return Result.Cancel;
                if (res == Rhino.Input.GetResult.Object) break;

                if (res == Rhino.Input.GetResult.Number)
                {
                    int val = (int)go.Number();
                    if (val >= 1 && val <= 3) _evalG = val;
                    else RhinoApp.WriteLine("Numeric input must be 1, 2, or 3.");
                    continue;
                }

                if (res == Rhino.Input.GetResult.Option)
                {
                    var opt = go.Option();
                    if (opt.Index == optG) _evalG = opt.CurrentListOptionIndex + 1;
                    else if (opt.Index == optG1) _g1AngleTolDeg = optValG1Ang.CurrentValue;
                    else if (_evalG >= 2 && opt.Index == optG2) _g2PlusAngleTolDeg = optValG2Ang.CurrentValue;
                    else if (_evalG >= 2 && opt.Index == optMag) _vectMagTolPct = optValMagPct.CurrentValue;
                    else if (opt.Index == optAdd) _addPts = optValAddPts.CurrentValue;
                    else if (opt.Index == optSplit) _splitCurve = optValSplit.CurrentValue;
                    else if (opt.Index == optEcho) _echo = optValEcho.CurrentValue;
                    else if (opt.Index == optDebug) _debug = optValDebug.CurrentValue;
                }
            }

            List<Guid> splitOutputs = new List<Guid>();
            int totalDiscontinuities = 0;
            int splitCurveCount = 0;

            foreach (var objRef in go.Objects())
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

                    // Mathematical optimization: A knot cannot have a G(N) discontinuity 
                    // unless its multiplicity is strictly greater than (Degree - N).
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

                    // Delegate the math entirely to the unified core engine
                    int? actualGLevel = ContinuityUtils.GetContinuityLevel(
                        vecsB, vecsA, 
                        doc.ModelAbsoluteTolerance, 
                        _g1AngleTolDeg, 
                        _g2PlusAngleTolDeg, 
                        _vectMagTolPct);

                    // If the actual continuity is a gap (null) or lower than our target evaluation level, mark it to split
                    if (actualGLevel == null || actualGLevel.Value < _evalG)
                    {
                        splitParams.Add(tEval);
                        if (_debug) 
                            RhinoApp.WriteLine($"Break at {tEval}. Target: G{_evalG}, Actual: {ContinuityUtils.FormatContinuityString(actualGLevel)}");
                    }

                    iK += m;
                }

                if (splitParams.Count > 0)
                {
                    totalDiscontinuities += splitParams.Count;

                    if (_addPts)
                    {
                        foreach (double t in splitParams) doc.Objects.AddPoint(nc.PointAt(t));
                    }

                    if (_splitCurve)
                    {
                        Curve[] split = nc.Split(splitParams);
                        if (split != null && split.Length > 0)
                        {
                            splitCurveCount++;
                            foreach (var s in split)
                            {
                                Guid id = doc.Objects.AddCurve(s, objRef.Object().Attributes);
                                if (id != Guid.Empty) splitOutputs.Add(id);
                            }
                            doc.Objects.Delete(objRef.ObjectId, true);
                        }
                    }
                }
            }

            if (_echo)
            {
                if (totalDiscontinuities > 0)
                {
                    string msg = $"Found {totalDiscontinuities} G{_evalG} discontinuities.";
                    if (_splitCurve) msg += $" Input was split into {splitOutputs.Count} curves.";
                    RhinoApp.WriteLine(msg);
                }
                else
                {
                    RhinoApp.WriteLine($"No G{_evalG} discontinuities found.");
                }
            }

            if (_splitCurve && splitOutputs.Count > 0)
            {
                doc.Objects.UnselectAll();
                foreach (Guid id in splitOutputs) doc.Objects.Select(id); 
            }

            doc.Views.Redraw();
            return Result.Success;
        }
    }
}