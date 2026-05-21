#nullable enable
using Microsoft.Win32;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Buffers;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FluentWinForms.Core
{
    public enum FormBackdropType
    {
        None = 0,
        Acrylic = 3
    }

    public static class WindowsVersionHelper
    {
        private static int? _cachedBuild;

        public static int GetBuildNumber()
        {
            if (_cachedBuild.HasValue) return _cachedBuild.Value;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (key != null)
                {
                    string? buildStr = key.GetValue("CurrentBuild") as string ?? key.GetValue("CurrentBuildNumber") as string;
                    if (!string.IsNullOrEmpty(buildStr) && int.TryParse(buildStr, out int build))
                    {
                        _cachedBuild = build;
                        return build;
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"WindowsVersionHelper: {ex.Message}"); }
            _cachedBuild = Environment.OSVersion.Version.Build;
            return _cachedBuild.Value;
        }

        public static bool IsWindows11OrGreater() => GetBuildNumber() >= 22000;
        public static bool IsWindows10OrGreater() => GetBuildNumber() >= 10240;
    }

    [ToolboxItem(true)]
    [DesignTimeVisible(true)]
    [ToolboxBitmap(typeof(Form))]
    [Description("Gestor avanzado del Formulario: Motor Inteligente, Acrílico Multi-SO y bordes Skia HD.")]
    public class ModernFormManager : Component
    {
        private Form? _targetForm;
        private Control? _dragControl;
        private double _originalOpacity = 1.0;

        private Bitmap? _desktopAcrylicCache;
        private ModernFormOverlay? _overlayWindow;

        // 🔥 VACUNA 1: Token de cancelación para evitar que capturas asíncronas choquen
        private CancellationTokenSource? _captureCts;

        private int _borderRadius = 12;
        private bool _transparentStyle = false;
        private FormBackdropType _backdropType = FormBackdropType.None;

        private bool _autoSetBlackBackground = false;

        public ModernFormManager() { }
        public ModernFormManager(IContainer container) { container?.Add(this); }

        private void UnsubscribeTargetForm()
        {
            if (_targetForm == null) return;
            try
            {
                _targetForm.ResizeBegin -= TargetForm_ResizeBegin;
                _targetForm.ResizeEnd -= TargetForm_ResizeEnd;
                _targetForm.HandleCreated -= TargetForm_HandleCreated;
                _targetForm.MouseDown -= AutoDrag_MouseDown;
                _targetForm.Resize -= TargetForm_Resize;
                _targetForm.Paint -= TargetForm_Paint_Hybrid;
                _targetForm.Paint -= TargetForm_PaintDesktopAcrylic;
                _targetForm.ResizeEnd -= CaptureDesktopBackgroundEvent;
                _targetForm.Move -= CaptureDesktopBackgroundEvent;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"UnsubscribeTargetForm: {ex.Message}"); }
        }

        [Category("Modern Form")]
        public Form? TargetForm
        {
            get => _targetForm;
            set
            {
                UnsubscribeTargetForm();

                _targetForm = value;

                if (_targetForm != null)
                {
                    _targetForm.ResizeBegin += TargetForm_ResizeBegin;
                    _targetForm.ResizeEnd += TargetForm_ResizeEnd;
                    _targetForm.MouseDown += AutoDrag_MouseDown;
                    _targetForm.Resize += TargetForm_Resize;
                    _targetForm.Paint += TargetForm_Paint_Hybrid;

                    if (!DesignMode)
                    {
                        _overlayWindow = new ModernFormOverlay(_targetForm);
                    }

                    if (_targetForm.IsHandleCreated)
                    {
                        _overlayWindow?.Show();
                        ApplyModernEffects();
                    }
                    else
                    {
                        _targetForm.HandleCreated += (s, e) =>
                        {
                            if (_targetForm != null && !_targetForm.IsDisposed)
                                _overlayWindow?.Show();
                        };
                        _targetForm.HandleCreated += TargetForm_HandleCreated;
                    }
                }
            }
        }

        [Category("Modern Form")]
        public bool EnableDrag { get; set; } = true;

        [Category("Modern Form")]
        public Control? DragControl
        {
            get => _dragControl;
            set
            {
                if (_dragControl != null) _dragControl.MouseDown -= AutoDrag_MouseDown;
                _dragControl = value;
                if (_dragControl != null) _dragControl.MouseDown += AutoDrag_MouseDown;
            }
        }

        [Category("Modern Form - Animation")]
        public double DragOpacity { get; set; } = 1.0;

        private bool _useSkia = true;
        [Category("Modern Form - Engine")]
        public bool UseSkia
        {
            get => _useSkia;
            set { _useSkia = value; _targetForm?.Invalidate(); }
        }

        private Color _borderColor = Color.FromArgb(40, 255, 255, 255);
        [Category("Modern Form - Visuals")]
        public Color BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; _targetForm?.Invalidate(); }
        }

        private float _borderThickness = 0f;
        [Category("Modern Form - Visuals")]
        public float BorderThickness
        {
            get => _borderThickness;
            set { _borderThickness = Math.Max(0, value); _targetForm?.Invalidate(); }
        }

        [Category("Modern Form - Visuals")]
        [RefreshProperties(RefreshProperties.All)]
        public FormBackdropType BackdropType
        {
            get => _backdropType;
            set
            {
                _backdropType = value;

                if (_backdropType != FormBackdropType.None)
                {
                    _transparentStyle = false;
                    _autoSetBlackBackground = true;
                }
                else
                {
                    _autoSetBlackBackground = false;
                }

                UpdateFormBackground();
                ApplyModernEffects();

                if (DesignMode)
                {
                    _targetForm?.Refresh();
                    TypeDescriptor.Refresh(this);
                }
                else _targetForm?.Invalidate();
            }
        }

        [Category("Modern Form - Visuals")]
        [RefreshProperties(RefreshProperties.All)]
        public bool AutoSetBlackBackground
        {
            get => _autoSetBlackBackground;
            set
            {
                _autoSetBlackBackground = value;
                UpdateFormBackground();
            }
        }

        [Category("Modern Form - Visuals")]
        [RefreshProperties(RefreshProperties.All)]
        public bool TransparentStyle
        {
            get => _transparentStyle;
            set
            {
                _transparentStyle = value;
                if (_transparentStyle) _backdropType = FormBackdropType.None;

                UpdateFormBackground();

                if (DesignMode)
                {
                    _targetForm?.Refresh();
                    TypeDescriptor.Refresh(this);
                }
                else _targetForm?.Invalidate();
            }
        }

        [Category("Modern Form - Visuals")]
        public int BorderRadius
        {
            get => _borderRadius;
            set
            {
                if (_targetForm == null)
                {
                    _borderRadius = Math.Max(0, value);
                }
                else
                {
                    int w = Math.Max(1, _targetForm.Width);
                    int h = Math.Max(1, _targetForm.Height);
                    int maxLogical = (Math.Min(w, h) / 2) - 2;
                    maxLogical = Math.Max(0, maxLogical);
                    _borderRadius = Math.Max(0, Math.Min(value, maxLogical));
                }

                ApplyCustomRegion();
                if (DesignMode) _targetForm?.Refresh();
                else _targetForm?.Invalidate();
            }
        }

        [Category("Modern Form - Visuals")]
        public bool UseModernRoundedCorners { get; set; } = true;

        [Category("Modern Form - Visuals")]
        public bool ForceDarkModeTitleBar { get; set; } = false;

        private void TargetForm_HandleCreated(object? sender, EventArgs e) => ApplyModernEffects();

        private void TargetForm_Paint_Hybrid(object? sender, PaintEventArgs e)
        {
            if (_targetForm == null || _borderRadius <= 0) return;

            bool isWin11 = WindowsVersionHelper.IsWindows11OrGreater();
            if (isWin11 && _backdropType == FormBackdropType.Acrylic && _borderThickness <= 0) return;

            if (DesignMode)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float drawThick = _borderThickness > 0 ? _borderThickness : 1.5f;
                Color drawColor = _borderThickness > 0 ? _borderColor : Color.DarkGray;

                using (var path = CreateRoundedPath(new RectangleF(drawThick / 2, drawThick / 2, _targetForm.Width - drawThick - 1f, _targetForm.Height - drawThick - 1f), _borderRadius))
                using (var pen = new Pen(drawColor, drawThick) { DashStyle = _borderThickness > 0 ? DashStyle.Solid : DashStyle.Dash })
                {
                    e.Graphics.DrawPath(pen, path);
                }
                return;
            }

            if (UseSkia)
            {
                try { PaintSkiaSmoothing(e.Graphics); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PaintSkiaSmoothing failed: {ex.Message}");
                    PaintGdiSmoothing(e.Graphics);
                }
            }
            else
            {
                PaintGdiSmoothing(e.Graphics);
            }
        }

        private SKBitmap? _skBorderBitmap;
        private Bitmap? _gdiBorderBitmap;
        private SKPaint? _skSharedPaint;

        private int _lastWidth, _lastHeight, _lastRadius;
        private Color _lastColor;
        private float _lastThickness;

        private float GetScaleFactor()
        {
            if (_targetForm == null) return 1f;
            using (Graphics g = _targetForm.CreateGraphics())
            {
                return g.DpiX / 96f;
            }
        }

        private void PaintSkiaSmoothing(Graphics g)
        {
            if (_targetForm == null) return;
            int w = _targetForm.Width;
            int h = _targetForm.Height;
            if (w <= 0 || h <= 0) return;

            float scale = GetScaleFactor();
            float actualThickness = _borderThickness > 0 ? _borderThickness : 0f;
            Color actualColor = _borderThickness > 0 ? _borderColor : Color.Transparent;

            int scaledRadius = (int)Math.Round((double)(_borderRadius * scale));

            if (_skBorderBitmap == null || _gdiBorderBitmap == null || _lastWidth != w || _lastHeight != h ||
                _lastRadius != _borderRadius || _lastColor != actualColor || Math.Abs(_lastThickness - actualThickness) > 0.01f)
            {
                SafeDispose(ref _gdiBorderBitmap);
                SafeDispose(ref _skBorderBitmap);

                _skBorderBitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var canvas = new SKCanvas(_skBorderBitmap))
                {
                    canvas.Clear(SKColors.Transparent);

                    if (_skSharedPaint == null)
                    {
                        _skSharedPaint = new SKPaint
                        {
                            IsAntialias = true,
                            Style = SKPaintStyle.Stroke,
                            StrokeJoin = SKStrokeJoin.Round,
                            StrokeCap = SKStrokeCap.Round
                        };
                    }

                    if (_backdropType == FormBackdropType.None && scaledRadius > 0)
                    {
                        float strokeW = 4f * scale;
                        float strokeInset = strokeW / 2f;

                        using var hidePaint = new SKPaint
                        {
                            Color = _targetForm.BackColor.ToSKColor(),
                            IsAntialias = true,
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = strokeW
                        };

                        SKRect hideRect = new SKRect(strokeInset, strokeInset, w - strokeInset, h - strokeInset);
                        float hideRad = Math.Max(0, scaledRadius - strokeInset);

                        canvas.DrawRoundRect(hideRect, hideRad, hideRad, hidePaint);
                    }
                    else if (_backdropType == FormBackdropType.Acrylic && scaledRadius > 0 && !WindowsVersionHelper.IsWindows11OrGreater())
                    {
                        float strokeW = 2f * scale;
                        float strokeInset = strokeW / 2f;
                        using var smoothPaint = new SKPaint
                        {
                            IsAntialias = true,
                            Style = SKPaintStyle.Stroke,
                            Color = Color.FromArgb(40, 255, 255, 255).ToSKColor(),
                            StrokeWidth = strokeW
                        };

                        SKRect smoothRect = new SKRect(strokeInset, strokeInset, w - strokeInset, h - strokeInset);
                        float smoothRad = Math.Max(0, scaledRadius - strokeInset);
                        canvas.DrawRoundRect(smoothRect, smoothRad, smoothRad, smoothPaint);
                    }

                    if (actualThickness > 0)
                    {
                        float strokeW = actualThickness * scale;
                        float strokeInset = strokeW / 2f;

                        _skSharedPaint.Color = actualColor.ToSKColor();
                        _skSharedPaint.StrokeWidth = strokeW;

                        SKRect drawRect = new SKRect(strokeInset, strokeInset, w - strokeInset, h - strokeInset);
                        float rad = Math.Max(0, scaledRadius - strokeInset);

                        canvas.DrawRoundRect(drawRect, rad, rad, _skSharedPaint);
                    }
                }

                if (DesignMode || _overlayWindow == null || _overlayWindow.IsDisposed)
                {
                    _gdiBorderBitmap = new Bitmap(w, h, _skBorderBitmap.RowBytes, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, _skBorderBitmap.GetPixels());
                }

                _lastWidth = w; _lastHeight = h; _lastRadius = _borderRadius;
                _lastColor = actualColor; _lastThickness = actualThickness;
            }

            if (!DesignMode && _overlayWindow != null && !_overlayWindow.IsDisposed)
            {
                _overlayWindow.SetSkiaBitmapForOverlay(_skBorderBitmap);
            }
            else if (_gdiBorderBitmap != null)
            {
                var oldComposite = g.CompositingMode;
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.DrawImageUnscaled(_gdiBorderBitmap, 0, 0);
                g.CompositingMode = oldComposite;
            }
        }

        private void PaintGdiSmoothing(Graphics g)
        {
            if (_targetForm == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            int w = _targetForm.Width;
            int h = _targetForm.Height;
            float actualThickness = _borderThickness > 0 ? _borderThickness : 2f;
            Color actualColor = _borderThickness > 0 ? _borderColor : _targetForm.BackColor;

            float scale = GetScaleFactor();
            float rad = Math.Max(0, (_borderRadius * scale) - ((actualThickness + 1f) / 2f));
            float inset = (actualThickness + 1f) / 2f;

            using (var path = CreateRoundedPath(new RectangleF(inset, inset, w - actualThickness - 1f, h - actualThickness - 1f), rad))
            using (var pen = new Pen(actualColor, actualThickness + 1f))
            {
                g.DrawPath(pen, path);
            }
        }

        private GraphicsPath CreateRoundedPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2;
            if (d <= 0) return path;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void UpdateFormBackground()
        {
            if (_targetForm == null || DesignMode) return;

            if (BackdropType != FormBackdropType.None && WindowsVersionHelper.IsWindows11OrGreater())
            {
                _targetForm.TransparencyKey = Color.Empty;

                if (_autoSetBlackBackground)
                {
                    _targetForm.BackColor = Color.Black;
                }
            }
            else if (_transparentStyle)
            {
                _targetForm.BackColor = Color.Fuchsia;
                _targetForm.TransparencyKey = Color.Fuchsia;
            }
            else
            {
                _targetForm.TransparencyKey = Color.Empty;
            }
        }

        private void TargetForm_ResizeBegin(object? sender, EventArgs e)
        {
            if (_targetForm != null && EnableDrag && DragOpacity < 1.0)
            {
                _originalOpacity = _targetForm.Opacity;
                _targetForm.Opacity = DragOpacity;
            }
        }

        private void TargetForm_ResizeEnd(object? sender, EventArgs e)
        {
            if (_targetForm != null && EnableDrag && DragOpacity < 1.0) _targetForm.Opacity = _originalOpacity;
        }

        private void TargetForm_Resize(object? sender, EventArgs e)
        {
            if (_targetForm != null)
            {
                int w = Math.Max(1, _targetForm.Width);
                int h = Math.Max(1, _targetForm.Height);
                int maxLogical = (Math.Min(w, h) / 2) - 2;
                maxLogical = Math.Max(0, maxLogical);
                if (_borderRadius > maxLogical)
                {
                    _borderRadius = maxLogical;
                }
            }
            ApplyCustomRegion();
        }

        private void ApplyCustomRegion()
        {
            if (_targetForm == null || DesignMode) return;

            if (_backdropType == FormBackdropType.Acrylic && WindowsVersionHelper.IsWindows11OrGreater())
            {
                _targetForm.Region = null;
                return;
            }

            if (_borderRadius > 0)
            {
                try
                {
                    float scale = GetScaleFactor();
                    int physRadius = (int)Math.Round((double)(_borderRadius * scale));

                    int inset = (_backdropType == FormBackdropType.None) ? 2 : 0;
                    int gdiRadius = Math.Max(1, physRadius - inset);
                    int diameter = gdiRadius * 2;

                    IntPtr ptr = NativeMethods.CreateRoundRectRgn(inset, inset, _targetForm.Width - inset + 1, _targetForm.Height - inset + 1, diameter, diameter);
                    _targetForm.Region = Region.FromHrgn(ptr);
                    NativeMethods.DeleteObject(ptr);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ApplyCustomRegion: {ex.Message}"); }
            }
            else
            {
                _targetForm.Region = null;
            }
        }

        private void AutoDrag_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _targetForm != null && EnableDrag)
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(_targetForm.Handle, NativeMethods.WM_NCLBUTTONDOWN, NativeMethods.HT_CAPTION, 0);
            }
        }

        private void EnableAcrylic()
        {
            if (_targetForm == null || DesignMode) return;

            int gradientColor = (0x40 << 24) | (255 << 16) | (255 << 8) | 255;

            var accent = new NativeMethods.AccentPolicy
            {
                AccentState = NativeMethods.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 0,
                GradientColor = gradientColor,
                AnimationId = 0
            };

            var accentStructSize = Marshal.SizeOf(typeof(NativeMethods.AccentPolicy));
            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new NativeMethods.WindowCompositionAttributeData
            {
                Attribute = NativeMethods.WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = accentStructSize,
                Data = accentPtr
            };

            try { NativeMethods.SetWindowCompositionAttribute(_targetForm.Handle, ref data); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"EnableAcrylic: {ex.Message}"); }
            finally { Marshal.FreeHGlobal(accentPtr); }
        }

        public void ApplyModernEffects()
        {
            if (_targetForm == null || !_targetForm.IsHandleCreated || DesignMode) return;

            bool isWin11 = WindowsVersionHelper.IsWindows11OrGreater();
            bool isWin10 = WindowsVersionHelper.IsWindows10OrGreater();

            if (_backdropType == FormBackdropType.None)
            {
                if (isWin11)
                {
                    try
                    {
                        int noneVal = 1;
                        NativeMethods.DwmSetWindowAttribute(_targetForm.Handle, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref noneVal, Marshal.SizeOf(typeof(int)));

                        int cornerPref = (_borderRadius == 0 && UseModernRoundedCorners) ? NativeMethods.DWMWCP_ROUND : NativeMethods.DWMWCP_DONOTROUND;
                        NativeMethods.DwmSetWindowAttribute(_targetForm.Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, Marshal.SizeOf(typeof(int)));
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"DWMWA Fallback: {ex.Message}"); }
                }

                _targetForm.Paint -= TargetForm_PaintDesktopAcrylic;
                SafeDispose(ref _desktopAcrylicCache);

                ApplyCustomRegion();
                if (_transparentStyle)
                {
                    _targetForm.BackColor = Color.Fuchsia;
                    _targetForm.TransparencyKey = Color.Fuchsia;
                }
                else
                {
                    _targetForm.TransparencyKey = Color.Empty;
                    if (_targetForm.BackColor == Color.Black || _targetForm.BackColor == Color.Fuchsia)
                    {
                        _targetForm.BackColor = SystemColors.Control;
                    }
                }
                return;
            }

            if (isWin11)
            {
                try
                {
                    int trueVal = ForceDarkModeTitleBar ? 1 : 0;
                    NativeMethods.DwmSetWindowAttribute(_targetForm.Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueVal, Marshal.SizeOf(typeof(int)));

                    NativeMethods.MARGINS margins = new NativeMethods.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
                    NativeMethods.DwmExtendFrameIntoClientArea(_targetForm.Handle, ref margins);

                    EnableAcrylic();

                    int backdropValue = (int)BackdropType;
                    NativeMethods.DwmSetWindowAttribute(_targetForm.Handle, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropValue, Marshal.SizeOf(typeof(int)));

                    int cornerPreference = NativeMethods.DWMWCP_ROUND;
                    NativeMethods.DwmSetWindowAttribute(_targetForm.Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, Marshal.SizeOf(typeof(int)));

                    _targetForm.Region = null;
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Win11 Acrylic Fallback: {ex.Message}"); }
            }
            else if (isWin10)
            {
                EnableAcrylic();
                ApplyCustomRegion();
            }
            else
            {
                _targetForm.Paint -= TargetForm_PaintDesktopAcrylic;
                _targetForm.Paint += TargetForm_PaintDesktopAcrylic;

                _targetForm.ResizeEnd -= CaptureDesktopBackgroundEvent;
                _targetForm.Move -= CaptureDesktopBackgroundEvent;
                _targetForm.ResizeEnd += CaptureDesktopBackgroundEvent;
                _targetForm.Move += CaptureDesktopBackgroundEvent;

                ApplyCustomRegion();
                _ = CaptureDesktopBackgroundAsync();
            }

            UpdateFormBackground();
        }

        private void CaptureDesktopBackgroundEvent(object? sender, EventArgs e) => _ = CaptureDesktopBackgroundAsync();

        // 🔥 VACUNA 2: Cancelación segura si el form se mueve/cierra
        private async Task CaptureDesktopBackgroundAsync()
        {
            if (_targetForm == null || _targetForm.IsDisposed || _targetForm.Width <= 0 || _targetForm.Height <= 0) return;

            _captureCts?.Cancel();
            _captureCts = new CancellationTokenSource();
            var token = _captureCts.Token;

            double oldOpacity = 1.0;

            try
            {
                // 🔥 VACUNA 3: Verificación estricta de Hilo y Handle antes de Invoke
                if (_targetForm.InvokeRequired)
                {
                    if (!_targetForm.IsHandleCreated || _targetForm.IsDisposed) return;
                    _targetForm.Invoke(new Action(() =>
                    {
                        if (_targetForm.IsDisposed) return;
                        oldOpacity = _targetForm.Opacity;
                        _targetForm.Opacity = 0;
                    }));
                }
                else
                {
                    oldOpacity = _targetForm.Opacity;
                    _targetForm.Opacity = 0;
                }

                if (token.IsCancellationRequested) return;

                Bitmap newBlur = await AcrylicHelper.CaptureBackdropAsync(
                    _targetForm.Handle,
                    new Rectangle(_targetForm.Left, _targetForm.Top, _targetForm.Width, _targetForm.Height),
                    Color.FromArgb(150, 20, 20, 20),
                    20
                );

                if (token.IsCancellationRequested || _targetForm == null || _targetForm.IsDisposed)
                {
                    newBlur?.Dispose();
                    return;
                }

                if (_targetForm.InvokeRequired)
                {
                    if (!_targetForm.IsHandleCreated || _targetForm.IsDisposed)
                    {
                        newBlur?.Dispose();
                        return;
                    }
                    _targetForm.BeginInvoke(new Action(() => ApplyCapturedBackdrop(newBlur, oldOpacity)));
                }
                else
                {
                    ApplyCapturedBackdrop(newBlur, oldOpacity);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CaptureDesktopBackgroundAsync: {ex.Message}");
            }
        }

        private void ApplyCapturedBackdrop(Bitmap newBlur, double oldOpacity)
        {
            if (_targetForm == null || _targetForm.IsDisposed)
            {
                newBlur?.Dispose();
                return;
            }
            _targetForm.Opacity = oldOpacity;
            _desktopAcrylicCache?.Dispose();
            _desktopAcrylicCache = newBlur;
            _targetForm.Invalidate();
        }

        private void TargetForm_PaintDesktopAcrylic(object? sender, PaintEventArgs e)
        {
            if (_desktopAcrylicCache != null)
            {
                e.Graphics.DrawImageUnscaled(_desktopAcrylicCache, 0, 0);
            }
        }

        private void SafeDispose<T>(ref T? obj) where T : class, IDisposable
        {
            try { obj?.Dispose(); } catch { } finally { obj = null; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _captureCts?.Cancel();
                _captureCts?.Dispose();

                UnsubscribeTargetForm();

                if (_dragControl != null)
                {
                    _dragControl.MouseDown -= AutoDrag_MouseDown;
                }

                SafeDispose(ref _skSharedPaint);
                SafeDispose(ref _gdiBorderBitmap);
                SafeDispose(ref _skBorderBitmap);
                SafeDispose(ref _desktopAcrylicCache);

                if (_overlayWindow != null && !_overlayWindow.IsDisposed)
                {
                    _overlayWindow.Dispose();
                    _overlayWindow = null;
                }

                GC.SuppressFinalize(this);
            }
            base.Dispose(disposing);
        }

        private static class NativeMethods
        {
            private const string User32Lib = "user32.dll";
            private const string DwmapiLib = "dwmapi.dll";
            private const string Gdi32Lib = "gdi32.dll";

            public const int WM_NCLBUTTONDOWN = 0xA1;
            public const int HT_CAPTION = 0x2;

            public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
            public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
            public const int DWMWA_MICA_EFFECT = 1029;
            public const int DWMWCP_ROUND = 2;
            public const int DWMWCP_DONOTROUND = 1;

            public const int ACCENT_ENABLE_BLURBEHIND = 3;
            public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

            [StructLayout(LayoutKind.Sequential)]
            public struct AccentPolicy
            {
                public int AccentState;
                public int AccentFlags;
                public int GradientColor;
                public int AnimationId;
            }

            public enum WindowCompositionAttribute
            {
                WCA_ACCENT_POLICY = 19
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct WindowCompositionAttributeData
            {
                public WindowCompositionAttribute Attribute;
                public IntPtr Data;
                public int SizeOfData;
            }

            [DllImport(User32Lib)]
            public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

            [StructLayout(LayoutKind.Sequential)]
            public struct MARGINS
            {
                public int cxLeftWidth;
                public int cxRightWidth;
                public int cyTopHeight;
                public int cyBottomHeight;
            }

            [DllImport(User32Lib)]
            public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

            [DllImport(User32Lib)]
            public static extern bool ReleaseCapture();

            [DllImport(DwmapiLib)]
            public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

            [DllImport(DwmapiLib)]
            public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

            [DllImport(Gdi32Lib)]
            public static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

            [DllImport(Gdi32Lib)]
            public static extern bool DeleteObject(IntPtr hObject);
        }
    }

    public sealed class ModernFormOverlay : Form
    {
        private readonly Form _owner;

        private readonly object _refreshLock = new();
        private int _lastRefreshMs = 0;
        private const int MinRefreshMs = 16;

        private SKBitmap? _skBorderBitmapCache = null;

        public ModernFormOverlay(Form owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));

            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.AutoScaleMode = AutoScaleMode.None;

            _owner.LocationChanged += (s, e) => UpdateLocation();
            _owner.SizeChanged += (s, e) => UpdateSize();
            _owner.VisibleChanged += (s, e) => this.Visible = _owner.Visible;

            this.Owner = _owner;
        }

        private void UpdateLocation()
        {
            if (this.Location != _owner.Location)
                this.Location = _owner.Location;
        }

        private void UpdateSize()
        {
            if (this.Size != _owner.Size)
                this.Size = _owner.Size;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000;
                cp.ExStyle |= 0x00000020;
                cp.ExStyle |= 0x08000000;
                return cp;
            }
        }

        public void SetSkiaBitmapForOverlay(SKBitmap sk)
        {
            _skBorderBitmapCache = sk;
            RequestRefresh();
        }

        private void RequestRefresh()
        {
            lock (_refreshLock)
            {
                int now = Environment.TickCount;
                if (now - _lastRefreshMs < MinRefreshMs) return;
                _lastRefreshMs = now;
            }
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
                this.BeginInvoke((Action)(() => RefreshLayer()));
            else
                RefreshLayer();
        }

        public void RefreshLayer()
        {
            if (this.IsDisposed) return;
            if (_skBorderBitmapCache == null) return;

            if (this.InvokeRequired)
            {
                if (!this.IsHandleCreated || this.IsDisposed) return;
                this.BeginInvoke((Action)(() => RefreshLayer()));
                return;
            }

            try { this.Left = this.Owner?.Left ?? this.Left; this.Top = this.Owner?.Top ?? this.Top; } catch { }

            SKBitmap sk = _skBorderBitmapCache;
            int w = sk.Width;
            int h = sk.Height;
            if (w <= 0 || h <= 0) return;

            float scale = 1f;
            if (this.Owner != null && !this.Owner.IsDisposed)
            {
                try { using (var g = this.Owner.CreateGraphics()) scale = g.DpiX / 96f; } catch { }
            }

            int physW = (int)Math.Round((double)(w * scale));
            int physH = (int)Math.Round((double)(h * scale));

            if (physW <= 0 || physH <= 0) return;

            NativeMethodsOverlay.BITMAPINFO bmi = new NativeMethodsOverlay.BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<NativeMethodsOverlay.BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = physW;
            bmi.bmiHeader.biHeight = -physH;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;

            IntPtr dibBits = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr memDc = IntPtr.Zero;
            IntPtr screenDc = IntPtr.Zero;
            IntPtr oldBmp = IntPtr.Zero;

            try
            {
                screenDc = NativeMethodsOverlay.GetDC(IntPtr.Zero);
                memDc = NativeMethodsOverlay.CreateCompatibleDC(screenDc);
                hBitmap = NativeMethodsOverlay.CreateDIBSection(memDc, ref bmi, 0, out dibBits, IntPtr.Zero, 0);
                if (hBitmap == IntPtr.Zero || dibBits == IntPtr.Zero) return;

                int bytes = physW * physH * 4;
                byte[] tmp = ArrayPool<byte>.Shared.Rent(bytes);
                try
                {
                    IntPtr skPtr = sk.GetPixels();
                    if (skPtr == IntPtr.Zero) return;

                    int skStride = sk.RowBytes;
                    if (skStride == physW * 4 && scale == 1f)
                    {
                        Marshal.Copy(skPtr, tmp, 0, bytes);
                    }
                    else
                    {
                        for (int y = 0; y < h; y++)
                        {
                            IntPtr rowSrc = IntPtr.Add(skPtr, y * skStride);
                            int copyLen = Math.Min(skStride, physW * 4);
                            Marshal.Copy(rowSrc, tmp, y * physW * 4, copyLen);
                        }
                    }

                    Marshal.Copy(tmp, 0, dibBits, bytes);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(tmp, false);
                }

                oldBmp = NativeMethodsOverlay.SelectObject(memDc, hBitmap);

                NativeMethodsOverlay.BLENDFUNCTION blend = new NativeMethodsOverlay.BLENDFUNCTION
                {
                    BlendOp = 0x00,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = 0x01
                };

                NativeMethodsOverlay.POINT topPos = new NativeMethodsOverlay.POINT(this.Left, this.Top);
                NativeMethodsOverlay.SIZE size = new NativeMethodsOverlay.SIZE(physW, physH);
                NativeMethodsOverlay.POINT src = new NativeMethodsOverlay.POINT(0, 0);

                NativeMethodsOverlay.UpdateLayeredWindow(
                    this.Handle, screenDc, ref topPos, ref size, memDc,
                    ref src, 0, ref blend, 2);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"RefreshLayer Error: {ex.Message}"); }
            finally
            {
                if (oldBmp != IntPtr.Zero) NativeMethodsOverlay.SelectObject(memDc, oldBmp);
                if (hBitmap != IntPtr.Zero) NativeMethodsOverlay.DeleteObject(hBitmap);
                if (memDc != IntPtr.Zero) NativeMethodsOverlay.DeleteDC(memDc);
                if (screenDc != IntPtr.Zero) NativeMethodsOverlay.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static class NativeMethodsOverlay
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct POINT { public int x; public int y; public POINT(int x, int y) { this.x = x; this.y = y; } }

            [StructLayout(LayoutKind.Sequential)]
            public struct SIZE { public int cx; public int cy; public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; } }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct BLENDFUNCTION { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }

            [StructLayout(LayoutKind.Sequential)]
            public struct BITMAPINFOHEADER
            {
                public uint biSize;
                public int biWidth;
                public int biHeight;
                public ushort biPlanes;
                public ushort biBitCount;
                public uint biCompression;
                public uint biSizeImage;
                public int biXPelsPerMeter;
                public int biYPelsPerMeter;
                public uint biClrUsed;
                public uint biClrImportant;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct BITMAPINFO
            {
                public BITMAPINFOHEADER bmiHeader;
                public uint bmiColors;
            }

            [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
            public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

            [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
            [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
            [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
            [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
            [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
            [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
            [DllImport("gdi32.dll")]
            public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);
        }
    }
}