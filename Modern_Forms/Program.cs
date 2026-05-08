#nullable enable
using System;
using System.Windows.Forms;

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
            // 🔥 INYECCIÓN PRO: Bifurcación de arranque para soportar Multi-targeting perfectamente
#if NETFRAMEWORK
            // Arranque clásico a prueba de balas para .NET Framework 4.8
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // (Nota: En .NET 4.8, el soporte HighDPI se debe habilitar en el archivo app.manifest)
#elif NET6_0_OR_GREATER
            // Arranque moderno para .NET 6+ (usa ApplicationConfiguration cuando está disponible)
            ApplicationConfiguration.Initialize();
#else
            // Fallback para targets donde ApplicationConfiguration no está disponible.
            // Configure High DPI and visual styles manually.
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
            }
            catch
            {
                // If SetHighDpiMode doesn't exist on the target framework, ignore.
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
#endif

            Application.Run(new Form1());
        }
    }
}