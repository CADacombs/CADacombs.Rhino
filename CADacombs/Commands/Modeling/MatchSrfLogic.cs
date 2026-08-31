using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    public static class MatchSrfLogic
    {
        public static Result Execute(RhinoDoc doc, ObjRef objRefToMod, ObjRef objRefRef)
        {
            // 1. Extract Target Geometry
            BrepTrim trimToMod = objRefToMod.Trim();
            if (trimToMod == null)
            {
                RhinoApp.WriteLine("Target is not a valid surface edge to modify.");
                return Result.Failure;
            }

            IsoStatus sideM = trimToMod.IsoStatus;
            NurbsSurface nsM = trimToMod.Face.UnderlyingSurface().ToNurbsSurface();
            
            if (nsM.IsRational)
            {
                NurbsMatchMath.MakeNonRational(nsM);
                {
                    if (nsM.IsRational && MatchSrfOptions.Echo)
                        RhinoApp.WriteLine("Warning... Surface to modify is rational. Check results.");
                }
            }

            // 2. Determine alignment
            bool bMatchWithParamsAligned = NurbsMatchMath.AreParamsAlignedPerPickPts(objRefToMod, objRefRef);

            // 3. Delegate to Core Math
            var result = NurbsMatchMath.CreateSurface(
                nsM, 
                sideM, 
                objRefRef, 
                bMatchWithParamsAligned,
                MatchSrfOptions.Continuity, 
                MatchSrfOptions.PreserveOtherEnd, 
                MatchSrfOptions.MaintainDegree, 
                MatchSrfOptions.Echo, 
                MatchSrfOptions.Debug, 
                MatchSrfOptions.AddRefs);

            if (result.Surface == null)
            {
                RhinoApp.WriteLine("Surface could not be created.");
                return Result.Failure;
            }

            if (MatchSrfOptions.Echo)
            {
                RhinoApp.WriteLine($"Continuity was modified toward G{result.AchievedContinuity}.");
            }

            // 4. Handle Document Addition / Replacement
            if (!MatchSrfOptions.Replace)
            {
                Guid gBOut = doc.Objects.AddSurface(result.Surface);
                if (gBOut == Guid.Empty)
                    RhinoApp.WriteLine("Could not add modified surface.");
                else if (MatchSrfOptions.Echo)
                    RhinoApp.WriteLine("Surface was added.");
            }
            else
            {
                RhinoObject originalObj = objRefToMod.Object();
                Brep originalBrep = objRefToMod.Brep();
                
                if (originalBrep.Faces.Count == 1)
                {
                    Brep newBrep = result.Surface.ToBrep();
                    if (ReplaceAndPreserveModes(doc, objRefToMod.ObjectId, newBrep))
                    {
                        if (MatchSrfOptions.Echo) RhinoApp.WriteLine("Replaced monoface brep with new surface.");
                    }
                    else
                    {
                        RhinoApp.WriteLine("Could not replace monoface brep with new surface.");
                    }
                }
                else
                {
                    // Polyface Brep logic: Add the new surface, and attempt to strip the old face from the Brep
                    ObjectAttributes attr = originalObj.Attributes;
                    Guid gBOut = doc.Objects.AddSurface(result.Surface, attr);
                    
                    if (gBOut == Guid.Empty)
                    {
                        RhinoApp.WriteLine("Could not add modified surface.");
                    }
                    else
                    {
                        Brep wipBrep = originalBrep.DuplicateBrep();
                        wipBrep.Faces.RemoveAt(objRefToMod.Face().FaceIndex);
                        
                        Brep[] unionedBreps = Brep.CreateBooleanUnion(new[] { wipBrep }, doc.ModelAbsoluteTolerance);
                        wipBrep.Dispose();

                        if (unionedBreps != null && unionedBreps.Length == 1)
                        {
                            if (ReplaceAndPreserveModes(doc, objRefToMod.ObjectId, unionedBreps[0]))
                                if (MatchSrfOptions.Echo) RhinoApp.WriteLine("Added new surface and deleted face of brep.");
                            else
                                RhinoApp.WriteLine("Added new surface but could not delete face of brep.");
                        }
                        else
                        {
                            // Fallback: Delete the original Brep entirely and add the exploded pieces
                            doc.Objects.Delete(originalObj, false);
                            int pieceCount = 0;
                            if (unionedBreps != null)
                            {
                                foreach (var b in unionedBreps)
                                {
                                    doc.Objects.AddBrep(b, attr);
                                    pieceCount++;
                                }
                            }
                            if (MatchSrfOptions.Echo) 
                                RhinoApp.WriteLine($"Added new surface and deleted face of brep. Remainder is now {pieceCount} breps.");
                        }
                    }
                }
            }

            doc.Views.Redraw();
            return Result.Success;
        }

        /// <summary>
        /// Replaces the geometry while maintaining Draft Angle, Zebra, and Emap visual modes.
        /// </summary>
        public static bool ReplaceAndPreserveModes(RhinoDoc doc, Guid objId, Brep newGeom)
        {
            var obj = doc.Objects.FindId(objId);
            if (obj == null || newGeom == null) return false;

            var activeModes = new List<VisualAnalysisMode>();
            
            Guid[] knownGuids = new[] {
                VisualAnalysisMode.RhinoZebraStripeAnalysisModeId,
                VisualAnalysisMode.RhinoEmapAnalysisModeId,
                VisualAnalysisMode.RhinoDraftAngleAnalysisModeId
            };

            foreach (var guid in knownGuids)
            {
                var mode = VisualAnalysisMode.Find(guid);
                if (mode != null && obj.InVisualAnalysisMode(mode))
                {
                    activeModes.Add(mode);
                }
            }

            bool rc = doc.Objects.Replace(objId, newGeom);

            if (rc && activeModes.Count > 0)
            {
                var newObj = doc.Objects.FindId(objId);
                if (newObj != null)
                {
                    foreach (var mode in activeModes)
                        newObj.EnableVisualAnalysisMode(mode, true);
                }
            }

            return rc;
        }
    }
}