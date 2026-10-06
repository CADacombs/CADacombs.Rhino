using Rhino;
using Rhino.Commands;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    public class ProjectSrfCommand : Command
    {
        public ProjectSrfCommand() { Instance = this; }
        public static ProjectSrfCommand Instance { get; private set; }
        public override string EnglishName => "ccProjectSrf";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            DrapeOptions.HasRunBefore = true; // Mark as run so ccDrape doesn't overwrite settings if called later

            // Force projection settings for this run
            DrapeOptions.UserProvidesStartingSrf = true;

            // If the user currently has a Drape method active, swap to Greville Projection. 
            // If they already have CP Projection active, leave it alone.
            if (DrapeOptions.FitMethod < 2) 
                DrapeOptions.FitMethod = 2; 

            return DrapeCommand.RunSharedCommand(doc, mode);
        }
    }
}