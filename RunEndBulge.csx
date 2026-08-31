#r "Eto"
#pragma warning disable 1701

// ----------------------------------------------------
// Load Core Logic
// ----------------------------------------------------
#load "CADacombs/Core/EndBulgeMath.cs"
#load "CADacombs/Core/EndBulgeOptions.cs"
#load "CADacombs/Core/EndBulgeConduit.cs"
#load "CADacombs/Core/EndBulgeDialog.cs"

// ----------------------------------------------------
// Load Command & UI Logic
// ----------------------------------------------------
#load "CADacombs/Commands/Modeling/EndBulgeCurveDialog.cs"
#load "CADacombs/Commands/Modeling/EndBulgeCurveLogic.cs"
#load "CADacombs/Commands/Modeling/EndBulgeSurfaceConduit.cs"
#load "CADacombs/Commands/Modeling/EndBulgeSurfaceDialog.cs"
#load "CADacombs/Commands/Modeling/EndBulgeSurfaceLogic.cs"
#load "CADacombs/Commands/Modeling/EndBulgeCommand.cs"

using System;
using System.Reflection;
using Rhino;
using Rhino.Commands;

RhinoApp.WriteLine("Loading CADacombs EndBulge Test Environment...");

// 1. Instantiate the master command
var cmd = new CADacombs.Commands.Modeling.EndBulgeCommand();

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