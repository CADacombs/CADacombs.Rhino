using System;
using Rhino;

namespace CADacombs.Core
{
    public class EscapeTracker : IDisposable
    {
        public bool IsCanceled { get; private set; }

        public EscapeTracker()
        {
            IsCanceled = false;
            RhinoApp.EscapeKeyPressed += OnEscapeKeyPressed;
        }

        private void OnEscapeKeyPressed(object sender, EventArgs e)
        {
            IsCanceled = true;
        }

        public void Dispose()
        {
            // Guaranteed to fire when the 'using' block ends, even if an exception occurs
            RhinoApp.EscapeKeyPressed -= OnEscapeKeyPressed;
        }
    }
}