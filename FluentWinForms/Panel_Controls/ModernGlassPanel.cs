#nullable enable
using FluentWinForms.Core;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace FluentWinForms.Panel_Controls
{
    [Designer("System.Windows.Forms.Design.ParentControlDesigner, System.Design")]
    [ToolboxItem(true)]
    [DesignTimeVisible(true)]
    [Description("Panel Glassmorphism Supremo. Full Zero-Alloc, seguro contra reentrancia y optimizado para .NET Multi-Target.")]
    public class ModernGlassPanel : ModernControlBase
    {
        // =========================================
        // Ocultar propiedades heredadas en el diseñador
        // =========================================
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new bool UseAcrylic { get => base.UseAcrylic; set => base.UseAcrylic = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Color BackgroundColor { get => base.BackgroundColor; set => base.BackgroundColor = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Color BackgroundColor2 { get => base.BackgroundColor2; set => base.BackgroundColor2 = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new float BorderThickness { get => base.BorderThickness; set => base.BorderThickness = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new float FocusThickness { get => base.FocusThickness; set => base.FocusThickness = value; }

        // =========================================
        // Propiedades públicas (Unificadas a float)
        // =========================================
        private Color _glassTint = Color.FromArgb(20, 255, 255, 255);
        [Category("Glassmorphism")]
        public Color GlassTint { get => _glassTint; set { _glassTint = value; UpdatePaints(); InvalidateCaches(); } }

        private Color _glassBorder = Color.FromArgb(120, 255, 255, 255);
        [Category("Glassmorphism")]
        public Color GlassBorder { get => _glassBorder; set { _glassBorder = value; UpdatePaints(); InvalidateCaches(); } }

        private float _glassBorderRadius = 15f; // 🔥 Cambiado a float
        [Category("Glassmorphism")]
        public float GlassBorderRadius { get => _glassBorderRadius; set { _glassBorderRadius = Math.Max(0f, value); InvalidateCaches(); } }

        private float _blurAmount = 15f; // 🔥 Cambiado a float
        [Category("Glassmorphism")]
        [Description("0 = usar Acrylic nativo (Win11). >0 = captura+blur solo del área del panel.")]
        public float BlurAmount { get => _blurAmount; set { _blurAmount = Math.Max(0f, value); UpdatePaints(); InvalidateCaches(); } }

        // =========================================
        // Campos internos reutilizables (zero-alloc en caliente)
        // =========================================
        private SKBitmap? _blurredCache;
        private SKBitmap? _rawWrapperBitmap;
        private bool _cacheDirty = true;

        private SKPaint? _tintPaint;
        private SKPaint? _borderPaint;
        private SKPaint? _blurPaint;

        // CancellationTokenSource seguro (swap atómico)
        private CancellationTokenSource? _genCts;
        private long _lastGenTicks = 0;

        // =========================================
        // Constructor (ligero, seguro para diseñador)
        // =========================================
        public ModernGlassPanel()
        {
            SetStyle(ControlStyles.ContainerControl | ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.Transparent;

            // Intentamos desactivar propiedades heredadas si existen
            try
            {
                base.UseAcrylic = false;
                base.BackgroundColor = Color.Transparent;
                base.BackgroundColor2 = Color.Transparent;
                base.BorderThickness = 0f;
                base.FocusThickness = 0f;
            }
            catch { /* ignorar si ModernControlBase no expone esas propiedades */ }

            UpdatePaints();
        }

        // =========================================
        // Helpers de escala / casts
        // =========================================
        private static float S(float v) => v;
        private static int ToIntRound(float v) => (int)Math.Round(v);
        private static int S(int v) => v;

        // =========================================
        // Actualización de pinceles (segura)
        // =========================================
        private void UpdatePaints()
        {
            try
            {
                _tintPaint?.Dispose();
                _tintPaint = new SKPaint { Color = _glassTint.ToSKColor(), Style = SKPaintStyle.Fill, IsAntialias = true };

                _borderPaint?.Dispose();
                _borderPaint = new SKPaint { Color = _glassBorder.ToSKColor(), Style = SKPaintStyle.Stroke, StrokeWidth = S(1.5f), IsAntialias = true };

                _blurPaint?.Dispose();
                if (_blurAmount > 0f)
                {
                    float blurRadius = S(_blurAmount); // Ya no requiere cast (float) explícito
                    _blurPaint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(blurRadius, blurRadius) };
                }
                else
                {
                    _blurPaint = null;
                }
            }
            catch
            {
                // No fallar en diseño/tiempo de edición
            }
        }

        private void InvalidateCaches()
        {
            _cacheDirty = true;
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); InvalidateCaches(); }
        protected override void OnMove(EventArgs e) { base.OnMove(e); InvalidateCaches(); }

        // =========================================
        // Silenciadores de eventos base (evitar repintados no deseados)
        // =========================================
        protected override void OnMouseDown(MouseEventArgs e) { /* intencionalmente vacío */ }
        protected override void OnMouseUp(MouseEventArgs e) { /* intencionalmente vacío */ }
        protected override void OnMouseMove(MouseEventArgs e) { /* intencionalmente vacío */ }
        protected override void OnMouseEnter(EventArgs e) { /* intencionalmente vacío */ }
        protected override void OnMouseLeave(EventArgs e) { /* intencionalmente vacío */ }

        // =========================================
        // Generación del cache de blur (robusta y compatible con SkiaSharp 2.88.8)
        // =========================================
        private void GenerateBlurCache()
        {
            if (DesignMode) return;
            int w = this.Width;
            int h = this.Height;
            if (Parent == null || w <= 0 || h <= 0 || _blurAmount <= 0f) return;

            // Debounce simple (~30 fps máximo)
            long now = Stopwatch.GetTimestamp();
            if ((now - _lastGenTicks) < (Stopwatch.Frequency / 30)) return;
            _lastGenTicks = now;

            // Swap atómico del CTS antiguo por uno nuevo
            var newCts = new CancellationTokenSource();
            var oldCts = Interlocked.Exchange(ref _genCts, newCts);
            try { oldCts?.Cancel(); } catch { }
            try { oldCts?.Dispose(); } catch { }
            var token = newCts.Token;

            // 🔥 CORRECCIÓN: El pool pide dimensiones enteras estrictas (int), removidos los Helpers incorrectos aquí
            Bitmap bmp = AcrylicHelper.BitmapPool.Rent(w, h);
            BitmapData? bmpData = null;
            try
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.TranslateTransform(-this.Left, -this.Top);

                    // Solo pintar el fondo del padre para evitar reentrancia
                    var pe = new PaintEventArgs(g, Parent.ClientRectangle);
                    InvokePaintBackground(Parent, pe);

                    if (token.IsCancellationRequested) return;

                    // Dibujar controles hermanos simples (evitar HWND-hosted)
                    int myIndex = Parent.Controls.GetChildIndex(this);
                    for (int i = Parent.Controls.Count - 1; i > myIndex; i--)
                    {
                        if (token.IsCancellationRequested) return;
                        Control c = Parent.Controls[i];
                        if (!c.Visible || !c.Bounds.IntersectsWith(this.Bounds)) continue;

                        string tn = c.GetType().FullName ?? string.Empty;
                        bool isComplex = tn.Contains("AxHost") || tn.Contains("WebBrowser") || tn.Contains("HwndHost") || tn.Contains("DirectX");
                        if (isComplex) continue;

                        int cw = Math.Max(1, c.Width);
                        int ch = Math.Max(1, c.Height);
                        Bitmap cBmp = AcrylicHelper.BitmapPool.Rent(cw, ch);
                        try
                        {
                            c.DrawToBitmap(cBmp, new Rectangle(0, 0, cw, ch));
                            g.DrawImageUnscaled(cBmp, c.Left, c.Top);
                        }
                        finally
                        {
                            AcrylicHelper.BitmapPool.Return(cBmp);
                        }
                    }
                }

                if (token.IsCancellationRequested) return;

                // LockBits y envolver memoria en Skia sin copias extra
                var rect = new Rectangle(0, 0, w, h);
                bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);

                // Reusar wrapper nativo
                if (_rawWrapperBitmap == null || _rawWrapperBitmap.Width != w || _rawWrapperBitmap.Height != h)
                {
                    _rawWrapperBitmap?.Dispose();
                    _rawWrapperBitmap = new SKBitmap();
                }

                // InstallPixels envuelve el puntero nativo (SkiaSharp 2.88.8)
                _rawWrapperBitmap.InstallPixels(info, bmpData.Scan0, bmpData.Stride);

                if (token.IsCancellationRequested) return;

                if (_blurredCache == null || _blurredCache.Width != w || _blurredCache.Height != h)
                {
                    _blurredCache?.Dispose();
                    _blurredCache = new SKBitmap(info);
                }

                using (var canvas = new SKCanvas(_blurredCache))
                {
                    canvas.Clear(SKColors.Transparent);
                    if (_blurPaint != null)
                        canvas.DrawBitmap(_rawWrapperBitmap, 0, 0, _blurPaint);
                    else
                        canvas.DrawBitmap(_rawWrapperBitmap, 0, 0);
                    canvas.Flush();
                }
            }
            catch
            {
                // Fallback silencioso ante redimensionamiento agresivo
            }
            finally
            {
                try { if (bmpData != null) bmp.UnlockBits(bmpData); } catch { }
                try { AcrylicHelper.BitmapPool.Return(bmp); } catch { }
            }

            _cacheDirty = false;
        }

        // =========================================
        // Pipeline Skia (invocado por ModernControlBase)
        // =========================================
        protected override void PaintSkia(SKCanvas canvas, SKRect contentRect, SKRect paddedRect)
        {
            canvas.Clear(SKColors.Transparent);

            float rad = S(_glassBorderRadius);

            using (var path = new SKPath())
            {
                path.AddRoundRect(paddedRect, rad, rad);
                canvas.Save();
                canvas.ClipPath(path, SKClipOperation.Intersect, true);

                if (_blurAmount > 0f)
                {
                    if (_cacheDirty || _blurredCache == null) GenerateBlurCache();

                    if (_blurredCache != null && !_cacheDirty)
                    {
                        canvas.DrawBitmap(_blurredCache, new SKRect(0f, 0f, (float)this.Width, (float)this.Height));
                    }
                }

                if (_tintPaint != null) canvas.DrawRect(paddedRect, _tintPaint);

                canvas.Restore();

                if (_borderPaint != null)
                {
                    float offset = S(0.75f);
                    var borderRect = new SKRect(paddedRect.Left + offset, paddedRect.Top + offset, paddedRect.Right - offset, paddedRect.Bottom - offset);
                    canvas.DrawRoundRect(borderRect, rad, rad, _borderPaint);
                }
            }
        }

        // =========================================
        // Fallback GDI+ (diseñador)
        // =========================================
        protected override void PaintGDIPipeline(Graphics g, RectangleF contentRect, RectangleF paddedRect)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = S(_glassBorderRadius);

            using (var path = GetRoundedPath(paddedRect, rad))
            using (var brush = new SolidBrush(_glassTint))
            using (var pen = new Pen(_glassBorder, S(1.5f)))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
        }

        private GraphicsPath GetRoundedPath(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2f;
            if (d <= 0f) { path.AddRectangle(rect); return path; }

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // =========================================
        // Dispose robusto (no lanzar en diseñador)
        // =========================================
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var cts = Interlocked.Exchange(ref _genCts, null);
                try { cts?.Cancel(); } catch { }
                try { cts?.Dispose(); } catch { }

                try { _blurredCache?.Dispose(); } catch { }
                try { _rawWrapperBitmap?.Dispose(); } catch { }

                try { _tintPaint?.Dispose(); } catch { }
                try { _borderPaint?.Dispose(); } catch { }
                try { _blurPaint?.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}