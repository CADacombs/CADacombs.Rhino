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
            DrapeOptions.HasRunBefore = true; 
            DrapeOptions.UserProvidesStartingSrf = true;

            if (DrapeOptions.FitMethod < 2) 
                DrapeOptions.FitMethod = 2; 

            return DrapeCommand.RunSharedCommand(doc, mode, DrapeCommandMode.Project);
        }
    }
}