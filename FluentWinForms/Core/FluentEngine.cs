#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;

// 🔥 FIX DE COMPATIBILIDAD MULTI-TARGET (Resuelve la advertencia CS0436)
// Solo inyectamos este atributo si el framework es más viejo que .NET 5 (ej. .NET 4.8). 
// .NET 8 ya lo trae de fábrica, así que lo ignora mágicamente.
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}
#endif

namespace FluentWinForms.Core
{
    /// <summary>
    /// El motor de arranque fantasma. 
    /// Se auto-ejecuta cuando la DLL se carga en memoria (Plug & Play real).
    /// </summary>
    public static class FluentEngine
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(int dpiFlag);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private static bool _isInitialized = false;

        // 🔥 ESTA ETIQUETA HACE QUE SE EJECUTE SOLO AL INSTALAR EL NUGET
        [System.Runtime.CompilerServices.ModuleInitializer]
        public static void Initialize()
        {
            if (_isInitialized) return;

            ForceHighDpiAwareness();

            // Iniciamos el tracker del Tema de Windows automáticamente en segundo plano
            AppTheme.SyncWithSystemTheme();

            _isInitialized = true;
            Trace.WriteLine("[FluentWinForms] Motor auto-inicializado en Alto Rendimiento (Plug & Play).");
        }

        private static void ForceHighDpiAwareness()
        {
            try
            {
                // Intentamos activar "PerMonitorV2" (Soporte Retina para Windows 10/11)
                // -4 equivale a DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
                if (!SetProcessDpiAwarenessContext(-4))
                {
                    // Fallback para Windows antiguos (Windows 7 / 8)
                    SetProcessDPIAware();
                }
            }
            catch
            {
                // Silenciamos si hay restricciones extremas en el SO del usuario
            }
        }
    }
}