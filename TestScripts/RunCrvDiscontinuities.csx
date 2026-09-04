#r "Eto"
#pragma warning disable 1701
#load "../CADacombs/Core/Curves/ContinuityUtils.cs"
#load "../CADacombs/Commands/Analysis/Curves/FindCurveDiscontinuitiesCommand.cs"

using System;
using System.Reflection;
using Rhino;
using Rhino.Commands;

RhinoApp.WriteLine("Loading FindCrvDiscontinuities...");
var cmd = new CADacombs.Commands.Analysis.Curves.FindCurveDiscontinuitiesCommand();
var runMethod = cmd.GetType().GetMethod("RunCommand", BindingFlags.NonPublic | BindingFlags.Instance);
runMethod?.Invoke(cmd, new object[] { RhinoDoc.ActiveDoc, RunMode.Interactive });