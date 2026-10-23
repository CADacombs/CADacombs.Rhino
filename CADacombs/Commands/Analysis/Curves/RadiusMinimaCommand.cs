using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using CADacombs.Core.Curves;
using CADacombs.Core.Reporting;

namespace CADacombs.Commands.Analysis.Curves
{
    [System.Runtime.InteropServices.Guid("e567f1a2-3b4c-49d8-9e1f-7c6d8a9b1234")] // TODO: Replace with unique GUID
    public class RadiusMinimaCommand : Command
    {
        public override string EnglishName => "ccRadiusMinima";

        // Sticky settings
        private double _fMinRadToReport = 0.0;
        private double _fMaxRadToReport = 0.0;
        private double _fRadEqTol = -1.0; 
        private bool _bIncludeArcs = true;
        private bool _bAddPt = false;
        private bool _bAddDot = false;
        private int _iDotHeight = 11;
        private bool _bAddOnlyMinimumPt = true;
        private bool _bAddOnlyMinimumPtOfAllCrvs = true;
        private bool _bEcho = true;
        private bool _bDebug = false;

        public RadiusMinimaCommand()
        {
            Instance = this;
        }

        public static RadiusMinimaCommand Instance { get; private set; }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            LoadSettings(doc);

            RhinoApp.WriteLine("0 for MinRadToReport or MaxRadToReport will disable that limit.");

            var go = new GetObject();
            go.SetCommandPrompt("Select curves");
            go.GeometryFilter = ObjectType.Curve;
            go.SubObjectSelect = true; 
            go.AcceptNumber(true, true);
            go.DeselectAllBeforePostSelect = false;
            go.EnableClearObjectsOnEntry(false);
            go.EnableUnselectObjectsOnExit(false);

            bool preselectedChecked = false;

            while (true)
            {
                go.ClearCommandOptions();

                var optMinRad = new OptionDouble(_fMinRadToReport);
                var optMaxRad = new OptionDouble(_fMaxRadToReport);
                var optRadEqTol = new OptionDouble(_fRadEqTol);
                var optArcs = new OptionToggle(_bIncludeArcs, "No", "Yes");
                var optAddPt = new OptionToggle(_bAddPt, "No", "Yes");
                var optAddDot = new OptionToggle(_bAddDot, "No", "Yes");
                var optDotHeight = new OptionInteger(_iDotHeight, 3, 100);
                var optMinPt = new OptionToggle(_bAddOnlyMinimumPt, "AllMinima", "Minimum");
                var optMinPtAll = new OptionToggle(_bAddOnlyMinimumPtOfAllCrvs, "EachCrv", "AllCrvs");
                var optEcho = new OptionToggle(_bEcho, "No", "Yes");
                var optDebug = new OptionToggle(_bDebug, "No", "Yes");

                go.AddOptionDouble("MinRadToReport", ref optMinRad);
                go.AddOptionDouble("MaxRadToReport", ref optMaxRad);
                go.AddOptionDouble("RadEqTol", ref optRadEqTol);
                go.AddOptionToggle("IncludeArcs", ref optArcs);
                go.AddOptionToggle("AddPt", ref optAddPt);
                go.AddOptionToggle("AddDot", ref optAddDot);
                
                if (_bAddDot) go.AddOptionInteger("DotHeight", ref optDotHeight);
                
                go.AddOptionToggle("At", ref optMinPt);
                
                if (_bAddOnlyMinimumPt) go.AddOptionToggle("Of", ref optMinPtAll);

                go.AddOptionToggle("Echo", ref optEcho);
                go.AddOptionToggle("Debug", ref optDebug);

                GetResult res = go.GetMultiple(1, 0);

                if (!preselectedChecked && go.ObjectsWerePreselected)
                {
                    preselectedChecked = true;
                    go.EnablePreSelect(false, true);
                }
                else if (res == GetResult.Object)
                {
                    break;
                }
                else if (res == GetResult.Number)
                {
                    _fMinRadToReport = Math.Max(0.0, go.Number());
                    SaveSettings();
                }
                else if (res == GetResult.Option)
                {
                    _fMinRadToReport = Math.Max(0.0, optMinRad.CurrentValue);
                    _fMaxRadToReport = Math.Max(0.0, optMaxRad.CurrentValue);
                    
                    _fRadEqTol = optRadEqTol.CurrentValue;
                    if (_fRadEqTol < 0.0) _fRadEqTol = 10.0 * doc.ModelAbsoluteTolerance;
                    else if (_fRadEqTol < RhinoMath.ZeroTolerance) _fRadEqTol = RhinoMath.ZeroTolerance;

                    _bIncludeArcs = optArcs.CurrentValue;
                    _bAddPt = optAddPt.CurrentValue;
                    _bAddDot = optAddDot.CurrentValue;
                    _iDotHeight = optDotHeight.CurrentValue;
                    _bAddOnlyMinimumPt = optMinPt.CurrentValue;
                    _bAddOnlyMinimumPtOfAllCrvs = optMinPtAll.CurrentValue;
                    _bEcho = optEcho.CurrentValue;
                    _bDebug = optDebug.CurrentValue;
                    
                    SaveSettings();
                }
                else
                {
                    return Result.Cancel;
                }
            }

