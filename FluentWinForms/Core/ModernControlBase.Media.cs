#nullable enable
#pragma warning disable CA1416 // 🔥 INYECCIÓN PRO: Silencia la advertencia de compatibilidad cross-platform de System.Drawing
#pragma warning disable IDE0090 // Silencia sugerencias de simplificar 'new'
#pragma warning disable IDE0028 // Silencia sugerencias de inicialización
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FluentWinForms.Core
{
    public abstract partial class ModernControlBase
    {
        #region 🔵 Multimedia: Imágenes Seguras
        private Image? _backgroundImg;
        [Category("ModernForms - Media")]
        [Description("La imagen de fondo del control.\nThe background image of the control.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Image? BackgroundImg
        {
            get => _backgroundImg;
            set { _backgroundImg = value; CacheSkiaImage(); RefreshVisuals(); }
        }
        [Category("ModernForms - Media")]
        [Description("El diseño de la imagen de fondo del control.\nThe layout of the control's background image.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public ImageLayout BackgroundImgLayout { get; set; } = ImageLayout.Zoom;
        [Category("ModernForms - Media")]
        [Description("La opacidad de la imagen de fondo del control.\nThe opacity of the control's background image.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public float ImageOpacity { get; set; } = 1.0f;

        protected SKImage? _cachedSkImage;

        private void CacheSkiaImage()
        {
            SafeDispose(ref _cachedSkImage);
            if (_backgroundImg == null) return;

            if (_backgroundImg.Width <= 0 || _backgroundImg.Height <= 0)
            {
                Trace.TraceWarning($"[ModernForms Media] Imagen ignorada por dimensiones inválidas: {_backgroundImg.Width}x{_backgroundImg.Height}");
                return;
            }

            try
            {
                using (var ms = new MemoryStream())
                {
                    _backgroundImg.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    _cachedSkImage = SKImage.FromEncodedData(ms);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[ModernForms Media] Fallo decodificando imagen para GPU: {ex.Message}");
                _cachedSkImage = null;
            }
        }

        protected void ClearImageCache() => SafeDispose(ref _cachedSkImage);
        #endregion

        #region 🔵 Multimedia: Textos Optimizados
        [Category("ModernForms - Text")]
        [Description("El color del texto del control.\nThe color of the control's text.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color TextColor { get; set; } = Color.Black;
        [Category("ModernForms - Text")]
        [Description("El tamaño del texto del control.\nThe size of the control's text.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public float FontSize { get; set; } = 12;

        private string _fontFamily = "Segoe UI";
        [Category("ModernForms - Text")]
        [Description("La familia de fuentes del texto del control.\nThe font family of the control's text.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public string FontFamily { get => _fontFamily; set { _fontFamily = value; ClearTextCache(); RefreshVisuals(); } }

        private bool _fontWeightBold = false;
        [Category("ModernForms - Text")]
        [Description("Indica si el texto del control es negrita.\nIndicates whether the control's text is bold.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public bool FontWeightBold { get => _fontWeightBold; set { _fontWeightBold = value; ClearTextCache(); RefreshVisuals(); } }

        [Category("ModernForms - Text")]
        [Description("La alineación horizontal del texto del control.\nThe horizontal alignment of the control's text.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public StringAlignment TextAlignment { get; set; } = StringAlignment.Center;
        [Category("ModernForms - Text")]
        [Description("La alineación vertical del texto del control.\nThe vertical alignment of the control's text.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public StringAlignment VerticalAlignment { get; set; } = StringAlignment.Center;
        [Category("ModernForms - Text")]
        [Description("Indica si el texto del control se ajusta automáticamente a varias líneas.\nIndicates whether the control's text wraps automatically.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public bool WordWrap { get; set; } = false;

        protected SKTypeface? _cachedTypeface;

        protected void ClearTextCache() => SafeDispose(ref _cachedTypeface);

        // Utilidad interna para envolver el texto si WordWrap está activado
        protected List<string> WrapTextSkia(string text, SKPaint paint, float maxWidth)
        {
            var words = text.Split(' ');
            var lines = new List<string>();
            string currentLine = "";

            foreach (var word in words)
            {
                if (paint.MeasureText(word) > maxWidth)
                {
                    if (!string.IsNullOrEmpty(currentLine)) { lines.Add(currentLine); currentLine = ""; }

                    // Cortar palabras anormalmente largas (Anti-DoS)
                    if (word.Length > 80)
                    {
                        lines.Add(word.Substring(0, 77) + "...");
                        continue;
                    }

                    string tempWord = "";
                    foreach (char c in word)
                    {
                        if (paint.MeasureText(tempWord + c) <= maxWidth) tempWord += c;
                        else { lines.Add(tempWord); tempWord = c.ToString(); }
                    }
                    currentLine = tempWord;
                    continue;
                }

                string testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                if (paint.MeasureText(testLine) <= maxWidth)
                    currentLine = testLine;
                else
                {
                    lines.Add(currentLine);
                    currentLine = word;
                }
            }
            if (!string.IsNullOrEmpty(currentLine)) lines.Add(currentLine);

            return lines;
        }
        #endregion
    }
}