using System.Drawing;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using CADacombs.Core.Curves;

namespace CADacombs.Commands.Modeling.Curves
{
    public class MatchCrvConduit : DisplayConduit
    {
        public NurbsCurve ResultA { get; set; }
        public NurbsCurve ResultB { get; set; }
        public Color FeedbackColor { get; set; }

        public MatchCrvConduit()
        {
            FeedbackColor = Rhino.ApplicationSettings.AppearanceSettings.FeedbackColor;
        }

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            if (ResultA != null) e.IncludeBoundingBox(ResultA.GetBoundingBox(false));
            if (ResultB != null) e.IncludeBoundingBox(ResultB.GetBoundingBox(false));
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            var displayMode = e.Display.Viewport.DisplayMode;
            int crvThk = displayMode.DisplayAttributes.CurveThickness + 1;

            if (ResultA != null) e.Display.DrawCurve(ResultA, FeedbackColor, crvThk);
            if (ResultB != null) e.Display.DrawCurve(ResultB, FeedbackColor, crvThk);
        }
    }
}