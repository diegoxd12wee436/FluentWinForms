#nullable enable
using FluentWinForms.Core;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace FluentWinForms.Panel_Controls
{
    public enum PanelBackdropStyle { Solid, Gradient, Glass }

    [Designer("System.Windows.Forms.Design.ParentControlDesigner, System.Design")]
    [ToolboxItem(true)]
    [DesignTimeVisible(true)]
    [Description("Panel Estático Premium. Cero Ripple, Captura estática, transparencia real de Skia, y bordes perfectos.")]
    public class ModernGlassPanel : ModernControlBase
    {
        // =========================================================
        // 🚫 OCULTANDO PROPIEDADES INNECESARIAS DE TU MOTOR 🚫
        // =========================================================
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new bool UseAcrylic { get => base.UseAcrylic; set => base.UseAcrylic = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new float FocusThickness { get => base.FocusThickness; set => base.FocusThickness = value; }

        // =========================================================
        // ✨ PROPIEDADES EXCLUSIVAS DEL PANEL ✨
        // =========================================================

        // 🔥 Nace por defecto en Glass
        private PanelBackdropStyle _backdropStyle = PanelBackdropStyle.Glass;
        [Category("Modern Appearance")]
        public PanelBackdropStyle BackdropStyle { get => _backdropStyle; set { _backdropStyle = value; InvalidateCaches(); } }

        private float _borderRadius = 15f;
        [Category("Modern Appearance")]
        public float BorderRadius { get => _borderRadius; set { _borderRadius = Math.Max(0f, value); InvalidateCaches(); } }

        // 🔥 Borde blanquito premium por defecto (120 de opacidad)
        private Color _borderColor = Color.FromArgb(120, 255, 255, 255);
        [Category("Modern Appearance")]
        public Color BorderColor { get => _borderColor; set { _borderColor = value; InvalidateCaches(); } }

        private float _gradientAngle = 45f;
        [Category("Modern Appearance")]
        public float GradientAngle { get => _gradientAngle; set { _gradientAngle = value % 360f; InvalidateCaches(); } }

        private Color _glassTint = Color.FromArgb(40, 255, 255, 255);
        [Category("Modern Appearance - Glass")]
        public Color GlassTint { get => _glassTint; set { _glassTint = value; InvalidateCaches(); } }

        private float _blurAmount = 25f;
        [Category("Modern Appearance - Glass")]
        public float BlurAmount { get => _blurAmount; set { _blurAmount = Math.Max(0f, value); InvalidateCaches(); } }

        // =========================================================
        // VARIABLES ESTÁTICAS Y SEGURAS
        // =========================================================
        private SKBitmap? _blurredCache;
        private SKBitmap? _sharpCache;
        private bool _cacheDirty = true;

        public ModernGlassPanel()
        {
            SetStyle(ControlStyles.ContainerControl | ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.Transparent;
            try { base.UseAcrylic = false; } catch { }
        }

        private static float S(float v) => v;

        // 🛑 SILENCIADOS PARA CERO RIPPLE 🛑
        protected override void OnMouseDown(MouseEventArgs e) { /* SILENCIADO */ }
        protected override void OnMouseUp(MouseEventArgs e) { /* SILENCIADO */ }
        protected override void OnMouseMove(MouseEventArgs e) { /* SILENCIADO */ }
        protected override void OnMouseEnter(EventArgs e) { /* SILENCIADO */ }
        protected override void OnMouseLeave(EventArgs e) { /* SILENCIADO */ }

        private void InvalidateCaches()
        {
            _cacheDirty = true;
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); InvalidateCaches(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); InvalidateCaches(); }
        protected override void OnMove(EventArgs e) { base.OnMove(e); InvalidateCaches(); }

        // =========================================================
        // 📸 CAPTURA ESTÁTICA
        // =========================================================
        private void GenerateStaticCache()
        {
            if (DesignMode || Parent == null || Width <= 0 || Height <= 0) return;

            if (_backdropStyle != PanelBackdropStyle.Glass)
            {
                _cacheDirty = false;
                return;
            }

            try
            {
                using (Bitmap bmp = new Bitmap(Width, Height))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.TranslateTransform(-Left, -Top);

                        using (var pe = new PaintEventArgs(g, Parent.ClientRectangle))
                            InvokePaintBackground(Parent, pe);

                        int myIndex = Parent.Controls.GetChildIndex(this);
                        for (int i = Parent.Controls.Count - 1; i > myIndex; i--)
                        {
                            Control c = Parent.Controls[i];
                            if (!c.Visible || !c.Bounds.IntersectsWith(this.Bounds)) continue;
                            if (c.GetType().FullName?.Contains("AxHost") == true) continue;

                            int cw = Math.Max(1, c.Width);
                            int ch = Math.Max(1, c.Height);

                            Bitmap cBmp = AcrylicHelper.BitmapPool.Rent(cw, ch);
                            try
                            {
                                c.DrawToBitmap(cBmp, new Rectangle(0, 0, cw, ch));
                                g.DrawImageUnscaled(cBmp, c.Left, c.Top);
                            }
                            finally { AcrylicHelper.BitmapPool.Return(cBmp); }
                        }
                    }

                    using (var ms = new System.IO.MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        ms.Position = 0;

                        using (var rawSkia = SKBitmap.Decode(ms))
                        {
                            _sharpCache?.Dispose();
                            _sharpCache = rawSkia.Copy();

                            _blurredCache?.Dispose();
                            _blurredCache = new SKBitmap(rawSkia.Info);

                            using (var canvas = new SKCanvas(_blurredCache))
                            {
                                canvas.Clear(SKColors.Transparent);
                                if (_blurAmount > 0)
                                {
                                    using (var blurPaint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(S(_blurAmount), S(_blurAmount), SKShaderTileMode.Clamp) })
                                    {
                                        canvas.DrawBitmap(rawSkia, 0, 0, blurPaint);
                                    }
                                }
                                else
                                {
                                    canvas.DrawBitmap(rawSkia, 0, 0);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                _cacheDirty = false;
            }
        }

        private void GetGradientPoints(float width, float height, float angle, out SKPoint start, out SKPoint end)
        {
            float angleRad = angle * (float)(Math.PI / 180f);
            float diagonal = (float)Math.Sqrt(width * width + height * height);
            float dx = (float)(Math.Cos(angleRad) * diagonal / 2f);
            float dy = (float)(Math.Sin(angleRad) * diagonal / 2f);
            float cx = width / 2f;
            float cy = height / 2f;
            start = new SKPoint(cx - dx, cy - dy);
            end = new SKPoint(cx + dx, cy + dy);
        }

        // =========================================================
        // 🎨 RENDERIZADO FINAL "TUANI"
        // =========================================================
        protected override void PaintSkia(SKCanvas canvas, SKRect contentRect, SKRect paddedRect)
        {
            float rad = S(_borderRadius);
            canvas.Clear(SKColors.Transparent);

            // 🛠️ EL FIX: Usamos contentRect en lugar de paddedRect. 
            // Así el fondo y el cristal ignoran el grosor del borde y llenan el 100% del panel.
            using (var path = new SKPath())
            {
                path.AddRoundRect(contentRect, rad, rad);

                // 🛠️ MODO DISEÑADOR
                if (DesignMode)
                {
                    if (BackdropStyle == PanelBackdropStyle.Glass)
                    {
                        using (var preview = new SKPaint { Color = new SKColor(180, 180, 180, 80), Style = SKPaintStyle.Fill, IsAntialias = true })
                            canvas.DrawPath(path, preview);
                        using (var tint = new SKPaint { Color = GlassTint.ToSKColor(), Style = SKPaintStyle.Fill, IsAntialias = true })
                            canvas.DrawPath(path, tint);
                    }
                    else if (BackdropStyle == PanelBackdropStyle.Gradient)
                    {
                        GetGradientPoints(contentRect.Width, contentRect.Height, _gradientAngle, out SKPoint p1, out SKPoint p2);
                        using (var grad = new SKPaint { Shader = SKShader.CreateLinearGradient(p1, p2, new[] { BackgroundColor.ToSKColor(), BackgroundColor2.ToSKColor() }, new[] { 0f, 1f }, SKShaderTileMode.Clamp), IsAntialias = true })
                            canvas.DrawPath(path, grad);
                    }
                    else
                    {
                        using (var solid = new SKPaint { Color = BackgroundColor.ToSKColor(), Style = SKPaintStyle.Fill, IsAntialias = true })
                            canvas.DrawPath(path, solid);
                    }

                    if (BorderThickness > 0)
                    {
                        float offset = S(BorderThickness) / 2f;
                        float borderRad = Math.Max(0, rad - offset); // Curva perfecta para bordes
                        var borderRect = new SKRect(contentRect.Left + offset, contentRect.Top + offset, contentRect.Right - offset, contentRect.Bottom - offset);
                        using (var border = new SKPaint { Color = BorderColor.ToSKColor(), Style = SKPaintStyle.Stroke, StrokeWidth = S(BorderThickness), IsAntialias = true })
                            canvas.DrawRoundRect(borderRect, borderRad, borderRad, border);
                    }
                    return;
                }

                // 🚀 MODO EJECUCIÓN ESTÁTICO
                if (BackdropStyle == PanelBackdropStyle.Glass && (_cacheDirty || _blurredCache == null || _sharpCache == null))
                {
                    GenerateStaticCache();
                }

                if (BackdropStyle == PanelBackdropStyle.Glass)
                {
                    canvas.Save();
                    canvas.ClipPath(path, SKClipOperation.Intersect, true);

                    if (_sharpCache != null) canvas.DrawBitmap(_sharpCache, 0, 0); // 🔥 adentro del clip

                    // El blur ahora se dibuja de extremo a extremo sin dejar marcos a los lados
                    if (_blurredCache != null) canvas.DrawBitmap(_blurredCache, 0, 0);

                    using (var tint = new SKPaint { Color = GlassTint.ToSKColor(), Style = SKPaintStyle.Fill, IsAntialias = true })
                        canvas.DrawRect(contentRect, tint);

                    canvas.Restore();
                }
                else if (BackdropStyle == PanelBackdropStyle.Gradient)
                {
                    GetGradientPoints(contentRect.Width, contentRect.Height, _gradientAngle, out SKPoint p1, out SKPoint p2);
                    using (var grad = new SKPaint { Shader = SKShader.CreateLinearGradient(p1, p2, new[] { BackgroundColor.ToSKColor(), BackgroundColor2.ToSKColor() }, new[] { 0f, 1f }, SKShaderTileMode.Clamp), IsAntialias = true })
                        canvas.DrawPath(path, grad);
                }
                else
                {
                    using (var solid = new SKPaint { Color = BackgroundColor.ToSKColor(), Style = SKPaintStyle.Fill, IsAntialias = true })
                        canvas.DrawPath(path, solid);
                }

                // 🌟 DIBUJADO DE BORDE PREMIUM
                if (BorderThickness > 0)
                {
                    // Metemos el borde exactamente la mitad de su grosor hacia adentro para que encaje como un guante
                    float offset = S(BorderThickness) / 2f;
                    float borderRad = Math.Max(0, rad - offset);

                    var borderRect = new SKRect(contentRect.Left + offset, contentRect.Top + offset, contentRect.Right - offset, contentRect.Bottom - offset);

                    using (var borderPaint = new SKPaint { Color = BorderColor.ToSKColor(), Style = SKPaintStyle.Stroke, StrokeWidth = S(BorderThickness), IsAntialias = true })
                    {
                        canvas.DrawRoundRect(borderRect, borderRad, borderRad, borderPaint);
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _blurredCache?.Dispose();
                _blurredCache = null;
                _sharpCache?.Dispose();
                _sharpCache = null;
            }
            base.Dispose(disposing);
        }
    }
}