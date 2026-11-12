using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace CADacombs.Commands.Analysis.Curves
{
    [System.Runtime.InteropServices.Guid("d4345bb3-2e40-410a-8a1c-99c9c8695d7b")]
    public class InflectionPtsCommand : Command
    {
        public override string EnglishName => "ccInflectionPts";

        private bool _bAddPts = true;
        private bool _bSplitCrv = false;
        private bool _bDeleteInput = true;
        private bool _bEcho = true;
        private bool _bDebug = false;

        public InflectionPtsCommand()
        {
            Instance = this;
        }

        public static InflectionPtsCommand Instance { get; private set; }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            LoadSettings();

            var go = new GetObject();
            go.SetCommandPrompt("Select curves");
            go.GeometryFilter = ObjectType.Curve;
            go.SubObjectSelect = true; // To allow BrepEdge selection
            go.AcceptNumber(true, true);
            go.DeselectAllBeforePostSelect = false;
            go.EnableClearObjectsOnEntry(false);
            go.EnableUnselectObjectsOnExit(false);

            while (true)
            {
                go.ClearCommandOptions();

                var optAddPts = new OptionToggle(_bAddPts, "No", "Yes");
                var optSplit = new OptionToggle(_bSplitCrv, "No", "Yes");
                var optDelete = new OptionToggle(_bDeleteInput, "No", "Yes");
                var optEcho = new OptionToggle(_bEcho, "No", "Yes");
                var optDebug = new OptionToggle(_bDebug, "No", "Yes");

                int idxAddPts = go.AddOptionToggle("AddPts", ref optAddPts);
                int idxSplit = go.AddOptionToggle("SplitCrv", ref optSplit);
                
                int idxDelete = -1;
                if (_bSplitCrv)
                {
                    idxDelete = go.AddOptionToggle("DeleteInput", ref optDelete);
                }

                int idxEcho = go.AddOptionToggle("Echo", ref optEcho);
                int idxDebug = go.AddOptionToggle("Debug", ref optDebug);

                GetResult res = go.GetMultiple(1, 0);

                if (res == GetResult.Option)
                {
                    _bAddPts = optAddPts.CurrentValue;
                    _bSplitCrv = optSplit.CurrentValue;
                    if (_bSplitCrv) _bDeleteInput = optDelete.CurrentValue;
                    _bEcho = optEcho.CurrentValue;
                    _bDebug = optDebug.CurrentValue;
                    SaveSettings();
                    continue;
                }
                else if (res != GetResult.Object)
                {
                    return Result.Cancel;
                }

                if (go.ObjectsWerePreselected)
                {
                    go.EnablePreSelect(false, true);
                    continue; // Re-evaluate to show options on CLI
                }

                break;
            }

            ObjRef[] objRefs = go.Objects();
            if (objRefs == null || objRefs.Length == 0) return Result.Cancel;

            if (!_bDebug) doc.Views.RedrawEnabled = false;
            doc.Objects.UnselectAll();

            var newCurves = ProcessCurves(doc, objRefs);

            if (newCurves != null && newCurves.Count > 0)
            {
                foreach (var id in newCurves)
                {
                    doc.Objects.Select(id);
                }
            }

            doc.Views.RedrawEnabled = true;
            return Result.Success;
        }

        private List<Guid> ProcessCurves(RhinoDoc doc, ObjRef[] objRefs)
        {
            var replacedOrAddedCurves = new List<Guid>();
            int totalInflectionPoints = 0;
            int totalPointsAdded = 0;
            int totalCurvesSplit = 0;
            var fails = new List<string>();

            bool echoInLoop = _bEcho && objRefs.Length == 1;
            int step = Math.Max(1, objRefs.Length / 10);

            for (int i = 0; i < objRefs.Length; i++)
            {
                if (i % step == 0 && objRefs.Length > 1)
                {
                    RhinoApp.SetCommandPrompt($"Processing curve {i + 1} of {objRefs.Length} ...");
                }

                ObjRef objRef = objRefs[i];
                Curve crv = objRef.Curve();

                if (crv == null)
                {
                    fails.Add($"Geometry not found for object.");
                    continue;
                }

                bool isEdge = objRef.Geometry() is BrepEdge;
                bool canDelete = _bDeleteInput && !isEdge;

                // Native V7+ Inflection Points
                Point3d[] inflectionPts = crv.InflectionPoints();

                if (inflectionPts == null)
                {
                    string failMsg = $"Inflection calculation returned null for object. Bad input Curve?";
                    fails.Add(failMsg);
                    if (echoInLoop) RhinoApp.WriteLine(failMsg);
                    continue;
                }

                if (echoInLoop)
                {
                    RhinoApp.WriteLine($"{inflectionPts.Length} inflection(s) found.");
                }

                if (inflectionPts.Length == 0) continue;

                totalInflectionPoints += inflectionPts.Length;

                if (_bAddPts)
                {
                    foreach (Point3d pt in inflectionPts)
                    {
                        Guid ptId = doc.Objects.AddPoint(pt);
                        if (ptId != Guid.Empty) totalPointsAdded++;
                    }
                }

                if (_bSplitCrv)
                {
                    // Map points back to parameters for splitting
                    var splitParams = new List<double>();
                    foreach (Point3d pt in inflectionPts)
                    {
                        if (crv.ClosestPoint(pt, out double t))
                        {
                            splitParams.Add(t);
                        }
                    }

                    Curve[] splitCurves = crv.Split(splitParams);
                    
                    if (splitCurves == null)
                    {
                        fails.Add("Curve could not be split.");
                    }
                    else if (splitCurves.Length == 1)
                    {
                        fails.Add("Curve was split into just one curve. Inflection point must be too close to an endpoint.");
                    }
                    else
                    {
                        var attrs = canDelete && objRef.Object() != null ? objRef.Object().Attributes : null;
                        bool failOnAdd = false;
                        var tempAdded = new List<Guid>();

                        foreach (Curve splitCrv in splitCurves)
                        {
                            Guid newId = doc.Objects.AddCurve(splitCrv, attrs);
                            if (newId != Guid.Empty)
                            {
                                tempAdded.Add(newId);
                            }
                            else
                            {
                                failOnAdd = true;
                            }
                        }

                        if (!failOnAdd)
                        {
                            replacedOrAddedCurves.AddRange(tempAdded);
                            totalCurvesSplit += splitCurves.Length;

                            if (canDelete)
                            {
                                doc.Objects.Delete(objRef.ObjectId, true);
                            }
                            
                            if (echoInLoop) RhinoApp.WriteLine($"Curve was successfully split into {splitCurves.Length}.");
                        }
                        else
                        {
                            string failMsg = canDelete 
                                ? "Curve was not deleted because not all split curves could be added." 
                                : "Not all split curves could be added to the document.";
                            
                            fails.Add(failMsg);
                            if (echoInLoop) RhinoApp.WriteLine(failMsg);
                        }
                    }
                }
            }

            if (_bEcho && objRefs.Length > 1)
            {
                RhinoApp.WriteLine($"Out of {objRefs.Length} curves selected:");
                
                var distinctFails = fails.GroupBy(x => x).Select(g => $"[{g.Count()}] {g.Key}");
                foreach (var fail in distinctFails)
                {
                    RhinoApp.WriteLine(fail);
                }

                if (totalInflectionPoints > 0)
                {
                    RhinoApp.WriteLine($"{totalInflectionPoints} inflection point(s) found.");
                }

                if (_bAddPts && totalPointsAdded > 0)
                {
                    RhinoApp.WriteLine($"{totalPointsAdded} points added.");
                }

                if (_bSplitCrv)
                {
                    if (replacedOrAddedCurves.Count > 0)
                    {
                        RhinoApp.WriteLine($"{replacedOrAddedCurves.Count} curves were generated from splits.");
                    }
                    else
                    {
                        RhinoApp.WriteLine("No curves were added or replaced.");
                    }
                }
            }

            return replacedOrAddedCurves;
        }

        private void LoadSettings()
        {
            _bAddPts = Settings.GetBool("bAddPts", _bAddPts);
            _bSplitCrv = Settings.GetBool("bSplitCrv", _bSplitCrv);
            _bDeleteInput = Settings.GetBool("bDeleteInput", _bDeleteInput);
            _bEcho = Settings.GetBool("bEcho", _bEcho);
            _bDebug = Settings.GetBool("bDebug", _bDebug);
        }

        private void SaveSettings()
        {
            Settings.SetBool("bAddPts", _bAddPts);
            Settings.SetBool("bSplitCrv", _bSplitCrv);
            Settings.SetBool("bDeleteInput", _bDeleteInput);
            Settings.SetBool("bEcho", _bEcho);
            Settings.SetBool("bDebug", _bDebug);
        }
    }
}