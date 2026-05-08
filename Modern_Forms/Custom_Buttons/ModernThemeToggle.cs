#nullable enable
using FluentWinForms.Core; 
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FluentWinForms.Custom_Buttons 
{
    /// <summary>
    /// Enumeración de los estilos visuales disponibles para el Toggle. 
    /// </summary>
    public enum ToggleStyle
    {
        Style1_Standard,
        Style2_ThinTrack,
        Style3_LineTrack,
        Style4_Square,
        Style5_Text,
        Style6_WideThumb,
        Style7_Checkmark,
        Style8_Ring,
        Weather_Legacy // Diseño original intacto
    }

    [ToolboxItem(true)]
    [DesignTimeVisible(true)]
    [ToolboxBitmap(typeof(CheckBox))]
    [DefaultEvent("Click")]
    [DefaultProperty("IsChecked")]
    public class ModernThemeToggle : ModernControlBase
    {
        // -------------------------------------------------------------------------
        // Variables de estado interno | Internal state variables
        // -------------------------------------------------------------------------
        private float _toggleProgress = 0f;

        // =========================================================================
        // PROPIEDADES DEL PAQUETE NUGET | NUGET PACKAGE PROPERTIES (Styles & Colors)
        // =========================================================================

        [Category("Toggle Appearance")]
        [Description("Define el estilo visual del Toggle basado en 9 diseños preestablecidos.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public ToggleStyle Style { get; set; } = ToggleStyle.Weather_Legacy;

        [Category("Modern Appearance")]
        [Description("Color de la pista cuando está activado.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color ToggleColorOn { get; set; } = Color.FromArgb(138, 43, 226);

        [Category("Toggle Appearance")]
        [Description("Color de la pista cuando está desactivado.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color ToggleColorOff { get; set; } = Color.FromArgb(220, 224, 232);

        [Category("Toggle Appearance")]
        [Description("Color del indicador (thumb) cuando está activado.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color ThumbColorOn { get; set; } = Color.White;

        [Category("Toggle Appearance")]
        [Description("Color del indicador (thumb) cuando está desactivado.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color ThumbColorOff { get; set; } = Color.White;

        // =========================================================================
        // VARIABLES ORIGINALES INTACTAS | ORIGINAL INTACT VARIABLES
        // =========================================================================
        private readonly SKColor _bgDaySK = SKColor.Parse("#3D7EAE");
        private readonly SKColor _bgNightSK = SKColor.Parse("#1D1F2C");
        private readonly SKColor _sunColorSK = SKColor.Parse("#ECCA2F");
        private readonly SKColor _moonColorSK = SKColor.Parse("#C4C9D1");
        private readonly SKColor _spotColorSK = SKColor.Parse("#959DB1");

        private readonly Color _bgDayGDI = Color.FromArgb(61, 126, 174);
        private readonly Color _bgNightGDI = Color.FromArgb(29, 31, 44);
        private readonly Color _sunColorGDI = Color.FromArgb(236, 202, 47);
        private readonly Color _moonColorGDI = Color.FromArgb(196, 201, 209);
        private readonly Color _spotColorGDI = Color.FromArgb(149, 157, 177);

        public ModernThemeToggle()
        {
            
            this.MinimumSize = new Size(45, 22);

            Width = 45;
            Height = 22;

            BackgroundColor = Color.Transparent;
            BackgroundColor2 = Color.Transparent;
            CheckedColor = Color.Transparent;
            CheckedColor2 = Color.Transparent;
            HoverColor = Color.Transparent;
            HoverColor2 = Color.Transparent;
            PressColor = Color.Transparent;
            PressColor2 = Color.Transparent;

            BorderThickness = 0;
            FocusThickness = 0;
            UseRipple = false;
            Cursor = Cursors.Hand;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            BorderRadius = Height / 2f;
        }

        protected override bool CustomAnimationLoop(float dt, float step)
        {
            bool isMoving = false;
            float target = IsChecked ? 1f : 0f;

            if (Math.Abs(_toggleProgress - target) > 0.005f)
            {
                _toggleProgress += (target - _toggleProgress) * (dt / 150f);
                isMoving = true;
            }
            else
            {
                _toggleProgress = target;
            }

            return isMoving;
        }

        protected override void OnClick(EventArgs e)
        {
            IsChecked = !IsChecked;
            base.OnClick(e);
        }

        // =========================================================================
        // 🔵 RUTA 1: RENDERIZADO ACELERADO POR HARDWARE (SKIA) 
        // =========================================================================
        protected override void PaintSkia(SKCanvas canvas, SKRect contentRect, SKRect paddedRect)
        {
            if (_sharedPaint == null) _sharedPaint = new SKPaint();
            _sharedPaint.Reset();
            _sharedPaint.IsAntialias = true;

            float t = Easing.EaseInOutQuad(_toggleProgress);

            if (Style == ToggleStyle.Weather_Legacy)
            {
                DrawWeatherLegacySkia(canvas, contentRect, t);
            }
            else
            {
                DrawModernStylesSkia(canvas, contentRect, t);
            }
        }

        // =========================================================================
        // 🔵 RUTA 2: RENDERIZADO POR SOFTWARE (GDI+ FALLBACK)
        // =========================================================================
        protected override void PaintGDI(Graphics g, RectangleF contentRect, RectangleF paddedRect)
        {
            float t = Easing.EaseInOutQuad(_toggleProgress);

            if (Style == ToggleStyle.Weather_Legacy)
            {
                DrawWeatherLegacyGDI(g, contentRect, t);
            }
            else
            {
                DrawModernStylesGDI(g, contentRect, t);
            }
        }

        private void DrawWeatherLegacySkia(SKCanvas canvas, SKRect contentRect, float t)
        {
            Color cDay = Color.FromArgb(_bgDaySK.Alpha, _bgDaySK.Red, _bgDaySK.Green, _bgDaySK.Blue);
            Color cNight = Color.FromArgb(_bgNightSK.Alpha, _bgNightSK.Red, _bgNightSK.Green, _bgNightSK.Blue);
            Color currentBg = LerpColor(cDay, cNight, t);

            _sharedPaint!.Color = new SKColor(currentBg.R, currentBg.G, currentBg.B, currentBg.A);
            canvas.DrawRoundRect(contentRect, contentRect.Height / 2, contentRect.Height / 2, _sharedPaint);

            _sharedPaint.Style = SKPaintStyle.Stroke;
            _sharedPaint.StrokeWidth = S(2);
            _sharedPaint.Color = SKColors.Black.WithAlpha(30);
            canvas.DrawRoundRect(contentRect, contentRect.Height / 2, contentRect.Height / 2, _sharedPaint);
            _sharedPaint.Style = SKPaintStyle.Fill;

            if (t > 0.1f)
            {
                float starYOffset = (1f - t) * -(contentRect.Height);
                _sharedPaint.Color = SKColors.White.WithAlpha((byte)(255 * t));
                canvas.DrawCircle(contentRect.Left + S(20), contentRect.Top + S(10) + starYOffset, S(1.5f), _sharedPaint);
                canvas.DrawCircle(contentRect.Left + S(35), contentRect.Top + S(22) + starYOffset, S(1f), _sharedPaint);
                canvas.DrawCircle(contentRect.Left + S(15), contentRect.Top + S(28) + starYOffset, S(2f), _sharedPaint);
                canvas.DrawCircle(contentRect.Left + S(45), contentRect.Top + S(12) + starYOffset, S(1f), _sharedPaint);
            }

            if (t < 0.9f)
            {
                float cloudYOffset = t * contentRect.Height;
                _sharedPaint.Color = SKColor.Parse("#F3FDFF").WithAlpha((byte)(255 * (1f - t)));
                canvas.DrawCircle(contentRect.Right - S(25), contentRect.Bottom + S(2) + cloudYOffset, S(12), _sharedPaint);
                canvas.DrawCircle(contentRect.Right - S(10), contentRect.Bottom + S(8) + cloudYOffset, S(16), _sharedPaint);
                canvas.DrawCircle(contentRect.Right - S(40), contentRect.Bottom + S(10) + cloudYOffset, S(10), _sharedPaint);
            }

            float thumbPadding = S(4f);
            float thumbSize = contentRect.Height - (thumbPadding * 2);
            float minX = contentRect.Left + thumbPadding;
            float maxX = contentRect.Right - thumbSize - thumbPadding;
            float currentX = minX + (maxX - minX) * t;

            _sharedPaint.Color = SKColors.White.WithAlpha(20);
            canvas.DrawCircle(currentX + (thumbSize / 2), contentRect.MidY, (thumbSize / 2) + S(6), _sharedPaint);
            _sharedPaint.Color = SKColors.White.WithAlpha(10);
            canvas.DrawCircle(currentX + (thumbSize / 2), contentRect.MidY, (thumbSize / 2) + S(12), _sharedPaint);

            var thumbRect = new SKRect(currentX, contentRect.Top + thumbPadding, currentX + thumbSize, contentRect.Top + thumbPadding + thumbSize);

            canvas.Save();
            if (_sharedPath == null) _sharedPath = new SKPath();
            _sharedPath.Reset();
            _sharedPath.AddOval(thumbRect);
            canvas.ClipPath(_sharedPath, SKClipOperation.Intersect, true);

            _sharedPaint.Color = _sunColorSK;
            canvas.DrawRect(thumbRect, _sharedPaint);

            float moonOffsetX = thumbSize * (1f - t);
            var moonRect = new SKRect(thumbRect.Left + moonOffsetX, thumbRect.Top, thumbRect.Right + moonOffsetX, thumbRect.Bottom);
            _sharedPaint.Color = _moonColorSK;
            canvas.DrawOval(moonRect, _sharedPaint);

            _sharedPaint.Color = _spotColorSK;
            canvas.DrawCircle(moonRect.Left + S(8), moonRect.Top + S(8), S(3f), _sharedPaint);
            canvas.DrawCircle(moonRect.Left + S(18), moonRect.Top + S(16), S(4.5f), _sharedPaint);
            canvas.DrawCircle(moonRect.Left + S(10), moonRect.Top + S(22), S(2f), _sharedPaint);

            canvas.Restore();

            _sharedPaint.Style = SKPaintStyle.Stroke;
            _sharedPaint.StrokeWidth = S(1);
            _sharedPaint.Color = SKColors.Black.WithAlpha(20);
            canvas.DrawOval(thumbRect, _sharedPaint);
        }

        private void DrawWeatherLegacyGDI(Graphics g, RectangleF contentRect, float t)
        {
            Color currentBg = LerpColor(_bgDayGDI, _bgNightGDI, t);
            float radius = contentRect.Height / 2f;

            using (var path = GdiRenderer.CreateRoundedRectPath(contentRect, radius))
            using (var brush = new SolidBrush(currentBg))
            {
                g.FillPath(brush, path);
            }

            var clipState = g.Save();
            using (var clipPath = GdiRenderer.CreateRoundedRectPath(contentRect, radius))
            {
                g.SetClip(clipPath, CombineMode.Intersect);

                if (t > 0.1f)
                {
                    float starYOffset = (1f - t) * -contentRect.Height;
                    using (var brush = new SolidBrush(Color.FromArgb((int)(255 * t), Color.White)))
                    {
                        g.FillEllipse(brush, contentRect.Left + S(20), contentRect.Top + S(10) + starYOffset, S(3f), S(3f));
                        g.FillEllipse(brush, contentRect.Left + S(35), contentRect.Top + S(22) + starYOffset, S(2f), S(2f));
                        g.FillEllipse(brush, contentRect.Left + S(15), contentRect.Top + S(28) + starYOffset, S(4f), S(4f));
                        g.FillEllipse(brush, contentRect.Left + S(45), contentRect.Top + S(12) + starYOffset, S(2f), S(2f));
                    }
                }

                if (t < 0.9f)
                {
                    float cloudYOffset = t * contentRect.Height;
                    using (var brush = new SolidBrush(Color.FromArgb((int)(255 * (1f - t)), 243, 253, 255)))
                    {
                        g.FillEllipse(brush, contentRect.Right - S(25) - S(12), contentRect.Bottom + S(2) + cloudYOffset - S(12), S(24), S(24));
                        g.FillEllipse(brush, contentRect.Right - S(10) - S(16), contentRect.Bottom + S(8) + cloudYOffset - S(16), S(32), S(32));
                        g.FillEllipse(brush, contentRect.Right - S(40) - S(10), contentRect.Bottom + S(10) + cloudYOffset - S(10), S(20), S(20));
                    }
                }

                float thumbPadding = S(4f);
                float thumbSize = contentRect.Height - (thumbPadding * 2);
                float minX = contentRect.Left + thumbPadding;
                float maxX = contentRect.Right - thumbSize - thumbPadding;
                float currentX = minX + (maxX - minX) * t;

                float midY = contentRect.Top + (contentRect.Height / 2f);
                float thumbCenterX = currentX + (thumbSize / 2f);

                using (var brush = new SolidBrush(Color.FromArgb(20, Color.White)))
                    g.FillEllipse(brush, thumbCenterX - (thumbSize / 2 + S(6)), midY - (thumbSize / 2 + S(6)), thumbSize + S(12), thumbSize + S(12));
                using (var brush = new SolidBrush(Color.FromArgb(10, Color.White)))
                    g.FillEllipse(brush, thumbCenterX - (thumbSize / 2 + S(12)), midY - (thumbSize / 2 + S(12)), thumbSize + S(24), thumbSize + S(24));

                var thumbRect = new RectangleF(currentX, contentRect.Top + thumbPadding, thumbSize, thumbSize);

                var oldState = g.Save();
                using (var thumbPath = new GraphicsPath())
                {
                    thumbPath.AddEllipse(thumbRect);
                    g.SetClip(thumbPath, CombineMode.Intersect);

                    using (var sunBrush = new SolidBrush(_sunColorGDI))
                        g.FillRectangle(sunBrush, thumbRect);

                    float moonOffsetX = thumbSize * (1f - t);
                    var moonRect = new RectangleF(thumbRect.Left + moonOffsetX, thumbRect.Top, thumbSize, thumbSize);

                    using (var moonBrush = new SolidBrush(_moonColorGDI))
                        g.FillEllipse(moonBrush, moonRect);

                    using (var spotBrush = new SolidBrush(_spotColorGDI))
                    {
                        g.FillEllipse(spotBrush, moonRect.Left + S(8), moonRect.Top + S(8), S(6f), S(6f));
                        g.FillEllipse(spotBrush, moonRect.Left + S(18), moonRect.Top + S(16), S(9f), S(9f));
                        g.FillEllipse(spotBrush, moonRect.Left + S(10), moonRect.Top + S(22), S(4f), S(4f));
                    }
                }
                g.Restore(oldState);

                using (var pen = new Pen(Color.FromArgb(20, Color.Black), S(1)))
                {
                    g.DrawEllipse(pen, thumbRect);
                }
            }

            g.Restore(clipState);

            GdiRenderer.DrawInnerShadow(g, contentRect, radius, Color.FromArgb(30, 0, 0, 0), S(2));
        }

        private void DrawModernStylesSkia(SKCanvas canvas, SKRect rect, float t)
        {
            Color currentTrackColor = LerpColor(ToggleColorOff, ToggleColorOn, t);
            Color currentThumbColor = LerpColor(ThumbColorOff, ThumbColorOn, t);

            _sharedPaint!.Style = SKPaintStyle.Fill;
            _sharedPaint.Color = currentTrackColor.ToSKColor();

            float padding = S(4f);
            float h = rect.Height;
            float thumbSize = h - (padding * 2);
            float minX = rect.Left + padding;
            float maxX = rect.Right - thumbSize - padding;
            float thumbX = minX + (maxX - minX) * t;

            switch (Style)
            {
                case ToggleStyle.Style1_Standard:
                    canvas.DrawRoundRect(rect, h / 2f, h / 2f, _sharedPaint);
                    // 🔥 FIX: Radio de sombra igual a la mitad del pulgar, no de la pista
                    DrawThumbShadowSkia(canvas, thumbX, rect.Top + padding, thumbSize, thumbSize, thumbSize / 2f);
                    _sharedPaint.Color = currentThumbColor.ToSKColor();
                    canvas.DrawOval(thumbX + thumbSize / 2f, rect.MidY, thumbSize / 2f, thumbSize / 2f, _sharedPaint);
                    break;

                case ToggleStyle.Style2_ThinTrack:
                    float trackH2 = h * 0.5f;
                    var trackRect2 = new SKRect(rect.Left, rect.MidY - trackH2 / 2f, rect.Right, rect.MidY + trackH2 / 2f);
                    canvas.DrawRoundRect(trackRect2, trackH2 / 2f, trackH2 / 2f, _sharedPaint);

                    float thumbRadius2 = (h * 0.8f) / 2f;
                    float minX2 = rect.Left + thumbRadius2;
                    float maxX2 = rect.Right - thumbRadius2;
                    float thumbX2 = minX2 + (maxX2 - minX2) * t;

                    DrawThumbShadowSkia(canvas, thumbX2 - thumbRadius2, rect.MidY - thumbRadius2, thumbRadius2 * 2, thumbRadius2 * 2, thumbRadius2);
                    _sharedPaint.Color = currentThumbColor.ToSKColor();
                    canvas.DrawCircle(thumbX2, rect.MidY, thumbRadius2, _sharedPaint);
                    break;

                case ToggleStyle.Style3_LineTrack:
                    _sharedPaint.Style = SKPaintStyle.Stroke;
                    _sharedPaint.StrokeWidth = S(4f);
                    _sharedPaint.StrokeCap = SKStrokeCap.Round;
                    canvas.DrawLine(rect.Left + padding, rect.MidY, rect.Right - padding, rect.MidY, _sharedPaint);

                    _sharedPaint.Style = SKPaintStyle.Fill;
                    float thumbRadius3 = (h * 0.7f) / 2f;
                    float minX3 = rect.Left + thumbRadius3;
                    float maxX3 = rect.Right - thumbRadius3;
                    float thumbX3 = minX3 + (maxX3 - minX3) * t;

                    DrawThumbShadowSkia(canvas, thumbX3 - thumbRadius3, rect.MidY - thumbRadius3, thumbRadius3 * 2, thumbRadius3 * 2, thumbRadius3);
                    _sharedPaint.Color = currentThumbColor.ToSKColor();
                    canvas.DrawCircle(thumbX3, rect.MidY, thumbRadius3, _sharedPaint);
                    break;

                case ToggleStyle.Style4_Square:
                    canvas.DrawRoundRect(rect, S(4f), S(4f), _sharedPaint);
                    DrawThumbShadowSkia(canvas, thumbX, rect.Top + padding, thumbSize, thumbSize, S(2f));
                    _sharedPaint.Color = currentThumbColor.ToSKColor();
                    canvas.DrawRoundRect(new SKRect(thumbX, rect.Top + padding, thumbX + thumbSize, rect.Top + padding + thumbSize), S(4f), S(4f), _sharedPaint);
                    break;

                case ToggleStyle.Style5_Text:
                    canvas.DrawRoundRect(rect, S(6f), S(6f), _sharedPaint);
                    _sharedPaint.Color = SKColors.White.WithAlpha(180);
                    _sharedPaint.TextSize = h * 0.4f;
                    _sharedPaint.TextAlign = SKTextAlign.Center;

                    // 🔥 FIX LEAK: Usamos caché de fuentes en lugar de instanciar SKTypeface cada frame
                    _sharedPaint.Typeface = GetOrCreateTypeface("Segoe UI", true);

                    if (t > 0.5f) canvas.DrawText("ON", rect.Left + (rect.Width / 4f), rect.MidY - (_sharedPaint.FontMetrics.Ascent / 2f), _sharedPaint);
                    else canvas.DrawText("OFF", rect.Right - (rect.Width / 4f), rect.MidY - (_sharedPaint.FontMetrics.Ascent / 2f), _sharedPaint);

                    DrawThumbShadowSkia(canvas, thumbX, rect.Top + padding, thumbSize, thumbSize, S(4f));
                    _sharedPaint.Color = currentThumbColor.ToSKColor();
                    canvas.DrawRoundRect(new SKRect(thumbX, rect.Top + padding, thumbX + thumbSize, rect.Top + padding + thumbSize), S(4f), S(4f), _sharedPaint);
                    break;

                case ToggleStyle.Style6_WideThumb:
                    canvas.DrawRoundRect(rect, h / 2f, h / 2f, _sharedPaint);
                    float wideThumbWidth = thumbSize * 1.5f;
                    float maxX6 = rect.Right - wideThumbWidth - padding;
                    float thumbX6 = minX + (maxX6 - minX) * t;

                    DrawThumbShadowSkia(canvas, thumbX6, rect.Top + padding, wideThumbWidth, thumbSize, thumbSize / 2f);
                    _sharedPaint.Color = currentThumbColor.ToSKColor();
                    canvas.DrawRoundRect(new SKRect(thumbX6, rect.Top + padding, thumbX6 + wideThumbWidth, rect.Top + padding + thumbSize), thumbSize / 2f, thumbSize / 2f, _sharedPaint);
                    break;

                case ToggleStyle.Style7_Checkmark:
                    canvas.DrawRoundRect(rect, h / 2f, h / 2f, _sharedPaint);
                    // 🔥 FIX: Radio de sombra corregido a thumbSize / 2f
                    DrawThumbShadowSkia(canvas, thumbX, rect.Top + padding, thumbSize, thumbSize, thumbSize / 2f);
                    _sharedPaint.Color = currentThumbColor.ToSKColor();
                    canvas.DrawOval(thumbX + thumbSize / 2f, rect.MidY, thumbSize / 2f, thumbSize / 2f, _sharedPaint);

                    _sharedPaint.Style = SKPaintStyle.Stroke;
                    _sharedPaint.StrokeWidth = S(2f);
                    _sharedPaint.StrokeCap = SKStrokeCap.Round;
                    _sharedPaint.Color = currentTrackColor.ToSKColor();

                    if (t > 0.5f)
                    {
                        canvas.DrawLine(thumbX + thumbSize * 0.3f, rect.MidY, thumbX + thumbSize * 0.45f, rect.MidY + thumbSize * 0.15f, _sharedPaint);
                        canvas.DrawLine(thumbX + thumbSize * 0.45f, rect.MidY + thumbSize * 0.15f, thumbX + thumbSize * 0.7f, rect.MidY - thumbSize * 0.15f, _sharedPaint);
                    }
                    else
                    {
                        canvas.DrawLine(thumbX + thumbSize * 0.35f, rect.MidY - thumbSize * 0.15f, thumbX + thumbSize * 0.65f, rect.MidY + thumbSize * 0.15f, _sharedPaint);
                        canvas.DrawLine(thumbX + thumbSize * 0.65f, rect.MidY - thumbSize * 0.15f, thumbX + thumbSize * 0.35f, rect.MidY + thumbSize * 0.15f, _sharedPaint);
                    }
                    break;

                case ToggleStyle.Style8_Ring:
                    _sharedPaint.Color = LerpColor(Color.FromArgb(80, 80, 80), ToggleColorOn, t).ToSKColor();
                    canvas.DrawRoundRect(rect, h / 2f, h / 2f, _sharedPaint);

                    _sharedPaint.Color = currentThumbColor.ToSKColor();
                    canvas.DrawOval(thumbX + thumbSize / 2f, rect.MidY, thumbSize / 2f, thumbSize / 2f, _sharedPaint);

                    _sharedPaint.Color = LerpColor(Color.FromArgb(80, 80, 80), ToggleColorOn, t).ToSKColor();
                    float innerRad = thumbSize * 0.25f;
                    canvas.DrawOval(thumbX + thumbSize / 2f, rect.MidY, innerRad, innerRad, _sharedPaint);
                    break;
            }
        }

        /// <summary>
        /// Método auxiliar para dibujar sombras de alto rendimiento en el Thumb de Skia.
        /// </summary>
        private void DrawThumbShadowSkia(SKCanvas canvas, float x, float y, float w, float h, float r)
        {
            // 🔥 FIX LEAK NATIVO: Usamos using var para asegurar que el ImageFilter (C++) se libere de RAM instantáneamente
            using var shadowPaint = new SKPaint { IsAntialias = true, Color = SKColors.Transparent };
            using var filter = SKImageFilter.CreateDropShadow(0, S(2f), S(3f), S(3f), SKColors.Black.WithAlpha(40));
            shadowPaint.ImageFilter = filter;

            canvas.DrawRoundRect(new SKRect(x, y, x + w, y + h), r, r, shadowPaint);
        }

        private void DrawModernStylesGDI(Graphics g, RectangleF rect, float t)
        {
            Color currentTrackColor = LerpColor(ToggleColorOff, ToggleColorOn, t);
            Color currentThumbColor = LerpColor(ThumbColorOff, ThumbColorOn, t);

            float padding = S(4f);
            float h = rect.Height;
            float thumbSize = h - (padding * 2);
            float minX = rect.Left + padding;
            float maxX = rect.Right - thumbSize - padding;
            float thumbX = minX + (maxX - minX) * t;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            switch (Style)
            {
                case ToggleStyle.Style1_Standard:
                    using (var path = GdiRenderer.CreateRoundedRectPath(rect, h / 2f))
                    using (var brush = new SolidBrush(currentTrackColor)) g.FillPath(brush, path);

                    using (var thumbBrush = new SolidBrush(currentThumbColor))
                        g.FillEllipse(thumbBrush, thumbX, rect.Top + padding, thumbSize, thumbSize);
                    break;

                case ToggleStyle.Style2_ThinTrack:
                    float trackH2 = h * 0.5f;
                    var trackRect2 = new RectangleF(rect.Left, rect.Top + (h - trackH2) / 2f, rect.Width, trackH2);
                    using (var path = GdiRenderer.CreateRoundedRectPath(trackRect2, trackH2 / 2f))
                    using (var brush = new SolidBrush(currentTrackColor)) g.FillPath(brush, path);

                    float thumbRadius2 = h * 0.8f;
                    float minX2 = rect.Left;
                    float maxX2 = rect.Right - thumbRadius2;
                    float thumbX2 = minX2 + (maxX2 - minX2) * t;

                    using (var thumbBrush = new SolidBrush(currentThumbColor))
                        g.FillEllipse(thumbBrush, thumbX2, rect.Top + (h - thumbRadius2) / 2f, thumbRadius2, thumbRadius2);
                    break;

                case ToggleStyle.Style3_LineTrack:
                    using (var pen = new Pen(currentTrackColor, S(4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLine(pen, rect.Left + padding, rect.Top + h / 2f, rect.Right - padding, rect.Top + h / 2f);

                    float thumbRadius3 = h * 0.7f;
                    float minX3 = rect.Left;
                    float maxX3 = rect.Right - thumbRadius3;
                    float thumbX3 = minX3 + (maxX3 - minX3) * t;

                    using (var thumbBrush = new SolidBrush(currentThumbColor))
                        g.FillEllipse(thumbBrush, thumbX3, rect.Top + (h - thumbRadius3) / 2f, thumbRadius3, thumbRadius3);
                    break;

                case ToggleStyle.Style4_Square:
                    using (var path = GdiRenderer.CreateRoundedRectPath(rect, S(4f)))
                    using (var brush = new SolidBrush(currentTrackColor)) g.FillPath(brush, path);

                    using (var pathT = GdiRenderer.CreateRoundedRectPath(new RectangleF(thumbX, rect.Top + padding, thumbSize, thumbSize), S(4f)))
                    using (var thumbBrush = new SolidBrush(currentThumbColor)) g.FillPath(thumbBrush, pathT);
                    break;

                case ToggleStyle.Style5_Text:
                case ToggleStyle.Style6_WideThumb:
                case ToggleStyle.Style7_Checkmark:
                case ToggleStyle.Style8_Ring:
                    using (var path = GdiRenderer.CreateRoundedRectPath(rect, h / 2f))
                    using (var brush = new SolidBrush(currentTrackColor)) g.FillPath(brush, path);

                    using (var thumbBrush = new SolidBrush(currentThumbColor))
                        g.FillEllipse(thumbBrush, thumbX, rect.Top + padding, thumbSize, thumbSize);
                    break;
            }
        }
    }
}