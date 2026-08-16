using System;
using Rhino;
using Rhino.PlugIns;

namespace CADacombs
{
    ///<summary>
    /// The master front-door for the plugin assembly.
    /// Rhino requires exactly one class inheriting from PlugIn.
    ///</summary>
    public class CADacombsPlugin : PlugIn
    {
        public CADacombsPlugin()
        {
            Instance = this;
        }

        public static CADacombsPlugin Instance { get; private set; }
    }
}