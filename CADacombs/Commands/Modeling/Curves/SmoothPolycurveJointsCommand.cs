using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using CADacombs.Core.Curves;
using CADacombs.Core.Reporting; 

namespace CADacombs.Commands.Modeling.Curves
{
    [System.Runtime.InteropServices.Guid("f8a2b1c3-d4e5-4f6a-9b8c-7d6e5f4a3b2c")]
    public class SmoothCrvJointsCommand : Command
    {
        public SmoothCrvJointsCommand() { Instance = this; }
        public static SmoothCrvJointsCommand Instance { get; private set; }

        public override string EnglishName => "ccSmoothPolycrvJoints";

        private readonly string[] _continuityOptions = { "G1", "G2WithG1Fallback", "G2Only" };

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            SmoothCrvJointsOptions.Load(this.Settings);

            var go = new GetObject();
            go.SetCommandPrompt("Select polycurves to smooth joints");
            go.GeometryFilter = ObjectType.Curve;
            go.SubObjectSelect = false;
            go.GroupSelect = true;
            go.AcceptNumber(true, true);

            go.SetCustomGeometryFilter((RhinoObject rhObj, GeometryBase geom, ComponentIndex compIdx) =>
            {
                if (rhObj.ObjectType == ObjectType.InstanceReference) return false;
                return geom is PolyCurve;
            });

            while (true)
            {
                go.ClearCommandOptions();

                int optContinuity = go.AddOptionList("Continuity", _continuityOptions, SmoothCrvJointsOptions.ContinuityMode);
                
                var optMinKink = new OptionDouble(SmoothCrvJointsOptions.MinKinkAngle);
                var optSkip = new OptionToggle(SmoothCrvJointsOptions.SkipIfAlreadyAtContinuity, "Process", "Skip");
                var optSkipAngle = new OptionDouble(SmoothCrvJointsOptions.SkipAngleTolDeg);
                var optSkipCurv = new OptionDouble(SmoothCrvJointsOptions.SkipCurvTolPct);

                var optLimitDev = new OptionToggle(SmoothCrvJointsOptions.LimitCrvDev, "No", "Yes");
                var optDevTol = new OptionDouble(SmoothCrvJointsOptions.DevTol);
                var optAction = new OptionToggle(SmoothCrvJointsOptions.DeleteInput, "No", "Yes");
                var optEcho = new OptionToggle(SmoothCrvJointsOptions.Echo, "No", "Yes");
                var optDebug = new OptionToggle(SmoothCrvJointsOptions.Debug, "No", "Yes");

                go.AddOptionDouble("MinKinkAngle", ref optMinKink);
                go.AddOptionToggle("IfAlreadyAtContinuity", ref optSkip);
                
                if (SmoothCrvJointsOptions.SkipIfAlreadyAtContinuity)
                {
                    go.AddOptionDouble("SkipAngleTol", ref optSkipAngle);
                    if (SmoothCrvJointsOptions.ContinuityMode >= 1)
                    {
                        go.AddOptionDouble("SkipCurvRelPct", ref optSkipCurv);
                    }
                }
                    
                go.AddOptionToggle("LimitCrvDev", ref optLimitDev);
                if (SmoothCrvJointsOptions.LimitCrvDev)
                {
                    go.AddOptionDouble("DevTol", ref optDevTol);
                }
                
                go.AddOptionToggle("DeleteInput", ref optAction);
                go.AddOptionToggle("Echo", ref optEcho);
                go.AddOptionToggle("Debug", ref optDebug);

                GetResult res = go.GetMultiple(1, 0);

                if (res == GetResult.Number)
                {
                    SmoothCrvJointsOptions.DevTol = go.Number();
                    if (SmoothCrvJointsOptions.DevTol < 0) 
                        SmoothCrvJointsOptions.DevTol = 0.1 * doc.ModelAbsoluteTolerance;
                    SmoothCrvJointsOptions.Save(this.Settings);
                    continue;
                }
                else if (res == GetResult.Option)
                {
                    var opt = go.Option();
                    if (opt.Index == optContinuity)
                    {
                        SmoothCrvJointsOptions.ContinuityMode = opt.CurrentListOptionIndex;
                    }
                    
                    SmoothCrvJointsOptions.MinKinkAngle = optMinKink.CurrentValue;
                    if (SmoothCrvJointsOptions.MinKinkAngle <= 0) 
                        SmoothCrvJointsOptions.MinKinkAngle = 5.0;

                    SmoothCrvJointsOptions.SkipIfAlreadyAtContinuity = optSkip.CurrentValue;
                    SmoothCrvJointsOptions.SkipAngleTolDeg = optSkipAngle.CurrentValue;
                    SmoothCrvJointsOptions.SkipCurvTolPct = optSkipCurv.CurrentValue;

                    SmoothCrvJointsOptions.LimitCrvDev = optLimitDev.CurrentValue;
                    SmoothCrvJointsOptions.DevTol = optDevTol.CurrentValue;
                    if (SmoothCrvJointsOptions.DevTol < 0) 
                        SmoothCrvJointsOptions.DevTol = 0.1 * doc.ModelAbsoluteTolerance;

                    SmoothCrvJointsOptions.DeleteInput = optAction.CurrentValue;
                    SmoothCrvJointsOptions.Echo = optEcho.CurrentValue;
                    SmoothCrvJointsOptions.Debug = optDebug.CurrentValue;

                    SmoothCrvJointsOptions.Save(this.Settings);
                    continue;
                }
                else if (res == GetResult.Object)
                {
                    break;
                }
                return Result.Cancel;
            }

