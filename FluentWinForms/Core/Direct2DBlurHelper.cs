#nullable enable
using System;
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
        private volatile bool _dpiDirty;

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

        private byte[]? _bdWork;
        private readonly byte[]?[] _bdHandoffs = new byte[]?[2];
        private readonly bool[] _bdSlotInFlight = new bool[2];
        private readonly object _slotLock = new();

        private long _bdLastTicks;
        private long _bdLastSignature;
        private long _bdLastCaptureSignature;
        private int _bdInFlight;
        private long _bdLastEnqueueTicks;
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
        private EventHandler? _boundsChangedHandler;

        public BlurRenderState CurrentState { get; set; } = BlurRenderState.HighPerformance;

        public float BlurAmount
        {
            get => _blurAmount;
            set
            {
                lock (_renderLock)
                {
                    _blurAmount = Math.Max(1f, Math.Min(40f, value));
                    // Cambiar la intensidad debe verse ya: forzamos el próximo
                    // frame. También se invalida la firma de la CAPTURA, porque
                    // con el escritorio quieto el early-return no dejaría
                    // re-difuminar. Todo bajo el lock para que el hilo de render
                    // no reescriba las firmas con el frame viejo.
                    Interlocked.Exchange(ref _bdLastTicks, 0);
                    Interlocked.Exchange(ref _bdLastSignature, 0);
                    Interlocked.Exchange(ref _bdLastCaptureSignature, 0);
                    _blurEffect?.SetValue(0, _blurAmount);
                }
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

            // El worker NO puede hacer `Invoke` al hilo UI en cada iteración:
            // si la UI está pintando, el render esperaba ~20 ms y el pipeline
            // quedaba serializado. Los bounds se cachean desde eventos (hilo UI)
            // y el worker solo lee la instantánea. Se desuscriben en Dispose.
            _boundsChangedHandler = (s, e) => _updateBoundsAction();
            _form.LocationChanged += _boundsChangedHandler;
            _form.SizeChanged += _boundsChangedHandler;
            _updateBoundsAction();
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
                    BlurRenderState.HighPerformance => 4,
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

                // Sin `Invoke` al hilo UI: los bounds llegan por eventos
                // (LocationChanged/SizeChanged) y aquí solo se leen. Se leen con
                // Volatile porque los escribe el hilo UI.
                int w = Volatile.Read(ref _currentW);
                int h = Volatile.Read(ref _currentH);
                int x = Volatile.Read(ref _currentX);
                int y = Volatile.Read(ref _currentY);

                if (w > 0 && h > 0 && _form.Visible)
                {
                    RenderFrame(w, h, x, y);
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

        /// <summary>
        /// Pide un frame al worker sin bloquear al que llama. Es lo que se usa
        /// desde el hilo UI (resize, DPI): `ForceImmediateRender` ejecuta
        /// BitBlt + blur bajo el lock y bloquea la UI.
        /// </summary>
        public void RequestRender()
        {
            Interlocked.Exchange(ref _bdLastTicks, 0);
            Interlocked.Exchange(ref _bdLastCaptureSignature, 0);
        }

        /// <summary>
        /// El DPI de la ventana cambió. Los DIB, el backdrop reducido y los
        /// recursos Direct2D están atados a píxeles físicos: se liberan en el
        /// próximo frame y se recrean con el tamaño/DPI nuevos.
        /// </summary>
        public void NotifyDpiChanged()
        {
            _dpiDirty = true;
            Interlocked.Exchange(ref _bdLastTicks, 0);
            Interlocked.Exchange(ref _bdLastSignature, 0);
            Interlocked.Exchange(ref _bdLastCaptureSignature, 0);
            // NO se resetea _bdInFlight: los frames ya encolados siguen vivos y
            // resetearlo permitiría encolar más de 2 sobre los mismos slots.
            // El watchdog por encolado se encarga de un contador atascado.
        }

        private void RenderFrame(int w, int h, int x, int y)
        {
            lock (_renderLock)
            {
                if (_factory == null || _disposed) return;

                if (_dpiDirty)
                {
                    _dpiDirty = false;
                    CleanupDIB();
                    CleanupBackdrop();
                    ReleaseDeviceResources();
                }

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
            _bdLastCaptureSignature = 0;
            if (sink == null) ResetBackdropSlots();
        }

        /// <summary>
        /// Cadencia del backdrop. La calibra el consumidor con el coste real de
        /// repintar la ventana: repintar de más satura el hilo UI.
        /// </summary>
        public int BackdropIntervalMs { get; set; } = 60;

        /// <summary>Devuelve el buffer entregado al sink. El manager DEBE llamarlo.</summary>
        public void ReleaseBackdropBuffer(byte[]? buffer)
        {
            // Doble buffer: liberar un slot permite al worker preparar el
            // siguiente frame mientras la UI pinta el actual. El slot se
            // identifica por referencia; un doble release se ignora.
            if (buffer == null) return;

            lock (_slotLock)
            {
                for (int i = 0; i < _bdHandoffs.Length; i++)
                {
                    if (!ReferenceEquals(_bdHandoffs[i], buffer)) continue;
                    if (!_bdSlotInFlight[i]) return;
                    _bdSlotInFlight[i] = false;
                    if (_bdInFlight > 0) _bdInFlight--;
                    return;
                }
            }
        }

        /// <summary>Reserva un slot libre (0/1). Devuelve -1 si los dos están en vuelo.</summary>
        private int AcquireSlot()
        {
            lock (_slotLock)
            {
                for (int i = 0; i < _bdSlotInFlight.Length; i++)
                {
                    if (_bdSlotInFlight[i]) continue;
                    _bdSlotInFlight[i] = true;
                    _bdInFlight++;
                    return i;
                }
                return -1;
            }
        }

        /// <summary>Libera un slot reservado cuyo frame no llegó a entregarse.</summary>
        private void AbandonSlot(int slot)
        {
            lock (_slotLock)
            {
                if (slot < 0 || slot >= _bdSlotInFlight.Length) return;
                if (!_bdSlotInFlight[slot]) return;
                _bdSlotInFlight[slot] = false;
                if (_bdInFlight > 0) _bdInFlight--;
            }
        }

        /// <summary>Resetea la contabilidad de slots y el contador juntos.</summary>
        private void ResetBackdropSlots()
        {
            lock (_slotLock)
            {
                for (int i = 0; i < _bdSlotInFlight.Length; i++) _bdSlotInFlight[i] = false;
                _bdInFlight = 0;
            }
        }

        /// <summary>
        /// Control de cadencia del backdrop. Se evalúa UNA vez por frame y es
        /// la única fuente de throttle, para que el camino CPU y el GPU no
        /// compitan por los mismos recursos.
        /// </summary>
        private bool BackdropDue()
        {
            if (!_backdropEnabled) return false;

            if (Volatile.Read(ref _bdInFlight) >= 2)
            {
                // Watchdog: si un frame quedó encolado y su callback nunca se
                // ejecutó (handle recreado, form cerrado sin disponer), el
                // contador no debe congelar el blur para siempre. Se mide desde
                // el ENCOLADO, no desde la última liberación, para cubrir también
                // el caso de que nunca haya habido una liberación.
                long enqueued = Interlocked.Read(ref _bdLastEnqueueTicks);
                long nowTicks = Stopwatch.GetTimestamp();
                if (enqueued != 0 &&
                    (nowTicks - enqueued) * 1000L / Stopwatch.Frequency > 1000)
                {
                    ResetBackdropSlots();
                }
                else
                {
                    return false;
                }
            }

            long now = Stopwatch.GetTimestamp();
            long last = Interlocked.Read(ref _bdLastTicks);
            int interval = Math.Max(1, BackdropIntervalMs);
            if (last != 0 && (now - last) * 1000L / Stopwatch.Frequency < interval) return false;

            Interlocked.Exchange(ref _bdLastTicks, now);
            return true;
        }

        private void TryProduceBackdrop(int w, int h)
        {
            if (!_backdropEnabled) return;
            Action<byte[], int, int>? sink = _backdropSink;
            if (sink == null || _dibDc == IntPtr.Zero) return;

            // Si la captura no cambió, no hay nada que hacer: ni blur ni
            // emisión. Es lo que evita martillar la CPU con el escritorio
            // quieto en el modo normal (antes la firma se calculaba después
            // de difuminar).
            int captureBytes = w * h * 4;
            long captureSignature = ComputeSignaturePtr(_dibBits, captureBytes);
            if (captureSignature == _bdLastCaptureSignature) return;

            // Intensidad en píxeles físicos. Con sigma < 1 px no se difumina
            // (sería invisible); a partir de ahí, siempre.
            float dpiScale = _form.DeviceDpi / 96f;
            float sigma = _blurAmount * dpiScale;
            bool blur = sigma >= 1f;

            // Escala de trabajo según la intensidad: a más blur, menos detalle
            // hace falta y el coste cae (O(1) por píxel, pero menos píxeles).
            int d = !blur ? 1 : (int)Math.Min(4, Math.Max(2, Math.Round(sigma / 4.0)));
            int sw = Math.Max(1, w / d);
            int sh = Math.Max(1, h / d);

            EnsureBackdropResources(sw, sh, w, h);
            if (_bdBits == IntPtr.Zero || _bdWork == null || _dibBits == IntPtr.Zero) return;

            // Downscale (o copia 1:1) con GDI: HALFTONE promedia el bloque.
            SetStretchBltMode(_bdDc, HALFTONE);
            if (!StretchBlt(_bdDc, 0, 0, sw, sh, _dibDc, 0, 0, w, h, SRCCOPY)) return;

            int smallBytes = sw * sh * 4;
            Marshal.Copy(_bdBits, _bdWork, 0, smallBytes);

            if (blur)
            {
                // Dos pasadas de caja ≈ gaussiana (la segunda suaviza las
                // esquinas del kernel). El radio es O(1) por píxel, así que
                // subir BlurAmount no encarece el frame.
                int r = Math.Max(1, (int)Math.Round(sigma / d));
                StackBlurUltra.BlurInPlace(_bdWork!, sw, sh, r);
                StackBlurUltra.BlurInPlace(_bdWork!, sw, sh, r);
                _bdLastRadius = r;
            }

            int bytes = w * h * 4;
            if (blur)
            {
                // Volver al tamaño real: con blur ancho el remuestreo no se nota.
                Marshal.Copy(_bdWork, 0, _bdBits, smallBytes);
                SetStretchBltMode(_dibDc, HALFTONE);
                if (!StretchBlt(_dibDc, 0, 0, w, h, _bdDc, 0, 0, sw, sh, SRCCOPY)) return;
                Marshal.Copy(_dibBits, _bdWork, 0, bytes);
            }
            else
            {
                bytes = smallBytes;
            }

            // Tinte de vidrio (blanco 40), igual que el ModernGlassPanel.
            ApplyGlassTint(_bdWork, w * h);

            // Si el backdrop no cambió (ventana quieta, escritorio quieto) no hay
            // nada que repintar; pero la captura SÍ se procesó, así que se
            // compromete también aquí (si no, el próximo frame repetiría el
            // blur completo sin emitir, en bucle).
            long signature = ComputeSignature(_bdWork, bytes);
            if (signature == _bdLastSignature)
            {
                _bdLastCaptureSignature = captureSignature;
                return;
            }

            // Reservar el slot ANTES de comprometer la firma: si no hay slot,
            // el frame se reintenta en el próximo tick sin perder la señal.
            int slot = AcquireSlot();
            if (slot < 0) return;

            _bdLastSignature = signature;

            // Diagnóstico (solo con FWF_DUMP_BACKDROP=1): volcar el primer backdrop a PNG.
            if (Environment.GetEnvironmentVariable("FWF_DUMP_BACKDROP") == "1")
                DumpBackdropOnce(w, h, bytes);

            try
            {
                byte[]? handoff = _bdHandoffs[slot];
                if (handoff == null || handoff.Length < bytes || handoff.Length > (long)bytes * 4)
                {
                    handoff = new byte[bytes];
                    _bdHandoffs[slot] = handoff;
                }
                Buffer.BlockCopy(_bdWork, 0, handoff, 0, bytes);

                Interlocked.Exchange(ref _bdLastEnqueueTicks, Stopwatch.GetTimestamp());
                sink(handoff, w, h);
                // Solo se "compromete" la captura cuando el frame se entregó.
                _bdLastCaptureSignature = captureSignature;
            }
            catch
            {
                AbandonSlot(slot);
            }
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

        /// <summary>Firma barata sobre memoria nativa (captura de pantalla).</summary>
        private static long ComputeSignaturePtr(IntPtr ptr, int bytes)
        {
            unchecked
            {
                long hash = bytes;
                int step = Math.Max(4, bytes / 512) & ~3;
                for (int i = 0; i < bytes; i += step)
                    hash = hash * 31 + Marshal.ReadByte(ptr, i);
                return hash;
            }
        }

        private void EnsureBackdropResources(int sw, int sh, int fullW, int fullH)
        {
            if (_bdW != sw || _bdH != sh || _bdBits == IntPtr.Zero)
            {
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
            }

            // El buffer debe caber también el frame final a tamaño real (el
            // camino de blur fuerte trabaja a 1/2 y luego sube).
            int need = Math.Max(sw * sh, fullW * fullH) * 4;
            if (_bdWork == null || _bdWork.Length < need)
                _bdWork = new byte[need];
            else if (_bdWork.Length > (long)need * 4)
                _bdWork = new byte[need]; // encoger: no retener buffers de un resize 4K
        }

        /// <summary>Tinte de vidrio: blanco con alpha 40 (GlassTint del panel).</summary>
        private static void ApplyGlassTint(byte[] bgra, int pixels)
        {
            const int alpha = 40;
            for (int i = 0, o = 0; i < pixels; i++, o += 4)
            {
                bgra[o] = (byte)(bgra[o] + ((255 - bgra[o]) * alpha) / 255);
                bgra[o + 1] = (byte)(bgra[o + 1] + ((255 - bgra[o + 1]) * alpha) / 255);
                bgra[o + 2] = (byte)(bgra[o + 2] + ((255 - bgra[o + 2]) * alpha) / 255);
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
                    $"{DateTime.Now:HH:mm:ss.fff} dump {w}x{h} sigma={_blurAmount:0.##} rPx={_bdLastRadius}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"DumpBackdropOnce: {ex.Message}");
            }
        }

        private void CleanupBackdrop()
        {
            _bdLastSignature = 0;
            _bdLastCaptureSignature = 0;
            if (_bdDc != IntPtr.Zero) { DeleteDC(_bdDc); _bdDc = IntPtr.Zero; }
            if (_bdBitmap != IntPtr.Zero) { DeleteObject(_bdBitmap); _bdBitmap = IntPtr.Zero; }
            _bdBits = IntPtr.Zero;
            _bdW = _bdH = 0;
            // Los buffers de trabajo y los handoffs se conservan para reusarlos:
            // reasignarlos en cada cambio de tamaño generaba picos de 45 MB
            // por frame durante un resize grande. Se liberan en Dispose.

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

            if (_boundsChangedHandler != null)
            {
                try
                {
                    _form.LocationChanged -= _boundsChangedHandler;
                    _form.SizeChanged -= _boundsChangedHandler;
                }
                catch { }
                _boundsChangedHandler = null;
            }

            lock (_renderLock)
            {
                CleanupDIB();
                CleanupBackdrop();
                ReleaseDeviceResources();
                _factory?.Dispose();
                _factory = null;

                // Liberar también los buffers que CleanupBackdrop conserva.
                _bdWork = null;
                _bdHandoffs[0] = null;
                _bdHandoffs[1] = null;
            }
        }
    }
}