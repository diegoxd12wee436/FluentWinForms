#nullable enable
#pragma warning disable CA1416 // Silencia la advertencia de compatibilidad de System.Drawing
#pragma warning disable IDE0090 // Silencia sugerencias de simplificar 'new'
#pragma warning disable IDE0028 // Silencia sugerencias de inicialización de colecciones

using FluentWinForms.Custom_Controls;
using SkiaSharp;
using System;
using System.Collections.Concurrent; // 🔥 INYECCIÓN: Para Caché Concurrente
using System.Collections.Specialized; // 🔥 INYECCIÓN: Para Reactividad del Árbol
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices; // 🔥 INYECCIÓN: Para CopyMemory
using System.Windows.Forms;

namespace FluentWinForms.Core
{
    public enum RenderLayer { Background, Shadow, Acrylic, Image, Ripple, Content, Overlay }

    // 🔥 INYECCIÓN PRO: Ocultamos el motor base del Toolbox para que VS no crashee
    [ToolboxItem(false)]
    public abstract partial class ModernControlBase : Control
    {
        // 🔥 INYECCIÓN PRO: Retorno al DllImport infalible (evita el error de Partial Method en el diseñador)
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
        private static extern void RtlMoveMemory(IntPtr dest, IntPtr src, uint count);

#if NETFRAMEWORK
        // 🔥 VARIABLES PARA EL "AIR-GAP" ZERO-ALLOCATION EN .NET 4.8
        private byte[]? _netFxSafeBuffer;         // Buffer reutilizable para copia segura
        private Bitmap? _netFxSafeBitmap;         // Bitmap GDI+ reutilizable
#endif

       
        // 🔥 INYECCIÓN CACHÉ PRO: Mata la fuga de memoria y soporta Cursiva (Italic)
        private static readonly ConcurrentDictionary<(string family, bool bold, bool italic), SKTypeface> _typefaceCache = new();

        protected static SKTypeface GetOrCreateTypeface(string family, bool bold, bool italic)
        {
            var key = (family, bold, italic);
            return _typefaceCache.GetOrAdd(key, k =>
            {
                return SKTypeface.FromFamilyName(k.family,
                    k.bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                    SKFontStyleWidth.Normal,
                    k.italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright); // 🔥 LA CURSIVA NACE AQUÍ
            });
        }

        // 🔥 INYECCIÓN: El nodo raíz de la UI (El Lienzo)
        private RenderNode? _visualNode;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public RenderNode? VisualNode
        {
            get => _visualNode;
            set
            {
                // 🔥 FIX REACTIVIDAD: Nos desuscribimos del viejo y nos suscribimos al nuevo
                if (_visualNode != null) _visualNode.Children.CollectionChanged -= VisualNode_ChildrenChanged;

                _visualNode = value;

                if (_visualNode != null)
                {
                    _visualNode.Children.CollectionChanged += VisualNode_ChildrenChanged;
                    FluentWinForms.Core.LayoutEngine.ComputeLayout(_visualNode, new RectangleF(0, 0, Width, Height));
                }
                RefreshVisuals();
            }
        }

