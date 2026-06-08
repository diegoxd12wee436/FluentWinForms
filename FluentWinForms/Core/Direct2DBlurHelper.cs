#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Vortice.Mathematics;
using Vortice.Direct2D1;
using Vortice.DCommon;
using Vortice.DXGI;
using AlphaMode = Vortice.DCommon.AlphaMode;

namespace FluentWinForms.Core
{
    // 🔥 ESTADOS DE ENERGÍA: El núcleo del "Live Compositor"
    public enum BlurRenderState
    {
        HighPerformance, // 120 FPS (8ms) - Foco activo, ventana moviéndose o video detrás
        PowerSaving,     // 30 FPS (33ms) - Ventana inactiva pero visible
        Idle             // 1 FPS (1000ms) - Minimizada o completamente estática
    }

    public sealed class Direct2DBlurHelper : IDisposable
    {
        // ================================================================
        // P/INVOKE — Captura zero-allocation
        // ================================================================
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hDst, int xDst, int yDst, int w, int h, IntPtr hSrc, int xSrc, int ySrc, int rop);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hobj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);
        [DllImport("user32.dll")] private static extern uint SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
        private const int SRCCOPY = 0x00CC0020;

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth; public int biHeight;
            public ushort biPlanes; public ushort biBitCount; public uint biCompression;
            public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter;
            public uint biClrUsed; public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }

        // ================================================================
        // MOTOR DIRECT2D
        // ================================================================
        private readonly Form _form;
        private readonly object _renderLock = new object();

        private ID2D1Factory1? _factory;
        private ID2D1HwndRenderTarget? _hwndTarget;
        private ID2D1DeviceContext? _context;
        private ID2D1Bitmap? _bgBitmap;
        private ID2D1Effect? _blurEffect;

        // Buffer DIB
        private IntPtr _dibBitmap = IntPtr.Zero;
        private IntPtr _dibBits = IntPtr.Zero;
        private IntPtr _dibDc = IntPtr.Zero;
        private int _dibW, _dibH;
        private int _rtW, _rtH;

        private float _blurAmount = 20f;
        private volatile bool _running;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        // ================================================================
        // API PÚBLICA & CONTROL DE RENDIMIENTO
        // ================================================================

        // 🔥 PROPIEDAD DE ESTADO: Cambia los FPS al vuelo
        public BlurRenderState CurrentState { get; set; } = BlurRenderState.HighPerformance;

        public float BlurAmount
        {
            get => _blurAmount;
            set
            {
                _blurAmount = Math.Max(1f, Math.Min(40f, value));
                lock (_renderLock)
                    _blurEffect?.SetValue(0, _blurAmount);
            }
        }

        public Direct2DBlurHelper(Form form)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
            _factory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
        }

        public void Start()
        {
            if (_running || _form.IsDisposed) return;
            _running = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            SetWindowDisplayAffinity(_form.Handle, WDA_EXCLUDEFROMCAPTURE);

            // BUCLE INTELIGENTE (Adaptive Polling)
            Task.Run(async () =>
            {
                while (_running && !token.IsCancellationRequested)
                {
                    ExecuteRender();

                    // Ajuste de FPS basado en el estado
                    int delay = CurrentState switch
                    {
                        BlurRenderState.HighPerformance => 8,  // ~120 FPS
                        BlurRenderState.PowerSaving => 33,     // ~30 FPS
                        BlurRenderState.Idle => 1000,          // 1 FPS
                        _ => 16
                    };

                    try
                    {
                        await Task.Delay(delay, token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException) { break; }
                }
            }, token);
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            _cts?.Cancel();
            try
            {
                if (!_form.IsDisposed && _form.IsHandleCreated)
                    SetWindowDisplayAffinity(_form.Handle, WDA_NONE);
            }
            catch { }
        }

        // 🔥 MÉTODO TRIGGER: Llama a este método desde el WndProc del Form
        // cuando reciba WM_WINDOWPOSCHANGED o WM_SIZE para refresco instantáneo.
        public void ForceImmediateRender()
        {
            if (!_running || _form.IsDisposed || !_form.IsHandleCreated || !_form.Visible) return;
            ExecuteRender();
        }

        private void ExecuteRender()
        {
            int w = 0, h = 0, x = 0, y = 0;
            try
            {
                if (_form.IsDisposed || !_form.IsHandleCreated) return;

                if (_form.InvokeRequired)
                {
                    _form.Invoke(new Action(() =>
                    {
                        if (_form.IsDisposed) return;
                        w = _form.Width; h = _form.Height;
                        x = _form.Left; y = _form.Top;
                    }));
                }
                else
                {
                    w = _form.Width; h = _form.Height;
                    x = _form.Left; y = _form.Top;
                }

                if (w > 0 && h > 0 && _form.Visible)
                {
                    RenderFrame(w, h, x, y);
                }
            }
            catch (ObjectDisposedException) { _running = false; }
            catch { }
        }

        // ================================================================
        // PIPELINE DE RENDER DIRECT2D
        // ================================================================
        private void EnsureResources(int w, int h)
        {
            if (_dibW != w || _dibH != h || _dibBitmap == IntPtr.Zero)
            {
                CleanupDIB();
                _dibW = w; _dibH = h;

                IntPtr screenDc = GetDC(IntPtr.Zero);
                _dibDc = CreateCompatibleDC(screenDc);

                BITMAPINFO bmi = default;
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = w;
                bmi.bmiHeader.biHeight = -h;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0;

                _dibBitmap = CreateDIBSection(screenDc, ref bmi, 0u, out _dibBits, IntPtr.Zero, 0u);
                SelectObject(_dibDc, _dibBitmap);
                ReleaseDC(IntPtr.Zero, screenDc);
            }

            if (_hwndTarget != null && (_rtW != w || _rtH != h))
            {
                _hwndTarget.Resize(new SizeI(w, h));
                _rtW = w; _rtH = h;
                _bgBitmap?.Dispose();
                _bgBitmap = null;
            }

            if (_hwndTarget == null)
            {
                var rtProps = new RenderTargetProperties(
                    new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied));

                var hwndProps = new HwndRenderTargetProperties
                {
                    Hwnd = _form.Handle,
                    PixelSize = new SizeI(w, h),
                    PresentOptions = PresentOptions.Immediately
                };

                _hwndTarget = _factory!.CreateHwndRenderTarget(rtProps, hwndProps);
                _rtW = w; _rtH = h;

                _context = _hwndTarget.QueryInterface<ID2D1DeviceContext>();

                IntPtr effectPtr = _context.CreateEffect(EffectGuids.GaussianBlur);
                _blurEffect = new ID2D1Effect(effectPtr);
                _blurEffect.SetValue(0, _blurAmount);
            }

            if (_bgBitmap == null)
            {
                var bmpProps = new BitmapProperties(
                    new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied));

                _bgBitmap = _hwndTarget!.CreateBitmap(new SizeI(w, h), IntPtr.Zero, (uint)(w * 4), bmpProps);
            }
        }

        private void RenderFrame(int w, int h, int x, int y)
        {
            lock (_renderLock)
            {
                if (_factory == null || _disposed) return;

                EnsureResources(w, h);
                if (_context == null || _bgBitmap == null || _blurEffect == null) return;

                IntPtr screenDc = GetDC(IntPtr.Zero);
                BitBlt(_dibDc, 0, 0, w, h, screenDc, x, y, SRCCOPY);
                ReleaseDC(IntPtr.Zero, screenDc);

                _bgBitmap.CopyFromMemory(_dibBits, (uint)(w * 4));

                _context.BeginDraw();
                _context.Clear(new Color4(0f, 0f, 0f, 1f));

                _blurEffect.SetInput(0, _bgBitmap, true);

                using var output = _blurEffect.Output;
                if (output != null)
                    _context.DrawImage(output);

                using var tint = _context.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 0.05f));
                _context.FillRectangle(new Rect(0, 0, w, h), tint);

                try
                {
                    _context.EndDraw();
                }
                catch
                {
                    ReleaseDeviceResources();
                }
            }
        }

        // ================================================================
        // LIMPIEZA
        // ================================================================
        private void ReleaseDeviceResources()
        {
            _blurEffect?.Dispose(); _blurEffect = null;
            _bgBitmap?.Dispose(); _bgBitmap = null;
            _context?.Dispose(); _context = null;
            _hwndTarget?.Dispose(); _hwndTarget = null;
            _rtW = 0; _rtH = 0;
        }

        private void CleanupDIB()
        {
            if (_dibDc != IntPtr.Zero) { DeleteDC(_dibDc); _dibDc = IntPtr.Zero; }
            if (_dibBitmap != IntPtr.Zero) { DeleteObject(_dibBitmap); _dibBitmap = IntPtr.Zero; }
            _dibBits = IntPtr.Zero;
            _dibW = 0; _dibH = 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            lock (_renderLock)
            {
                CleanupDIB();
                ReleaseDeviceResources();
                _factory?.Dispose();
                _factory = null;
            }
        }
    }
}