            ObjRef[] objRefs = go.Objects();
            if (objRefs == null || objRefs.Length == 0) return Result.Cancel;

            if (!_bDebug) doc.Views.RedrawEnabled = false;
            doc.Objects.UnselectAll();

            ProcessCurves(doc, objRefs);

            doc.Views.RedrawEnabled = true;
            return Result.Success;
        }

        private void ProcessCurves(RhinoDoc doc, ObjRef[] objRefs)
        {
            var crvsFoundAll = new List<Guid>();
            var ptsPassPerCrv = new List<List<Point3d>>();
            var radiiPassPerCrv = new List<List<double>>();
            var radiiAcceptedAllCrvs = new List<double>();
            var fails = new List<string>();

            int lenCrvs = objRefs.Length;
            int step = Math.Max(1, lenCrvs / 10);

            for (int i = 0; i < lenCrvs; i++)
            {
                if (i % step == 0 && lenCrvs > 1)
                {
                    RhinoApp.SetCommandPrompt($"Processing curve {i + 1} of {lenCrvs} ...");
                }

                ObjRef objRef = objRefs[i];
                Curve crv = objRef.Curve();

                if (crv == null)
                {
                    fails.Add($"Geometry for object not found!");
                    continue;
                }

                // Delegating cleanly to the updated math class
                var minimaData = RadiusMinimaMath.GetRadiusMinima(crv, _bIncludeArcs, RhinoMath.ZeroTolerance);

                if (minimaData.Count == 0)
                {
                    fails.Add("Curve yielded no minimum radius points.");
                    continue;
                }

                var ptsPassThisCrv = new List<Point3d>();
                var radiiPassThisCrv = new List<double>();

                foreach (var data in minimaData)
                {
                    double r = data.Radius;
                    if (r == RhinoMath.UnsetValue) continue;

                    bool passMin = _fMinRadToReport <= 0.0 || r >= (_fMinRadToReport - _fRadEqTol);
                    bool passMax = _fMaxRadToReport <= 0.0 || r <= (_fMaxRadToReport + _fRadEqTol);

                    if (passMin && passMax)
                    {
                        ptsPassThisCrv.Add(data.Point);
                        radiiPassThisCrv.Add(r);
                        radiiAcceptedAllCrvs.Add(r);
                    }
                }

                if (ptsPassThisCrv.Count > 0)
                {
                    crvsFoundAll.Add(objRef.ObjectId);
                    ptsPassPerCrv.Add(ptsPassThisCrv);
                    radiiPassPerCrv.Add(radiiPassThisCrv);
                }
                else if (_fMinRadToReport > 0.0 || _fMaxRadToReport > 0.0)
                {
                    string fMsg = "No radii found";
                    if (_fMinRadToReport > 0.0) fMsg += $" above {_fMinRadToReport}";
                    if (_fMaxRadToReport > 0.0) fMsg += (_fMinRadToReport > 0.0 ? ", and" : "") + $" below {_fMaxRadToReport}";
                    fails.Add(fMsg + ".");
                }
            }

            if (ptsPassPerCrv.Count == 0)
            {
                string msg = "No minimum radii found";
                if (_fMinRadToReport > 0.0) msg += $" above {_fMinRadToReport}";
                if (_fMaxRadToReport > 0.0) msg += (_fMinRadToReport > 0.0 ? "," : "") + $" below {_fMaxRadToReport}";
                RhinoApp.WriteLine(msg + ".");
                return;
            }

            // Echo Reporting
            if (_bEcho)
            {
                if (lenCrvs == 1)
                {
                    int totalRads = radiiPassPerCrv[0].Count;
                    string s = $"{totalRads} radius minima found.";
                    if (_fMinRadToReport > 0.0 || _fMaxRadToReport > 0.0)
                    {
                        s += $" {totalRads} are";
                        if (_fMinRadToReport > 0.0) s += $" above {_fMinRadToReport}";
                        if (_fMaxRadToReport > 0.0) s += (_fMinRadToReport > 0.0 ? "," : "") + $" below {_fMaxRadToReport}";
                        s += ".";
                    }
                    s += $" Minimum is {FormatUtils.FormatDistance(radiiPassPerCrv[0].Min(), doc.ModelDistanceDisplayPrecision)}.";
                    if (fails.Count > 0) s += "\n" + fails[0];
                    RhinoApp.WriteLine(s);
                }
                else
                {
                    string s = $"Out of {lenCrvs} curves selected:";
                    var distinctFails = fails.GroupBy(x => x).Select(g => $"[{g.Count()}] {g.Key}");
                    foreach (var fail in distinctFails) s += $"\n{fail}";

                    if (_fMinRadToReport > 0.0)
                    {
                        double minAll = radiiAcceptedAllCrvs.Min();
                        double maxAll = radiiAcceptedAllCrvs.Max();
                        s += $"\n{radiiAcceptedAllCrvs.Count} radius minima [{Math.Round(minAll, doc.ModelDistanceDisplayPrecision + 1)},{Math.Round(maxAll, doc.ModelDistanceDisplayPrecision + 1)}] are less than {_fMinRadToReport}.";
                    }
                    else
                    {
                        s += $"\n{radiiAcceptedAllCrvs.Count} radius minima found.";
                    }
                    RhinoApp.WriteLine(s);
                }
            }

            // Add Geometry (Dots / Points)
            if (!_bAddPt && !_bAddDot)
            {
                SelectCurves(doc, crvsFoundAll);
                return;
            }

            var ptsToAdd = new List<Point3d>();
            var dotsToAdd = new List<TextDot>();
            double targetMinRad = double.MaxValue;
            string targetMinRadStr = "";

            if (_bAddOnlyMinimumPt && _bAddOnlyMinimumPtOfAllCrvs)
            {
                targetMinRad = radiiAcceptedAllCrvs.Min();
                targetMinRadStr = $"R{Math.Round(targetMinRad, doc.ModelDistanceDisplayPrecision)}";
            }

            for (int i = 0; i < crvsFoundAll.Count; i++)
            {
                var pts = ptsPassPerCrv[i];
                var rads = radiiPassPerCrv[i];
                double localMinRad = rads.Count > 0 ? rads.Min() : 0;

                for (int j = 0; j < rads.Count; j++)
                {
                    double r = rads[j];
                    Point3d pt = pts[j];

                    bool shouldAdd = false;
                    if (!_bAddOnlyMinimumPt)
                    {
                        shouldAdd = true;
                    }
                    else if (_bAddOnlyMinimumPtOfAllCrvs)
                    {
                        shouldAdd = Math.Abs(r - targetMinRad) <= _fRadEqTol;
                    }
                    else
                    {
                        shouldAdd = Math.Abs(r - localMinRad) <= _fRadEqTol;
                    }

                    if (shouldAdd)
                    {
                        if (_bAddPt && !ptsToAdd.Any(p => p.DistanceTo(pt) < 1e-12))
                        {
                            ptsToAdd.Add(pt);
                        }
                        if (_bAddDot)
                        {
                            string sRad = $"R{Math.Round(r, doc.ModelDistanceDisplayPrecision)}";
                            var dot = new TextDot(sRad, pt) { FontHeight = _iDotHeight };
                            dotsToAdd.Add(dot);
                        }
                    }
                }
            }

            var attrRed = new ObjectAttributes { ColorSource = ObjectColorSource.ColorFromObject, ObjectColor = System.Drawing.Color.Red };

            foreach (var dot in dotsToAdd) doc.Objects.AddTextDot(dot, attrRed);
            foreach (var pt in ptsToAdd) doc.Objects.AddPoint(pt);

            if (_bEcho)
            {
                string word = (_bAddPt && !_bAddDot) ? "points" : "dots";
                int count = Math.Max(ptsToAdd.Count, dotsToAdd.Count);

                if (_bAddOnlyMinimumPt && _bAddOnlyMinimumPtOfAllCrvs)
                    RhinoApp.WriteLine($"{count} {word} added at {targetMinRadStr}, minimum radius of all curves.");
                else if (_bAddOnlyMinimumPt)
                    RhinoApp.WriteLine($"{count} {word} added at minimum radius of each curve.");
                else
                    RhinoApp.WriteLine($"{count} {word} added at all radius minima.");
            }

            SelectCurves(doc, crvsFoundAll);
        }

