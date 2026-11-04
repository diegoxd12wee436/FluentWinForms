using SkiaSharp;

namespace FluentWinForms.Rendering
{
    /// <summary>Dibuja el cristal de UN control: fondo difuminado + tinte + borde, recortado a esquinas redondeadas.</summary>
    public sealed class GlassPainter : IDisposable
    {
        private readonly GlassBackdropCache _cache = new();
        private readonly SKPaint _fill = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPaint _stroke = new() { Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
        private SKRoundRect? _clip;
        private SKRect _clipRect;
        private float _clipRadius = -1f;

        /// <param name="backdropRegion">Zona de la fuente que queda detrás del control (px de la fuente).</param>
        public void Paint(SKCanvas canvas, SKRect rect, float radius, in GlassStyle style,
                          IBackdropSource? source, SKRectI backdropRegion)
        {
            if (_clip == null || _clipRect != rect || _clipRadius != radius)
            {
                _clip?.Dispose();
                _clip = new SKRoundRect(rect, radius, radius);
                _clipRect = rect;
                _clipRadius = radius;
            }

            canvas.Save();
            canvas.ClipRoundRect(_clip, SKClipOperation.Intersect, true);

            var blurred = source != null ? _cache.Get(source, backdropRegion, style) : null;
            if (blurred != null)
            {
                float ds = Math.Max(1, style.Downsample);
                var from = SKRect.Create(0, 0, backdropRegion.Width / ds, backdropRegion.Height / ds);
                canvas.DrawImage(blurred, from, rect, new SKSamplingOptions(SKFilterMode.Linear));
            }

            _fill.Color = style.Tint;
            canvas.DrawRect(rect, _fill);
            canvas.Restore();

            if (style.Border.Alpha > 0)
            {
                _stroke.Color = style.Border;
                var edge = new SKRect(rect.Left + 0.5f, rect.Top + 0.5f, rect.Right - 0.5f, rect.Bottom - 0.5f);
                canvas.DrawRoundRect(edge, radius, radius, _stroke);
            }
        }

        public void Dispose()
        {
            _cache.Dispose();
            _fill.Dispose();
            _stroke.Dispose();
            _clip?.Dispose();
        }
    }
}