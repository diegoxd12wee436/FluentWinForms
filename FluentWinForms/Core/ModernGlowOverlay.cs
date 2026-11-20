#nullable enable
using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SkiaSharp;

namespace FluentWinForms.Core
{
    /// <summary>
    /// Fachada pública del overlay Skia para el glow CSS. Se habilita una vez,
    /// antes de mostrar los forms: <c>ModernGlowOverlay.Enabled = true;</c>.
    /// El overlay es una ventana hija layered detrás de los controles; dibuja
    /// los halos con Skia (sin GDI+) y el mouse pasa a través.
    /// </summary>
    public static class ModernGlowOverlay
    {
        private static bool _enabled;

        /// <summary>Activa el overlay en los forms que se creen después.</summary>
        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public static bool IsAttached(Control? parent)
            => parent != null && ModernSkiaGlowOverlay.IsAttached(parent);

        /// <summary>El overlay existe y está al fondo de la z-order del padre.</summary>
        public static bool IsBehindControls(Control? parent)
            => parent != null && ModernSkiaGlowOverlay.IsBehindControls(parent);

        /// <summary>Resultado del último UpdateLayeredWindow (diagnóstico).</summary>
        public static bool LastUpdateSucceeded { get; private set; } = true;

        /// <summary>Win32 error del último UpdateLayeredWindow (0 si OK).</summary>
        public static int LastUpdateError { get; private set; }

        /// <summary>Diagnóstico: controles de glow dibujados en el último render.</summary>
        public static int LastGlowControls { get; private set; }

        /// <summary>Diagnóstico: píxeles con alpha &gt; 0 en el último render.</summary>
        public static long LastPaintedPixels { get; private set; }

        /// <summary>Solo para tests: escanea el DIB en cada render (coste alto).</summary>
        public static bool EnableRenderDiagnostics { get; set; }

        public static Rectangle LastOverlayBounds { get; private set; }
        public static Point LastOverlayScreen { get; private set; }

        internal static void SetRenderStats(int controls, long painted, Rectangle bounds, Point screen)
        {
            LastGlowControls = controls;
            LastPaintedPixels = painted;
            LastOverlayBounds = bounds;
            LastOverlayScreen = screen;
        }

        internal static void SetUpdateResult(bool ok, int error)
        {
            LastUpdateSucceeded = ok;
            LastUpdateError = ok ? 0 : error;
        }

        internal static void Attach(Control parent) => ModernSkiaGlowOverlay.Attach(parent);
        internal static void Request(Control? parent) => ModernSkiaGlowOverlay.Request(parent);
    }

    /// <summary>
    /// Ventana hija layered (WS_EX_LAYERED|TRANSPARENT|NOACTIVATE) que cubre el
    /// cliente del padre y dibuja los glows con Skia. Al estar detrás de los
    /// controles, el cuerpo del botón tapa su parte del halo y los hermanos no
    /// lo borran; el borde exterior queda visible completo.
    /// </summary>
    internal sealed class ModernSkiaGlowOverlay : Control
    {
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        private const uint ULW_ALPHA = 0x00000002;

        private readonly Control _parent;
        private IntPtr _memDc;
        private IntPtr _dib;
        private IntPtr _oldBitmap;
        private IntPtr _bits;
        private int _surfaceW, _surfaceH;
        private SKSurface? _surface;
        private SKPaint? _paint;
        private int _renderQueuedFlag;

        private static readonly ConditionalWeakTable<Control, ModernSkiaGlowOverlay> Overlays = new();

