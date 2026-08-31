using System;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    public class MatchSrfCommand : Command
    {
        public MatchSrfCommand()
        {
            Instance = this;
        }

        public static MatchSrfCommand Instance { get; private set; }

        public override string EnglishName => "spb_MatchSrf";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // ---------------------------------------------------------
            // 1. SELECT TARGET SURFACE EDGE TO MODIFY
            // ---------------------------------------------------------
            var goToMod = new GetObject();
            goToMod.SetCommandPrompt("Select untrimmed surface edge to change");
            goToMod.GeometryFilter = ObjectType.EdgeFilter;
            goToMod.AcceptNumber(true, true);
            goToMod.DisablePreSelect();

            goToMod.SetCustomGeometryFilter((rhObject, geom, compIdx) =>
            {
                if (!(geom is BrepTrim trim)) return false;

                // Must be a natural edge (West, South, East, North)
                if (trim.IsoStatus == IsoStatus.None || trim.IsoStatus == IsoStatus.X || trim.IsoStatus == IsoStatus.Y)
                    return false;

                Brep brep = trim.Brep;
                if (brep.Faces.Count > 1) return false;

                BrepFace face = brep.Faces[0];
                if (!face.IsSurface) return false;

                Surface srf = face.UnderlyingSurface();
                if (srf.IsClosed(0) || srf.IsClosed(1)) return false;

                if (ContainsShortOrCollapsedBorders(srf, doc.ModelAbsoluteTolerance)) return false;

                return true;
            });

            ObjRef objRefToMod = null;

            while (true)
            {
                if (!SetupAndProcessOptions(goToMod, out GetResult resultToMod)) 
                    continue;

                if (resultToMod == GetResult.Cancel) return Result.Cancel;
                if (resultToMod == GetResult.Object)
                {
                    objRefToMod = goToMod.Object(0);
                    doc.Objects.UnselectAll();
                    doc.Views.Redraw();
                    break;
                }
            }

            // ---------------------------------------------------------
            // 2. SELECT REFERENCE GEOMETRY
            // ---------------------------------------------------------
            var goRef = new GetObject();
            goRef.SetCommandPrompt("Select curve or edge with which to match");
            goRef.GeometryFilter = ObjectType.Curve;
            goRef.AcceptNumber(true, true);
            goRef.DisablePreSelect();

            goRef.SetCustomGeometryFilter((rhObject, geom, compIdx) =>
            {
                if (rhObject.Id == objRefToMod.ObjectId) return false;

                if (geom is BrepEdge edge)
                {
                    int[] faceIndices = edge.AdjacentFaces();
                    if (faceIndices.Length != 1) return false;

                    Surface srf = edge.Brep.Faces[faceIndices[0]].UnderlyingSurface();
                    if (srf is PlaneSurface) return true;
                    if (srf is RevSurface) return false;
                    if (srf.IsClosed(0) || srf.IsClosed(1)) return false;
                    if (ContainsShortOrCollapsedBorders(srf, doc.ModelAbsoluteTolerance)) return false;
                    
                    return true;
                }
                else if (geom is Curve crv)
                {
                    if (crv.IsClosed) return false;
                    if (crv is PolyCurve) return true;
                    if (crv is ArcCurve)
                    {
                        RhinoApp.WriteLine("Arc curve not accepted.");
                        return false;
                    }
                    return true; // LineCurve, NurbsCurve, etc.
                }

                return false;
            });

            ObjRef objRefRef = null;

            while (true)
            {
                if (!SetupAndProcessOptions(goRef, out GetResult resultRef)) 
                    continue;

                if (resultRef == GetResult.Cancel) return Result.Cancel;
                if (resultRef == GetResult.Object)
                {
                    objRefRef = goRef.Object(0);
                    doc.Objects.UnselectAll();
                    break;
                }
            }

            // ---------------------------------------------------------
            // 3. EXECUTE LOGIC
            // ---------------------------------------------------------
            return MatchSrfLogic.Execute(doc, objRefToMod, objRefRef);
        }

        /// <summary>
        /// Local helper to set up and parse the exact same options for both GetObject loops.
        /// Returns true if the loop should break/proceed, false if the loop should 'continue' (option was clicked).
        /// </summary>
        private bool SetupAndProcessOptions(GetObject go, out GetResult res)
        {
            go.ClearCommandOptions();

            string[] modeList = { "G0", "G1", "G2" };
            string[] preserveList = { "None", "G0", "G1", "G2" };

            var optMaintainDegree = new OptionToggle(MatchSrfOptions.MaintainDegree, "Degree", "SpanCt");
            var optReplace = new OptionToggle(MatchSrfOptions.Replace, "No", "Yes");
            var optEcho = new OptionToggle(MatchSrfOptions.Echo, "No", "Yes");
            var optDebug = new OptionToggle(MatchSrfOptions.Debug, "No", "Yes");

            int idxMode = go.AddOptionList("Mode", modeList, MatchSrfOptions.Continuity);
            int idxPreserve = go.AddOptionList("PreserveOtherEnd", preserveList, MatchSrfOptions.PreserveOtherEnd);
            int idxMaintainDegree = go.AddOptionToggle("IncreaseAsNeeded", ref optMaintainDegree);
            int idxReplace = go.AddOptionToggle("Replace", ref optReplace);
            int idxEcho = go.AddOptionToggle("Echo", ref optEcho);
            int idxDebug = go.AddOptionToggle("Debug", ref optDebug);

            int idxAddRefs = 0;
            var optAddRefs = new OptionToggle(MatchSrfOptions.AddRefs, "No", "Yes");
            if (MatchSrfOptions.Debug)
            {
                idxAddRefs = go.AddOptionToggle("AddRefs", ref optAddRefs);
            }

            res = go.Get();

            if (res == GetResult.Number)
            {
                int val = (int)go.Number();
                if (val >= 0 && val <= 2) MatchSrfOptions.Continuity = val;
                else RhinoApp.WriteLine("Numeric input is invalid.");
                return false;
            }

            if (res == GetResult.Option)
            {
                var opt = go.Option();
                if (opt.Index == idxMode) MatchSrfOptions.Continuity = opt.CurrentListOptionIndex;
                else if (opt.Index == idxPreserve) MatchSrfOptions.PreserveOtherEnd = opt.CurrentListOptionIndex;
                else if (opt.Index == idxMaintainDegree) MatchSrfOptions.MaintainDegree = optMaintainDegree.CurrentValue;
                else if (opt.Index == idxReplace) MatchSrfOptions.Replace = optReplace.CurrentValue;
                else if (opt.Index == idxEcho) MatchSrfOptions.Echo = optEcho.CurrentValue;
                else if (opt.Index == idxDebug) MatchSrfOptions.Debug = optDebug.CurrentValue;
                else if (MatchSrfOptions.Debug && opt.Index == idxAddRefs) MatchSrfOptions.AddRefs = optAddRefs.CurrentValue;
                
                return false;
            }

            return true; // Proceed with Object or Cancel
        }

        private bool ContainsShortOrCollapsedBorders(Surface srf, double tol)
        {
            var pts = new Point3d[4];
            pts[0] = srf.PointAt(srf.Domain(0).T0, srf.Domain(1).T0);
            pts[1] = srf.PointAt(srf.Domain(0).T0, srf.Domain(1).T1);
            pts[2] = srf.PointAt(srf.Domain(0).T1, srf.Domain(1).T1);
            pts[3] = srf.PointAt(srf.Domain(0).T1, srf.Domain(1).T0);

            for (int i = 0; i < 3; i++)
            {
                for (int j = i + 1; j < 4; j++)
                {
                    if (pts[i].DistanceTo(pts[j]) < tol)
                        return true;
                }
            }
            return false;
        }
    }
}