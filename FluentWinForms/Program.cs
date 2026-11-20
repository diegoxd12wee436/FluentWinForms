#nullable enable
using System;
using System.Windows.Forms;
using FluentWinForms.Core;

namespace FluentWinForms
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            // Glow CSS con overlay Skia (detrás de los controles, sin GDI+).
            ModernGlowOverlay.Enabled = true;
            Application.Run(new Form1());
        }
    }
}