        private ModernSkiaGlowOverlay(Control parent)
        {
            _parent = parent;
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Dock = DockStyle.Fill;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        internal static bool IsAttached(Control parent)
            => Overlays.TryGetValue(parent, out _);

        internal static bool IsBehindControls(Control parent)
            => Overlays.TryGetValue(parent, out ModernSkiaGlowOverlay? overlay) &&
               parent.Controls.GetChildIndex(overlay) == parent.Controls.Count - 1;

        internal static void Attach(Control parent)
        {
            if (!ModernGlowOverlay.Enabled || parent == null || parent.IsDisposed) return;
            if (Overlays.TryGetValue(parent, out _)) return;

            var overlay = new ModernSkiaGlowOverlay(parent);
            try
            {
                parent.Controls.Add(overlay);
                overlay.Dock = DockStyle.Fill;
                // Al fondo: los controles quedan por encima y tapan el cuerpo.
                parent.Controls.SetChildIndex(overlay, parent.Controls.Count - 1);
            }
            catch
            {
                try { overlay.Dispose(); } catch { }
                return;
            }

            Overlays.Add(parent, overlay);
            parent.SizeChanged += overlay.ParentResized;
            parent.Disposed += overlay.ParentDisposed;
            // Si se añaden controles después, el overlay debe seguir al fondo.
            parent.ControlAdded += overlay.ParentControlAdded;
            overlay.RequestRender();
        }

        private void ParentControlAdded(object? sender, ControlEventArgs e)
        {
            if (e.Control == this) return;
            try
            {
                _parent.Controls.SetChildIndex(this, _parent.Controls.Count - 1);
            }
            catch { }
            RequestRender();
        }

        internal static void Request(Control? parent)
        {
            if (parent == null) return;
            if (Overlays.TryGetValue(parent, out ModernSkiaGlowOverlay? overlay))
                overlay.RequestRender();
        }

        private void ParentResized(object? sender, EventArgs e)
        {
            ReleaseSurface();
            RequestRender();
        }

        private void ParentDisposed(object? sender, EventArgs e)
        {
            try { Dispose(); } catch { }
        }

        private void RequestRender()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (System.Threading.Interlocked.Exchange(ref _renderQueuedFlag, 1) == 1) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    System.Threading.Interlocked.Exchange(ref _renderQueuedFlag, 0);
                    if (!IsDisposed) RenderGlows();
                }));
            }
            catch
            {
                System.Threading.Interlocked.Exchange(ref _renderQueuedFlag, 0);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Bounds = new Rectangle(0, 0, Math.Max(1, _parent.ClientSize.Width), Math.Max(1, _parent.ClientSize.Height));
            RequestRender();
        }

        protected override void OnPaintBackground(PaintEventArgs pevent) { /* layered: lo maneja UpdateLayeredWindow */ }
        protected override void OnPaint(PaintEventArgs e) { /* idem */ }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }
            base.WndProc(ref m);
        }

        private void EnsureSurface(int w, int h)
        {
            if (_surface != null && _surfaceW == w && _surfaceH == h) return;
            ReleaseSurface();

            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = w,
                    biHeight = -h, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                    biSizeImage = 0
                }
            };

            _dib = CreateDIBSection(IntPtr.Zero, ref bmi, 0, out _bits, IntPtr.Zero, 0);
            if (_dib == IntPtr.Zero || _bits == IntPtr.Zero) return;

            _memDc = CreateCompatibleDC(IntPtr.Zero);
            _oldBitmap = SelectObject(_memDc, _dib);
            _surfaceW = w;
            _surfaceH = h;

            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            _surface = SKSurface.Create(info, _bits, w * 4);
            _paint ??= new SKPaint { IsAntialias = true };
        }

        private void RenderGlows()
        {
            if (_parent == null || _parent.IsDisposed) return;
            int w = _parent.ClientSize.Width;
            int h = _parent.ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            Bounds = new Rectangle(0, 0, w, h);
            EnsureSurface(w, h);
            if (_surface == null) return;

            SKCanvas canvas = _surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            int drawn = 0;
            Control.ControlCollection controls = _parent.Controls;
            for (int i = 0; i < controls.Count; i++)
            {
                if (controls[i] is not ModernControlBase control ||
                    control.IsDisposed || !control.Visible) continue;

                if (!control.TryGetRootGlow(out SKImage? image, out float x, out float y,
                        out float scaleX, out float scaleY, out float centerX, out float centerY))
                    continue;

                drawn++;
                canvas.Save();
                try
                {
                    if (scaleX != 1f || scaleY != 1f)
                    {
                        canvas.Translate(centerX, centerY);
                        canvas.Scale(scaleX, scaleY);
                        canvas.Translate(-centerX, -centerY);
                    }
                    canvas.DrawImage(image, x, y,
                        new SKSamplingOptions(SKFilterMode.Linear), _paint);
                }
                finally
                {
                    canvas.Restore();
                }
            }

            canvas.Flush();

            long painted = 0;
            if (ModernGlowOverlay.EnableRenderDiagnostics && _bits != IntPtr.Zero)
            {
                try
                {
                    int len = w * h * 4;
                    var buffer = new byte[len];
                    Marshal.Copy(_bits, buffer, 0, len);
                    for (int i = 3; i < len; i += 4)
                        if (buffer[i] != 0) painted++;
                }
                catch { }
            }
            UpdateLayered(w, h);
            // Medir la posición final después del update.
            ModernGlowOverlay.SetRenderStats(drawn, painted, Bounds, PointToScreen(Point.Empty));
        }

        private void UpdateLayered(int w, int h)
        {
            if (_memDc == IntPtr.Zero) return;

            // En una ventana HIJA, pptDst es relativo al cliente del padre
            // (no coordenadas de pantalla): el overlay está en (0,0) con Dock Fill.
            var ptDstPoint = new POINT { X = Left, Y = Top };
            var size = new SIZE { cx = w, cy = h };
            var ptSrc = new POINT();
            var blend = new BLENDFUNCTION
            {
                BlendOp = 0,             // AC_SRC_OVER
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = 1          // AC_SRC_ALPHA
            };

            ModernGlowOverlay.SetUpdateResult(
                UpdateLayeredWindow(Handle, IntPtr.Zero, ref ptDstPoint, ref size,
                    _memDc, ref ptSrc, 0, ref blend, ULW_ALPHA),
                Marshal.GetLastWin32Error());
        }

        private void ReleaseSurface()
        {
            _surface?.Dispose();
            _surface = null;
            if (_memDc != IntPtr.Zero)
            {
                if (_oldBitmap != IntPtr.Zero) SelectObject(_memDc, _oldBitmap);
                DeleteDC(_memDc);
                _memDc = IntPtr.Zero;
                _oldBitmap = IntPtr.Zero;
            }
            if (_dib != IntPtr.Zero)
            {
                DeleteObject(_dib);
                _dib = IntPtr.Zero;
            }
            _bits = IntPtr.Zero;
            _surfaceW = _surfaceH = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_parent != null)
                {
                    try { _parent.SizeChanged -= ParentResized; } catch { }
                    try { _parent.Disposed -= ParentDisposed; } catch { }
                    try { _parent.ControlAdded -= ParentControlAdded; } catch { }
                    Overlays.Remove(_parent);
                }
                ReleaseSurface();
                _paint?.Dispose();
                _paint = null;
            }
            base.Dispose(disposing);
        }

        #region Win32

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi,
            uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
            uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

        #endregion
    }
}
