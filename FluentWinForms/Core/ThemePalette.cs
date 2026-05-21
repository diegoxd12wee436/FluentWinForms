#nullable enable
using Microsoft.Win32;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace FluentWinForms.Core
{
    /// <summary>
    /// Molde para crear paletas de colores. 
    /// Contiene los temas por defecto (Light/Dark), pero es 100% editable por el usuario.
    /// </summary>
    public class ThemePalette
    {
        public bool IsDark { get; set; } = false;
        public string Surface { get; set; } = "#FFFFFF";
        public string SurfaceAlt { get; set; } = "#F3F3F3";
        public string Text { get; set; } = "#000000";
        public string TextMuted { get; set; } = "#555555";
        public string Border { get; set; } = "#CCCCCC";

        // Colores de Marca (Brand)
        public string Primary { get; set; } = "#0078D4";
        public string PrimaryHover { get; set; } = "#106EBE";
        public string PrimaryPress { get; set; } = "#005A9E";

        // ==========================================
        // TEMAS PREDEFINIDOS (Editables globalmente)
        // ==========================================

        private static ThemePalette _light = new ThemePalette { IsDark = false };
        private static ThemePalette _dark = new ThemePalette
        {
            IsDark = true,
            Surface = "#1E1E24",
            SurfaceAlt = "#2A2A35",
            Text = "#FFFFFF",
            TextMuted = "#AAAAAA",
            Border = "#444444",
            Primary = "#0078D4",
            PrimaryHover = "#106EBE",
            PrimaryPress = "#005A9E"
        };

        public static ThemePalette Light => _light;
        public static ThemePalette Dark => _dark;
    }

    /// <summary>
    /// Gestor global del tema. Aquí viven los colores activos de la aplicación.
    /// Escucha los cambios del sistema operativo, reacciona a velocidad luz y se auto-gestiona.
    /// </summary>
    public static class AppTheme
    {
        // 🔥 CACHÉ NATIVO: Parseo único para máximo rendimiento.
        public static bool IsDarkMode { get; private set; }
        public static Color Surface { get; private set; }
        public static Color SurfaceAlt { get; private set; }
        public static Color Text { get; private set; }
        public static Color TextMuted { get; private set; }
        public static Color Border { get; private set; }
        public static Color Primary { get; private set; }
        public static Color PrimaryHover { get; private set; }
        public static Color PrimaryPress { get; private set; }

        public static event Action? ThemeChanged;

        private static bool _isSyncingWithSystem = false;
        private static readonly object _syncLock = new object(); // Candado Anti-Choques de Hilos

        static AppTheme()
        {
            ApplyTheme(ThemePalette.Light); // Inicialización segura por defecto
        }

        public static void ApplyTheme(ThemePalette palette)
        {
            if (palette == null) return;

            IsDarkMode = palette.IsDark;
            Surface = SafeParse(palette.Surface, Color.White);
            SurfaceAlt = SafeParse(palette.SurfaceAlt, Color.WhiteSmoke);
            Text = SafeParse(palette.Text, Color.Black);
            TextMuted = SafeParse(palette.TextMuted, Color.Gray);
            Border = SafeParse(palette.Border, Color.LightGray);
            Primary = SafeParse(palette.Primary, Color.DodgerBlue);
            PrimaryHover = SafeParse(palette.PrimaryHover, Color.Blue);
            PrimaryPress = SafeParse(palette.PrimaryPress, Color.DarkBlue);

            ThemeChanged?.Invoke();
        }

        /// <summary>
        /// Fuerza la recarga del tema actual. 
        /// Útil si el usuario modificó ThemePalette.Light o ThemePalette.Dark en código.
        /// </summary>
        public static void Reload()
        {
            if (IsDarkMode) ApplyTheme(ThemePalette.Dark);
            else ApplyTheme(ThemePalette.Light);
        }

        private static Color SafeParse(string hex, Color fallback)
        {
            try { return ColorTranslator.FromHtml(hex); }
            catch { return fallback; }
        }

        public static void SetDarkMode() => ApplyTheme(ThemePalette.Dark);
        public static void SetLightMode() => ApplyTheme(ThemePalette.Light);

        // ==========================================
        // 🔄 SINCRONIZACIÓN AUTOMÁTICA CON WINDOWS
        // ==========================================

        /// <summary>
        /// Activa la sincronización en tiempo real con la configuración de Windows.
        /// Thread-Safe: Seguro para llamarse desde cualquier hilo.
        /// </summary>
        public static void SyncWithSystemTheme()
        {
            lock (_syncLock)
            {
                if (!_isSyncingWithSystem)
                {
                    SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                    _isSyncingWithSystem = true;
                }
            }
            ApplySystemTheme();
        }

        /// <summary>
        /// Detiene la sincronización con Windows.
        /// </summary>
        public static void StopSystemSync()
        {
            lock (_syncLock)
            {
                if (_isSyncingWithSystem)
                {
                    SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                    _isSyncingWithSystem = false;
                }
            }
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                ApplySystemTheme();
            }
        }

        private static void ApplySystemTheme()
        {
            bool isSystemDark = GetSystemThemeIsDark();

            // Solo redibuja la UI si hubo un cambio real
            if (isSystemDark && !IsDarkMode) SetDarkMode();
            else if (!isSystemDark && IsDarkMode) SetLightMode();
        }

        private static bool GetSystemThemeIsDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        var value = key.GetValue("AppsUseLightTheme");
                        if (value is int i) return i == 0; // 0 = Oscuro, 1 = Claro
                    }
                }
            }
            catch { /* Ignorar errores en Sistemas Operativos antiguos o por falta de permisos */ }
            return false;
        }

        // ==========================================
        // 🪄 MAGIA PARA FORMULARIOS (Cero esfuerzo, Cero Crash)
        // ==========================================

        /// <summary>
        /// Aplica el tema al Formulario y lo mantiene sincronizado automáticamente.
        /// Blindado contra Cross-Thread Exceptions, ObjectDisposed y Handle-Leaks.
        /// </summary>
        public static void ApplyToForm(Form form)
        {
            if (form == null || form.IsDisposed) return;

            // Pintado inicial asumiendo que estamos en el hilo de UI
            try { form.BackColor = SurfaceAlt; } catch { }

            // Función de actualización en vivo ultra-blindada
            Action themeUpdater = () =>
            {
                try
                {
                    // Validamos que la ventana no esté muerta Y que ya tenga un Handle válido
                    if (form.IsDisposed || !form.IsHandleCreated) return;

                    if (form.InvokeRequired)
                    {
                        form.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                // Segunda validación dentro del hilo UI (Previene la "Carrera de la Muerte")
                                if (!form.IsDisposed && form.IsHandleCreated)
                                {
                                    form.BackColor = SurfaceAlt;
                                }
                            }
                            catch { /* Silenciamos cualquier muerte súbita del form en este milisegundo */ }
                        }));
                    }
                    else
                    {
                        form.BackColor = SurfaceAlt;
                    }
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            };

            ThemeChanged += themeUpdater;

            // Auto-limpieza maestra al cerrar la ventana
            FormClosedEventHandler? cleanupHandler = null;
            cleanupHandler = (sender, args) =>
            {
                ThemeChanged -= themeUpdater;
                if (cleanupHandler != null)
                {
                    form.FormClosed -= cleanupHandler; // Nos desuscribimos de nuestra propia limpieza
                }
            };

            form.FormClosed += cleanupHandler;
        }
    }
}