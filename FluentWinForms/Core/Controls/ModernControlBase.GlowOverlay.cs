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
            ModernGlowOverlay.IsAttached(Parent);

        private Control? _overlayAttachParent;
        private EventHandler? _overlayAttachHandler;

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            // Si el padre anterior aún no tenía handle, no dejar el hook.
            UnhookOverlayAttach();
            if (ModernGlowOverlay.Enabled && Parent != null)
            {
                Control parent = Parent;
                if (parent.IsHandleCreated)
                {
                    // Fallback por si el handle se recrea antes del callback.
                    HookOverlayAttach(parent);
                    // El padre ya existe: adjuntar después del evento actual.
                    try { parent.BeginInvoke(new Action(() => AttachDeferred(parent))); }
                    catch { AttachDeferred(parent); }
                }
                else
                {
                    HookOverlayAttach(parent);
                }
            }
            // Al cambiar de padre hay que recalcular el margen: si el nuevo
            // padre no tiene overlay, el control debe volver a dibujar la
            // sombra completa en su HWND (y viceversa).
            try
            {
                if (Parent != null && !Parent.IsDisposed &&
                    Parent.ClientSize.Width > 0 && Parent.ClientSize.Height > 0)
                    RefreshPhysicalBounds();
            }
            catch { }
            NotifyGlowOverlay();
        }

        private void HookOverlayAttach(Control parent)
        {
            UnhookOverlayAttach();
            _overlayAttachParent = parent;
            EventHandler? handler = null;
            handler = (s, args) =>
            {
                if (ReferenceEquals(_overlayAttachHandler, handler))
                {
                    parent.HandleCreated -= handler;
                    _overlayAttachParent = null;
                    _overlayAttachHandler = null;
                }
                AttachDeferred(parent);
            };
            _overlayAttachHandler = handler;
            parent.HandleCreated += handler;
        }

        private void UnhookOverlayAttach()
        {
            if (_overlayAttachParent != null && _overlayAttachHandler != null)
            {
                try { _overlayAttachParent.HandleCreated -= _overlayAttachHandler; }
                catch { }
            }
            _overlayAttachParent = null;
            _overlayAttachHandler = null;
        }

        private void AttachDeferred(Control parent)
        {
            if (!ModernGlowOverlay.Enabled || parent.IsDisposed || IsDisposed)
            {
                UnhookOverlayAttach();
                return;
            }
            if (!ReferenceEquals(Parent, parent)) return;
            if (!parent.IsHandleCreated)
            {
                // El handle se destruyó entre el BeginInvoke y el callback.
                HookOverlayAttach(parent);
                return;
            }
            ModernGlowOverlay.Attach(parent);
            if (ModernGlowOverlay.IsAttached(parent)) UnhookOverlayAttach();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            NotifyGlowOverlay();
        }

        internal void NotifyGlowOverlay()
        {
            if (Parent != null)
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
