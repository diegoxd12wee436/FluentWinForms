#nullable enable
#pragma warning disable CA1416 // Silencia advertencias de compatibilidad de System.Drawing en .NET 6+
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace FluentWinForms.Core
{
    public abstract partial class ModernControlBase
    {
        private SKPath? _cachedClipPath;

        // RUTA MAESTRA DE DIBUJADO
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;

            // 🔥 BLINDAJE MULTI-TARGET: En .NET 4.8 _gdiWrapper se descarta intencionalmente para evitar AccessViolation
#if NETFRAMEWORK
            if (!_isRenderable || (_useSkiaGraphics && (_skCanvas == null || _skBitmap == null)))
#else
            if (!_isRenderable || (_useSkiaGraphics && (_gdiWrapper == null || _skCanvas == null || _skBitmap == null)))
#endif
            {
                e.Graphics.Clear(Color.FromArgb(240, 240, 240));
                using (var pen = new Pen(Color.Red, 2)) e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
                e.Graphics.DrawString("Render Error (OOM)", SystemFonts.DefaultFont, Brushes.Red, 5, 5);
                return;
            }

            if (Parent != null)
            {
                var state = e.Graphics.Save();
                e.Graphics.TranslateTransform(-Left, -Top);
                using (var pe = new PaintEventArgs(e.Graphics, Parent.ClientRectangle))
                    InvokePaintBackground(Parent, pe);
                e.Graphics.Restore(state);
            }

            // ==========================================
            // 🔥 INTELIGENCIA ESPACIAL (ZONA SEGURA AAA)
            // ==========================================
            float maxBorder = Math.Max(BorderThickness, FocusThickness);
            float maxShadowSpace = UseShadow ? S(ShadowBlur) : 0;

            // El margen maestro NUNCA cambia en tiempo real, evitando recortes y brincos visuales.
            float margin = maxShadowSpace + S(maxBorder / 2f);

            // El borde activo sí se actualiza para dibujar correctamente
            float activeBorder = S(IsFocusedControl ? FocusThickness : BorderThickness);
            var contentRectF = new RectangleF(margin, margin, Width - (margin * 2), Height - (margin * 2));

            contentRectF.Inflate(-(activeBorder / 2f), -(activeBorder / 2f));
            var paddedRectF = new RectangleF(contentRectF.Left + S(Padding.Left), contentRectF.Top + S(Padding.Top), contentRectF.Width - S(Padding.Horizontal), contentRectF.Height - S(Padding.Vertical));

            var shadowRectSK = new SKRect(margin, margin, Width - margin, Height - margin);
            var contentRectSK = shadowRectSK; contentRectSK.Inflate(-(activeBorder / 2f), -(activeBorder / 2f));
            var paddedRectSK = new SKRect(contentRectSK.Left + S(Padding.Left), contentRectSK.Top + S(Padding.Top), contentRectSK.Right - S(Padding.Right), contentRectSK.Bottom - S(Padding.Bottom));

            if (UseSkiaGraphics)
            {
                try
                {
                    _skCanvas!.Clear(SKColors.Transparent);
                    _skCanvas.Save();
                    // =========================================================
                    // 🔥 EL ANCLA MÓVIL: Centrado automático + Traslación CSS
                    // =========================================================
                    if (!_logicalBounds.IsEmpty)
                    {
                        float ox = _logicalBounds.X - this.Left;
                        float oy = _logicalBounds.Y - this.Top;
                        _skCanvas.Translate(ox + this.TranslateX, oy + this.TranslateY);
                    }

                    _sharedPaint ??= new SKPaint { IsAntialias = true };
                    _sharedPath ??= new SKPath();

                    // 🔥 INYECCIÓN: Si hay un VisualNode, dibujamos el árbol Fluent
                    if (_visualNode != null)
                    {
                        RenderNodeTree(_skCanvas, _visualNode);
                    }
                    else
                    {
                        float cx = Width / 2f; float cy = Height / 2f;
                        _skCanvas.Translate(cx, cy);
                        if (_animatedRotation != 0) _skCanvas.RotateDegrees(_animatedRotation);
                        if (_animatedScale != 1.0f) _skCanvas.Scale(ScaleX * _animatedScale, ScaleY * _animatedScale);
                        _skCanvas.Translate(-cx, -cy);

                        if (_cachedClipPath == null) UpdateCachedClipPath();

                        foreach (var layer in GetRenderOrder())
                        {
                            switch (layer)
                            {
                                case RenderLayer.Background:
                                    DrawBaseEngine(_skCanvas, shadowRectSK, activeBorder);
                                    break;
                                case RenderLayer.Image:
                                    if (_cachedClipPath != null)
                                    {
                                        _skCanvas.Save();
                                        _skCanvas.ClipPath(_cachedClipPath, SKClipOperation.Intersect, true);
                                        DrawSkiaImage(_skCanvas, contentRectSK);
                                        _skCanvas.Restore();
                                    }
                                    break;
                                case RenderLayer.Ripple:
                                    if (_isRippling && Enabled) DrawRipple(_skCanvas);
                                    break;
                                case RenderLayer.Content:
                                case RenderLayer.Overlay:
                                    if (_cachedClipPath != null)
                                    {
                                        _skCanvas.Save();
                                        _skCanvas.ClipPath(_cachedClipPath, SKClipOperation.Intersect, true);
                                        PaintSkia(_skCanvas, contentRectSK, paddedRectSK);
                                        _skCanvas.Restore();
                                    }
                                    break;
                            }
                        }
                    }

                    _skCanvas.Restore();

                    // 🔥 CRUCIAL: Obligar a Skia a mandar sus pixeles a la memoria RAM
                    _skCanvas.Flush();

                    e.Graphics.CompositingMode = CompositingMode.SourceOver;
                    e.Graphics.CompositingQuality = CompositingQuality.HighSpeed;
                    e.Graphics.InterpolationMode = InterpolationMode.Low;

#if NETFRAMEWORK
                    // 🔥 SOLUCIÓN DEFINITIVA AIR-GAP: Copia cruda de memoria (Zero-Allocation)
                    IntPtr skiaPtr = _skBitmap!.GetPixels();
                    if (skiaPtr != IntPtr.Zero)
                    {
                        int width = _skBitmap.Width;
                        int height = _skBitmap.Height;
                        int skiaStride = _skBitmap.RowBytes;
                        int totalBytes = skiaStride * height;

                        if (_netFxSafeBuffer == null || _netFxSafeBuffer.Length < totalBytes)
                            _netFxSafeBuffer = new byte[totalBytes];

                        Marshal.Copy(skiaPtr, _netFxSafeBuffer, 0, totalBytes);

                        if (_netFxSafeBitmap == null || _netFxSafeBitmap.Width != width || _netFxSafeBitmap.Height != height)
                        {
                            _netFxSafeBitmap?.Dispose();
                            // El constructor limpio que no le duele a GDI+
                            _netFxSafeBitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                        }

                        var bmpData = _netFxSafeBitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                        int bmpStride = Math.Abs(bmpData.Stride);

                        if (bmpStride == skiaStride)
                        {
                            Marshal.Copy(_netFxSafeBuffer, 0, bmpData.Scan0, totalBytes);
                        }
                        else
                        {
                            for (int y = 0; y < height; y++)
                            {
                                Marshal.Copy(_netFxSafeBuffer, y * skiaStride, bmpData.Scan0 + (y * bmpStride), width * 4);
                            }
                        }

                        _netFxSafeBitmap.UnlockBits(bmpData);

                        e.Graphics.DrawImageUnscaled(_netFxSafeBitmap, 0, 0);
                    }
#else
                    // 🔥 MÉTODO ZERO-COPY (.NET 8/10): Al dibujar el GDI Wrapper, ya contiene los pixeles mágicos de Skia.
                    if (_gdiWrapper != null)
                    {
                        e.Graphics.DrawImageUnscaled(_gdiWrapper, 0, 0);
                    }
#endif
                }
                catch (Exception ex)
                {
                    // 🔥 FIX BUCLE INFINITO DE LA MUERTE: Eliminado el Invalidate()
                    Trace.TraceError($"[ModernForms] Fallo SkiaRender, cambiando a GDI: {ex}");
                    _useSkiaGraphics = false;
                    e.Graphics.DrawString("OOM Interceptado. Cambiando motor...", SystemFonts.DefaultFont, Brushes.Red, 0, 0);
                }
            }
            else
            {
                PaintGDIPipeline(e.Graphics, contentRectF, paddedRectF);
            }
        }

        // 🔥 FIX DEBILIDAD 1: Renderizado Interpolado (Suavizado de estados)
        private void RenderNodeTree(SKCanvas canvas, RenderNode node)
        {
            if (!node.IsVisible) return;

            canvas.Save();

            // 1. BASES DEL NODO
            BackgroundData bg = node.Background;
            BorderData bd = node.Border;
            ShadowData sh = node.Shadow;
            float currentOpacity = node.Opacity;
            float targetScaleX = node.ScaleX;
            float targetScaleY = node.ScaleY;
            Color currentTextColor = node.Content.TextColor; // 
            Color currentIconColor = node.SvgTintColor;       // 🆕
            float currentTransX = node.TranslateX;        // 
            float currentTransY = node.TranslateY;        // 

            // 2. INTERPOLACIÓN (LERP) PARA HOVER (Transición Suave)
            if (node.HoverProgress > 0)
            {
                // 🔥 Se usa la fisica que el desarrollador haya elegido para el Hover, haciendo que la transición sea más fluida y personalizada
                float e = Easing.Calculate(node.Easing, node.HoverProgress);

                if (node.HoverState.Background.HasValue)
                {
                    bg.Color1 = LerpColor(bg.Color1, node.HoverState.Background.Value.Color1, e);
                    bg.Color2 = LerpColor(bg.Color2, node.HoverState.Background.Value.Color2, e);
                    if (node.HoverState.Background.Value.IsGradient)
                    {
                        bg.IsGradient = true;
                        bg.GradientAngle += (node.HoverState.Background.Value.GradientAngle - bg.GradientAngle) * e;
                    }
                }
                if (node.HoverState.Border.HasValue) bd.NormalColor = LerpColor(bd.NormalColor, node.HoverState.Border.Value.NormalColor, e);
                if (node.HoverState.Shadow.HasValue) sh.Color = LerpColor(sh.Color, node.HoverState.Shadow.Value.Color, e);
                if (node.HoverState.Opacity.HasValue) currentOpacity += (node.HoverState.Opacity.Value - currentOpacity) * e;
                if (node.HoverState.Scale.HasValue)
                {
                    targetScaleX += (node.HoverState.Scale.Value - targetScaleX) * e;
                    targetScaleY += (node.HoverState.Scale.Value - targetScaleY) * e;
                }
                if (node.HoverState.TextColor.HasValue)
                    currentTextColor = LerpColor(currentTextColor, node.HoverState.TextColor.Value, e); // 🆕
                if (node.HoverState.IconColor.HasValue)
                    currentIconColor = LerpColor(currentIconColor, node.HoverState.IconColor.Value, e); // 🆕
                if (node.HoverState.TranslateX.HasValue)
                    currentTransX += (node.HoverState.TranslateX.Value - currentTransX) * e;           // 🆕
                if (node.HoverState.TranslateY.HasValue)
                    currentTransY += (node.HoverState.TranslateY.Value - currentTransY) * e;           // 🆕
            }

            // 3. INTERPOLACIÓN (LERP) PARA PRESS (Transición Suave sobre el Hover)
            if (node.PressProgress > 0)
            {
                // 🔥 Se usa la física elegida
                float e = Easing.Calculate(node.Easing, node.PressProgress);

                if (node.PressState.Background.HasValue)
                {
                    bg.Color1 = LerpColor(bg.Color1, node.PressState.Background.Value.Color1, e);
                    bg.Color2 = LerpColor(bg.Color2, node.PressState.Background.Value.Color2, e);
                    if (node.PressState.Background.Value.IsGradient)
                    {
                        bg.IsGradient = true;
                        bg.GradientAngle += (node.PressState.Background.Value.GradientAngle - bg.GradientAngle) * e;
                    }
                }
                if (node.PressState.Border.HasValue) bd.NormalColor = LerpColor(bd.NormalColor, node.PressState.Border.Value.NormalColor, e);
                if (node.PressState.Shadow.HasValue) sh.Color = LerpColor(sh.Color, node.PressState.Shadow.Value.Color, e);
                if (node.PressState.Opacity.HasValue) currentOpacity += (node.PressState.Opacity.Value - currentOpacity) * e;
                if (node.PressState.Scale.HasValue)
                {
                    targetScaleX += (node.PressState.Scale.Value - targetScaleX) * e;
                    targetScaleY += (node.PressState.Scale.Value - targetScaleY) * e;
                }
                if (node.PressState.TextColor.HasValue)
                    currentTextColor = LerpColor(currentTextColor, node.PressState.TextColor.Value, e); // 🆕
                if (node.PressState.IconColor.HasValue)
                    currentIconColor = LerpColor(currentIconColor, node.PressState.IconColor.Value, e); // 🆕
                if (node.PressState.TranslateX.HasValue)
                    currentTransX += (node.PressState.TranslateX.Value - currentTransX) * e;            // 🆕
                if (node.PressState.TranslateY.HasValue)
                    currentTransY += (node.PressState.TranslateY.Value - currentTransY) * e;            // 🆕
            }
            // 3b. ESTADO DISABLED — sin lerp, instantáneo
            if (!node.Enabled)
            {
                if (node.DisabledState.Background.HasValue) { bg = node.DisabledState.Background.Value; }
                if (node.DisabledState.Border.HasValue) bd.NormalColor = node.DisabledState.Border.Value.NormalColor;
                if (node.DisabledState.Shadow.HasValue) sh.Color = node.DisabledState.Shadow.Value.Color;
                if (node.DisabledState.Opacity.HasValue) currentOpacity = node.DisabledState.Opacity.Value;
                if (node.DisabledState.TextColor.HasValue) currentTextColor = node.DisabledState.TextColor.Value;
                if (node.DisabledState.IconColor.HasValue) currentIconColor = node.DisabledState.IconColor.Value;
                if (node.DisabledState.Scale.HasValue) { targetScaleX = node.DisabledState.Scale.Value; targetScaleY = node.DisabledState.Scale.Value; }
            }
            // 4. TRANSFORMACIONES (Traslación, Rotación y Escala)
            // 4.0 Translate del nodo: mueve el elemento en world-space, como CSS
            if (currentTransX != 0f || currentTransY != 0f)
                canvas.Translate(currentTransX, currentTransY); // 🆕 usa la versión interpolada

            float cx = node.Layout.Left + (node.Layout.Width * node.TransformOrigin.X);
            float cy = node.Layout.Top + (node.Layout.Height * node.TransformOrigin.Y);

            canvas.Translate(cx, cy);
            if (node.Rotation != 0) canvas.RotateDegrees(node.Rotation);
            if (targetScaleX != 1.0f || targetScaleY != 1.0f) canvas.Scale(targetScaleX, targetScaleY);
            canvas.Translate(-cx, -cy);

            // =========================================================
            // 🔥 INYECCIÓN: EL EFECTO SWEEP (Capa 3 - Skia Render)
            // =========================================================
            if (node.Sweep.IsEnabled && node.HoverProgress > 0)
            {
                // Calculamos el radio máximo para que cubra todo el control
                float maxRadius = (float)Math.Sqrt(node.Layout.Width * node.Layout.Width + node.Layout.Height * node.Layout.Height) * 1.1f;

                // top: 100%, left: 100% (Esquina inferior derecha)
                float startX = node.Layout.Width + (maxRadius / 2f);
                float startY = node.Layout.Height + (maxRadius / 2f);

                // Centro final
                float endX = node.Layout.Width / 2f;
                float endY = node.Layout.Height / 2f;

                // Interpolación basada en tu HoverProgress (0.0 a 1.0)
                float currX = startX + (endX - startX) * node.HoverProgress;
                float currY = startY + (endY - startY) * node.HoverProgress;

                // CSS: overflow: hidden (Clip)
                using var clipPath = new SKPath();
                clipPath.AddRoundRect(new SKRect(0, 0, node.Layout.Width, node.Layout.Height), node.Corners.TopLeft, node.Corners.TopLeft);

                canvas.Save();
                canvas.ClipPath(clipPath, SKClipOperation.Intersect, true);

                // Dibujar el pseudo-elemento ::before (Sweep)
                using var sweepPaint = new SKPaint
                {
                    Color = node.Sweep.ThemeColor.ToSKColor(),
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawCircle(currX, currY, maxRadius, sweepPaint);

                canvas.Restore();
            }

            // 5. OPACIDAD
            if (currentOpacity < 1.0f)
            {
                byte alpha = (byte)Math.Max(0, Math.Min(255, currentOpacity * 255f));
                using (var alphaPaint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) })
                {
                    canvas.SaveLayer(alphaPaint);
                }
            }

            var rect = new SKRect(node.Layout.Left, node.Layout.Top, node.Layout.Right, node.Layout.Bottom);
            _sharedPaint ??= new SKPaint();

            // 6. SOMBRA
            if (sh.Radius > 0 && sh.Color.A > 0)
            {
                _sharedPaint.Reset();
                _sharedPaint.IsAntialias = true;
                _sharedPaint.Style = SKPaintStyle.Fill;
                _sharedPaint.Color = bg.Color1.A > 0 ? bg.Color1.ToSKColor() : SKColors.White;

                if (node._cachedShadowFilter == null)
                {
                    node._cachedShadowFilter = SKImageFilter.CreateDropShadow(
                        S(sh.OffsetX), S(sh.OffsetY), S(sh.Radius) / 2f, S(sh.Radius) / 2f, sh.Color.ToSKColor());
                }
                _sharedPaint.ImageFilter = node._cachedShadowFilter;

                if (node.Corners.TopLeft > 0) canvas.DrawRoundRect(rect, node.Corners.TopLeft, node.Corners.TopLeft, _sharedPaint);
                else canvas.DrawRect(rect, _sharedPaint);

                _sharedPaint.ImageFilter?.Dispose();
                _sharedPaint.ImageFilter = null;
            }

            // 🔥 INYECCIÓN 2 CORREGIDA: CRISTAL ÓPTICO (Skia 2.88.8 - Cero Lag)
            // Solo preguntamos si está habilitado (los Structs nunca son null)
            if (node.Acrylic.IsEnabled)
            {
                canvas.Save();

                // 1. Recortamos el cristal respetando tus bordes curvos
                if (node.Corners.TopLeft > 0)
                {
                    using var clipPath = new SKPath();
                    clipPath.AddRoundRect(rect, node.Corners.TopLeft, node.Corners.TopLeft);
                    canvas.ClipPath(clipPath, SKClipOperation.Intersect, true);
                }
                else
                {
                    canvas.ClipRect(rect, SKClipOperation.Intersect, true);
                }

                // 2. Tinte Cristalino (Glassmorphism por Transparencia Alpha)
                var tint = node.Acrylic.TintColor;
                using var tintPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = new SKColor(tint.R, tint.G, tint.B, tint.A),
                    IsAntialias = true
                };
                canvas.DrawRect(rect, tintPaint);

                // 3. Efecto "Bisel" (Glow Interno) para simular volumen de cristal 3D
                using var glowPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    // Si el cristal es oscuro, brillo blanco sutil. Si es claro, brillo más fuerte.
                    Color = new SKColor(255, 255, 255, (byte)(tint.A > 100 ? 25 : 60)),
                    StrokeWidth = 1.5f,
                    IsAntialias = true
                };

                // Dibujamos el reflejo 1 pixel hacia adentro
                var glowRect = new SKRect(rect.Left + 1, rect.Top + 1, rect.Right - 1, rect.Bottom - 1);
                if (node.Corners.TopLeft > 0)
                    canvas.DrawRoundRect(glowRect, node.Corners.TopLeft, node.Corners.TopLeft, glowPaint);
                else
                    canvas.DrawRect(glowRect, glowPaint);

                canvas.Restore();
            }

            // 🔥 7.  FONDO Y FILTROS CSS
            _sharedPaint.Reset();
            _sharedPaint.IsAntialias = true;

            SKImageFilter? blurFilter = null;
            SKColorFilter? colorFilter = null;

            if (node.Filters.Blur > 0)
            {
                blurFilter = SKImageFilter.CreateBlur(node.Filters.Blur, node.Filters.Blur);
            }

            if (node.Filters.Grayscale > 0 || node.Filters.Brightness != 1f || node.Filters.Contrast != 1f)
            {
                float b = node.Filters.Brightness;
                float c = node.Filters.Contrast;
                float t = (1.0f - c) / 2.0f;
                float g = node.Filters.Grayscale;
                float invG = 1f - g;

                float lumR = 0.2126f * g;
                float lumG = 0.7152f * g;
                float lumB = 0.0722f * g;

                float[] matrix = new float[] {
                    (lumR + invG) * c * b, (lumG) * c * b,        (lumB) * c * b,        0, t * 255,
                    (lumR) * c * b,        (lumG + invG) * c * b, (lumB) * c * b,        0, t * 255,
                    (lumR) * c * b,        (lumG) * c * b,        (lumB + invG) * c * b, 0, t * 255,
                    0,                     0,                     0,                     1, 0
                };
                colorFilter = SKColorFilter.CreateColorMatrix(matrix);
            }

            if (blurFilter != null && colorFilter != null)
                _sharedPaint.ImageFilter = SKImageFilter.CreateCompose(blurFilter, SKImageFilter.CreateColorFilter(colorFilter));
            else if (blurFilter != null)
                _sharedPaint.ImageFilter = blurFilter;
            else if (colorFilter != null)
                _sharedPaint.ColorFilter = colorFilter;

            if (bg.Color1.A > 0 || bg.Color2.A > 0)
            {
                _sharedPaint.Style = SKPaintStyle.Fill;
                if (bg.IsGradient)
                {
                    _sharedPaint.Shader = SKShader.CreateLinearGradient(
                        new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
                        new SKColor[] { bg.Color1.ToSKColor(), bg.Color2.ToSKColor() }, null, SKShaderTileMode.Clamp);
                }
                else
                {
                    _sharedPaint.Color = bg.Color1.ToSKColor();
                }

                if (node.Corners.TopLeft > 0) canvas.DrawRoundRect(rect, node.Corners.TopLeft, node.Corners.TopLeft, _sharedPaint);
                else canvas.DrawRect(rect, _sharedPaint);

                _sharedPaint.Shader?.Dispose();
                _sharedPaint.Shader = null;
            }

            _sharedPaint.ImageFilter?.Dispose();
            _sharedPaint.ImageFilter = null;
            _sharedPaint.ColorFilter?.Dispose();
            _sharedPaint.ColorFilter = null;

            // 8. RIPPLE EFFECT EN NODO
            if (_isRippling && Enabled && node == _currentPressedNode && node.Ripple.Color.A > 0)
            {
                canvas.Save();
                if (node.Corners.TopLeft > 0)
                {
                    using var rClip = new SKPath(); // 🔥 FIX LEAK: Cierre hermético de memoria C++
                    rClip.AddRoundRect(rect, node.Corners.TopLeft, node.Corners.TopLeft);
                    canvas.ClipPath(rClip, SKClipOperation.Intersect, true);
                }
                else canvas.ClipRect(rect, SKClipOperation.Intersect, true);

                using (var ripplePaint = new SKPaint { Color = node.Ripple.Color.ToSKColor().WithAlpha((byte)(node.Ripple.Opacity * 255)), IsAntialias = true })
                {
                    canvas.DrawCircle(_rippleCenter.X, _rippleCenter.Y, _rippleRadius, ripplePaint);
                }
                canvas.Restore();
            }

            // 9. BORDE ASIMÉTRICO
            if (bd.NormalColor.A > 0)
            {
                _sharedPaint.Reset();
                _sharedPaint.IsAntialias = true;
                _sharedPaint.Style = SKPaintStyle.Stroke;
                _sharedPaint.Color = bd.NormalColor.ToSKColor();

                bool isUniform = bd.Thickness.Top == bd.Thickness.Right && bd.Thickness.Top == bd.Thickness.Bottom && bd.Thickness.Top == bd.Thickness.Left;

                if (isUniform && bd.Thickness.Top > 0)
                {
                    _sharedPaint.StrokeWidth = bd.Thickness.Top;
                    if (node.Corners.TopLeft > 0) canvas.DrawRoundRect(rect, node.Corners.TopLeft, node.Corners.TopLeft, _sharedPaint);
                    else canvas.DrawRect(rect, _sharedPaint);
                }
                else if (!isUniform)
                {
                    if (bd.Thickness.Top > 0) { _sharedPaint.StrokeWidth = bd.Thickness.Top; canvas.DrawLine(rect.Left, rect.Top + bd.Thickness.Top / 2, rect.Right, rect.Top + bd.Thickness.Top / 2, _sharedPaint); }
                    if (bd.Thickness.Right > 0) { _sharedPaint.StrokeWidth = bd.Thickness.Right; canvas.DrawLine(rect.Right - bd.Thickness.Right / 2, rect.Top, rect.Right - bd.Thickness.Right / 2, rect.Bottom, _sharedPaint); }
                    if (bd.Thickness.Bottom > 0) { _sharedPaint.StrokeWidth = bd.Thickness.Bottom; canvas.DrawLine(rect.Left, rect.Bottom - bd.Thickness.Bottom / 2, rect.Right, rect.Bottom - bd.Thickness.Bottom / 2, _sharedPaint); }
                    if (bd.Thickness.Left > 0) { _sharedPaint.StrokeWidth = bd.Thickness.Left; canvas.DrawLine(rect.Left + bd.Thickness.Left / 2, rect.Top, rect.Left + bd.Thickness.Left / 2, rect.Bottom, _sharedPaint); }
                }
            }

            // 🔥 9.5  IMÁGENES DEL NODO
            if (node.Content.Image != null && node.Content.ImageOpacity > 0)
            {
                _sharedPaint.Reset();
                _sharedPaint.IsAntialias = true;

                if (node.Content.ImageOpacity < 1.0f)
                {
                    _sharedPaint.ColorFilter = SKColorFilter.CreateBlendMode(
                        SKColors.White.WithAlpha((byte)(node.Content.ImageOpacity * 255)), SKBlendMode.DstIn);
                }

                using (var ms = new System.IO.MemoryStream())
                {
                    node.Content.Image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    using (var skImage = SKImage.FromEncodedData(ms))
                    {
                        var destRect = rect;
                        canvas.DrawImage(skImage, destRect, _sharedPaint);
                    }
                }
                _sharedPaint.ColorFilter?.Dispose();
                _sharedPaint.ColorFilter = null;
            }

            // 🔥 10.  TEXTO ALINEADO, WORDWRAP Y DECORACIONES MAS SVG  
            var textRect = rect;
            if (node.SvgPicture != null && node.IconPosition != IconAlign.Center)
            {
                switch (node.IconPosition)
                {
                    case IconAlign.Left: textRect.Left += node.SvgSize.Width + node.IconGap; break;
                    case IconAlign.Right: textRect.Right -= node.SvgSize.Width + node.IconGap; break;
                    case IconAlign.Top: textRect.Top += node.SvgSize.Height + node.IconGap; break;
                    case IconAlign.Bottom: textRect.Bottom -= node.SvgSize.Height + node.IconGap; break;
                }
            }
            if (!string.IsNullOrEmpty(node.Content.Text))
            {
                _sharedPaint.Reset();
                _sharedPaint.IsAntialias = true;
                _sharedPaint.LcdRenderText = true;
                _sharedPaint.SubpixelText = true;
                _sharedPaint.HintingLevel = SKPaintHinting.Full;
                _sharedPaint.Color = currentTextColor.ToSKColor();
                _sharedPaint.TextSize = S(node.Content.FontSize);
                _sharedPaint.Typeface = GetOrCreateTypeface(node.Content.FontFamily, node.Content.IsBold, node.Content.IsItalic);

                float tx = textRect.Left;
                if (node.Content.HorizontalAlignment == StringAlignment.Center) { _sharedPaint.TextAlign = SKTextAlign.Center; tx = textRect.MidX; }
                else if (node.Content.HorizontalAlignment == StringAlignment.Far) { _sharedPaint.TextAlign = SKTextAlign.Right; tx = textRect.Right; }
                else { _sharedPaint.TextAlign = SKTextAlign.Left; }

                if (node.Content.WordWrap)
                {
                    var lines = WrapTextSkia(node.Content.Text, _sharedPaint, textRect.Width);
                    float lineHeight = _sharedPaint.FontMetrics.Descent - _sharedPaint.FontMetrics.Ascent;
                    float totalHeight = lines.Count * lineHeight;

                    float ty = textRect.Top - _sharedPaint.FontMetrics.Ascent;
                    if (node.Content.VerticalAlignment == StringAlignment.Center) ty = textRect.MidY - (totalHeight / 2f) - _sharedPaint.FontMetrics.Ascent;
                    else if (node.Content.VerticalAlignment == StringAlignment.Far) ty = textRect.Bottom - totalHeight - _sharedPaint.FontMetrics.Ascent;

                    foreach (var line in lines)
                    {
                        canvas.DrawText(line, tx, ty, _sharedPaint);
                        if (node.Content.Decoration == TextDecoration.Underline)
                        {
                            SKRect lb = new SKRect();
                            _sharedPaint.MeasureText(line, ref lb);
                            float underlineY = ty + S(2f);
                            canvas.DrawLine(lb.Left + tx, underlineY, lb.Right + tx, underlineY, _sharedPaint);
                        }
                        else if (node.Content.Decoration == TextDecoration.Strikethrough)
                        {
                            SKRect lb = new SKRect();
                            _sharedPaint.MeasureText(line, ref lb);
                            float strikeY = ty - (lb.Height / 2f);
                            canvas.DrawLine(lb.Left + tx, strikeY, lb.Right + tx, strikeY, _sharedPaint);
                        }
                        ty += lineHeight;
                    }
                }
                else
                {
                    SKRect textBounds = new SKRect();
                    _sharedPaint.MeasureText(node.Content.Text, ref textBounds);

                    float ty = textRect.Top;
                    if (node.Content.VerticalAlignment == StringAlignment.Center) ty = textRect.MidY - textBounds.MidY;
                    else if (node.Content.VerticalAlignment == StringAlignment.Far) ty = textRect.Bottom - textBounds.Bottom;
                    else ty = textRect.Top - textBounds.Top;

                    if (node.Content.Decoration == TextDecoration.Underline)
                    {
                        float underlineY = ty + S(2f);
                        canvas.DrawLine(textBounds.Left + tx, underlineY, textBounds.Right + tx, underlineY, _sharedPaint);
                    }
                    else if (node.Content.Decoration == TextDecoration.Strikethrough)
                    {
                        float strikeY = ty - (textBounds.Height / 2f);
                        canvas.DrawLine(textBounds.Left + tx, strikeY, textBounds.Right + tx, strikeY, _sharedPaint);
                    }

                    canvas.DrawText(node.Content.Text, tx, ty, _sharedPaint);
                }
            }
            // 10.5. SVG ICON — vectorial, escala sin pixelar
            // 10.5. SVG ICON — vectorial, escala sin pixelar (🔥 FIX: Matriz corregida)
            if (node.SvgPicture != null)
            {
                _sharedPaint.Reset();
                _sharedPaint.IsAntialias = true;

                if (currentIconColor != Color.Empty)
                {
                    if (node._cachedSvgTint == null || node._lastSvgTintColor != currentIconColor)
                    {
                        node._cachedSvgTint?.Dispose();
                        node._cachedSvgTint = SKColorFilter.CreateBlendMode(
                            new SKColor(currentIconColor.R, currentIconColor.G, currentIconColor.B, currentIconColor.A),
                            SKBlendMode.SrcIn);
                        node._lastSvgTintColor = currentIconColor;
                    }
                    _sharedPaint.ColorFilter = node._cachedSvgTint;
                }

                var cull = node.SvgPicture.CullRect;
                float scaleX = cull.Width > 0 ? node.SvgSize.Width / cull.Width : 1f;
                float scaleY = cull.Height > 0 ? node.SvgSize.Height / cull.Height : 1f;

                float px, py;
                switch (node.IconPosition)
                {
                    case IconAlign.Left: px = rect.Left; py = rect.MidY - node.SvgSize.Height / 2f; break;
                    case IconAlign.Right: px = rect.Right - node.SvgSize.Width; py = rect.MidY - node.SvgSize.Height / 2f; break;
                    case IconAlign.Top: px = rect.MidX - node.SvgSize.Width / 2f; py = rect.Top; break;
                    case IconAlign.Bottom: px = rect.MidX - node.SvgSize.Width / 2f; py = rect.Bottom - node.SvgSize.Height; break;
                    default: px = rect.MidX - node.SvgSize.Width / 2f; py = rect.MidY - node.SvgSize.Height / 2f; break;
                }

                // 🔥 FIX PIXELADO: Compensar el offset del CullRect para que el SVG se dibuje nítido
                var matrix = SKMatrix.CreateScaleTranslation(scaleX, scaleY, px - cull.Left * scaleX, py - cull.Top * scaleY);

                canvas.Save();
                canvas.Concat(ref matrix);
                canvas.DrawPicture(node.SvgPicture, _sharedPaint);
                canvas.Restore();

                _sharedPaint.Reset();
            }

            // 11. RECURSIVIDAD DE HIJOS
            if (node.Children.Count > 0)
            {
                canvas.Save();
                if (node.Corners.TopLeft > 0)
                {
                    using var clipPath = new SKPath();
                    clipPath.AddRoundRect(rect, node.Corners.TopLeft, node.Corners.TopLeft);
                    canvas.ClipPath(clipPath, SKClipOperation.Intersect, true);
                }
                else canvas.ClipRect(rect, SKClipOperation.Intersect, true);

                foreach (var child in node.Children) RenderNodeTree(canvas, child);
                canvas.Restore();
            }
            // 12. BADGE — zero-alloc, top-right del nodo
            if (node.Badge.IsVisible)
            {
                float bHalf = (float)(node.Badge.Size * 0.5);
                float bX = node.Layout.Right + (float)node.Badge.OffsetX - bHalf;
                float bY = node.Layout.Top + (float)node.Badge.OffsetY + bHalf;

                _sharedPaint.Reset();
                _sharedPaint.IsAntialias = true;
                _sharedPaint.Style = SKPaintStyle.Fill;
                _sharedPaint.Color = node.Badge.Background.ToSKColor();
                canvas.DrawCircle(bX, bY, bHalf, _sharedPaint);

                if (!string.IsNullOrEmpty(node.Badge.Text))
                {
                    _sharedPaint.Color = node.Badge.TextColor.ToSKColor();
                    _sharedPaint.TextSize = (float)(node.Badge.Size * 0.55);
                    _sharedPaint.TextAlign = SKTextAlign.Center;
                    _sharedPaint.FakeBoldText = true;
                    canvas.DrawText(node.Badge.Text, bX, bY + _sharedPaint.TextSize * 0.35f, _sharedPaint);
                }

                _sharedPaint.Reset();
            }

            if (currentOpacity < 1.0f) canvas.Restore();
            canvas.Restore();
        }

        /// <summary>
        /// Temas
        /// </summary>
        private Action? _themeUpdater;

        public void WatchTheme(Action updater)
        {
            // limpia suscripción previa
            if (_themeUpdater != null)
                AppTheme.ThemeChanged -= _themeUpdater;

            _themeUpdater = updater;
            AppTheme.ThemeChanged += _themeUpdater;
        }

       

        protected void DrawBaseEngine(SKCanvas canvas, SKRect shadowRect, float activeBorder)
        {
            if (canvas == null) return;

            _sharedPaint ??= new SKPaint { IsAntialias = true };
            _sharedPath ??= new SKPath();

            _sharedPaint.Reset();
            _sharedPaint.ImageFilter = null;
            _sharedPaint.Shader?.Dispose();
            _sharedPaint.Shader = null;

            _sharedPath.Reset();
            float rad = Math.Max(0, S(BorderRadius));
            _sharedPath.AddRoundRect(shadowRect, rad, rad);

            var fillRect = new SKRect(shadowRect.Left + activeBorder / 2f, shadowRect.Top + activeBorder / 2f,
                                      shadowRect.Right - activeBorder / 2f, shadowRect.Bottom - activeBorder / 2f);

            using var fillPath = new SKPath(); // Ya estaba protegido, se mantiene.
            float innerRad = Math.Max(0, rad - (activeBorder / 2f));
            fillPath.AddRoundRect(fillRect, innerRad, innerRad);

            CalculateStateColors(out Color bgColor1, out Color bgColor2, out Color borderColorTarget);

            var acrylicFilter = GetOrCreateAcrylicFilter();
            _sharedPaint.ImageFilter = acrylicFilter;
            _sharedPaint.Style = SKPaintStyle.Fill;

            if (UseGradient)
            {
                using var shader = SKShader.CreateLinearGradient(
                    new SKPoint(fillRect.Left, fillRect.Top),
                    new SKPoint(fillRect.Right, fillRect.Bottom),
                    new SKColor[] { bgColor1.ToSKColor(), bgColor2.ToSKColor() },
                    null, SKShaderTileMode.Clamp);
                _sharedPaint.Shader = shader;
                canvas.DrawPath(fillPath, _sharedPaint);
                // 🔥 FIX LEAK 2: Doble Dispose eliminado para evitar corrupción en la memoria de Skia
            }
            else
            {
                _sharedPaint.Color = bgColor1.ToSKColor();
                canvas.DrawPath(fillPath, _sharedPaint);
            }

            if (activeBorder > 0)
            {
                _sharedPaint.Style = SKPaintStyle.Stroke;
                _sharedPaint.StrokeWidth = activeBorder;
                _sharedPaint.ImageFilter = null;
                _sharedPaint.Color = borderColorTarget.ToSKColor();
                canvas.DrawPath(_sharedPath, _sharedPaint);
            }

            var shadowFilter = GetOrCreateShadowFilter();
            if (shadowFilter != null)
            {
                _sharedPaint.ImageFilter = shadowFilter;
                _sharedPaint.Style = SKPaintStyle.Fill;
                _sharedPaint.Color = SKColors.Transparent;
                canvas.DrawPath(_sharedPath, _sharedPaint);
                _sharedPaint.ImageFilter = null;
            }

            _sharedPaint.Reset();
        }

        private void UpdateCachedClipPath()
        {
            try
            {
                SafeDispose(ref _cachedClipPath);

                // 🔥 INTELIGENCIA ESPACIAL EN EL RECORTE
                float maxBorder = Math.Max(BorderThickness, FocusThickness);
                float maxShadowSpace = UseShadow ? S(ShadowBlur) : 0;

                float margin = maxShadowSpace + S(maxBorder / 2f);
                float activeBorder = S(IsFocusedControl ? FocusThickness : BorderThickness);

                var shadowRectSK = new SKRect(margin, margin, Width - margin, Height - margin);
                var contentRectSK = shadowRectSK;
                contentRectSK.Inflate(-(activeBorder / 2f), -(activeBorder / 2f));

                _cachedClipPath = new SKPath();

                // Curva matemática perfecta restando la mitad del borde
                float innerRadius = Math.Max(0, S(BorderRadius) - (activeBorder / 2f));
                _cachedClipPath.AddRoundRect(contentRectSK, innerRadius, innerRadius);
            }
            catch { /* no crash on update */ }
        }

        protected virtual void DrawSkiaImage(SKCanvas canvas, SKRect contentRect) { /* tu implementación existente */ }
        protected virtual void DrawRipple(SKCanvas canvas) { /* tu implementación existente */ }
        protected virtual void PaintSkia(SKCanvas canvas, SKRect contentRect, SKRect paddedRect) { /* tu implementación existente */ }

        protected virtual void PaintGDI(Graphics g, RectangleF contentRect, RectangleF paddedRect)
        {
            if (g == null) return;
            using (var brush = new SolidBrush(BackgroundColor))
            {
                g.FillRectangle(brush, contentRect);
            }
        }

        protected virtual void PaintGDIPipeline(Graphics g, RectangleF contentRect, RectangleF paddedRect)
        {
            using (var brush = new SolidBrush(BackgroundColor)) g.FillRectangle(brush, contentRect);
        }
        // =========================================================
        // 🔥 PASO FINAL: MODO FANTASMA (HIT-TESTING A NIVEL OS CON SCALE)
        // =========================================================
        private static Point GetLParamPoint(IntPtr lParam)
        {
            int val = unchecked((int)(long)lParam);
            return new Point((short)(val & 0xffff), (short)((val >> 16) & 0xffff));
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0014) return; // WM_ERASEBKGND (Cero parpadeos nativos)

            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;

            if (m.Msg == WM_NCHITTEST && !DesignMode && Enabled)
            {
                base.WndProc(ref m); // Dejamos que Windows haga su cálculo base
                if (m.Result.ToInt32() == 1 && !_logicalBounds.IsEmpty && EngineOffset != PointF.Empty)
                {
                    Point screenPoint = GetLParamPoint(m.LParam);
                    Point clientPoint = this.PointToClient(screenPoint);

                    // 🎯 Buscamos la escala máxima activa en este momento
                    float currentScaleX = this.ScaleX;
                    float currentScaleY = this.ScaleY;

                    if (_visualNode != null && (_isAnimating || _isHoveringInternal || _isMouseDownInternal))
                    {
                        float _dum1 = 0f, _dum2 = 0f, _dum3 = 0f, _dum4 = 0f;
                        CalculateMaxOverflow(_visualNode, ref currentScaleX, ref currentScaleY,
                            ref _dum1, ref _dum2, ref _dum3, ref _dum4);
                    }

                    // Calculamos el tamaño exacto visual ya escalado
                    int visualW = (int)(_logicalBounds.IsEmpty ? this.Width : _logicalBounds.Width * currentScaleX);
                    int visualH = (int)(_logicalBounds.IsEmpty ? this.Height : _logicalBounds.Height * currentScaleY);

                    // Calculamos la caja final usando Offset, Traslación y Escala
                    // Compensamos el shift que genera el scale centrado en Skia
                    float halfShiftX = _logicalBounds.IsEmpty ? 0f : _logicalBounds.Width * (currentScaleX - 1f) / 2f;
                    float halfShiftY = _logicalBounds.IsEmpty ? 0f : _logicalBounds.Height * (currentScaleY - 1f) / 2f;

                    // Calculamos la caja final usando Offset, Traslación y Escala
                    // 🚀 FIX: Usamos RectangleF (float) para no perder precisión en la física
                    RectangleF drawnArea = new RectangleF(
                        EngineOffset.X + this.TranslateX - halfShiftX,
                        EngineOffset.Y + this.TranslateY - halfShiftY,
                        visualW,
                        visualH
                    );

                    // Damos 1px de margen de seguridad (Inflate) para absorber errores de redondeo nativo de Windows
                    drawnArea.Inflate(1f, 1f);

                    // Evaluamos las coordenadas exactas pasando X y Y como floats
                    if (!drawnArea.Contains(clientPoint.X, clientPoint.Y))
                    {
                        m.Result = (IntPtr)HTTRANSPARENT;
                    }
                }
                return;
            }

            base.WndProc(ref m);
        }
    }
}