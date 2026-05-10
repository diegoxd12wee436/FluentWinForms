#nullable enable
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace FluentWinForms.Core
{
    public enum FormBackdropType
    {
        None = 0,
        Mica = 2,       // Efecto Mica Windows 11
        Acrylic = 3,    // Efecto Acrílico Windows 11
        MicaAlt = 4     // Mica Alternativo
    }

    [ToolboxItem(true)]
    [DesignTimeVisible(true)]
    [ToolboxBitmap(typeof(Form))]
    [Description("Gestor avanzado del Formulario: Control de arrastre, Acrílico nativo y esquinas redondeadas HD.\nAdvanced Form Manager: Drag control, native Acrylic, and HD rounded corners.")]
    public class ModernFormManager : Component
    {
        private Form? _targetForm;
        private Control? _dragControl;
        private double _originalOpacity = 1.0;

        // 🔥 DEV-FRIENDLY: Valores perfectos por defecto
        private int _borderRadius = 12;
        private bool _transparentStyle = false;
        private FormBackdropType _backdropType = FormBackdropType.None;
        private bool _autoSetBlackBackground = true;

        // ==========================================
        // 🏗️ CONSTRUCTORES
        // ==========================================
        public ModernFormManager() { }
        public ModernFormManager(IContainer container) { container?.Add(this); }

        // ==========================================
        // 🎛️ PROPIEDADES LÓGICAS Y VISUALES
        // ==========================================

        [Category("Modern Form")]
        [Description("El formulario que este componente va a controlar.\nThe form that this component will control.")]
        public Form? TargetForm
        {
            get => _targetForm;
            set
            {
                if (_targetForm != null)
                {
                    _targetForm.ResizeBegin -= TargetForm_ResizeBegin;
                    _targetForm.ResizeEnd -= TargetForm_ResizeEnd;
                    _targetForm.HandleCreated -= TargetForm_HandleCreated;
                    _targetForm.MouseDown -= AutoDrag_MouseDown;
                    _targetForm.Resize -= TargetForm_Resize;
                    _targetForm.Paint -= TargetForm_Paint_Hybrid;
                }

                _targetForm = value;

                if (_targetForm != null)
                {
                    _targetForm.ResizeBegin += TargetForm_ResizeBegin;
                    _targetForm.ResizeEnd += TargetForm_ResizeEnd;
                    _targetForm.MouseDown += AutoDrag_MouseDown;
                    _targetForm.Resize += TargetForm_Resize;
                    _targetForm.Paint += TargetForm_Paint_Hybrid;

                    if (_targetForm.IsHandleCreated) ApplyModernEffects();
                    else _targetForm.HandleCreated += TargetForm_HandleCreated;
                }
            }
        }

        [Category("Modern Form")]
        [Description("Habilita el arrastre de la ventana al hacer clic y mover.\nEnables window dragging on click and move.")]
        public bool EnableDrag { get; set; } = true;

        [Category("Modern Form")]
        [Description("El control (Ej: Panel superior) que servirá como barra de título.\nThe control that will serve as the title bar for dragging.")]
        public Control? DragControl
        {
            get => _dragControl;
            set
            {
                if (_dragControl != null) _dragControl.MouseDown -= AutoDrag_MouseDown;
                _dragControl = value;
                if (_dragControl != null) _dragControl.MouseDown += AutoDrag_MouseDown;
            }
        }

        [Category("Modern Form - Animation")]
        [Description("Opacidad del formulario mientras se está arrastrando.\nForm opacity while dragging.")]
        public double DragOpacity { get; set; } = 1.0;

        private bool _useSkia = true;
        [Category("Modern Form - Engine")]
        [Description("Usa SkiaSharp para dibujar un borde HD perfecto que oculte el pixelado de Windows.\nUses SkiaSharp to draw a perfect HD border hiding Windows pixelation.")]
        public bool UseSkia
        {
            get => _useSkia;
            set { _useSkia = value; _targetForm?.Invalidate(); }
        }

        private Color _borderColor = Color.FromArgb(40, 255, 255, 255);
        [Category("Modern Form - Visuals")]
        [Description("Color del borde HD del formulario.\nColor of the form's HD border.")]
        public Color BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; _targetForm?.Invalidate(); }
        }

        private float _borderThickness = 0f;
        [Category("Modern Form - Visuals")]
        [Description("Grosor del borde HD del formulario. Si es 0, solo suaviza el corte.\nThickness of the form's HD border. If 0, only smooths the cutout.")]
        public float BorderThickness
        {
            get => _borderThickness;
            set { _borderThickness = Math.Max(0, value); _targetForm?.Invalidate(); }
        }

        [Category("Modern Form - Visuals")]
        [Description("Tipo de fondo nativo de Windows 11. (Mica, Acrylic, etc.).\nNative Windows 11 backdrop type (Mica, Acrylic, etc.).")]
        public FormBackdropType BackdropType
        {
            get => _backdropType;
            set
            {
                _backdropType = value;
                if (_backdropType != FormBackdropType.None) _transparentStyle = false;

                UpdateFormBackground();
                ApplyModernEffects();
                if (DesignMode) _targetForm?.Refresh();
                else _targetForm?.Invalidate();
            }
        }

        [Category("Modern Form - Visuals")]
        [Description("Fuerza el color negro en el fondo automáticamente. ¡VITAL para ver el Mica/Acrylic!\nAutomatically forces a black background. VITAL for Mica/Acrylic to be visible!")]
        public bool AutoSetBlackBackground
        {
            get => _autoSetBlackBackground;
            set
            {
                _autoSetBlackBackground = value;
                UpdateFormBackground();
            }
        }

        [Category("Modern Form - Visuals")]
        [Description("Usa TransparencyKey. ADVERTENCIA: Rompe el Anti-Alias. Se desactiva si usas Mica/Acrylic.\nUses TransparencyKey. WARNING: Breaks Anti-Alias. Disabled if using Mica/Acrylic.")]
        public bool TransparentStyle
        {
            get => _transparentStyle;
            set
            {
                _transparentStyle = value;
                if (_transparentStyle) _backdropType = FormBackdropType.None;

                UpdateFormBackground();
                if (DesignMode) _targetForm?.Refresh();
                else _targetForm?.Invalidate();
            }
        }

        [Category("Modern Form - Visuals")]
        [Description("Radio del borde (Estilo custom). Si es 0, usa el redondeo nativo de Windows 11.\nCustom border radius. If 0, uses native Windows 11 rounding.")]
        public int BorderRadius
        {
            get => _borderRadius;
            set
            {
                _borderRadius = Math.Max(0, value);
                ApplyCustomRegion();
                if (DesignMode) _targetForm?.Refresh();
                else _targetForm?.Invalidate();
            }
        }

        [Category("Modern Form - Visuals")]
        [Description("Forzar esquinas redondeadas nativas de Windows 11 (Solo aplica si BorderRadius es 0).\nForces native Windows 11 rounded corners.")]
        public bool UseModernRoundedCorners { get; set; } = true;

        [Category("Modern Form - Visuals")]
        [Description("Fuerza el modo oscuro en la barra de título del Formulario.\nForces dark mode on the Form's title bar.")]
        public bool ForceDarkModeTitleBar { get; set; } = true;

        // ==========================================
        // ⚙️ LÓGICA CORE: PINTURA Y REGIONES
        // ==========================================

        private void TargetForm_HandleCreated(object? sender, EventArgs e) => ApplyModernEffects();

        private void TargetForm_Paint_Hybrid(object? sender, PaintEventArgs e)
        {
            if (_targetForm == null || _borderRadius <= 0) return;

            bool hasNativeCorners = BackdropType != FormBackdropType.None && Environment.OSVersion.Version.Build >= 22000;
            if (hasNativeCorners && _borderThickness <= 0) return;

            if (DesignMode)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float drawThick = _borderThickness > 0 ? _borderThickness : 1.5f;
                Color drawColor = _borderThickness > 0 ? _borderColor : Color.DarkGray;

                using (var path = CreateRoundedPath(new RectangleF(drawThick / 2, drawThick / 2, _targetForm.Width - drawThick - 1f, _targetForm.Height - drawThick - 1f), _borderRadius))
                using (var pen = new Pen(drawColor, drawThick) { DashStyle = _borderThickness > 0 ? DashStyle.Solid : DashStyle.Dash })
                {
                    e.Graphics.DrawPath(pen, path);
                }
                return;
            }

            if (UseSkia)
            {
                try { PaintSkiaSmoothing(e.Graphics); }
                catch { PaintGdiSmoothing(e.Graphics); }
            }
            else
            {
                PaintGdiSmoothing(e.Graphics);
            }
        }

        private SKBitmap? _skBorderBitmap;
        private Bitmap? _gdiBorderBitmap;
        private SKPaint? _skSharedPaint;

        private int _lastWidth, _lastHeight, _lastRadius;
        private Color _lastColor;
        private float _lastThickness;

        private void PaintSkiaSmoothing(Graphics g)
        {
            if (_targetForm == null) return;
            int w = _targetForm.Width;
            int h = _targetForm.Height;
            if (w <= 0 || h <= 0) return;

            float actualThickness = _borderThickness > 0 ? _borderThickness : 2f;
            Color actualColor = _borderThickness > 0 ? _borderColor : _targetForm.BackColor;

            if (_skBorderBitmap == null || _gdiBorderBitmap == null || _lastWidth != w || _lastHeight != h ||
                _lastRadius != _borderRadius || _lastColor != actualColor || Math.Abs(_lastThickness - actualThickness) > 0.01f)
            {
                SafeDispose(ref _gdiBorderBitmap);
                SafeDispose(ref _skBorderBitmap);

                _skBorderBitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var canvas = new SKCanvas(_skBorderBitmap))
                {
                    canvas.Clear(SKColors.Transparent);

                    if (_skSharedPaint == null)
                    {
                        _skSharedPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
                    }

                    _skSharedPaint.Color = actualColor.ToSKColor();
                    _skSharedPaint.StrokeWidth = actualThickness + 1f;

                    float inset = (actualThickness + 1f) / 2f;
                    SKRect drawRect = new SKRect(inset, inset, w - inset, h - inset);
                    float rad = Math.Max(0, _borderRadius - inset);

                    canvas.DrawRoundRect(drawRect, rad, rad, _skSharedPaint);
                }

                using (var skImage = SKImage.FromBitmap(_skBorderBitmap))
                using (var data = skImage.Encode(SKEncodedImageFormat.Png, 100))
                using (var ms = new MemoryStream(data.ToArray()))
                {
                    _gdiBorderBitmap = new Bitmap(ms);
                }

                _lastWidth = w; _lastHeight = h; _lastRadius = _borderRadius;
                _lastColor = actualColor; _lastThickness = actualThickness;
            }

            if (_gdiBorderBitmap != null)
            {
                var oldComposite = g.CompositingMode;
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImageUnscaled(_gdiBorderBitmap, 0, 0);
                g.CompositingMode = oldComposite;
            }
        }

        private void PaintGdiSmoothing(Graphics g)
        {
            if (_targetForm == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            int w = _targetForm.Width;
            int h = _targetForm.Height;
            float actualThickness = _borderThickness > 0 ? _borderThickness : 2f;
            Color actualColor = _borderThickness > 0 ? _borderColor : _targetForm.BackColor;

            float inset = (actualThickness + 1f) / 2f;
            float rad = Math.Max(0, _borderRadius - inset);

            using (var path = CreateRoundedPath(new RectangleF(inset, inset, w - actualThickness - 1f, h - actualThickness - 1f), rad))
            using (var pen = new Pen(actualColor, actualThickness + 1f))
            {
                g.DrawPath(pen, path);
            }
        }

        private GraphicsPath CreateRoundedPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2;
            if (d <= 0) return path;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void UpdateFormBackground()
        {
            if (_targetForm == null || DesignMode) return;

            if (BackdropType != FormBackdropType.None && Environment.OSVersion.Version.Build >= 22000)
            {
                _targetForm.TransparencyKey = Color.Empty;

                if (_autoSetBlackBackground)
                {
                    _targetForm.BackColor = Color.Black;
                }
            }
            else if (_transparentStyle)
            {
                _targetForm.BackColor = Color.Fuchsia;
                _targetForm.TransparencyKey = Color.Fuchsia;
            }
            else
            {
                _targetForm.TransparencyKey = Color.Empty;
            }
        }

        private void TargetForm_ResizeBegin(object? sender, EventArgs e)
        {
            if (_targetForm != null && EnableDrag && DragOpacity < 1.0)
            {
                _originalOpacity = _targetForm.Opacity;
                _targetForm.Opacity = DragOpacity;
            }
        }

        private void TargetForm_ResizeEnd(object? sender, EventArgs e)
        {
            if (_targetForm != null && EnableDrag && DragOpacity < 1.0) _targetForm.Opacity = _originalOpacity;
        }

        private void TargetForm_Resize(object? sender, EventArgs e) => ApplyCustomRegion();

        private void ApplyCustomRegion()
        {
            if (_targetForm == null || DesignMode) return;

            if (BackdropType != FormBackdropType.None && Environment.OSVersion.Version.Build >= 22000)
            {
                _targetForm.Region = null;
                return;
            }

            if (_borderRadius > 0)
            {
                try
                {
                    int diameter = _borderRadius * 2;
                    IntPtr ptr = NativeMethods.CreateRoundRectRgn(0, 0, _targetForm.Width, _targetForm.Height, diameter, diameter);
                    _targetForm.Region = Region.FromHrgn(ptr);
                    NativeMethods.DeleteObject(ptr);
                }
                catch { }
            }
            else
            {
                _targetForm.Region = null;
            }
        }

        private void AutoDrag_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _targetForm != null && EnableDrag)
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(_targetForm.Handle, NativeMethods.WM_NCLBUTTONDOWN, NativeMethods.HT_CAPTION, 0);
            }
        }

        // ==========================================
        // 🔥 INYECCIÓN: EL PUENTE HACIA LA COMPOSICIÓN (ACCENT POLICY)
        // ==========================================
        private void EnableAcrylic()
        {
            if (_targetForm == null || DesignMode) return;

            // Color de tinte para el acrílico (ARGB). Blanco semi-transparente que sugirió el experto.
            int gradientColor = (0x40 << 24) | (255 << 16) | (255 << 8) | 255;

            var accent = new NativeMethods.AccentPolicy
            {
                AccentState = NativeMethods.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 0,
                GradientColor = gradientColor,
                AnimationId = 0
            };

            var accentStructSize = Marshal.SizeOf(typeof(NativeMethods.AccentPolicy));
            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new NativeMethods.WindowCompositionAttributeData
            {
                Attribute = NativeMethods.WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = accentStructSize,
                Data = accentPtr
            };

            NativeMethods.SetWindowCompositionAttribute(_targetForm.Handle, ref data);
            Marshal.FreeHGlobal(accentPtr);
        }

        // ==========================================
        // 🎨 MAGIA DE WINDOWS (DWM)
        // ==========================================
        public void ApplyModernEffects()
        {
            if (_targetForm == null || !_targetForm.IsHandleCreated || DesignMode) return;

            ApplyCustomRegion();
            UpdateFormBackground();

            bool isWindows11 = Environment.OSVersion.Version.Build >= 22000;
            bool isWindows11_22H2 = Environment.OSVersion.Version.Build >= 22621;

            if (isWindows11)
            {
                try
                {
                    int trueVal = ForceDarkModeTitleBar ? 1 : 0;
                    NativeMethods.DwmSetWindowAttribute(_targetForm.Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueVal, Marshal.SizeOf(typeof(int)));

                    if (BackdropType != FormBackdropType.None)
                    {
                        NativeMethods.MARGINS margins = new NativeMethods.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
                        NativeMethods.DwmExtendFrameIntoClientArea(_targetForm.Handle, ref margins);

                        // 🔥 INYECCIÓN: Llamada a la API secreta de composición
                        EnableAcrylic();

                        if (isWindows11_22H2)
                        {
                            int backdropValue = (int)BackdropType;
                            NativeMethods.DwmSetWindowAttribute(_targetForm.Handle, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropValue, Marshal.SizeOf(typeof(int)));
                        }
                        else
                        {
                            int enableMica = 1;
                            NativeMethods.DwmSetWindowAttribute(_targetForm.Handle, NativeMethods.DWMWA_MICA_EFFECT, ref enableMica, Marshal.SizeOf(typeof(int)));
                        }
                    }

                    if ((UseModernRoundedCorners && _borderRadius == 0) || BackdropType != FormBackdropType.None)
                    {
                        int cornerPreference = NativeMethods.DWMWCP_ROUND;
                        NativeMethods.DwmSetWindowAttribute(_targetForm.Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, Marshal.SizeOf(typeof(int)));
                    }
                }
                catch { }
            }
        }

        // ==========================================
        // 🧹 LIMPIEZA DE MEMORIA
        // ==========================================
        private void SafeDispose<T>(ref T? obj) where T : class, IDisposable
        {
            try { obj?.Dispose(); } catch { } finally { obj = null; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_targetForm != null)
                {
                    _targetForm.ResizeBegin -= TargetForm_ResizeBegin;
                    _targetForm.ResizeEnd -= TargetForm_ResizeEnd;
                    _targetForm.HandleCreated -= TargetForm_HandleCreated;
                    _targetForm.MouseDown -= AutoDrag_MouseDown;
                    _targetForm.Resize -= TargetForm_Resize;
                    _targetForm.Paint -= TargetForm_Paint_Hybrid;
                }
                if (_dragControl != null)
                {
                    _dragControl.MouseDown -= AutoDrag_MouseDown;
                }

                SafeDispose(ref _skSharedPaint);
                SafeDispose(ref _gdiBorderBitmap);
                SafeDispose(ref _skBorderBitmap);
            }
            base.Dispose(disposing);
        }

        // ==========================================
        // ⚙️ P/INVOKE NATIVO ULTRA COMPATIBLE
        // ==========================================
        private static class NativeMethods
        {
            private const string User32Lib = "user32.dll";
            private const string DwmapiLib = "dwmapi.dll";
            private const string Gdi32Lib = "gdi32.dll";

            public const int WM_NCLBUTTONDOWN = 0xA1;
            public const int HT_CAPTION = 0x2;

            public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
            public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
            public const int DWMWA_MICA_EFFECT = 1029;
            public const int DWMWCP_ROUND = 2;

            // 🔥 INYECCIÓN: Nuevas constantes y estructuras para SetWindowCompositionAttribute
            public const int ACCENT_ENABLE_BLURBEHIND = 3;
            public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

            [StructLayout(LayoutKind.Sequential)]
            public struct AccentPolicy
            {
                public int AccentState;
                public int AccentFlags;
                public int GradientColor;
                public int AnimationId;
            }

            public enum WindowCompositionAttribute
            {
                WCA_ACCENT_POLICY = 19
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct WindowCompositionAttributeData
            {
                public WindowCompositionAttribute Attribute;
                public IntPtr Data;
                public int SizeOfData;
            }

            [DllImport(User32Lib)]
            public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
            // 🔥 FIN INYECCIÓN

            [StructLayout(LayoutKind.Sequential)]
            public struct MARGINS
            {
                public int cxLeftWidth;
                public int cxRightWidth;
                public int cyTopHeight;
                public int cyBottomHeight;
            }

            [DllImport(User32Lib)]
            public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

            [DllImport(User32Lib)]
            public static extern bool ReleaseCapture();

            [DllImport(DwmapiLib)]
            public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

            [DllImport(DwmapiLib)]
            public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

            [DllImport(Gdi32Lib)]
            public static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

            [DllImport(Gdi32Lib)]
            public static extern bool DeleteObject(IntPtr hObject);
        }
    }
}