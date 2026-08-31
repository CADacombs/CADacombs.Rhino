#r "Eto"
#pragma warning disable 1701

#load "CADacombs/Commands/CADacombsAboutCommand.cs"

using System;
using System.Reflection;
using Rhino;
using Rhino.Commands;

RhinoApp.WriteLine("Loading CADacombs About Test Environment...");

// 1. Instantiate the master command
var cmd = new CADacombs.Commands.CADacombsAboutCommand();

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