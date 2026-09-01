using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    public class DrapeCommand : Command
    {
        public DrapeCommand()
        {
            Instance = this;
        }

        public static DrapeCommand Instance { get; private set; }

        public override string EnglishName => "spb_Drape";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // Set initial SpanSpacing based on doc units if it hasn't been modified
            if (doc.ModelUnitSystem != UnitSystem.Inches && DrapeOptions.SpanSpacing == 1.0)
            {
                DrapeOptions.SpanSpacing = 25.0 * RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem);
            }

            // ---------------------------------------------------------
            // 1. SELECT TARGET BREPS / MESHES
            // ---------------------------------------------------------
            var goTargets = new GetObject();
            goTargets.SetCommandPrompt("Select target breps or meshes");
            goTargets.GeometryFilter = ObjectType.Brep | ObjectType.Mesh;
            goTargets.AcceptNumber(true, true);

            ObjRef[] targetRefs = null;

            while (true)
            {
                if (!SetupAndProcessOptions(goTargets, out GetResult resTargets))
                    continue;

                if (resTargets == GetResult.Cancel) return Result.Cancel;
                
                if (resTargets == GetResult.Object)
                {
                    targetRefs = goTargets.Objects();
                    doc.Objects.UnselectAll();
                    doc.Views.Redraw();
                    break;
                }
            }

            if (targetRefs == null || targetRefs.Length == 0) return Result.Cancel;

            // ---------------------------------------------------------
            // 2. OPTIONAL: SELECT STARTING SURFACE
            // ---------------------------------------------------------
            ObjRef startingSrfRef = null;

            if (DrapeOptions.UserProvidesStartingSrf)
            {
                var goSrf = new GetObject();
                goSrf.SetCommandPrompt("Select starting surface");
                goSrf.GeometryFilter = ObjectType.Surface;
                goSrf.DisablePreSelect();

                goSrf.SetCustomGeometryFilter((rhObject, geom, compIdx) =>
                {
                    // Ensure it wasn't selected as a target
                    foreach (var t in targetRefs)
                    {
                        if (rhObject.Id == t.ObjectId) return false;
                    }

                    if (geom is Brep brep && brep.Faces.Count == 1)
                    {
                        return IsStartingSrfSupported(brep.Faces[0].UnderlyingSurface().ToNurbsSurface());
                    }
                    if (geom is Surface srf)
                    {
                        return IsStartingSrfSupported(srf.ToNurbsSurface());
                    }
                    return false;
                });

                var resSrf = goSrf.Get();
                if (resSrf == GetResult.Cancel) return Result.Cancel;
                if (resSrf == GetResult.Object)
                {
                    startingSrfRef = goSrf.Object(0);
                    doc.Objects.UnselectAll();
                }
            }

            // ---------------------------------------------------------
            // 3. EXECUTE LOGIC
            // ---------------------------------------------------------
            return DrapeLogic.Execute(doc, targetRefs, startingSrfRef);
        }

        private bool SetupAndProcessOptions(GetObject go, out GetResult res)
        {
            go.ClearCommandOptions();

            string[] targetMissesList = { "FixToStartingSrf", "UseLowestNeighborHits", "LinearlyExtrapolateFromHits" }; //[cite: 1]

            // 1. Declare all option variables first (fixes CS1510)
            var optFlip = new OptionToggle(DrapeOptions.FlipCPlane, "NegCPlaneZAxis", "PosCPlaneZAxis"); //[cite: 1]
            var optUserSrf = new OptionToggle(DrapeOptions.UserProvidesStartingSrf, "Create", "UserProvides"); //[cite: 1]
            var optDelSrf = new OptionToggle(DrapeOptions.DeleteStartingSrf, "No", "Yes"); //[cite: 1]
            var optEcho = new OptionToggle(DrapeOptions.Echo, "No", "Yes"); //[cite: 1]
            var optDebug = new OptionToggle(DrapeOptions.Debug, "No", "Yes"); //[cite: 1]
            var optTol = new OptionDouble(DrapeOptions.Tolerance); //[cite: 1]
            var optSpanSpace = new OptionDouble(DrapeOptions.SpanSpacing); //[cite: 1]
            var optSpansBeyond = new OptionInteger(DrapeOptions.SpansBeyondEachSide); //[cite: 1]

            int idxFlip = go.AddOptionToggle("DrapeDir", ref optFlip); //[cite: 1]
            int idxTol = go.AddOptionDouble("fTolerance", ref optTol); //[cite: 1]
            int idxUserSrf = go.AddOptionToggle("StartingSrf", ref optUserSrf); //[cite: 1]
            
            int idxSpanSpace = 0;
            int idxSpansBeyond = 0;
            if (!DrapeOptions.UserProvidesStartingSrf)
            {
                idxSpanSpace = go.AddOptionDouble("SpanSpacing", ref optSpanSpace); //[cite: 1]
                idxSpansBeyond = go.AddOptionInteger("SpansBeyondEachSide", ref optSpansBeyond); //[cite: 1]
            }

            int idxTargetMisses = go.AddOptionList("TargetMisses", targetMissesList, DrapeOptions.TargetMisses); //[cite: 1]
            
            int idxDelSrf = 0;
            if (DrapeOptions.UserProvidesStartingSrf)
            {
                idxDelSrf = go.AddOptionToggle("DeleteStartingSrf", ref optDelSrf); //[cite: 1]
            }

            int idxEcho = go.AddOptionToggle("Echo", ref optEcho); //[cite: 1]
            int idxDebug = go.AddOptionToggle("Debug", ref optDebug); //[cite: 1]

            res = go.GetMultiple(1, 0); // Allow multiple selection[cite: 1]

            if (res == GetResult.Number)
            {
                if (DrapeOptions.UserProvidesStartingSrf)
                {
                    RhinoApp.WriteLine("Numeric input ignored."); //[cite: 1]
                }
                else
                {
                    double val = go.Number();
                    if (val > 10.0 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance) //[cite: 1]
                        DrapeOptions.SpanSpacing = val;
                    else
                        RhinoApp.WriteLine("Invalid input for tolerance."); //[cite: 1]
                }
                return false;
            }

            if (res == GetResult.Option) //[cite: 2]
            {
                var opt = go.Option(); //[cite: 2]
                
                // 2. Read from the Option variables' CurrentValue property (fixes CS1061)
                if (opt.Index == idxFlip) DrapeOptions.FlipCPlane = optFlip.CurrentValue;
                else if (opt.Index == idxTol) DrapeOptions.Tolerance = Math.Max(RhinoMath.ZeroTolerance, optTol.CurrentValue); //[cite: 1]
                else if (opt.Index == idxUserSrf) DrapeOptions.UserProvidesStartingSrf = optUserSrf.CurrentValue;
                else if (!DrapeOptions.UserProvidesStartingSrf && opt.Index == idxSpanSpace)
                {
                    if (optSpanSpace.CurrentValue > 10.0 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance) //[cite: 1]
                        DrapeOptions.SpanSpacing = optSpanSpace.CurrentValue;
                }
                else if (!DrapeOptions.UserProvidesStartingSrf && opt.Index == idxSpansBeyond) DrapeOptions.SpansBeyondEachSide = optSpansBeyond.CurrentValue;
                else if (opt.Index == idxTargetMisses) DrapeOptions.TargetMisses = opt.CurrentListOptionIndex; //[cite: 1]
                else if (DrapeOptions.UserProvidesStartingSrf && opt.Index == idxDelSrf) DrapeOptions.DeleteStartingSrf = optDelSrf.CurrentValue;
                else if (opt.Index == idxEcho) DrapeOptions.Echo = optEcho.CurrentValue; //[cite: 1]
                else if (opt.Index == idxDebug) DrapeOptions.Debug = optDebug.CurrentValue; //[cite: 1]

                return false; // Loop continues[cite: 2]
            }

            return true; // Break loop[cite: 2]
        }

        private bool IsStartingSrfSupported(NurbsSurface ns)
        {
            if (ns == null) return false;
            if (ns.Degree(0) != 3 || ns.Degree(1) != 3) return false;
            if (ns.IsClosed(0) || ns.IsClosed(1)) return false;

            // Check for interior knots with multiplicity > 1
            for (int iDir = 0; iDir < 2; iDir++)
            {
                var knots = iDir == 1 ? ns.KnotsV : ns.KnotsU;
                int degree = ns.Degree(iDir);
                int iK = ns.IsPeriodic(iDir) ? 0 : degree;
                int count = ns.IsPeriodic(iDir) ? knots.Count : knots.Count - degree;
                
                while (iK < count)
                {
                    DrapeLogic.CheckEscape();
                    if (knots.KnotMultiplicity(iK) > 1) return false;
                    iK++;
                }
            }
            return true;
        }
    }
}