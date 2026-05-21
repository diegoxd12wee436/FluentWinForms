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

namespace FluentWinForms.Custom_Buttons
{
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
        Style9_MinimalDayNight,
        Weather_Legacy
    }

    [ToolboxItem(true)]
    [DesignTimeVisible(true)]
    [ToolboxBitmap(typeof(CheckBox))]
    [DefaultEvent("Click")]
    [DefaultProperty("IsChecked")]
    public class ModernThemeToggle : ModernControlBase
    {
        private float _toggleProgress = 0f;

        [Category("Toggle Appearance")]
        [Description("Define el estilo visual del Toggle basado en 9 diseños preestablecidos.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public ToggleStyle Style { get; set; } = ToggleStyle.Weather_Legacy;

        [Category("Modern Appearance")]
        [Description("Color de la pista cuando está activado.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color ToggleColorOn { get; set; } = Color.FromArgb(45, 140, 240); // 🔥 Color azul #2d8cf0

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

        private bool _useShadow = true;
        [Category("Modern Appearance")]
        [Description("Activa o desactiva la sombra del indicador (thumb) para máximo rendimiento.\nEnables or disables the thumb shadow for maximum performance.")]
        public bool UseShadow
        {
            get => _useShadow;
            set { _useShadow = value; Invalidate(); }
        }

        private int _maxFps = 120;
        [Category("Modern Appearance")]
        [DefaultValue(120)]
        [Description("Tope máximo visual de fotogramas, síncrono con AnimationManager vMax.")]
        public int MaxFps
        {
            get => _maxFps;
            set { _maxFps = Math.Max(30, Math.Min(240, value)); }
        }

        private float _animationDurationMs = 150f;
        [Category("Modern Appearance")]
        [DefaultValue(150f)]
        [Description("Duración total de la animación en milisegundos para sincronización por Delta Time.")]
        public float AnimationDurationMs
        {
            get => _animationDurationMs;
            set { _animationDurationMs = Math.Max(10f, value); }
        }

        private readonly SKColor _bgDaySK = SKColor.Parse("#3D7EAE");
        private readonly SKColor _bgNightSK = SKColor.Parse("#1D1F2C");
        private readonly SKColor _sunColorSK = SKColor.Parse("#ECCA2F");
        private readonly SKColor _moonColorSK = SKColor.Parse("#C4C9D1");
        private readonly SKColor _spotColorSK = SKColor.Parse("#959DB1");
        private readonly SKColor _cloudColorSK = SKColor.Parse("#F3FDFF");

        private readonly Color _bgDayGDI = Color.FromArgb(61, 126, 174);
        private readonly Color _bgNightGDI = Color.FromArgb(29, 31, 44);
        private readonly Color _sunColorGDI = Color.FromArgb(236, 202, 47);
        private readonly Color _moonColorGDI = Color.FromArgb(196, 201, 209);
        private readonly Color _spotColorGDI = Color.FromArgb(149, 157, 177);

        // 🔥 POOL DE MEMORIA (GAME DEV ZERO-ALLOCATION)
        private Bitmap? _bgCache;
        private SKBitmap? _acrylicCache;
        private SKBitmap? _rawWrapperBitmap;
        private bool _cacheDirty = true;

        private readonly SKPaint _skPaint = new SKPaint { IsAntialias = true };
        private readonly SKPath _skPath = new SKPath();

        private SKPaint? _skBlurPaint;
        private float _cachedBlurRadius = -1f; // PARCHE C: Control de radio dinámico

        private SKPaint? _skThumbShadowPaint;
        private SKMaskFilter? _skThumbMaskFilter;

        private SKPaint? _skAcrylicTintPaint;
        private SKPaint? _skAcrylicFallbackPaint;
        private SKTypeface? _segoeTypeface;

        private readonly SolidBrush _gdiBrush = new SolidBrush(Color.Transparent);
        private readonly Pen _gdiPen = new Pen(Color.Transparent, 1f);
        private readonly GraphicsPath _gdiPath = new GraphicsPath();
        private readonly GraphicsPath _gdiClipPath = new GraphicsPath();

        private Font? _gdiFont;
        private StringFormat? _gdiStringFormat;

        private Control? _lastParent; // PARCHE A: Rastreo del padre anterior

        public ModernThemeToggle()
        {
            this.MinimumSize = new Size(45, 22);
            Width = 45; Height = 22;
            BackgroundColor = Color.Transparent; BackgroundColor2 = Color.Transparent;
            CheckedColor = Color.Transparent; CheckedColor2 = Color.Transparent;
            HoverColor = Color.Transparent; HoverColor2 = Color.Transparent;
            PressColor = Color.Transparent; PressColor2 = Color.Transparent;
            BorderThickness = 0; FocusThickness = 0; UseRipple = false;

            // 🔥 INYECCIÓN ANTI-LAG LOCAL
            SetStyle(ControlStyles.StandardDoubleClick, false);
            SetStyle(ControlStyles.StandardClick, true);
        }

        // =====================================================================
        // PARCHE A: MANEJO SEGURO DEL PADRE (Evita Zombie Handlers / Leaks)
        // =====================================================================
        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);

            if (_lastParent != null)
            {
                try
                {
                    _lastParent.BackColorChanged -= SafeParentInvalidate;
                    _lastParent.BackgroundImageChanged -= SafeParentInvalidate;
                    _lastParent.Resize -= SafeParentInvalidate;
                }
                catch { }
            }

            _lastParent = this.Parent;

            if (_lastParent != null)
            {
                _lastParent.BackColorChanged += SafeParentInvalidate;
                _lastParent.BackgroundImageChanged += SafeParentInvalidate;
                _lastParent.Resize += SafeParentInvalidate;
            }

            _cacheDirty = true;
            Invalidate();
        }

        private void SafeParentInvalidate(object? sender, EventArgs e)
        {
            _cacheDirty = true;
            if (!DesignMode && IsHandleCreated && !IsDisposed)
            {
                Invalidate();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            BorderRadius = Height / 2f;

            if (_bgCache != null && (_bgCache.Width != Width || _bgCache.Height != Height))
            {
                try { AcrylicHelper.BitmapPool.Return(_bgCache); } catch { _bgCache.Dispose(); }
                _bgCache = null;
            }
            InvalidateCaches();
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            InvalidateCaches();
        }

        private void InvalidateCaches()
        {
            _cacheDirty = true;
        }

        // =====================================================================
        // PARCHE B: ANIMACIÓN 120FPS PRO (Síncrono a dt real y limitador MaxFps)
        // =====================================================================
        protected override bool CustomAnimationLoop(float dt, float step)
        {
            // dt: segundos desde el último frame. AnimationDurationMs: milisegundos.
            float durationSec = Math.Max(1f, AnimationDurationMs) / 1000f;
            float velocity = dt / durationSec; // fracción de la animación por frame

            // Clamp por MaxFps: normalizamos el dt si es demasiado rápido
            float minDt = 1f / Math.Max(30, Math.Min(240, MaxFps));
            if (dt < minDt) dt = minDt;

            float target = IsChecked ? 1f : 0f;
            bool isMoving = false;

            if (Math.Abs(_toggleProgress - target) > 0.001f)
            {
                if (_toggleProgress < target)
                {
                    _toggleProgress = Math.Min(target, _toggleProgress + velocity);
                    isMoving = true;
                }
                else
                {
                    _toggleProgress = Math.Max(target, _toggleProgress - velocity);
                    isMoving = true;
                }
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

            if (Parent != null && !DesignMode)
            {
                Invalidate();
            }
        }

        // =====================================================================
        // PARCHE C: GetOrCreateBlurPaint que respeta cambios de radio
        // =====================================================================
        private SKPaint GetOrCreateBlurPaint(float radius)
        {
            if (_skBlurPaint == null || Math.Abs(_cachedBlurRadius - radius) > 0.001f)
            {
                _skBlurPaint?.Dispose();
                _skBlurPaint = new SKPaint { IsAntialias = true, ImageFilter = SKImageFilter.CreateBlur(radius, radius) };
                _cachedBlurRadius = radius;
            }
            return _skBlurPaint;
        }

        private void RefreshBackgroundCache()
        {
            if (Parent == null || this.Width <= 0 || this.Height <= 0 || DesignMode)
            {
                _cacheDirty = true;
                return;
            }

            if (_bgCache != null && (_bgCache.Width != this.Width || _bgCache.Height != this.Height))
            {
                AcrylicHelper.BitmapPool.Return(_bgCache);
                _bgCache = null;
            }

            BitmapData? bmpData = null;
            try
            {
                _bgCache ??= AcrylicHelper.BitmapPool.Rent(this.Width, this.Height);

                using (Graphics g = Graphics.FromImage(_bgCache))
                {
                    g.Clear(Color.Transparent);
                    g.TranslateTransform(-this.Left, -this.Top);
                    using (var pe = new PaintEventArgs(g, Parent.ClientRectangle))
                    {
                        InvokePaintBackground(Parent, pe);
                    }
                }

                if (UseAcrylic)
                {
                    var rect = new Rectangle(0, 0, this.Width, this.Height);
                    bmpData = _bgCache.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

                    var info = new SKImageInfo(this.Width, this.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

                    _rawWrapperBitmap ??= new SKBitmap();
                    _rawWrapperBitmap.InstallPixels(info, bmpData.Scan0, bmpData.Stride);

                    if (_acrylicCache == null || _acrylicCache.Width != this.Width || _acrylicCache.Height != this.Height)
                    {
                        _acrylicCache?.Dispose();
                        _acrylicCache = new SKBitmap(info);
                    }

                    using (var skCanvas = new SKCanvas(_acrylicCache))
                    {
                        skCanvas.Clear(SKColors.Transparent);
                        var blurPaint = GetOrCreateBlurPaint(S(15f));
                        skCanvas.DrawBitmap(_rawWrapperBitmap, 0, 0, blurPaint);
                        skCanvas.Flush();
                    }
                }
                _cacheDirty = false;
            }
            catch
            {
                _cacheDirty = true;
            }
            finally
            {
                // PARCHE E: Asegurar Return al pool en RefreshBackgroundCache si algo falla
                if (bmpData != null && _bgCache != null)
                {
                    try { _bgCache.UnlockBits(bmpData); } catch { }
                }

                // Garantizar que el bitmap quede en el pool si no se logró estabilizar
                if (_bgCache != null && _cacheDirty)
                {
                    try { AcrylicHelper.BitmapPool.Return(_bgCache); _bgCache = null; } catch { /* fallback */ }
                }
            }
        }

        private bool _isPumpingBackground = false;
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Parent != null && BackColor == Color.Transparent && !_isPumpingBackground)
            {
                _isPumpingBackground = true;
                try
                {
                    if (_cacheDirty || _bgCache == null) RefreshBackgroundCache();
                    if (_bgCache != null) e.Graphics.DrawImageUnscaled(_bgCache, 0, 0);
                }
                finally
                {
                    _isPumpingBackground = false;
                }
            }
            else
            {
                base.OnPaintBackground(e);
            }
        }

        private void DrawAcrylicPlugAndPlay(SKCanvas canvas, SKRect rect)
        {
            if (!UseAcrylic || Parent == null || this.Width <= 0 || this.Height <= 0) return;
            if (_cacheDirty || _acrylicCache == null) RefreshBackgroundCache();

            if (_acrylicCache != null)
            {
                canvas.Save();

                _skPath.Reset();
                _skPath.AddRoundRect(rect, rect.Height / 2f, rect.Height / 2f);
                canvas.ClipPath(_skPath, SKClipOperation.Intersect, true);

                canvas.DrawBitmap(_acrylicCache, new SKRect(0, 0, this.Width, this.Height));

                _skAcrylicTintPaint ??= new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.White.WithAlpha(40), IsAntialias = true };
                canvas.DrawRect(rect, _skAcrylicTintPaint);

                canvas.Restore();
            }
            else
            {
                _skAcrylicFallbackPaint ??= new SKPaint { Color = SKColors.Black.WithAlpha(100), IsAntialias = true };
                canvas.DrawRoundRect(rect, rect.Height / 2f, rect.Height / 2f, _skAcrylicFallbackPaint);
            }
        }

        protected override void PaintSkia(SKCanvas canvas, SKRect contentRect, SKRect paddedRect)
        {
            _skPaint.Reset();
            _skPaint.IsAntialias = true;

            float t = Easing.Calculate(this.AnimationType, _toggleProgress);

            if (Style == ToggleStyle.Weather_Legacy) DrawWeatherLegacySkia(canvas, contentRect, t);
            else DrawModernStylesSkia(canvas, contentRect, t);
        }

        protected override void PaintGDIPipeline(Graphics g, RectangleF contentRect, RectangleF paddedRect)
        {
            float t = Easing.Calculate(this.AnimationType, _toggleProgress);
            if (Style == ToggleStyle.Weather_Legacy) DrawWeatherLegacyGDI(g, contentRect, t);
            else DrawModernStylesGDI(g, contentRect, t);
        }

        private void DrawWeatherLegacySkia(SKCanvas canvas, SKRect contentRect, float t)
        {
            Color cDay = Color.FromArgb(_bgDaySK.Alpha, _bgDaySK.Red, _bgDaySK.Green, _bgDaySK.Blue);
            Color cNight = Color.FromArgb(_bgNightSK.Alpha, _bgNightSK.Red, _bgNightSK.Green, _bgNightSK.Blue);
            Color currentBg = LerpColor(cDay, cNight, t);

            if (UseAcrylic) DrawAcrylicPlugAndPlay(canvas, contentRect);
            else
            {
                _skPaint.Color = new SKColor(currentBg.R, currentBg.G, currentBg.B, currentBg.A);
                canvas.DrawRoundRect(contentRect, contentRect.Height / 2, contentRect.Height / 2, _skPaint);
            }

            _skPaint.Style = SKPaintStyle.Stroke;
            _skPaint.StrokeWidth = S(2);
            _skPaint.Color = SKColors.Black.WithAlpha(30);
            canvas.DrawRoundRect(contentRect, contentRect.Height / 2, contentRect.Height / 2, _skPaint);
            _skPaint.Style = SKPaintStyle.Fill;

            if (t > 0.1f)
            {
                float starYOffset = (1f - t) * -(contentRect.Height);
                _skPaint.Color = SKColors.White.WithAlpha((byte)(255 * t));
                canvas.DrawCircle(contentRect.Left + S(20), contentRect.Top + S(10) + starYOffset, S(1.5f), _skPaint);
                canvas.DrawCircle(contentRect.Left + S(35), contentRect.Top + S(22) + starYOffset, S(1f), _skPaint);
                canvas.DrawCircle(contentRect.Left + S(15), contentRect.Top + S(28) + starYOffset, S(2f), _skPaint);
                canvas.DrawCircle(contentRect.Left + S(45), contentRect.Top + S(12) + starYOffset, S(1f), _skPaint);
            }

            if (t < 0.9f)
            {
                float cloudYOffset = t * contentRect.Height;
                _skPaint.Color = _cloudColorSK.WithAlpha((byte)(255 * (1f - t)));
                canvas.DrawCircle(contentRect.Right - S(25), contentRect.Bottom + S(2) + cloudYOffset, S(12), _skPaint);
                canvas.DrawCircle(contentRect.Right - S(10), contentRect.Bottom + S(8) + cloudYOffset, S(16), _skPaint);
                canvas.DrawCircle(contentRect.Right - S(40), contentRect.Bottom + S(10) + cloudYOffset, S(10), _skPaint);
            }

            float thumbPadding = S(4f);
            float thumbSize = contentRect.Height - (thumbPadding * 2);
            float minX = contentRect.Left + thumbPadding;
            float maxX = contentRect.Right - thumbSize - thumbPadding;
            float currentX = minX + (maxX - minX) * t;

            _skPaint.Color = SKColors.White.WithAlpha(20);
            canvas.DrawCircle(currentX + (thumbSize / 2), contentRect.MidY, (thumbSize / 2) + S(6), _skPaint);
            _skPaint.Color = SKColors.White.WithAlpha(10);
            canvas.DrawCircle(currentX + (thumbSize / 2), contentRect.MidY, (thumbSize / 2) + S(12), _skPaint);

            var thumbRect = new SKRect(currentX, contentRect.Top + thumbPadding, currentX + thumbSize, contentRect.Top + thumbPadding + thumbSize);

            canvas.Save();
            _skPath.Reset();
            _skPath.AddOval(thumbRect);
            canvas.ClipPath(_skPath, SKClipOperation.Intersect, true);

            _skPaint.Color = _sunColorSK;
            canvas.DrawRect(thumbRect, _skPaint);
            float moonOffsetX = thumbSize * (1f - t);
            var moonRect = new SKRect(thumbRect.Left + moonOffsetX, thumbRect.Top, thumbRect.Right + moonOffsetX, thumbRect.Bottom);
            _skPaint.Color = _moonColorSK;
            canvas.DrawOval(moonRect, _skPaint);

            _skPaint.Color = _spotColorSK;
            canvas.DrawCircle(moonRect.Left + S(8), moonRect.Top + S(8), S(3f), _skPaint);
            canvas.DrawCircle(moonRect.Left + S(18), moonRect.Top + S(16), S(4.5f), _skPaint);
            canvas.DrawCircle(moonRect.Left + S(10), moonRect.Top + S(22), S(2f), _skPaint);
            canvas.Restore();

            if (UseShadow)
            {
                _skPaint.Style = SKPaintStyle.Stroke;
                _skPaint.StrokeWidth = S(1);
                _skPaint.Color = SKColors.Black.WithAlpha(20);
                canvas.DrawOval(thumbRect, _skPaint);
            }
        }

        private void DrawModernStylesSkia(SKCanvas canvas, SKRect rect, float t)
        {
            Color currentTrackColor = LerpColor(ToggleColorOff, ToggleColorOn, t);
            Color currentThumbColor = LerpColor(ThumbColorOff, ThumbColorOn, t);

            _skPaint.Style = SKPaintStyle.Fill;
            _skPaint.Color = currentTrackColor.ToSKColor();

            float padding = S(4f);
            float h = rect.Height;
            float thumbSize = h - (padding * 2);
            float minX = rect.Left + padding;
            float maxX = rect.Right - thumbSize - padding;
            float thumbX = minX + (maxX - minX) * t;

            switch (Style)
            {
                case ToggleStyle.Style1_Standard:
                    if (UseAcrylic) DrawAcrylicPlugAndPlay(canvas, rect);
                    else canvas.DrawRoundRect(rect, h / 2f, h / 2f, _skPaint);

                    if (UseShadow) DrawThumbShadowSkia(canvas, thumbX, rect.Top + padding, thumbSize, thumbSize, thumbSize / 2f);

                    _skPaint.Color = currentThumbColor.ToSKColor();
                    canvas.DrawOval(thumbX + thumbSize / 2f, rect.MidY, thumbSize / 2f, thumbSize / 2f, _skPaint);
                    break;
                case ToggleStyle.Style2_ThinTrack:
                    float trackH2 = h * 0.5f;
                    var trackRect2 = new SKRect(rect.Left, rect.MidY - trackH2 / 2f, rect.Right, rect.MidY + trackH2 / 2f);
                    if (UseAcrylic) DrawAcrylicPlugAndPlay(canvas, trackRect2);
                    else canvas.DrawRoundRect(trackRect2, trackH2 / 2f, trackH2 / 2f, _skPaint);
                    float thumbRadius2 = (h * 0.8f) / 2f;
                    float minX2 = rect.Left + thumbRadius2;
                    float maxX2 = rect.Right - thumbRadius2;
                    float thumbX2 = minX2 + (maxX2 - minX2) * t;

                    if (UseShadow) DrawThumbShadowSkia(canvas, thumbX2 - thumbRadius2, rect.MidY - thumbRadius2, thumbRadius2 * 2, thumbRadius2 * 2, thumbRadius2);

                    _skPaint.Color = currentThumbColor.ToSKColor();
                    canvas.DrawCircle(thumbX2, rect.MidY, thumbRadius2, _skPaint);
                    break;

                case ToggleStyle.Style3_LineTrack:
                    _skPaint.Style = SKPaintStyle.Stroke;
                    _skPaint.StrokeWidth = S(4f);
                    _skPaint.StrokeCap = SKStrokeCap.Round;
                    canvas.DrawLine(rect.Left + padding, rect.MidY, rect.Right - padding, rect.MidY, _skPaint);
                    _skPaint.Style = SKPaintStyle.Fill;
                    float thumbRadius3 = (h * 0.7f) / 2f;
                    float minX3 = rect.Left + thumbRadius3;
                    float maxX3 = rect.Right - thumbRadius3;
                    float thumbX3 = minX3 + (maxX3 - minX3) * t;

                    if (UseShadow) DrawThumbShadowSkia(canvas, thumbX3 - thumbRadius3, rect.MidY - thumbRadius3, thumbRadius3 * 2, thumbRadius3 * 2, thumbRadius3);

                    _skPaint.Color = currentThumbColor.ToSKColor();
                    canvas.DrawCircle(thumbX3, rect.MidY, thumbRadius3, _skPaint);
                    break;

                case ToggleStyle.Style4_Square:
                    if (UseAcrylic) DrawAcrylicPlugAndPlay(canvas, rect);
                    else canvas.DrawRoundRect(rect, S(4f), S(4f), _skPaint);

                    if (UseShadow) DrawThumbShadowSkia(canvas, thumbX, rect.Top + padding, thumbSize, thumbSize, S(2f));

                    _skPaint.Color = currentThumbColor.ToSKColor();
                    canvas.DrawRoundRect(new SKRect(thumbX, rect.Top + padding, thumbX + thumbSize, rect.Top + padding + thumbSize), S(4f), S(4f), _skPaint);
                    break;
                case ToggleStyle.Style5_Text:
                    if (UseAcrylic) DrawAcrylicPlugAndPlay(canvas, rect);
                    else canvas.DrawRoundRect(rect, S(6f), S(6f), _skPaint);
                    _skPaint.Color = SKColors.White.WithAlpha(180);
                    _skPaint.TextSize = h * 0.4f;
                    _skPaint.TextAlign = SKTextAlign.Center;

                    if (_segoeTypeface == null)
                    {
                        _segoeTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
                    }
                    _skPaint.Typeface = _segoeTypeface;

                    if (t > 0.5f) canvas.DrawText("ON", rect.Left + (rect.Width / 4f), rect.MidY - (_skPaint.FontMetrics.Ascent / 2f), _skPaint);
                    else canvas.DrawText("OFF", rect.Right - (rect.Width / 4f), rect.MidY - (_skPaint.FontMetrics.Ascent / 2f), _skPaint);

                    if (UseShadow) DrawThumbShadowSkia(canvas, thumbX, rect.Top + padding, thumbSize, thumbSize, S(4f));

                    _skPaint.Color = currentThumbColor.ToSKColor();
                    canvas.DrawRoundRect(new SKRect(thumbX, rect.Top + padding, thumbX + thumbSize, rect.Top + padding + thumbSize), S(4f), S(4f), _skPaint);
                    break;
                case ToggleStyle.Style6_WideThumb:
                    if (UseAcrylic) DrawAcrylicPlugAndPlay(canvas, rect);
                    else canvas.DrawRoundRect(rect, h / 2f, h / 2f, _skPaint);
                    float maxAllowedWidth = rect.Width - (padding * 2);
                    float wideThumbWidth = Math.Min(thumbSize * 1.5f, maxAllowedWidth * 0.8f);
                    float maxX6 = rect.Right - wideThumbWidth - padding;
                    float thumbX6 = minX + (maxX6 - minX) * t;

                    if (UseShadow) DrawThumbShadowSkia(canvas, thumbX6, rect.Top + padding, wideThumbWidth, thumbSize, thumbSize / 2f);

                    _skPaint.Color = currentThumbColor.ToSKColor();
                    canvas.DrawRoundRect(new SKRect(thumbX6, rect.Top + padding, thumbX6 + wideThumbWidth, rect.Top + padding + thumbSize), thumbSize / 2f, thumbSize / 2f, _skPaint);
                    break;

                case ToggleStyle.Style7_Checkmark:
                    if (UseAcrylic) DrawAcrylicPlugAndPlay(canvas, rect);
                    else canvas.DrawRoundRect(rect, h / 2f, h / 2f, _skPaint);

                    if (UseShadow) DrawThumbShadowSkia(canvas, thumbX, rect.Top + padding, thumbSize, thumbSize, thumbSize / 2f);

                    _skPaint.Color = currentThumbColor.ToSKColor();
                    canvas.DrawOval(thumbX + thumbSize / 2f, rect.MidY, thumbSize / 2f, thumbSize / 2f, _skPaint);
                    _skPaint.Style = SKPaintStyle.Stroke;
                    _skPaint.StrokeWidth = S(2f);
                    _skPaint.StrokeCap = SKStrokeCap.Round;
                    _skPaint.Color = currentTrackColor.ToSKColor();
                    if (t > 0.5f)
                    {
                        canvas.DrawLine(thumbX + thumbSize * 0.3f, rect.MidY, thumbX + thumbSize * 0.45f, rect.MidY + thumbSize * 0.15f, _skPaint);
                        canvas.DrawLine(thumbX + thumbSize * 0.45f, rect.MidY + thumbSize * 0.15f, thumbX + thumbSize * 0.7f, rect.MidY - thumbSize * 0.15f, _skPaint);
                    }
                    else
                    {
                        canvas.DrawLine(thumbX + thumbSize * 0.35f, rect.MidY - thumbSize * 0.15f, thumbX + thumbSize * 0.65f, rect.MidY + thumbSize * 0.15f, _skPaint);
                        canvas.DrawLine(thumbX + thumbSize * 0.65f, rect.MidY - thumbSize * 0.15f, thumbX + thumbSize * 0.35f, rect.MidY + thumbSize * 0.15f, _skPaint);
                    }
                    break;
                case ToggleStyle.Style8_Ring:
                    _skPaint.Color = LerpColor(Color.FromArgb(80, 80, 80), ToggleColorOn, t).ToSKColor();
                    if (UseAcrylic) DrawAcrylicPlugAndPlay(canvas, rect);
                    else canvas.DrawRoundRect(rect, h / 2f, h / 2f, _skPaint);
                    _skPaint.Color = currentThumbColor.ToSKColor();
                    canvas.DrawOval(thumbX + thumbSize / 2f, rect.MidY, thumbSize / 2f, thumbSize / 2f, _skPaint);
                    _skPaint.Color = LerpColor(Color.FromArgb(80, 80, 80), ToggleColorOn, t).ToSKColor();
                    float innerRad = thumbSize * 0.25f;
                    canvas.DrawOval(thumbX + thumbSize / 2f, rect.MidY, innerRad, innerRad, _skPaint);
                    break;
                case ToggleStyle.Style9_MinimalDayNight:
                    Color darkBg = Color.FromArgb(40, 41, 44);
                    Color lightBg = Color.FromArgb(216, 219, 224);
                    Color trackC = LerpColor(darkBg, lightBg, t);

                    _skPaint.Color = trackC.ToSKColor();
                    if (UseAcrylic) DrawAcrylicPlugAndPlay(canvas, rect);
                    else canvas.DrawRoundRect(rect, h / 2f, h / 2f, _skPaint);

                    float thumbR9 = h * 0.35f;
                    float padding9 = (h - (thumbR9 * 2)) / 2f;
                    float minX9 = rect.Left + padding9 + thumbR9;
                    float maxX9 = rect.Right - padding9 - thumbR9;
                    float thumbX9 = minX9 + (maxX9 - minX9) * t;

                    if (UseShadow) DrawThumbShadowSkia(canvas, thumbX9 - thumbR9, rect.MidY - thumbR9, thumbR9 * 2, thumbR9 * 2, thumbR9);

                    canvas.Save();
                    _skPath.Reset();
                    _skPath.AddCircle(thumbX9, rect.MidY, thumbR9);
                    canvas.ClipPath(_skPath, SKClipOperation.Intersect, true);

                    _skPaint.Color = lightBg.ToSKColor();
                    canvas.DrawRect(new SKRect(thumbX9 - thumbR9, rect.MidY - thumbR9, thumbX9 + thumbR9, rect.MidY + thumbR9), _skPaint);
                    float offset9 = thumbR9 * 0.9f * (1f - t);
                    _skPaint.Color = darkBg.ToSKColor();
                    canvas.DrawCircle(thumbX9 - offset9, rect.MidY - (offset9 * 0.2f), thumbR9, _skPaint);

                    canvas.Restore();
                    break;
            }
        }

        private void DrawThumbShadowSkia(SKCanvas canvas, float x, float y, float w, float h, float r)
        {
            if (_skThumbShadowPaint == null)
            {
                _skThumbShadowPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.Black.WithAlpha(40) };
            }
            if (_skThumbMaskFilter == null)
            {
                _skThumbMaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, S(2f));
            }

            _skThumbShadowPaint.MaskFilter = _skThumbMaskFilter;
            canvas.DrawRoundRect(new SKRect(x, y + S(2f), x + w, y + h + S(2f)), r, r, _skThumbShadowPaint);
        }

        private void AddRoundedRect(GraphicsPath path, RectangleF bounds, float radius)
        {
            float d = radius * 2f;
            if (d <= 0) { path.AddRectangle(bounds); return; }
            var size = new SizeF(d, d);
            var arc = new RectangleF(bounds.Location, size);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - d;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - d;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
        }

        private void DrawWeatherLegacyGDI(Graphics g, RectangleF contentRect, float t)
        {
            Color currentBg = LerpColor(_bgDayGDI, _bgNightGDI, t);
            float radius = contentRect.Height / 2f;

            _gdiPath.Reset();
            AddRoundedRect(_gdiPath, contentRect, radius);

            _gdiBrush.Color = currentBg;
            g.FillPath(_gdiBrush, _gdiPath);

            var clipState = g.Save();

            _gdiClipPath.Reset();
            AddRoundedRect(_gdiClipPath, contentRect, radius);
            g.SetClip(_gdiClipPath, CombineMode.Intersect);

            if (t > 0.1f)
            {
                float starYOffset = (1f - t) * -contentRect.Height;
                _gdiBrush.Color = Color.FromArgb((int)(255 * t), Color.White);
                g.FillEllipse(_gdiBrush, contentRect.Left + S(20), contentRect.Top + S(10) + starYOffset, S(3f), S(3f));
                g.FillEllipse(_gdiBrush, contentRect.Left + S(35), contentRect.Top + S(22) + starYOffset, S(2f), S(2f));
                g.FillEllipse(_gdiBrush, contentRect.Left + S(15), contentRect.Top + S(28) + starYOffset, S(4f), S(4f));
                g.FillEllipse(_gdiBrush, contentRect.Left + S(45), contentRect.Top + S(12) + starYOffset, S(2f), S(2f));
            }

            if (t < 0.9f)
            {
                float cloudYOffset = t * contentRect.Height;
                _gdiBrush.Color = Color.FromArgb((int)(255 * (1f - t)), 243, 253, 255);
                g.FillEllipse(_gdiBrush, contentRect.Right - S(25) - S(12), contentRect.Bottom + S(2) + cloudYOffset - S(12), S(24), S(24));
                g.FillEllipse(_gdiBrush, contentRect.Right - S(10) - S(16), contentRect.Bottom + S(8) + cloudYOffset - S(16), S(32), S(32));
                g.FillEllipse(_gdiBrush, contentRect.Right - S(40) - S(10), contentRect.Bottom + S(10) + cloudYOffset - S(10), S(20), S(20));
            }

            float thumbPadding = S(4f);
            float thumbSize = contentRect.Height - (thumbPadding * 2);
            float minX = contentRect.Left + thumbPadding;
            float maxX = contentRect.Right - thumbSize - thumbPadding;
            float currentX = minX + (maxX - minX) * t;

            float midY = contentRect.Top + (contentRect.Height / 2f);
            float thumbCenterX = currentX + (thumbSize / 2f);

            _gdiBrush.Color = Color.FromArgb(20, Color.White);
            g.FillEllipse(_gdiBrush, thumbCenterX - (thumbSize / 2 + S(6)), midY - (thumbSize / 2 + S(6)), thumbSize + S(12), thumbSize + S(12));
            _gdiBrush.Color = Color.FromArgb(10, Color.White);
            g.FillEllipse(_gdiBrush, thumbCenterX - (thumbSize / 2 + S(12)), midY - (thumbSize / 2 + S(12)), thumbSize + S(24), thumbSize + S(24));

            var thumbRect = new RectangleF(currentX, contentRect.Top + thumbPadding, thumbSize, thumbSize);

            var oldState = g.Save();
            _gdiPath.Reset();
            _gdiPath.AddEllipse(thumbRect);
            g.SetClip(_gdiPath, CombineMode.Intersect);

            _gdiBrush.Color = _sunColorGDI;
            g.FillRectangle(_gdiBrush, thumbRect);
            float moonOffsetX = thumbSize * (1f - t);
            var moonRect = new RectangleF(thumbRect.Left + moonOffsetX, thumbRect.Top, thumbSize, thumbSize);
            _gdiBrush.Color = _moonColorGDI;
            g.FillEllipse(_gdiBrush, moonRect);
            _gdiBrush.Color = _spotColorGDI;
            g.FillEllipse(_gdiBrush, moonRect.Left + S(8), moonRect.Top + S(8), S(6f), S(6f));
            g.FillEllipse(_gdiBrush, moonRect.Left + S(18), moonRect.Top + S(16), S(9f), S(9f));
            g.FillEllipse(_gdiBrush, moonRect.Left + S(10), moonRect.Top + S(22), S(4f), S(4f));

            g.Restore(oldState);

            if (UseShadow)
            {
                _gdiPen.Color = Color.FromArgb(20, Color.Black);
                _gdiPen.Width = S(1);
                g.DrawEllipse(_gdiPen, thumbRect);
            }

            g.Restore(clipState);

            if (UseShadow) GdiRenderer.DrawInnerShadow(g, contentRect, radius, Color.FromArgb(30, 0, 0, 0), S(2));
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
            float midY = rect.Top + (h / 2f);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            switch (Style)
            {
                case ToggleStyle.Style1_Standard:
                    _gdiPath.Reset();
                    AddRoundedRect(_gdiPath, rect, h / 2f);
                    _gdiBrush.Color = currentTrackColor;
                    g.FillPath(_gdiBrush, _gdiPath);

                    _gdiBrush.Color = currentThumbColor;
                    g.FillEllipse(_gdiBrush, thumbX, rect.Top + padding, thumbSize, thumbSize);
                    break;

                case ToggleStyle.Style2_ThinTrack:
                    float trackH2 = h * 0.5f;
                    var trackRect2 = new RectangleF(rect.Left, rect.Top + (h - trackH2) / 2f, rect.Width, trackH2);
                    _gdiPath.Reset();
                    AddRoundedRect(_gdiPath, trackRect2, trackH2 / 2f);
                    _gdiBrush.Color = currentTrackColor;
                    g.FillPath(_gdiBrush, _gdiPath);

                    float thumbRadius2 = h * 0.8f;
                    float minX2 = rect.Left;
                    float maxX2 = rect.Right - thumbRadius2;
                    float thumbX2 = minX2 + (maxX2 - minX2) * t;

                    _gdiBrush.Color = currentThumbColor;
                    g.FillEllipse(_gdiBrush, thumbX2, rect.Top + (h - thumbRadius2) / 2f, thumbRadius2, thumbRadius2);
                    break;

                case ToggleStyle.Style3_LineTrack:
                    _gdiPen.Color = currentTrackColor;
                    _gdiPen.Width = S(4f);
                    _gdiPen.StartCap = LineCap.Round;
                    _gdiPen.EndCap = LineCap.Round;
                    g.DrawLine(_gdiPen, rect.Left + padding, midY, rect.Right - padding, midY);

                    float thumbRadius3 = h * 0.7f;
                    float minX3 = rect.Left;
                    float maxX3 = rect.Right - thumbRadius3;
                    float thumbX3 = minX3 + (maxX3 - minX3) * t;

                    _gdiBrush.Color = currentThumbColor;
                    g.FillEllipse(_gdiBrush, thumbX3, rect.Top + (h - thumbRadius3) / 2f, thumbRadius3, thumbRadius3);
                    break;

                case ToggleStyle.Style4_Square:
                    _gdiPath.Reset();
                    AddRoundedRect(_gdiPath, rect, S(4f));
                    _gdiBrush.Color = currentTrackColor;
                    g.FillPath(_gdiBrush, _gdiPath);

                    _gdiPath.Reset();
                    AddRoundedRect(_gdiPath, new RectangleF(thumbX, rect.Top + padding, thumbSize, thumbSize), S(4f));
                    _gdiBrush.Color = currentThumbColor;
                    g.FillPath(_gdiBrush, _gdiPath);
                    break;

                case ToggleStyle.Style5_Text:
                    _gdiPath.Reset();
                    AddRoundedRect(_gdiPath, rect, S(6f));
                    _gdiBrush.Color = currentTrackColor;
                    g.FillPath(_gdiBrush, _gdiPath);

                    if (_gdiFont == null || _gdiFont.Size != h * 0.4f)
                    {
                        _gdiFont?.Dispose();
                        _gdiFont = new Font("Segoe UI", h * 0.4f, FontStyle.Bold, GraphicsUnit.Pixel);
                    }
                    if (_gdiStringFormat == null)
                    {
                        _gdiStringFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    }

                    _gdiBrush.Color = Color.FromArgb(180, 255, 255, 255);
                    if (t > 0.5f) g.DrawString("ON", _gdiFont, _gdiBrush, new RectangleF(rect.Left, rect.Top, rect.Width / 2f, rect.Height), _gdiStringFormat);
                    else g.DrawString("OFF", _gdiFont, _gdiBrush, new RectangleF(rect.Left + rect.Width / 2f, rect.Top, rect.Width / 2f, rect.Height), _gdiStringFormat);

                    _gdiPath.Reset();
                    AddRoundedRect(_gdiPath, new RectangleF(thumbX, rect.Top + padding, thumbSize, thumbSize), S(4f));
                    _gdiBrush.Color = currentThumbColor;
                    g.FillPath(_gdiBrush, _gdiPath);
                    break;

                case ToggleStyle.Style6_WideThumb:
                    _gdiPath.Reset();
                    AddRoundedRect(_gdiPath, rect, h / 2f);
                    _gdiBrush.Color = currentTrackColor;
                    g.FillPath(_gdiBrush, _gdiPath);

                    float maxAllowedWidth = rect.Width - (padding * 2);
                    float wideThumbWidth = Math.Min(thumbSize * 1.5f, maxAllowedWidth * 0.8f);
                    float maxX6 = rect.Right - wideThumbWidth - padding;
                    float thumbX6 = minX + (maxX6 - minX) * t;

                    _gdiPath.Reset();
                    AddRoundedRect(_gdiPath, new RectangleF(thumbX6, rect.Top + padding, wideThumbWidth, thumbSize), thumbSize / 2f);
                    _gdiBrush.Color = currentThumbColor;
                    g.FillPath(_gdiBrush, _gdiPath);
                    break;

                case ToggleStyle.Style7_Checkmark:
                    _gdiPath.Reset();
                    AddRoundedRect(_gdiPath, rect, h / 2f);
                    _gdiBrush.Color = currentTrackColor;
                    g.FillPath(_gdiBrush, _gdiPath);

                    _gdiBrush.Color = currentThumbColor;
                    g.FillEllipse(_gdiBrush, thumbX, rect.Top + padding, thumbSize, thumbSize);

                    _gdiPen.Color = currentTrackColor;
                    _gdiPen.Width = S(2f);
                    _gdiPen.StartCap = LineCap.Round;
                    _gdiPen.EndCap = LineCap.Round;
                    if (t > 0.5f)
                    {
                        g.DrawLine(_gdiPen, thumbX + thumbSize * 0.3f, midY, thumbX + thumbSize * 0.45f, midY + thumbSize * 0.15f);
                        g.DrawLine(_gdiPen, thumbX + thumbSize * 0.45f, midY + thumbSize * 0.15f, thumbX + thumbSize * 0.7f, midY - thumbSize * 0.15f);
                    }
                    else
                    {
                        g.DrawLine(_gdiPen, thumbX + thumbSize * 0.35f, midY - thumbSize * 0.15f, thumbX + thumbSize * 0.65f, midY + thumbSize * 0.15f);
                        g.DrawLine(_gdiPen, thumbX + thumbSize * 0.65f, midY - thumbSize * 0.15f, thumbX + thumbSize * 0.35f, midY + thumbSize * 0.15f);
                    }
                    break;

                case ToggleStyle.Style8_Ring:
                    Color ringColor = LerpColor(Color.FromArgb(80, 80, 80), ToggleColorOn, t);
                    _gdiPath.Reset();
                    AddRoundedRect(_gdiPath, rect, h / 2f);
                    _gdiBrush.Color = ringColor;
                    g.FillPath(_gdiBrush, _gdiPath);

                    _gdiBrush.Color = currentThumbColor;
                    g.FillEllipse(_gdiBrush, thumbX, rect.Top + padding, thumbSize, thumbSize);

                    float innerRad = thumbSize * 0.5f;
                    float innerOffset = (thumbSize - innerRad) / 2f;
                    _gdiBrush.Color = ringColor;
                    g.FillEllipse(_gdiBrush, thumbX + innerOffset, rect.Top + padding + innerOffset, innerRad, innerRad);
                    break;

                case ToggleStyle.Style9_MinimalDayNight:
                    Color darkBgGDI = Color.FromArgb(40, 41, 44);
                    Color lightBgGDI = Color.FromArgb(216, 219, 224);
                    Color trackCGDI = LerpColor(darkBgGDI, lightBgGDI, t);

                    _gdiPath.Reset();
                    AddRoundedRect(_gdiPath, rect, h / 2f);
                    _gdiBrush.Color = trackCGDI;
                    g.FillPath(_gdiBrush, _gdiPath);

                    float thumbR9 = h * 0.35f;
                    float padding9 = (h - (thumbR9 * 2)) / 2f;
                    float minX9 = rect.Left + padding9 + thumbR9;
                    float maxX9 = rect.Right - padding9 - thumbR9;
                    float thumbX9 = minX9 + (maxX9 - minX9) * t;

                    var oldState9 = g.Save();

                    _gdiClipPath.Reset();
                    _gdiClipPath.AddEllipse(thumbX9 - thumbR9, midY - thumbR9, thumbR9 * 2, thumbR9 * 2);
                    g.SetClip(_gdiClipPath, CombineMode.Intersect);

                    _gdiBrush.Color = lightBgGDI;
                    g.FillRectangle(_gdiBrush, thumbX9 - thumbR9, midY - thumbR9, thumbR9 * 2, thumbR9 * 2);

                    float offset9 = thumbR9 * 0.9f * (1f - t);
                    _gdiBrush.Color = darkBgGDI;
                    g.FillEllipse(_gdiBrush, thumbX9 - offset9 - thumbR9, midY - (offset9 * 0.2f) - thumbR9, thumbR9 * 2, thumbR9 * 2);

                    g.Restore(oldState9);
                    break;
            }
        }

        // =====================================================================
        // PARCHE D & E: LIMPIEZA ABSOLUTA DE CACHÉS Y DISPOSE SEGURO
        // =====================================================================
        private void ClearCaches()
        {
            try
            {
                if (_bgCache != null)
                {
                    try { AcrylicHelper.BitmapPool.Return(_bgCache); }
                    catch { _bgCache.Dispose(); }
                    _bgCache = null;
                }
            }
            catch { }

            try { _acrylicCache?.Dispose(); _acrylicCache = null; } catch { }
            try { _rawWrapperBitmap?.Dispose(); _rawWrapperBitmap = null; } catch { }
            try { _skBlurPaint?.Dispose(); _skBlurPaint = null; } catch { }
            try { _skThumbShadowPaint?.Dispose(); _skThumbShadowPaint = null; } catch { }
            try { _skThumbMaskFilter?.Dispose(); _skThumbMaskFilter = null; } catch { }
            try { _skAcrylicTintPaint?.Dispose(); _skAcrylicTintPaint = null; } catch { }
            try { _skAcrylicFallbackPaint?.Dispose(); _skAcrylicFallbackPaint = null; } catch { }
            try { _segoeTypeface?.Dispose(); _segoeTypeface = null; } catch { }

            // Objetos base readonly
            try { _skPaint.Dispose(); } catch { }
            try { _skPath.Dispose(); } catch { }
            try { _gdiBrush?.Dispose(); } catch { }
            try { _gdiPen?.Dispose(); } catch { }
            try { _gdiPath?.Dispose(); } catch { }
            try { _gdiClipPath?.Dispose(); } catch { }
            try { _gdiFont?.Dispose(); _gdiFont = null; } catch { }
            try { _gdiStringFormat?.Dispose(); _gdiStringFormat = null; } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Limpiar eventos del último padre antes de morir
                if (_lastParent != null)
                {
                    try
                    {
                        _lastParent.BackColorChanged -= SafeParentInvalidate;
                        _lastParent.BackgroundImageChanged -= SafeParentInvalidate;
                        _lastParent.Resize -= SafeParentInvalidate;
                    }
                    catch { }
                }

                try { ClearCaches(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}