#nullable enable
using System;
using System.Buffers;
using System.Diagnostics;
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
    public enum BlurRenderState
    {
        HighPerformance,
        PowerSaving,
        Idle
    }

    public sealed class Direct2DBlurHelper : IDisposable
    {
        // ================================================================
        // P/INVOKE
        // ================================================================
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hDst, int xDst, int yDst, int w, int h, IntPtr hSrc, int xSrc, int ySrc, int rop);
        [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, int rop);
        [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hobj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);
        [DllImport("user32.dll")] private static extern uint SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
        private const int SRCCOPY = 0x00CC0020;
        private const int HALFTONE = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER { public uint biSize; public int biWidth; public int biHeight; public ushort biPlanes; public ushort biBitCount; public uint biCompression; public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter; public uint biClrUsed; public uint biClrImportant; }

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
        private ID2D1SolidColorBrush? _tintBrush;

        private IntPtr _dibBitmap = IntPtr.Zero;
        private IntPtr _dibBits = IntPtr.Zero;
        private IntPtr _dibDc = IntPtr.Zero;
        private int _dibW, _dibH, _rtW, _rtH;

        // ================================================================
        // BACKDROP CPU (Bug A)
        // El blur vive en el HWND del form, pero los controles hijos son
        // HWNDs opacos y no pueden verlo. Este productor entrega una copia
        // difuminada de baja resolución para que el form la exponga como
        // BackgroundImage y WinForms la propague a los hijos.
        // ================================================================
        private IntPtr _bdDc = IntPtr.Zero;
        private IntPtr _bdBitmap = IntPtr.Zero;
        private IntPtr _bdBits = IntPtr.Zero;
        private int _bdW, _bdH;

        private IntPtr _bdFullDc = IntPtr.Zero;
        private IntPtr _bdFullBitmap = IntPtr.Zero;
        private IntPtr _bdFullBits = IntPtr.Zero;
        private int _bdFullW, _bdFullH;

        private byte[]? _bdWork;
        private long _bdLastTicks;
        private long _bdLastSignature;
        private int _bdInFlight;
        private static int _dumpDone;
        private int _bdLastRadius;
        private Action<byte[], int, int>? _backdropSink;
        private volatile bool _backdropEnabled;

        private float _blurAmount = 20f;
        private volatile bool _running;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        // 🔥 CACHÉ DE DELEGADO Y COORDENADAS (Para no generar basura en el Invoke)
        private int _currentW, _currentH, _currentX, _currentY;
        private readonly Action _updateBoundsAction;

        public BlurRenderState CurrentState { get; set; } = BlurRenderState.HighPerformance;

        public float BlurAmount
        {
            get => _blurAmount;
            set
            {
                _blurAmount = Math.Max(1f, Math.Min(40f, value));
                // Cambiar la intensidad debe verse ya: forzamos el próximo frame.
                Interlocked.Exchange(ref _bdLastTicks, 0);
                Interlocked.Exchange(ref _bdLastSignature, 0);
                lock (_renderLock)
                    _blurEffect?.SetValue(0, _blurAmount);
            }
        }

        public Direct2DBlurHelper(Form form)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
            _factory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);

            // 🔥 CREAMOS EL DELEGADO UNA SOLA VEZ
            _updateBoundsAction = () =>
            {
                if (!_form.IsDisposed)
                {
                    _currentW = _form.Width;
                    _currentH = _form.Height;
                    _currentX = _form.Left;
                    _currentY = _form.Top;
                }
            };
        }

        public void Start()
        {
            if (_running || _form.IsDisposed) return;
            _running = true;
            _cts = new CancellationTokenSource();

            SetWindowDisplayAffinity(_form.Handle, WDA_EXCLUDEFROMCAPTURE);

            Thread renderThread = new Thread(() => RenderLoop(_cts.Token))
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "Direct2DBlurRenderThread"
            };
            renderThread.Start();
        }

        private void RenderLoop(CancellationToken token)
        {
            while (_running && !token.IsCancellationRequested)
            {
                ExecuteRender();

                int delay = CurrentState switch
                {
                    BlurRenderState.HighPerformance => 8,
                    BlurRenderState.PowerSaving => 33,
                    BlurRenderState.Idle => 1000,
                    _ => 16
                };

                try
                {
                    if (token.WaitHandle.WaitOne(delay)) break;
                }
                catch { break; }
            }
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

        public void ForceImmediateRender(bool isDragging = false)
        {
            if (!_running || _form.IsDisposed || !_form.IsHandleCreated || !_form.Visible) return;
            CurrentState = isDragging ? BlurRenderState.PowerSaving : BlurRenderState.HighPerformance;
            ExecuteRender();
        }

        private void ExecuteRender()
        {
            try
            {
                if (_form.IsDisposed || !_form.IsHandleCreated) return;

                // 🔥 USAMOS EL DELEGADO CACHEADO (0 Bytes de RAM consumidos)
                if (_form.InvokeRequired)
                {
                    _form.Invoke(_updateBoundsAction);
                }
                else
                {
                    _updateBoundsAction.Invoke();
                }

                if (_currentW > 0 && _currentH > 0 && _form.Visible)
                {
                    RenderFrame(_currentW, _currentH, _currentX, _currentY);
                }
            }
            catch (ObjectDisposedException) { _running = false; }
            catch { }
        }

        private void EnsureDib(int w, int h)
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
        }

        private void EnsureDeviceResources(int w, int h)
        {
            if (_hwndTarget != null && (_rtW != w || _rtH != h))
            {
                _hwndTarget.Resize(new SizeI(w, h));
                _rtW = w; _rtH = h;

                _bgBitmap?.Dispose(); _bgBitmap = null;
                _tintBrush?.Dispose(); _tintBrush = null; // Limpiar para recrear con nuevo tamaño
            }

            if (_hwndTarget == null)
            {
                var rtProps = new RenderTargetProperties(
                    new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied));

                var hwndProps = new HwndRenderTargetProperties
                {
                    Hwnd = _form.Handle,
                    PixelSize = new SizeI(w, h),
                    PresentOptions = PresentOptions.None
                };

                _hwndTarget = _factory!.CreateHwndRenderTarget(rtProps, hwndProps);
                _rtW = w; _rtH = h;

                _context = _hwndTarget.QueryInterface<ID2D1DeviceContext>();

                IntPtr effectPtr = _context.CreateEffect(EffectGuids.GaussianBlur);
                _blurEffect = new ID2D1Effect(effectPtr);
                _blurEffect.SetValue(0, _blurAmount);

                _blurEffect.SetValue(1, 0); // Optimization = Speed
                _blurEffect.SetValue(2, 0); // BorderMode = Soft
            }

            if (_bgBitmap == null)
            {
                var bmpProps = new BitmapProperties(
                    new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied));

                _bgBitmap = _hwndTarget!.CreateBitmap(new SizeI(w, h), IntPtr.Zero, (uint)(w * 4), bmpProps);
            }

            // Pincel único en memoria
            if (_tintBrush == null && _context != null)
            {
                _tintBrush = _context.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 0.05f));
            }
        }

        private void RenderFrame(int w, int h, int x, int y)
        {
            lock (_renderLock)
            {
                if (_factory == null || _disposed) return;

                // ============================================================
                // CAMINO ÚNICO (Bug A): cuando el sink de backdrop está activo,
                // el formulario pinta el blur como su BackgroundImage. Si D2D
                // siguiera dibujando en el HWND habría DOS superficies de
                // vidrio distintas (la de la ventana y la de cada hijo) y por
                // eso el blur de los botones no coincidía con el de la ventana.
                // Con un solo escritor, ventana y halos comparten el mismo
                // bitmap: una única superficie continua.
                // ============================================================
                if (_backdropEnabled)
                {
                    if (!BackdropDue()) return;

                    EnsureDib(w, h);
                    if (_dibDc == IntPtr.Zero || _dibBits == IntPtr.Zero) return;

                    IntPtr cpuScreenDc = GetDC(IntPtr.Zero);
                    BitBlt(_dibDc, 0, 0, w, h, cpuScreenDc, x, y, SRCCOPY);
                    ReleaseDC(IntPtr.Zero, cpuScreenDc);

                    TryProduceBackdrop(w, h);
                    return; // nunca dibujamos en el HWND en este modo
                }

                EnsureDib(w, h);
                EnsureDeviceResources(w, h);
                if (_context == null || _bgBitmap == null || _blurEffect == null || _tintBrush == null) return;

                IntPtr screenDc = GetDC(IntPtr.Zero);
                BitBlt(_dibDc, 0, 0, w, h, screenDc, x, y, SRCCOPY);
                ReleaseDC(IntPtr.Zero, screenDc);

                _bgBitmap.CopyFromMemory(_dibBits, (uint)(w * 4));

                _context.BeginDraw();
                _context.Clear(new Color4(0f, 0f, 0f, 1f));

                _blurEffect.SetInput(0, _bgBitmap, true);

                _context.DrawImage(_blurEffect);

                // 🔥 CORRECCIÓN: Usamos el _tintBrush que ya existe. ¡Adiós a los 120 objetos/segundo!
                _context.FillRectangle(new Rect(0, 0, w, h), _tintBrush);

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
        // BACKDROP CPU — API consumida por ModernFormManager
        // ================================================================
        public void SetBackdropSink(Action<byte[], int, int>? sink)
        {
            _backdropSink = sink;
            _backdropEnabled = sink != null;
            _bdLastSignature = 0;
            if (sink == null) Interlocked.Exchange(ref _bdInFlight, 0);
        }

        /// <summary>
        /// Cadencia del backdrop. La calibra el consumidor con el coste real de
        /// repintar la ventana: repintar de más satura el hilo UI.
        /// </summary>
        public int BackdropIntervalMs { get; set; } = 60;

        /// <summary>Devuelve el buffer entregado al sink. El manager DEBE llamarlo.</summary>
        public void ReleaseBackdropBuffer(byte[]? buffer)
        {
            if (buffer != null) ArrayPool<byte>.Shared.Return(buffer);
            Interlocked.Exchange(ref _bdInFlight, 0);
        }

        /// <summary>
        /// Control de cadencia del backdrop. Se evalúa UNA vez por frame y es
        /// la única fuente de throttle, para que el camino CPU y el GPU no
        /// compitan por los mismos recursos.
        /// </summary>
        private bool BackdropDue()
        {
            if (!_backdropEnabled) return false;
            if (Volatile.Read(ref _bdInFlight) != 0) return false;

            long now = Stopwatch.GetTimestamp();
            long last = Interlocked.Read(ref _bdLastTicks);
            int interval = Math.Max(16, BackdropIntervalMs);
            if (last != 0 && (now - last) * 1000L / Stopwatch.Frequency < interval) return false;

            Interlocked.Exchange(ref _bdLastTicks, now);
            return true;
        }

        private void TryProduceBackdrop(int w, int h)
        {
            if (!_backdropEnabled) return;
            Action<byte[], int, int>? sink = _backdropSink;
            if (sink == null || _dibDc == IntPtr.Zero) return;

            // Resolución adaptativa: apuntamos a ~640 px de ancho. Un blur es
            // invariante al detalle, así que bajar resolución no se nota y el
            // coste de CPU queda acotado en pantallas grandes.
            int d = Math.Max(2, (int)Math.Ceiling(w / 640.0));
            int sw = Math.Max(1, w / d);
            int sh = Math.Max(1, h / d);
            EnsureBackdropResources(sw, sh);
            if (_bdBits == IntPtr.Zero || _bdWork == null) return;

            // Downscale con GDI: HALFTONE promedia el bloque, ideal para un blur.
            SetStretchBltMode(_bdDc, HALFTONE);
            if (!StretchBlt(_bdDc, 0, 0, sw, sh, _dibDc, 0, 0, w, h, SRCCOPY)) return;

            int bytes = sw * sh * 4;
            Marshal.Copy(_bdBits, _bdWork, 0, bytes);

            // El motor D2D usa desviación estándar (sigma) en DIPs. StackBlur usa
            // radio de caja en píxeles físicos: r ≈ sigma·√3·(dpi/96).
            // Ambas superficies deben partir del MISMO BlurAmount o la ventana y
            // los halos vuelven a diferir.
            double dpiScale = _form.DeviceDpi / 96.0;
            int radius = (int)Math.Round(_blurAmount * dpiScale * 1.7320508 / d);
            radius = Math.Max(1, Math.Min(128, radius));
            _bdLastRadius = radius;
            StackBlurUltra.BlurInPlace(_bdWork, sw, sh, radius);
            ApplyBackdropTint(_bdWork, sw * sh);

            // Si el backdrop no cambió (ventana quieta, escritorio quieto) no hay
            // nada que repintar: entregar un frame idéntico solo cuesta CPU.
            long signature = ComputeSignature(_bdWork, bytes);
            if (signature == _bdLastSignature) return;
            _bdLastSignature = signature;

            // Diagnóstico (solo con FWF_DUMP_BACKDROP=1): volcar el primer backdrop a PNG.
            if (Environment.GetEnvironmentVariable("FWF_DUMP_BACKDROP") == "1")
                DumpBackdropOnce(sw, sh, bytes);

            EmitFullSizeBackdrop(_bdWork, sw, sh, w, h, bytes);
        }

        /// <summary>
        /// Sube el blur de baja resolución al tamaño real de la ventana. El
        /// BackgroundImage a tamaño real se pinta con un blit 1:1 (~2 ms) en
        /// lugar del reescalado HQ bicúbico de WinForms (~7 ms por repintado).
        /// </summary>
        private void EmitFullSizeBackdrop(byte[] small, int sw, int sh, int w, int h, int smallBytes)
        {
            Action<byte[], int, int>? sink = _backdropSink;
            if (sink == null) return;

            EnsureFullBackdrop(w, h);
            if (_bdFullDc == IntPtr.Zero || _bdFullBits == IntPtr.Zero) return;

            Marshal.Copy(small, 0, _bdBits, smallBytes);

            SetStretchBltMode(_bdFullDc, HALFTONE);
            if (!StretchBlt(_bdFullDc, 0, 0, w, h, _bdDc, 0, 0, sw, sh, SRCCOPY)) return;

            int fullBytes = w * h * 4;
            byte[] handoff = ArrayPool<byte>.Shared.Rent(fullBytes);
            Marshal.Copy(_bdFullBits, handoff, 0, fullBytes);

            Interlocked.Exchange(ref _bdInFlight, 1);
            try { sink(handoff, w, h); }
            catch
            {
                ArrayPool<byte>.Shared.Return(handoff);
                Interlocked.Exchange(ref _bdInFlight, 0);
            }
        }

        private void EnsureFullBackdrop(int w, int h)
        {
            if (_bdFullW == w && _bdFullH == h && _bdFullBits != IntPtr.Zero) return;

            CleanupFullBackdrop();
            _bdFullW = w; _bdFullH = h;

            IntPtr screenDc = GetDC(IntPtr.Zero);
            _bdFullDc = CreateCompatibleDC(screenDc);

            BITMAPINFO bmi = default;
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = w;
            bmi.bmiHeader.biHeight = -h;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;

            _bdFullBitmap = CreateDIBSection(screenDc, ref bmi, 0u, out _bdFullBits, IntPtr.Zero, 0u);
            if (_bdFullBitmap != IntPtr.Zero) SelectObject(_bdFullDc, _bdFullBitmap);
            ReleaseDC(IntPtr.Zero, screenDc);
        }

        private void CleanupFullBackdrop()
        {
            if (_bdFullDc != IntPtr.Zero) { DeleteDC(_bdFullDc); _bdFullDc = IntPtr.Zero; }
            if (_bdFullBitmap != IntPtr.Zero) { DeleteObject(_bdFullBitmap); _bdFullBitmap = IntPtr.Zero; }
            _bdFullBits = IntPtr.Zero;
            _bdFullW = _bdFullH = 0;
        }

        /// <summary>
        /// Firma barata del buffer (muestreo disperso). Sirve para detectar si el
        /// backdrop cambió de verdad antes de gastar un repintado completo.
        /// </summary>
        private static long ComputeSignature(byte[] buffer, int bytes)
        {
            unchecked
            {
                long hash = bytes;
                int step = Math.Max(4, bytes / 512) & ~3;
                for (int i = 0; i < bytes; i += step)
                    hash = hash * 31 + buffer[i];
                return hash;
            }
        }

        private void EnsureBackdropResources(int sw, int sh)
        {
            if (_bdW == sw && _bdH == sh && _bdBits != IntPtr.Zero) return;
            CleanupBackdrop();
            _bdW = sw; _bdH = sh;

            IntPtr screenDc = GetDC(IntPtr.Zero);
            _bdDc = CreateCompatibleDC(screenDc);

            BITMAPINFO bmi = default;
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = sw;
            bmi.bmiHeader.biHeight = -sh;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;

            _bdBitmap = CreateDIBSection(screenDc, ref bmi, 0u, out _bdBits, IntPtr.Zero, 0u);
            if (_bdBitmap != IntPtr.Zero) SelectObject(_bdDc, _bdBitmap);
            ReleaseDC(IntPtr.Zero, screenDc);

            _bdWork = new byte[sw * sh * 4];
        }

        private static void ApplyBackdropTint(byte[] bgra, int pixels)
        {
            const int boost = 13; // ~5% de blanco, igual que el tinte del motor D2D
            for (int i = 0, o = 0; i < pixels; i++, o += 4)
            {
                int b = bgra[o] + boost;
                int g = bgra[o + 1] + boost;
                int r = bgra[o + 2] + boost;
                bgra[o] = (byte)(b > 255 ? 255 : b);
                bgra[o + 1] = (byte)(g > 255 ? 255 : g);
                bgra[o + 2] = (byte)(r > 255 ? 255 : r);
                bgra[o + 3] = 255;
            }
        }

        private void DumpBackdropOnce(int w, int h, int bytes)
        {
            if (Interlocked.Exchange(ref _dumpDone, 1) != 0) return;
            try
            {
                using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                try { Marshal.Copy(_bdWork!, 0, data.Scan0, bytes); }
                finally { bmp.UnlockBits(data); }

                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fwf-backdrop.png");
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);

                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fwf-backdrop-log.txt"),
                    $"{DateTime.Now:HH:mm:ss.fff} dump {w}x{h} sigma={_blurAmount:0.##} boxRadius={_bdLastRadius}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"DumpBackdropOnce: {ex.Message}");
            }
        }

        private void CleanupBackdrop()
        {
            _bdLastSignature = 0;
            if (_bdDc != IntPtr.Zero) { DeleteDC(_bdDc); _bdDc = IntPtr.Zero; }
            if (_bdBitmap != IntPtr.Zero) { DeleteObject(_bdBitmap); _bdBitmap = IntPtr.Zero; }
            _bdBits = IntPtr.Zero;
            _bdW = _bdH = 0;
            _bdWork = null;
        }

        private void ReleaseDeviceResources()
        {
            _tintBrush?.Dispose(); _tintBrush = null; // 🔥 MUY IMPORTANTE PARA EVITAR LEAKS DE GPU
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
                CleanupBackdrop();
                CleanupFullBackdrop();
                ReleaseDeviceResources();
                _factory?.Dispose();
                _factory = null;
            }
        }
    }
}