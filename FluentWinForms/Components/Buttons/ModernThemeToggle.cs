#nullable enable
using FluentWinForms.Core;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices; // 🔥 INYECCIÓN 1: Necesario para CopyMemory P/Invoke
using System.Threading;
using System.Threading.Tasks;
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
        // 🔥 INYECCIÓN 2: CopyMemory P/Invoke — misma velocidad que unsafe, cero flags de antivirus
        [DllImport("kernel32.dll", SetLastError = false)]
        private static extern void RtlMoveMemory(IntPtr destination, IntPtr source, UIntPtr length);

        private float _toggleProgress = 0f;

        #region Ocultar propiedades del base innecesarias para el Toggle
        // ── Acrylic — Solo funciona en ModernForm, oculto en controles ──────
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new bool UseAcrylic { get => base.UseAcrylic; set => base.UseAcrylic = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Color AcrylicTintColor { get => base.AcrylicTintColor; set => base.AcrylicTintColor = value; }

        // ── Modern - Animations ──────────────────────────────────────────
        // AnimationSpeed del base es reemplazado por AnimationDurationMs del Toggle
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new float AnimationSpeed { get => base.AnimationSpeed; set => base.AnimationSpeed = value; }

        // UseRipple no aplica al Toggle — bugea
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new bool UseRipple { get => base.UseRipple; set => base.UseRipple = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Color RippleColor { get => base.RippleColor; set => base.RippleColor = value; }

        // ── Modern - Appearance ──────────────────────────────────────────
        // El Toggle maneja sus propios colores — los del base no aplican
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Color BackgroundColor { get => base.BackgroundColor; set => base.BackgroundColor = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Color BackgroundColor2 { get => base.BackgroundColor2; set => base.BackgroundColor2 = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new bool UseGradient { get => base.UseGradient; set => base.UseGradient = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Color BorderColor { get => base.BorderColor; set => base.BorderColor = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new float BorderThickness { get => base.BorderThickness; set => base.BorderThickness = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new virtual float Opacity { get => base.Opacity; set => base.Opacity = value; }

        // ── Modern - Transform ───────────────────────────────────────────
        // Rotation y Scale rompen el blur del Toggle — deben estar ocultos
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new float Rotation { get => base.Rotation; set => base.Rotation = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new float ScaleX { get => base.ScaleX; set => base.ScaleX = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new float ScaleY { get => base.ScaleY; set => base.ScaleY = value; }

        // ── Modern - Effects (Sombra) ─────────────────────────────────────
        // El Toggle tiene su propio UseShadow — ocultar los del base
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Color ShadowColor { get => base.ShadowColor; set => base.ShadowColor = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new float ShadowOpacity { get => base.ShadowOpacity; set => base.ShadowOpacity = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new float ShadowOffsetX { get => base.ShadowOffsetX; set => base.ShadowOffsetX = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new float ShadowOffsetY { get => base.ShadowOffsetY; set => base.ShadowOffsetY = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new float ShadowBlur { get => base.ShadowBlur; set => base.ShadowBlur = value; }

        // ── Modern - States ───────────────────────────────────────────────
        // Hover/Press colors no aplican al Toggle — tiene animación propia
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Color HoverColor { get => base.HoverColor; set => base.HoverColor = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Color HoverColor2 { get => base.HoverColor2; set => base.HoverColor2 = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Color PressColor { get => base.PressColor; set => base.PressColor = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Color PressColor2 { get => base.PressColor2; set => base.PressColor2 = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Color CheckedColor { get => base.CheckedColor; set => base.CheckedColor = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Color CheckedColor2 { get => base.CheckedColor2; set => base.CheckedColor2 = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Color FocusColor { get => base.FocusColor; set => base.FocusColor = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new float FocusThickness { get => base.FocusThickness; set => base.FocusThickness = value; }

        // ── ModernForms - Text ────────────────────────────────────────────
        // El Toggle no usa texto del base
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Color TextColor { get => base.TextColor; set => base.TextColor = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new float FontSize { get => base.FontSize; set => base.FontSize = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new string FontFamily { get => base.FontFamily; set => base.FontFamily = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new bool FontWeightBold { get => base.FontWeightBold; set => base.FontWeightBold = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new StringAlignment TextAlignment { get => base.TextAlignment; set => base.TextAlignment = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new StringAlignment VerticalAlignment { get => base.VerticalAlignment; set => base.VerticalAlignment = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new bool WordWrap { get => base.WordWrap; set => base.WordWrap = value; }

        // ── ModernForms - Media ───────────────────────────────────────────
        // Imagen de fondo no aplica al Toggle
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new ImageLayout BackgroundImgLayout { get => base.BackgroundImgLayout; set => base.BackgroundImgLayout = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new float ImageOpacity { get => base.ImageOpacity; set => base.ImageOpacity = value; }

        // ── Animations extras ─────────────────────────────────────────────
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new bool EnableHover { get => base.EnableHover; set => base.EnableHover = value; }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new bool EnablePressEffect { get => base.EnablePressEffect; set => base.EnablePressEffect = value; }

        #endregion

        [Category("Toggle Appearance")]
        [Description("Define el estilo visual del Toggle basado en 9 diseños preestablecidos.\nDefines the visual style of the Toggle based on 9 presets.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public ToggleStyle Style { get; set; } = ToggleStyle.Weather_Legacy;

        [Category("Toggle Appearance")]
        [Description("Color de la pista cuando está activado.\nTrack color when the toggle is ON.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color ToggleColorOn { get; set; } = Color.FromArgb(45, 140, 240);

        [Category("Toggle Appearance")]
        [Description("Color de la pista cuando está desactivado.\nTrack color when the toggle is OFF.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color ToggleColorOff { get; set; } = Color.FromArgb(220, 224, 232);

        [Category("Toggle Appearance")]
        [Description("Color del indicador (thumb) cuando está activado.\nThumb indicator color when toggled ON.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color ThumbColorOn { get; set; } = Color.White;

        [Category("Toggle Appearance")]
        [Description("Color del indicador (thumb) cuando está desactivado.\nThumb indicator color when toggled OFF.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color ThumbColorOff { get; set; } = Color.White;

        private bool _useShadow = true;
        [Category("Modern Appearance")]
        [Description("Activa o desactiva la sombra del indicador (thumb) para máximo rendimiento.\nEnables or disables the thumb shadow for maximum performance.")]
        public new bool UseShadow
        {
            get => _useShadow;
            set { _useShadow = value; Invalidate(); }
        }

        private int _maxFps = 120;
        [Category("Modern Appearance")]
        [DefaultValue(120)]
        [Description("Tope máximo visual de fotogramas, síncrono con AnimationManager vMax.\nMax visual framerate cap, synced with AnimationManager vMax.")]
        public int MaxFps
        {
            get => _maxFps;
            set { _maxFps = Math.Max(30, Math.Min(240, value)); }
        }

        private float _animationDurationMs = 150f;
        [Category("Modern Appearance")]
        [DefaultValue(150f)]
        [Description("Duración total de la animación en milisegundos para sincronización por Delta Time.\nTotal animation duration in milliseconds for Delta Time sync.")]
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

        // 🔥 POOL DE MEMORIA ZERO-ALLOCATION
        private Bitmap? _bgCache;
        private SKBitmap? _acrylicCache;
        private SKImage? _acrylicCacheImage;
        private SKBitmap? _acrylicStagingBitmap; // 🔥 INYECCIÓN 3: Buffer Ping-Pong (elimina SKBitmap.Copy())
        private bool _cacheDirty = true;
        private bool _isCapturingBackdrop = false; // Control de asincronía para Acrylic
        private int _isInvalidating = 0;           // 🔥 INYECCIÓN 4: Candado debounce BeginInvoke

        private readonly SKPaint _skPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _skFont = new SKFont();
        private SKPath _skPath = new SKPath();

        private SKPaint? _skThumbShadowPaint;
        private SKMaskFilter? _skThumbMaskFilter;

        private SKPaint? _skAcrylicTintPaint;
        private SKPaint? _skAcrylicFallbackPaint;
        private SKTypeface? _segoeTypeface;

        private Control? _lastParent;

        public ModernThemeToggle()
        {
            this.MinimumSize = new Size(45, 22);
            Width = 45; Height = 22;

            // Reestablecer base para seguridad visual
            base.BackgroundColor = Color.Transparent; base.BackgroundColor2 = Color.Transparent;
            base.CheckedColor = Color.Transparent; base.CheckedColor2 = Color.Transparent;
            base.HoverColor = Color.Transparent; base.HoverColor2 = Color.Transparent;
            base.PressColor = Color.Transparent; base.PressColor2 = Color.Transparent;
            base.BorderThickness = 0; base.FocusThickness = 0; base.UseRipple = false;

            SetStyle(ControlStyles.StandardDoubleClick, false);
            SetStyle(ControlStyles.StandardClick, true);
        }

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
            _cacheDirty = true;
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            _cacheDirty = true;
        }

        // =====================================================================
        // 🔥 FIX PRO DE ANIMACIÓN: UNIDADES CORREGIDAS PARA dt Y FLUIDEZ MAXIMA
        // =====================================================================
        protected override bool CustomAnimationLoop(float dt, float step)
        {
            // dt VIENE EN MILISEGUNDOS DEL ANIMATION MANAGER.
            // Si hay un pico extremo de lag (ej: SO congelado), evitamos un salto masivo topándolo en 100ms.
            if (dt > 100f) dt = 100f;

            // Calculamos qué fracción del viaje total debemos recorrer en este frame
            float velocity = dt / Math.Max(10f, AnimationDurationMs);

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
        // 🔥 BACKGROUND WORKER CACHE: Cero bloqueos de Hilo UI
        // =====================================================================
        private async void RefreshBackgroundCacheAsync()
        {
            if (Parent == null || this.Width <= 0 || this.Height <= 0 || DesignMode)
            {
                _cacheDirty = false;
                return;
            }

            // PASO 1: SINCRÓNICO - Reconstruir _bgCache (GDI puro) ultra rápido
            if (_bgCache == null || _bgCache.Width != this.Width || _bgCache.Height != this.Height)
            {
                if (_bgCache != null) AcrylicHelper.BitmapPool.Return(_bgCache);
                _bgCache = AcrylicHelper.BitmapPool.Rent(this.Width, this.Height);
            }

            if (_cacheDirty || _bgCache == null)
            {
                try
                {
                    using (Graphics g = Graphics.FromImage(_bgCache!))
                    {
                        g.Clear(Color.Transparent);

                        // 🔥 FIX FlowLayoutPanel: coordenadas absolutas de pantalla
                        Form? parentForm = this.FindForm();
                        if (parentForm != null && this.IsHandleCreated)
                        {
                            Point screenPos = this.PointToScreen(Point.Empty);
                            Point formPos = parentForm.PointToClient(screenPos);
                            g.TranslateTransform(-formPos.X, -formPos.Y);
                            using (var pe = new PaintEventArgs(g, parentForm.ClientRectangle))
                            {
                                this.InvokePaintBackground(parentForm, pe);
                            }
                        }
                        else if (Parent != null)
                        {
                            g.TranslateTransform(-this.Left, -this.Top);
                            using (var pe = new PaintEventArgs(g, Parent.ClientRectangle))
                            {
                                InvokePaintBackground(Parent, pe);
                            }
                        }
                    }
                }
                catch { }
            }

            if (!UseAcrylic)
            {
                _cacheDirty = false;
                return;
            }

            // Control para no encadenar capturas si una está en progreso
            if (_isCapturingBackdrop) return;
            _isCapturingBackdrop = true;

            // PASO 2: ASÍNCRONO - Descargar Acrylic pesados al hilo del sistema
            try
            {
                Rectangle bounds = new Rectangle(this.Left, this.Top, this.Width, this.Height);
                IntPtr hwnd = Parent!.IsHandleCreated ? Parent.Handle : IntPtr.Zero;

                if (hwnd != IntPtr.Zero)
                {
                    var newAcrylic = await AcrylicHelper.CaptureBackdropAsync(hwnd, bounds, Color.Transparent, 15);

                    if (newAcrylic != null)
                    {
                        await Task.Run(() =>
                        {
                            BitmapData? bmpData = null;
                            try
                            {
                                var info = new SKImageInfo(newAcrylic.Width, newAcrylic.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

                                // 🔥 INYECCIÓN 3: Reusar staging buffer — cero allocations por frame
                                if (_acrylicStagingBitmap == null ||
                                    _acrylicStagingBitmap.Width != newAcrylic.Width ||
                                    _acrylicStagingBitmap.Height != newAcrylic.Height)
                                {
                                    _acrylicStagingBitmap?.Dispose();
                                    _acrylicStagingBitmap = new SKBitmap(info);
                                }

                                bmpData = newAcrylic.LockBits(
                                    new Rectangle(0, 0, newAcrylic.Width, newAcrylic.Height),
                                    ImageLockMode.ReadOnly,
                                    PixelFormat.Format32bppPArgb);

                                // 🔥 INYECCIÓN 2: CopyMemory — misma velocidad que unsafe, sin flags antivirus
                                RtlMoveMemory(
                                    _acrylicStagingBitmap.GetPixels(),
                                    bmpData.Scan0,
                                    new UIntPtr((uint)_acrylicStagingBitmap.ByteCount)
                                );                                
                                // 🔥 INYECCIÓN 3: Swap Ping-Pong atómico — cero bloqueos
                                _acrylicStagingBitmap = Interlocked.Exchange(ref _acrylicCache, _acrylicStagingBitmap);

                                // Imagen compañera para DrawImage — se reconstruye solo cuando el capture async completa (no por frame)
                                var newAcrylicImage = _acrylicCache != null ? SKImage.FromBitmap(_acrylicCache) : null;
                                Interlocked.Exchange(ref _acrylicCacheImage, newAcrylicImage)?.Dispose();
                            }
                            finally
                            {
                                if (bmpData != null) newAcrylic.UnlockBits(bmpData);
                                AcrylicHelper.BitmapPool.Return(newAcrylic);
                            }
                        });

                        _cacheDirty = false;

                        // 🔥 INYECCIÓN 4: Debounce del BeginInvoke — evita 1000 invalidaciones simultáneas
                        if (IsHandleCreated && !IsDisposed)
                        {
                            if (Interlocked.CompareExchange(ref _isInvalidating, 1, 0) == 0)
                            {
                                this.BeginInvoke(new Action(() =>
                                {
                                    try { if (!IsDisposed && IsHandleCreated) this.Invalidate(); } catch { }
                                    Interlocked.Exchange(ref _isInvalidating, 0);
                                }));
                            }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                _isCapturingBackdrop = false;
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
                    if (_cacheDirty || _bgCache == null) RefreshBackgroundCacheAsync();
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
            if (_cacheDirty || _acrylicCache == null) RefreshBackgroundCacheAsync();

            if (_acrylicCache != null)
            {
                canvas.Save();

                using var builder2 = new SKPathBuilder();
                builder2.AddRoundRect(rect, rect.Height / 2f, rect.Height / 2f);
                _skPath.Dispose();
                _skPath = builder2.Detach();
                canvas.ClipPath(_skPath, SKClipOperation.Intersect, true);

                // Ya no necesitamos aplicar Skia ImageFilter Blur. CaptureBackdropAsync lo hizo gratis
                if (_acrylicCacheImage != null)
                    canvas.DrawImage(_acrylicCacheImage, new SKRect(0, 0, this.Width, this.Height), SKSamplingOptions.Default);

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

        // =====================================================================
        // Dibujados Lógicos (Intactos y Seguros)
        // =====================================================================
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
            using var builder5 = new SKPathBuilder();
            builder5.AddOval(thumbRect);
            _skPath.Dispose();
            _skPath = builder5.Detach();
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
                    _skFont.Size = h * 0.4f;
                    if (_segoeTypeface == null) _segoeTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
                    _skFont.Typeface = _segoeTypeface;
                    if (t > 0.5f) canvas.DrawText("ON", rect.Left + (rect.Width / 4f), rect.MidY - (_skFont.Metrics.Ascent / 2f), SKTextAlign.Center, _skFont, _skPaint);
                    else canvas.DrawText("OFF", rect.Right - (rect.Width / 4f), rect.MidY - (_skFont.Metrics.Ascent / 2f), SKTextAlign.Center, _skFont, _skPaint);
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
                    if (t > 0.5f) { canvas.DrawLine(thumbX + thumbSize * 0.3f, rect.MidY, thumbX + thumbSize * 0.45f, rect.MidY + thumbSize * 0.15f, _skPaint); canvas.DrawLine(thumbX + thumbSize * 0.45f, rect.MidY + thumbSize * 0.15f, thumbX + thumbSize * 0.7f, rect.MidY - thumbSize * 0.15f, _skPaint); }
                    else { canvas.DrawLine(thumbX + thumbSize * 0.35f, rect.MidY - thumbSize * 0.15f, thumbX + thumbSize * 0.65f, rect.MidY + thumbSize * 0.15f, _skPaint); canvas.DrawLine(thumbX + thumbSize * 0.65f, rect.MidY - thumbSize * 0.15f, thumbX + thumbSize * 0.35f, rect.MidY + thumbSize * 0.15f, _skPaint); }
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
                {
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
                        using var builder9 = new SKPathBuilder();
                        builder9.AddCircle(thumbX9, rect.MidY, thumbR9);
                        _skPath.Dispose();
                        _skPath = builder9.Detach();
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


        private new void ClearCaches()
        {
            try { if (_bgCache != null) { try { AcrylicHelper.BitmapPool.Return(_bgCache); } catch { _bgCache.Dispose(); } _bgCache = null; } } catch { }
            try { _acrylicCache?.Dispose(); _acrylicCache = null; } catch { }
            try { _acrylicCacheImage?.Dispose(); _acrylicCacheImage = null; } catch { }
            try { _acrylicStagingBitmap?.Dispose(); _acrylicStagingBitmap = null; } catch { } // 🔥 INYECCIÓN 3: Limpiar staging buffer
            try { _skThumbShadowPaint?.Dispose(); _skThumbShadowPaint = null; } catch { }
            try { _skThumbMaskFilter?.Dispose(); _skThumbMaskFilter = null; } catch { }
            try { _skAcrylicTintPaint?.Dispose(); _skAcrylicTintPaint = null; } catch { }
            try { _skAcrylicFallbackPaint?.Dispose(); _skAcrylicFallbackPaint = null; } catch { }
            try { _segoeTypeface?.Dispose(); _segoeTypeface = null; } catch { }
            try { _skFont?.Dispose(); } catch { }
            try { _skPaint.Dispose(); } catch { }
            try { _skPath.Dispose(); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
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
