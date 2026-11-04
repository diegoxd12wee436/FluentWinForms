using SkiaSharp;

namespace FluentWinForms.Rendering
{
    /// <summary>Guarda el fondo ya difuminado de UNA región. Solo se recalcula si algo cambió.</summary>
    public sealed class GlassBackdropCache : IDisposable
    {
        private SKImage? _blurred;
        private IBackdropSource? _source;
        private int _version = -1;
        private SKRectI _region;
        private GlassStyle _style;

        public SKImage? Get(IBackdropSource source, SKRectI region, in GlassStyle style)
        {
            var img = source.Image;
            if (img == null || region.Width <= 0 || region.Height <= 0) return null;

            if (_blurred != null && ReferenceEquals(_source, source) && _version == source.Version
                && _region == region && _style.SameBackdropLook(style))
                return _blurred;

            _blurred?.Dispose();
            _blurred = Build(img, region, style);
            _source = source; _version = source.Version; _region = region; _style = style;
            return _blurred;
        }

        private static SKImage? Build(SKImage img, SKRectI region, in GlassStyle st)
        {
            int ds = Math.Max(1, st.Downsample);
            // Margen alrededor de la región (múltiplo de ds) para que el blur vea los vecinos y no oscurezca los bordes
            int pad = (int)MathF.Ceiling(st.BlurSigma * 3f / ds) * ds;

            var want = new SKRectI(region.Left - pad, region.Top - pad, region.Right + pad, region.Bottom + pad);
            var src = SKRectI.Intersect(want, new SKRectI(0, 0, img.Width, img.Height));
            if (src.Width <= 0 || src.Height <= 0) return null;

            int sw = (want.Width + ds - 1) / ds, sh = (want.Height + ds - 1) / ds;
            using var surface = SKSurface.Create(new SKImageInfo(sw, sh));
            if (surface == null) return null;

            var dest = SKRect.Create((src.Left - want.Left) / (float)ds, (src.Top - want.Top) / (float)ds,
                                     src.Width / (float)ds, src.Height / (float)ds);
            float sigma = st.BlurSigma / ds;
            using var blur = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp);
            using var look = SKColorFilter.CreateColorMatrix(LookMatrix(st.Saturation, st.Brightness));
            using var paint = new SKPaint { ImageFilter = blur, ColorFilter = look };

            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.DrawImage(img, SKRect.Create(src.Left, src.Top, src.Width, src.Height), dest,
                             new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);

            int p = pad / ds;
            var inner = new SKRectI(p, p, p + (region.Width + ds - 1) / ds, p + (region.Height + ds - 1) / ds);
            return surface.Snapshot(inner);
        }

        // Saturación + brillo (sin columna de traslación → no depende de su escala 0-1 / 0-255)
        private static float[] LookMatrix(float sat, float bright)
        {
            float i = 1f - sat;
            float r = 0.2126f * i, g = 0.7152f * i, b = 0.0722f * i;
            return new[]
            {
                (r + sat) * bright, g * bright,         b * bright,         0f, 0f,
                r * bright,         (g + sat) * bright, b * bright,         0f, 0f,
                r * bright,         g * bright,         (b + sat) * bright, 0f, 0f,
                0f,                 0f,                 0f,                 1f, 0f,
            };
        }

        public void Dispose() { _blurred?.Dispose(); _blurred = null; }
    }
}
