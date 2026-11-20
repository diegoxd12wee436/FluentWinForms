#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;
using SkiaSharp;

namespace FluentWinForms.Core
{
    public abstract partial class ModernControlBase
    {
        /// <summary>El overlay Skia está activo para este control/padre.</summary>
        internal bool UsesSkiaGlowOverlay =>
            ModernGlowOverlay.Enabled && ModernGlowOverlay.IsAttached(Parent);

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (ModernGlowOverlay.Enabled && Parent != null)
                ModernGlowOverlay.Attach(Parent);
            NotifyGlowOverlay();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            NotifyGlowOverlay();
        }

        internal void NotifyGlowOverlay()
        {
            if (ModernGlowOverlay.Enabled && Parent != null)
                ModernGlowOverlay.Request(Parent);
        }

        /// <summary>
        /// Entrega el bake del nodo raíz y su transformación en coordenadas del
        /// padre para que el overlay lo dibuje con Skia. No dibuja nada.
        /// </summary>
        internal bool TryGetRootGlow(out SKImage? image, out float x, out float y,
            out float scaleX, out float scaleY, out float centerX, out float centerY)
        {
            image = null;
            x = y = centerX = centerY = 0f;
            scaleX = scaleY = 1f;

            RenderNode? node = _visualNode;
            if (node == null || node._cachedShadowImage == null) return false;
            if (node.Shadow.Radius <= 0f) return false;
            if (GetGlowStepColor(node).A <= 0) return false;

            float tx = node.TranslateX;
            float ty = node.TranslateY;
            scaleX = node.ScaleX;
            scaleY = node.ScaleY;

            if (node.HoverProgress > 0)
            {
                float e = Easing.Calculate(node.Easing, node.HoverProgress);
                if (node.HoverState.TranslateX.HasValue) tx += (node.HoverState.TranslateX.Value - tx) * e;
                if (node.HoverState.TranslateY.HasValue) ty += (node.HoverState.TranslateY.Value - ty) * e;
                if (node.HoverState.Scale.HasValue)
                {
                    scaleX += (node.HoverState.Scale.Value - scaleX) * e;
                    scaleY += (node.HoverState.Scale.Value - scaleY) * e;
                }
            }

            if (node.PressProgress > 0)
            {
                float e = Easing.Calculate(node.Easing, node.PressProgress);
                if (node.PressState.TranslateX.HasValue) tx += (node.PressState.TranslateX.Value - tx) * e;
                if (node.PressState.TranslateY.HasValue) ty += (node.PressState.TranslateY.Value - ty) * e;
                if (node.PressState.Scale.HasValue)
                {
                    scaleX += (node.PressState.Scale.Value - scaleX) * e;
                    scaleY += (node.PressState.Scale.Value - scaleY) * e;
                }
            }

            if (!node.Enabled && node.DisabledState.Scale.HasValue)
            {
                scaleX = node.DisabledState.Scale.Value;
                scaleY = node.DisabledState.Scale.Value;
            }

            float baseX = _logicalBounds.X + TranslateX + tx;
            float baseY = _logicalBounds.Y + TranslateY + ty;

            x = baseX + node.Layout.Left - node._lastShadowImagePadLeft;
            y = baseY + node.Layout.Top - node._lastShadowImagePadTop;
            centerX = baseX + node.Layout.Left + node.Layout.Width * node.TransformOrigin.X;
            centerY = baseY + node.Layout.Top + node.Layout.Height * node.TransformOrigin.Y;
            image = node._cachedShadowImage;
            return true;
        }

        /// <summary>Color efectivo del step actual (misma regla que el bake).</summary>
        internal static Color GetGlowStepColor(RenderNode node)
        {
            Color color = node.Shadow.Color;
            if (node.HoverState.Shadow.HasValue && node.HoverProgress >= 0.5f)
                color = node.HoverState.Shadow.Value.Color;
            if (node.PressState.Shadow.HasValue && node.PressProgress >= 0.5f)
                color = node.PressState.Shadow.Value.Color;
            if (!node.Enabled && node.DisabledState.Shadow.HasValue)
                color = node.DisabledState.Shadow.Value.Color;
            return color;
        }
    }
}