        private void SelectCurves(RhinoDoc doc, List<Guid> ids)
        {
            foreach (var id in ids) doc.Objects.Select(id);
        }

        private void LoadSettings(RhinoDoc doc)
        {
            _fMinRadToReport = Settings.GetDouble("fMinRadToReport", _fMinRadToReport);
            _fMaxRadToReport = Settings.GetDouble("fMaxRadToReport", _fMaxRadToReport);
            _fRadEqTol = Settings.GetDouble("fRadEqTol", _fRadEqTol);
            if (_fRadEqTol < 0) _fRadEqTol = 10.0 * doc.ModelAbsoluteTolerance;

            _bIncludeArcs = Settings.GetBool("bIncludeArcs", _bIncludeArcs);
            _bAddPt = Settings.GetBool("bAddPt", _bAddPt);
            _bAddDot = Settings.GetBool("bAddDot", _bAddDot);
            _iDotHeight = Settings.GetInteger("iDotHeight", _iDotHeight);
            _bAddOnlyMinimumPt = Settings.GetBool("bAddOnlyMinimumPt", _bAddOnlyMinimumPt);
            _bAddOnlyMinimumPtOfAllCrvs = Settings.GetBool("bAddOnlyMinimumPtOfAllCrvs", _bAddOnlyMinimumPtOfAllCrvs);
            _bEcho = Settings.GetBool("bEcho", _bEcho);
            _bDebug = Settings.GetBool("bDebug", _bDebug);
        }

        private void SaveSettings()
        {
            Settings.SetDouble("fMinRadToReport", _fMinRadToReport);
            Settings.SetDouble("fMaxRadToReport", _fMaxRadToReport);
            Settings.SetDouble("fRadEqTol", _fRadEqTol);
            Settings.SetBool("bIncludeArcs", _bIncludeArcs);
            Settings.SetBool("bAddPt", _bAddPt);
            Settings.SetBool("bAddDot", _bAddDot);
            Settings.SetInteger("iDotHeight", _iDotHeight);
            Settings.SetBool("bAddOnlyMinimumPt", _bAddOnlyMinimumPt);
            Settings.SetBool("bAddOnlyMinimumPtOfAllCrvs", _bAddOnlyMinimumPtOfAllCrvs);
            Settings.SetBool("bEcho", _bEcho);
            Settings.SetBool("bDebug", _bDebug);
        }
    }
}