        // 🔥 FIX REACTIVIDAD: Cuando el programador hace AddChild, esto acomoda todo
        private void VisualNode_ChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_visualNode != null)
            {
                FluentWinForms.Core.LayoutEngine.ComputeLayout(_visualNode, new RectangleF(0, 0, Width, Height));
                RefreshVisuals();
            }
        }
        //Desing
        public ControlBuilder<ModernControlBase> Design()
        {
            _visualNode ??= new RenderNode();

            
            var builder = new ControlBuilder<ModernControlBase>(_visualNode, this);

            builder.OnApplied = node =>
            {
                // 🔥 FIX 2: Siempre forzar ComputeLayout — sin condición
                _visualNode = node;
                FluentWinForms.Core.LayoutEngine.ComputeLayout(_visualNode, new RectangleF(0, 0, Width, Height));
                RefreshVisuals();
            };
            return builder;
        }



        #region 🔵 Escalado DPI (Nivel Producción)
        protected float _dpiScale = 1.0f;
        protected float S(float value) => value * _dpiScale;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            using (Graphics g = CreateGraphics()) { _dpiScale = g.DpiX / 96f; }
            RebuildCanvas();

            if (!AnimationManager.IsRunning) AnimationManager.Start();

            // 🔥 FIX TEMAS: Suscripción a cambios globales de AppTheme
            AppTheme.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged()
        {
            ClearCaches();
            RefreshVisuals();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            using (Graphics g = CreateGraphics()) { _dpiScale = g.DpiX / 96f; }
            ClearCaches();
            RebuildCanvas();
        }
        #endregion

        protected virtual RenderLayer[] GetRenderOrder() =>
            new[] { RenderLayer.Background, RenderLayer.Shadow, RenderLayer.Acrylic, RenderLayer.Image, RenderLayer.Ripple, RenderLayer.Content, RenderLayer.Overlay };

        #region 🔵 Propiedades Visuales Base (Híbridas)
        private Color _backgroundColor = Color.White;
        [Category("Modern - Appearance")]
        [Description("El color de fondo del control. Si 'UseGradient' está habilitado, se usará como el primer color del degradado.\nThe background color of the control. If 'UseGradient' is enabled, it will be used as the first color of the gradient.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color BackgroundColor { get => _backgroundColor; set { _backgroundColor = value; RefreshVisuals(); } }

        private Color _backgroundColor2 = Color.LightGray;
        [Category("Modern - Appearance")]
        [Description("El segundo color de fondo del control cuando 'UseGradient' está habilitado.\nThe second background color of the control when 'UseGradient' is enabled.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color BackgroundColor2 { get => _backgroundColor2; set { _backgroundColor2 = value; RefreshVisuals(); } }

        private bool _useGradient = false;
        [Category("Modern - Appearance")]
        [Description("Habilita o deshabilita el uso de degradado de fondo.\nEnable or disable the use of background gradient.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public bool UseGradient { get => _useGradient; set { _useGradient = value; ClearCaches(); RefreshVisuals(); } }

        private Color _borderColor = Color.Transparent;
        [Category("Modern - Appearance")]
        [Description("El color del borde del control. Si 'BorderThickness' esta activo .\nThe border color of the control. If 'BorderThickness' is active.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color BorderColor { get => _borderColor; set { _borderColor = value; RefreshVisuals(); } }

        private float _borderThickness = 0;
        [Category("Modern - Appearance")]
        [Description("El grosor del borde del control. Si es 0, no se dibujará ningún borde.\nThe thickness of the control's border. If 0, no border will be drawn.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public float BorderThickness { get => _borderThickness; set { _borderThickness = Math.Max(0, value); RefreshVisuals(); } }

        private float _borderRadius = 0;
        [Category("Modern - Appearance")]
        [Description("El radio de las esquinas del control. Si es 0, las esquinas serán cuadradas.\nThe radius of the control's corners. If 0, corners will be square.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public float BorderRadius
        {
            get => _borderRadius;
            set
            {
                _borderRadius = Math.Max(0, value);
                UpdateCachedClipPath();
                RefreshVisuals();
            }
        }

        private float _opacity = 1.0f;
        [Category("Modern - Appearance")]
        [Description("nivel de transparencia del control (0.0 a 1.0). \nThe transparency level of the control (0.0 to 1.0).")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public virtual float Opacity { get => _opacity; set { _opacity = Math.Max(0, Math.Min(1, value)); ClearCaches(); RefreshVisuals(); } }
        [Category("Modern -  Transform")]
        [Description("Rotación del control en grados. \nRotation of the control in degrees.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]

        public float Rotation { get => _animatedRotation; set { _animatedRotation = value; RefreshVisuals(); } }

        private float _scaleX = 1.0f; [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)][Category("Modern -  Transform")][Description("Escala del control en el eje X. \nScale of the control on the X axis.")] public float ScaleX { get => _scaleX; set { _scaleX = value; RefreshVisuals(); } }
        private float _scaleY = 1.0f; [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)][Category("Modern -  Transform")][Description("Escala del control en el eje Y. \nScale of the control on the Y axis.")] public float ScaleY { get => _scaleY; set { _scaleY = value; RefreshVisuals(); } }

        private bool _useSkiaGraphics = true;
        [Category("Modern - Engine")]
        [Description("Habilita o deshabilita el uso de SkiaSharp para renderizar el control. Deshabilitarlo puede mejorar la compatibilidad en entornos de diseño, pero reducirá la calidad visual y el rendimiento.\nEnable or disable the use of SkiaSharp for rendering the control. Disabling it may improve compatibility in design environments but will reduce visual quality and performance.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public bool UseSkiaGraphics { get => _useSkiaGraphics; set { _useSkiaGraphics = value; ClearCaches(); RefreshVisuals(); } }
        #endregion

        // Invalidación Segura
        public void InvalidateIfVisible()
        {
            if (Visible && Width > 0 && Height > 0) Invalidate();
        }

        protected void RefreshContentArea(Rectangle rect) => Invalidate(rect);

        // Buffers de Memoria
        private SKBitmap? _skBitmap;
        private SKCanvas? _skCanvas;
        private Bitmap? _gdiWrapper;

        protected SKPath? _sharedPath;
        protected SKPaint? _sharedPaint;

        protected bool _isRenderable = false;

        // 🔥 DEBOUNCE RESTAURADO: Variables para proteger la memoria en Resize
        private DateTime _lastRebuildAttempt = DateTime.MinValue;
        private const int REBUILD_DEBOUNCE_MS = 500;

        public ModernControlBase()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            DoubleBuffered = true;
            BackColor = Color.Transparent;

            InitAnimations();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (_visualNode != null)
            {
                FluentWinForms.Core.LayoutEngine.ComputeLayout(_visualNode, new RectangleF(0, 0, Width, Height));
            }

            RebuildCanvas();
            UpdateRippleBounds();
            UpdateCachedClipPath();
        }

        // Reconstrucción Híbrida Inteligente (segura)
        private void RebuildCanvas()
        {
            if (Width <= 0 || Height <= 0) { _isRenderable = false; return; }

            bool isDesignTime = LicenseManager.UsageMode == LicenseUsageMode.Designtime;

            // 🔥 DEBOUNCE RESTAURADO: Evita recrear buffers si falló recientemente
            if (!_isRenderable && (DateTime.Now - _lastRebuildAttempt).TotalMilliseconds < REBUILD_DEBOUNCE_MS)
                return;

            _lastRebuildAttempt = DateTime.Now;

            SafeDispose(ref _gdiWrapper);
            SafeDispose(ref _skCanvas);
            SafeDispose(ref _skBitmap);

            if (isDesignTime)
            {
                // Designer host is sensitive to native Skia allocations; keep a safe GDI-only path.
                _useSkiaGraphics = false;
                _isRenderable = true;
                UpdateCachedClipPath();
                RefreshVisuals();
                return;
            }

#if NETFRAMEWORK
            // 🔥 INYECCIÓN PRO: Compactar el LOH (Large Object Heap) en .NET 4.8 
            // Evita el falso OOM por fragmentación de memoria al redimensionar mucho la ventana
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
#endif

            try
            {
                _skBitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul));
                _skCanvas = new SKCanvas(_skBitmap);

#if NETFRAMEWORK
                // 🔥 MÉTODO BLINDADO .NET 4.8: Instanciamos el Bitmap normal (Constructor seguro) sin punteros cruzados.
                _gdiWrapper = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
#else
                // 🔥 MAGIA DE ARQUITECTO: Mapeo de memoria directo (Zero-Copy)
                _gdiWrapper = new Bitmap(Width, Height, _skBitmap.RowBytes, PixelFormat.Format32bppPArgb, _skBitmap.GetPixels());
#endif

                _isRenderable = true;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[ModernForms Core] Falla de buffers (OOM): {ex}");
                _isRenderable = false;
                SafeDispose(ref _gdiWrapper);
                SafeDispose(ref _skCanvas);
                SafeDispose(ref _skBitmap);
            }

            UpdateCachedClipPath();
            RefreshVisuals();
        }

        protected static void SafeDispose<T>(ref T? obj) where T : class, IDisposable
        {
            try { obj?.Dispose(); }
            catch { }
            finally { obj = null; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 🔥 FIX TEMAS: Limpiamos la suscripción para evitar Memory Leaks
                AppTheme.ThemeChanged -= OnThemeChanged;
                if (_visualNode != null) _visualNode.Children.CollectionChanged -= VisualNode_ChildrenChanged;

                DisposeAnimations();
                ClearCaches();
                SafeDispose(ref _sharedPaint);
                SafeDispose(ref _sharedPath);
                SafeDispose(ref _gdiWrapper);
                SafeDispose(ref _skCanvas);
                SafeDispose(ref _skBitmap);
                SafeDispose(ref _cachedClipPath);

#if NETFRAMEWORK
                // 🔥 LIMPIEZA AIR-GAP .NET 4.8
                _netFxSafeBitmap?.Dispose();
                _netFxSafeBitmap = null;
                _netFxSafeBuffer = null;
#endif
            }
            base.Dispose(disposing);
        }

        public void RefreshVisuals() => InvalidateIfVisible();
    }
}