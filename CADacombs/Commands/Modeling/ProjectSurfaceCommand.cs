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
            ConformSrfOptions.MappingDirection = 1; // Z-axis raycast
            ConformSrfOptions.Constrain1D = true;   // Force 1D vertical displacement

            return ConformSrfCommand.RunSharedCommand(doc, mode);
        }
    }
}