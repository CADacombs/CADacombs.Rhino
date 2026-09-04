#r "Eto"
#pragma warning disable 1701

// Load the Core math and memory states first
#load "../CADacombs/Core/MatchSrfOptions.cs"
#load "../CADacombs/Core/NurbsMatchMath.cs"

// Then load the Logic and Command files that depend on them
#load "../CADacombs/Commands/Modeling/MatchSrfLogic.cs"
#load "../CADacombs/Commands/Modeling/MatchSrfCommand.cs"

using System;
using System.Reflection;
using Rhino;
using Rhino.Commands;

RhinoApp.WriteLine("Calling MatchSrfCommand.cs from a .csx ...");

// 1. Instantiate the master command
var cmd = new CADacombs.Commands.Modeling.MatchSrfCommand();

// 2. Use Reflection to access the protected RunCommand method safely
var runMethod = cmd.GetType().GetMethod("RunCommand", BindingFlags.NonPublic | BindingFlags.Instance);

// 3. Invoke the command
if (runMethod != null)
{
    try
    {
        runMethod.Invoke(cmd, new object[] { RhinoDoc.ActiveDoc, RunMode.Interactive });
    }
    catch (Exception ex)
    {
        RhinoApp.WriteLine($"Script Error: {ex.InnerException?.Message ?? ex.Message}");
    }
}
else
{
    RhinoApp.WriteLine("Failed to find RunCommand method. Check namespace and class definitions.");
}