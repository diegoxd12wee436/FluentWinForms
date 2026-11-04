using SkiaSharp;

namespace FluentWinForms.Rendering
{
    /// <summary>Fuente de "lo que hay detrás" del cristal. La implementa quien conozca la escena (el motor, no DWM).</summary>
    public interface IBackdropSource
    {
        /// <summary>Escena detrás de los cristales, en coordenadas del host. Null = sin fondo.</summary>
        SKImage? Image { get; }

        /// <summary>Sube cada vez que la imagen cambia; invalida los blurs cacheados.</summary>
        int Version { get; }
    }

    /// <summary>Receta visual del cristal (CSS: backdrop-filter + background + border).</summary>
    public readonly record struct GlassStyle(
        SKColor Tint,
        float BlurSigma = 16f,
        float Saturation = 1.4f,
        float Brightness = 1f,
        int Downsample = 4,
        SKColor Border = default)
    {
        /// <summary>El blur cacheado solo depende de estos campos (el tinte y el borde no lo afectan).</summary>
        internal bool SameBackdropLook(in GlassStyle o) =>
            BlurSigma == o.BlurSigma && Saturation == o.Saturation &&
            Brightness == o.Brightness && Downsample == o.Downsample;
    }
}