            ObjRef[] objRefs = go.Objects();
            if (objRefs == null || objRefs.Length == 0) return Result.Cancel;

            int processedCount = 0;

            foreach (var objRef in objRefs)
            {
                PolyCurve originalPc = objRef.Curve() as PolyCurve;
                if (originalPc == null) continue;

                if (SmoothCrvJointsOptions.Debug)
                {
                    RhinoApp.WriteLine($"--- Processing PolyCurve ({originalPc.SegmentCount} Segments) ---");
                }

                Curve newCurve = PolyCurveSmoothingSolver.Solve(
                    originalPc,
                    SmoothCrvJointsOptions.ContinuityMode,
                    SmoothCrvJointsOptions.MinKinkAngle,
                    SmoothCrvJointsOptions.SkipAngleTolDeg,
                    SmoothCrvJointsOptions.SkipCurvTolPct,
                    SmoothCrvJointsOptions.DevTol,
                    SmoothCrvJointsOptions.SkipIfAlreadyAtContinuity,
                    SmoothCrvJointsOptions.LimitCrvDev,
                    SmoothCrvJointsOptions.Debug
                );

                if (newCurve != null && newCurve != originalPc)
                {
                    ProcessResults(doc, objRef, originalPc, newCurve);
                    processedCount++;
                }
                else
                {
                    if (SmoothCrvJointsOptions.Echo)
                        RhinoApp.WriteLine("No segments were replaced for this curve.");
                }
            }

            doc.Views.Redraw();
            return processedCount > 0 ? Result.Success : Result.Failure;
        }

        private void ProcessResults(RhinoDoc doc, ObjRef objRef, Curve originalCrv, Curve newCrv)
        {
            if (SmoothCrvJointsOptions.DeleteInput)
            {
                if (doc.Objects.Replace(objRef.ObjectId, newCrv))
                {
                    if (SmoothCrvJointsOptions.Echo) RhinoApp.WriteLine("Curve was replaced.");
                    doc.Objects.Select(objRef.ObjectId);
                }
            }
            else
            {
                Guid g1 = doc.Objects.AddCurve(newCrv);
                if (g1 != Guid.Empty)
                {
                    if (SmoothCrvJointsOptions.Echo) RhinoApp.WriteLine("Curve was added.");
                    doc.Objects.Select(g1);
                }
            }

            if (SmoothCrvJointsOptions.Echo || SmoothCrvJointsOptions.Debug)
            {
                int prec = doc.ModelDistanceDisplayPrecision;

                int infPre = originalCrv.InflectionPoints()?.Length ?? 0;
                int infPost = newCrv.InflectionPoints()?.Length ?? 0;

                double? radPre = RadiusMinimaMath.GetMinimumRadius(originalCrv);
                double? radPost = RadiusMinimaMath.GetMinimumRadius(newCrv);
                
                string sRadPre = radPre.HasValue ? FormatUtils.FormatDistance(radPre.Value, prec) : "(None)";
                string sRadPost = radPost.HasValue ? FormatUtils.FormatDistance(radPost.Value, prec) : "(None)";

                double maxDev = CurveMatchMath.GetMaximumDeviation(originalCrv, newCrv);
                string sDev = FormatUtils.FormatDistance(maxDev, prec);

                RhinoApp.WriteLine($"InflectionCount:{infPre}->{infPost}  MinimumRadius:{sRadPre}->{sRadPost}  Deviation:{sDev}");
            }
        }
    }
}