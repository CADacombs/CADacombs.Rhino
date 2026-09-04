#r "Eto"
#pragma warning disable 1701

#load "../CADacombs/Core/Curves/ContinuityUtils.cs"
#load "../CADacombs/Core/Curves/SpanConversionUtils.cs"
#load "../CADacombs/Commands/Modeling/Curves/ConvertToLineOptions.cs"
#load "../CADacombs/Commands/Modeling/Curves/ConvertToLineLogic.cs"
#load "../CADacombs/Commands/Modeling/Curves/ConvertToArcOptions.cs"
#load "../CADacombs/Commands/Modeling/Curves/ConvertToArcLogic.cs"
#load "../CADacombs/Commands/Modeling/Curves/SimplifyCrvLogic.cs"
#load "../CADacombs/Commands/Modeling/Curves/SimplifyCrvConduit.cs"
#load "../CADacombs/Commands/Modeling/Curves/SimplifyCrvDialog.cs"
#load "../CADacombs/Commands/Modeling/Curves/SimplifyCrvCommand.cs"

using System;
using System.Reflection;
using Rhino;
using Rhino.Commands;

RhinoApp.WriteLine("Loading SimplifyCrv...");
var cmd = new CADacombs.Commands.Modeling.Curves.SimplifyCrvCommand();
var runMethod = cmd.GetType().GetMethod("RunCommand", BindingFlags.NonPublic | BindingFlags.Instance);
runMethod?.Invoke(cmd, new object[] { RhinoDoc.ActiveDoc, RunMode.Interactive });