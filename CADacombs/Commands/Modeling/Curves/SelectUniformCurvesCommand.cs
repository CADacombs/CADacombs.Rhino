using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using CADacombs.Core.Curves;

namespace CADacombs.Commands.Modeling.Curves
{
    public class SelectUniformCurvesCommand : Command
    {
        public SelectUniformCurvesCommand() { Instance = this; }
        public static SelectUniformCurvesCommand Instance { get; private set; }
        
        public override string EnglishName => "ccSelUniformNurbsCrv";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RhinoApp.SetCommandPrompt("Searching for uniform NURBS curves...");
            
            int selectedCount = 0;
            
            // Iterate through all normal, selectable objects
            var settings = new ObjectEnumeratorSettings
            {
                NormalObjects = true,
                LockedObjects = false,
                IncludeLights = false,
                IncludeGrips = false,
                ObjectTypeFilter = ObjectType.Curve
            };

            doc.Views.RedrawEnabled = false;

            foreach (var rhinoObject in doc.Objects.GetObjectList(settings))
            {
                // Skip if already selected to avoid redundant processing
                if (rhinoObject.IsSelected(false) > 0) continue;

                var curve = rhinoObject.Geometry as Curve;
                if (curve == null) continue;

                // The script strictly filters for objects whose type is exactly NurbsCurve
                if (curve.ObjectType == ObjectType.Curve && curve is NurbsCurve nc)
                {
                    if (UniformityChecker.IsUniform(nc))
                    {
                        rhinoObject.Select(true);
                        selectedCount++;
                    }
                }
            }

            doc.Views.RedrawEnabled = true;

            if (selectedCount > 0)
            {
                RhinoApp.WriteLine($"{selectedCount} curve{(selectedCount == 1 ? "" : "s")} added to selection.");
            }
            else
            {
                RhinoApp.WriteLine("No curves added to selection.");
            }

            return Result.Success;
        }
    }
}