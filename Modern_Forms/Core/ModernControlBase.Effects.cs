#nullable enable
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;

using System.Windows.Forms;

namespace FluentWinForms.Core
{
    public abstract partial class ModernControlBase
    {
        #region 🔵 Efecto: Sombra (Shadow)
        private bool _useShadow = false;
        [Category("Modern - Effects")]
        [Description("Habilita o deshabilita el efecto de sombra para este control.\nEnable or disable the shadow effect for this control.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public bool UseShadow { get => _useShadow; set { if (_useShadow == value) return; _useShadow = value; ClearCaches(); RefreshVisuals(); } }

        private Color _shadowColor = Color.FromArgb(50, 0, 0, 0);
        [Category("Modern - Effects")]
        [Description("El color de la sombra del control.\nThe color of the control's shadow.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color ShadowColor { get => _shadowColor; set { if (_shadowColor == value) return; _shadowColor = value; ClearCaches(); RefreshVisuals(); } }

        private float _shadowOpacity = 0.5f;
        [Category("Modern - Effects")]
        [Description("La opacidad de la sombra del control.\nThe opacity of the control's shadow.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public float ShadowOpacity { get => _shadowOpacity; set { if (Math.Abs(_shadowOpacity - value) < 0.01f) return; _shadowOpacity = value; ClearCaches(); RefreshVisuals(); } }

        private float _shadowOffsetX = 0;
        [Category("Modern - Effects")]
        [Description("El desplazamiento horizontal de la sombra del control.\nThe horizontal offset of the control's shadow.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public float ShadowOffsetX { get => _shadowOffsetX; set { if (Math.Abs(_shadowOffsetX - value) < 0.01f) return; _shadowOffsetX = value; ClearCaches(); RefreshVisuals(); } }

        private float _shadowOffsetY = 4;
        [Category("Modern - Effects")]
        [Description("El desplazamiento vertical de la sombra del control.\nThe vertical offset of the control's shadow.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public float ShadowOffsetY { get => _shadowOffsetY; set { if (Math.Abs(_shadowOffsetY - value) < 0.01f) return; _shadowOffsetY = value; ClearCaches(); RefreshVisuals(); } }

        private float _shadowBlur = 8;
        [Category("Modern - Effects")]
        [Description("El desenfoque de la sombra del control.\nThe blur of the control's shadow.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public float ShadowBlur { get => _shadowBlur; set { if (Math.Abs(_shadowBlur - value) < 0.01f) return; _shadowBlur = value; ClearCaches(); RefreshVisuals(); } }

        protected SKImageFilter? _cachedShadowFilter;
        #endregion

        #region 🔵 Efecto: Acrílico (Acrylic)
        private bool _useAcrylic = false;
        [Category("Modern - Acrylic Effects")]
        [Description("Habilita o deshabilita el efecto de acrílico para este control.\nEnable or disable the acrylic effect for this control.")]

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public bool UseAcrylic { get => _useAcrylic; set { if (_useAcrylic == value) return; _useAcrylic = value; ClearCaches(); RefreshVisuals(); } }

        private Color _acrylicTintColor = Color.FromArgb(40, 255, 255, 255);
        [Category("Modern - Acrylic Effects")]
        [Description("El color de tinte del efecto acrílico.\nThe tint color of the acrylic effect.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color AcrylicTintColor { get => _acrylicTintColor; set { if (_acrylicTintColor == value) return; _acrylicTintColor = value; ClearCaches(); RefreshVisuals(); } }

        protected SKImageFilter? _cachedAcrylicFilter;
        #endregion

        // Debounce purge
        private bool _isPurgePending = false;
        private readonly object _purgeLock = new object();

        protected void ClearCaches()
        {
            // Dispose de filtros cacheados (cuando el usuario pide limpiar)
            SafeDispose(ref _cachedShadowFilter);
            SafeDispose(ref _cachedAcrylicFilter);
            ClearImageCache();
            ClearTextCache();

            lock (_purgeLock)
            {
                if (_isPurgePending) return;
                _isPurgePending = true;
            }

            Task.Run(async () =>
            {
                await Task.Delay(50);
                lock (_purgeLock) { _isPurgePending = false; }
                GdiRenderer.ClearCache();
            });
        }

        // Crear o devolver filtro de sombra cacheado
        protected SKImageFilter? GetOrCreateShadowFilter()
        {
            if (!UseShadow || !Enabled || IsFocusedControl) return null;
            if (_cachedShadowFilter != null) return _cachedShadowFilter;

            try
            {
                var drop = SKImageFilter.CreateDropShadow(
                    S(ShadowOffsetX), S(ShadowOffsetY),
                    S(ShadowBlur) / 2f, S(ShadowBlur) / 2f,
                    ShadowColor.ToSKColor());
                _cachedShadowFilter = drop;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[Effects] Shadow filter creation failed: {ex.Message}");
                _cachedShadowFilter = null;
            }

            return _cachedShadowFilter;
        }

        // Crear o devolver filtro de acrílico cacheado (tint + blur)
        protected SKImageFilter? GetOrCreateAcrylicFilter()
        {
            if (!UseAcrylic || !Enabled) return null;
            if (_cachedAcrylicFilter != null) return _cachedAcrylicFilter;

            try
            {
                var blur = SKImageFilter.CreateBlur(S(15), S(15));
                var tint = SKImageFilter.CreateColorFilter(SKColorFilter.CreateBlendMode(AcrylicTintColor.ToSKColor(), SKBlendMode.SrcOver));
                var composed = SKImageFilter.CreateCompose(tint, blur);
                _cachedAcrylicFilter = composed;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[Effects] Acrylic filter creation failed: {ex.Message}");
                _cachedAcrylicFilter = null;
            }

            return _cachedAcrylicFilter;
        }
    }
}
