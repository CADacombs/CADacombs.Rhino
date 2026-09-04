#r "Eto"
#r "System.Drawing.Primitives"
#pragma warning disable 1701

// ----------------------------------------------------
// 1. CORE MATH & UTILITIES
// ----------------------------------------------------
#load "CADacombs/Core/Curves/ContinuityUtils.cs"
#load "CADacombs/Core/Curves/SpanConversionUtils.cs"
#load "CADacombs/Core/Curves/ConvertToLineOptions.cs"
#load "CADacombs/Core/Curves/ConvertToArcOptions.cs"

// ----------------------------------------------------
// 2. LOGIC & UI
// ----------------------------------------------------
#load "CADacombs/Commands/Modeling/Curves/ConvertToLineLogic.cs"
#load "CADacombs/Commands/Modeling/Curves/ConvertToArcLogic.cs"
#load "CADacombs/Commands/Modeling/Curves/SimplifyCrvLogic.cs"
#load "CADacombs/Commands/Modeling/Curves/SimplifyCrvConduit.cs"
#load "CADacombs/Commands/Modeling/Curves/SimplifyCrvDialog.cs"

// ----------------------------------------------------
// 3. COMMANDS
// ----------------------------------------------------
#load "CADacombs/Commands/Modeling/Curves/SimplifyCrvCommand.cs"
#load "CADacombs/Commands/Analysis/Curves/FindCurveDiscontinuitiesCommand.cs"
#load "CADacombs/Commands/Analysis/Curves/CompareCurveContinuityCommand.cs"

using System;
using System.Reflection;
using Rhino;
using Rhino.Commands;
using Rhino.Input.Custom;

RhinoApp.WriteLine("--- CADacombs Curve Dev Router ---");

var go = new GetOption();
go.SetCommandPrompt("Select command to test");
int optSimplify = go.AddOption("SimplifyCrv");
int optFindDisc = go.AddOption("spb_CrvDiscontinuities");
int optGCon = go.AddOption("spb_GCon");

var getResult = go.Get();

if (getResult == Rhino.Input.GetResult.Option)
{
    var opt = go.Option();
    Command cmdToRun = null;

    if (opt.Index == optSimplify) cmdToRun = new CADacombs.Commands.Modeling.Curves.SimplifyCrvCommand();
    else if (opt.Index == optFindDisc) cmdToRun = new CADacombs.Commands.Analysis.Curves.FindCurveDiscontinuitiesCommand();
    else if (opt.Index == optGCon) cmdToRun = new CADacombs.Commands.Analysis.Curves.CompareCurveContinuityCommand();

    if (cmdToRun != null)
    {
        var runMethod = cmdToRun.GetType().GetMethod("RunCommand", BindingFlags.NonPublic | BindingFlags.Instance);
        if (runMethod != null)
        {
            try { runMethod.Invoke(cmdToRun, new object[] { RhinoDoc.ActiveDoc, RunMode.Interactive }); }
            catch (Exception ex) { RhinoApp.WriteLine($"Script Error: {ex.InnerException?.Message ?? ex.Message}"); }
        }
    }
}