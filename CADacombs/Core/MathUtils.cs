using System;

namespace CADacombs.Core
{
    public static class MathUtils
    {
        public static bool AreEpsilonEqual(double a, double b, double epsilon)
        {
            double delta = Math.Abs(a - b);
            double maxAbs = Math.Max(Math.Abs(a), Math.Abs(b));
            
            if (maxAbs == 0.0) return true; 

            double relComp = delta / maxAbs;
            return relComp < epsilon;
        }
    }
}