using Rhino;
using Rhino.Commands;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    public class PullSrfCommand : Command
    {
        public PullSrfCommand() { Instance = this; }
        public static PullSrfCommand Instance { get; private set; }
        public override string EnglishName => "ccPullSrf";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            ConformSrfOptions.MappingDirection = 0; // Closest point pull
            ConformSrfOptions.Constrain1D = false;  // Allow full 3D relaxation

            return ConformSrfCommand.RunSharedCommand(doc, mode);
        }
    }
}