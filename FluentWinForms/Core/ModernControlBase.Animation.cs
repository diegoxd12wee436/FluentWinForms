#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;


namespace FluentWinForms.Core
{
    public abstract partial class ModernControlBase
    {
        // Configuraciones de Animación
        [Category("Modern -  Animations")]
        [Description("Tipo de curva de animación que usará el control principal.\nAnimation curve type used by the main control.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public AnimationEasing AnimationType { get; set; } = AnimationEasing.EaseInOut;
        [Category("Modern -  Animations")]
        [Description("Velocidad de las animaciones (en milisegundos para completar la transición) \nSpeed of animations (in milliseconds to complete the transition)")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public float AnimationSpeed { get; set; } = 150f;

        [Category("Modern -  Animations")]
        [Description("Habilita o deshabilita el efecto de hover \nEnable or disable the hover effect")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public bool EnableHover { get; set; } = true;
        [Category("Modern -  Animations")]
        [Description("Habilita o deshabilita el efecto de presión \nEnable or disable the press effect")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public bool EnablePressEffect { get; set; } = true;
        [Category("Modern -  Animations")]
        [Description("Habilita o deshabilita el efecto de ripple (onda expansiva) \nEnable or disable the ripple effect")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]

        public bool UseRipple { get; set; } = false;
        [Category("Modern -  Animations")]
        [Description("Color del efecto ripple (onda expansiva) \nColor of the ripple effect")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color RippleColor { get; set; } = Color.FromArgb(60, 0, 0, 0);

        // Variables de Estado Animado (Zero-Allocation)
        protected float _hoverProgress = 0f;
        protected float _pressProgress = 0f;
        protected float _animatedScale = 1.0f;
        protected float _animatedRotation = 0f;

        private float _rippleRadius = 0f;
        private float _rippleOpacity = 0f;
        private PointF _rippleCenter;
        private bool _isRippling = false;
        private float _maxRippleRadius = 0f;

        protected bool _isHoveringInternal = false;
        protected bool _isMouseDownInternal = false;
        private bool _isAnimating = false;

        // 🔥 HIT-TESTING: Nodos detectados por el mouse
        private RenderNode? _currentHoveredNode;
        private RenderNode? _currentPressedNode;

        private void InitAnimations()
        {
            // El motor central se encarga. No creamos Timers aquí.
        }

        private void DisposeAnimations()
        {
            _isAnimating = false;
            AnimationManager.Unregister(this);
        }

        private void UpdateRippleBounds() => _maxRippleRadius = (float)Math.Sqrt(Width * Width + Height * Height);

        private void StartAnimation()
        {
            if (!_isAnimating && !DesignMode)
            {
                _isAnimating = true;
                AnimationManager.Register(this);
            }
        }

        // 🔥 FIX DEBILIDAD 1: Motor recursivo que avanza la animación de CADA nodo
        private bool UpdateNodeAnimations(RenderNode node, float step)
        {
            bool moving = false;

            if (node.IsHovered)
            {
                if (node.HoverProgress < 1f) { node.HoverProgress = Math.Min(1f, node.HoverProgress + step); moving = true; }
            }
            else
            {
                if (node.HoverProgress > 0f) { node.HoverProgress = Math.Max(0f, node.HoverProgress - step); moving = true; }
            }

            if (node.IsPressed)
            {
                if (node.PressProgress < 1f) { node.PressProgress = Math.Min(1f, node.PressProgress + step); moving = true; }
            }
            else
            {
                if (node.PressProgress > 0f) { node.PressProgress = Math.Max(0f, node.PressProgress - step); moving = true; }
            }

            foreach (var child in node.Children)
            {
                if (UpdateNodeAnimations(child, step)) moving = true;
            }

            return moving;
        }

        // EL CORAZÓN DE LA ANIMACIÓN (Invocado por AnimationManager)
        public virtual void AnimationTick(float dt)
        {
            // 1. Validación de Ciclo de Vida
            if (!Enabled || IsDisposed || DesignMode || !IsHandleCreated)
            {
                _isAnimating = false;
                AnimationManager.Unregister(this);
                return;
            }

            // 2. Normalización de tiempo
            dt = Math.Min(dt, 32f);
            bool isMoving = false;
            float step = dt / AnimationSpeed;

            // 3. Easing de Hover (Para el control padre legacy)
            if (_isHoveringInternal && EnableHover)
            {
                if (_hoverProgress < 1f) { _hoverProgress = Math.Min(1f, _hoverProgress + step); isMoving = true; }
            }
            else
            {
                if (_hoverProgress > 0f) { _hoverProgress = Math.Max(0f, _hoverProgress - step); isMoving = true; }
            }

            // 4. Easing de Press (Para el control padre legacy)
            if (_isMouseDownInternal && EnablePressEffect)
            {
                if (_pressProgress < 1f) { _pressProgress = Math.Min(1f, _pressProgress + step); isMoving = true; }
            }
            else
            {
                if (_pressProgress > 0f) { _pressProgress = Math.Max(0f, _pressProgress - step); isMoving = true; }
            }

            // 5. Easing de Ripple
            if (_isRippling)
            {
                _rippleRadius += (dt * S(1.2f));
                if (!_isMouseDownInternal) _rippleOpacity -= (step * 1.5f);

                if (_rippleOpacity <= 0) _isRippling = false;
                isMoving = true;
            }

            // 6. Easing de Escala (Legacy)
            float targetScale = _isMouseDownInternal ? 0.95f : 1.0f;
            if (Math.Abs(_animatedScale - targetScale) > 0.001f)
            {
                _animatedScale += (targetScale - _animatedScale) * (dt / 60f);
                isMoving = true;
            }

            // 🔥 FIX DEBILIDAD 1: Procesar las animaciones internas del Árbol Visual
            if (_visualNode != null)
            {
                if (UpdateNodeAnimations(_visualNode, step)) isMoving = true;
            }

            // 7. Gancho para animaciones personalizadas
            try
            {
                if (CustomAnimationLoop(dt, step)) isMoving = true;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[ModernControl API] Error en CustomAnimationLoop: {ex.Message}");
            }

#if !NETFRAMEWORK
            // 🔥 INYECCIÓN GC PRO: Recolector de basura periódico en .NET 8 para evitar acumulación silenciosa a 120 FPS
            if (AnimationManager.Frames > 0 && (AnimationManager.Frames % 600) == 0)
            {
                GC.Collect();
            }
#endif

            // 8. Gestión de Renderizado
            if (isMoving)
            {
                this.Invalidate();
            }
            else
            {
                _isAnimating = false;
                AnimationManager.Unregister(this);
            }
        }

        protected virtual bool CustomAnimationLoop(float dt, float step) => false;


        // 🔥 FIX 1: BOUNDING BOX CULLING (Optimización extrema de CPU == Menos consumo RAM)
        private RenderNode? HitTest(RenderNode node, PointF pt)
        {
            if (!node.IsVisible || !node.Enabled) return null;

            // 🛡️ ESCUDO: Si el ratón no está dentro del rectángulo de este nodo, 
            // ignoramos automáticamente a este nodo y a todos sus cientos de hijos.
            if (!node.Layout.Contains(pt)) return null;

            // Si el mouse SÍ está adentro, revisamos a los hijos de arriba hacia abajo (Z-Index)
            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                var hit = HitTest(node.Children[i], pt);
                if (hit != null) return hit; // Si tocamos un hijo, devolvemos el hijo
            }

            // Si ningún hijo fue tocado, significa que tocamos el fondo de este nodo padre y adios papu XD
            return node;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!Enabled) return;

            if (_visualNode != null)
            {
                var hit = HitTest(_visualNode, e.Location);
                if (hit != _currentHoveredNode)
                {
                    if (_currentHoveredNode != null)
                    {
                        _currentHoveredNode.IsHovered = false;
                        // 🔥 INYECCIÓN: Avisamos a la API Fluent que el mouse SALIÓ de este nodo
                        _currentHoveredNode.OnHoverLeaveAction?.Invoke(_currentHoveredNode);
                    }

                    _currentHoveredNode = hit;

                    if (_currentHoveredNode != null)
                    {
                        _currentHoveredNode.IsHovered = true;
                        // Mantenemos el viejo por si acaso
                        _currentHoveredNode.OnHoverAction?.Invoke(_currentHoveredNode);
                        // 🔥 INYECCIÓN: Avisamos a la API Fluent que el mouse ENTRÓ a este nodo
                        _currentHoveredNode.OnHoverEnterAction?.Invoke(_currentHoveredNode);
                    }
                    StartAnimation(); // Activa el motor para que anime el progreso
                }
            }

            if (!_isHoveringInternal)
            {
                _isHoveringInternal = true;
                StartAnimation();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (Enabled)
            {
                if (_currentHoveredNode != null)
                {
                    _currentHoveredNode.IsHovered = false;
                    // 🔥 INYECCIÓN: Si el mouse sale del control entero, forzamos el Leave del nodo
                    _currentHoveredNode.OnHoverLeaveAction?.Invoke(_currentHoveredNode);
                    _currentHoveredNode = null;
                }
                _isHoveringInternal = false;
                _isMouseDownInternal = false;
                StartAnimation();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            if (Enabled) { _isHoveringInternal = true; StartAnimation(); }
            base.OnMouseEnter(e);
        }


        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Enabled)
            {
                Focus();
                _isMouseDownInternal = true;

                if (_visualNode != null)
                {
                    var hit = HitTest(_visualNode, e.Location);
                    if (hit != null)
                    {
                        _currentPressedNode = hit;
                        _currentPressedNode.IsPressed = true;
                    }
                }

                if (UseRipple)
                {
                    _rippleCenter = e.Location;
                    _rippleRadius = S(10f);
                    _rippleOpacity = 1f;
                    _isRippling = true;
                }
                StartAnimation();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (Enabled)
            {
                _isMouseDownInternal = false;

                if (_currentPressedNode != null)
                {
                    _currentPressedNode.IsPressed = false;
                    if (_currentPressedNode.Layout.Contains(e.Location))
                    {
                        _currentPressedNode.OnClickAction?.Invoke(_currentPressedNode);
                    }
                    _currentPressedNode = null;
                }

                StartAnimation();
            }
            base.OnMouseUp(e);
        }

        public static class Easing
        {
            // 🔥 El Selector Maestro
            public static float Calculate(AnimationEasing type, float t)
            {
                switch (type)
                {
                    case AnimationEasing.Linear: return Linear(t);
                    case AnimationEasing.EaseInOut: return EaseInOutQuad(t);
                    case AnimationEasing.EaseOutBack: return EaseOutBack(t);
                    case AnimationEasing.Spring: return Spring(t);
                    default: return EaseInOutQuad(t);
                }
            }

            public static float Linear(float t) => t;

            public static float EaseInOutQuad(float t) => t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;

            public static float EaseOutBack(float t)
            {
                float c1 = 1.70158f; float c3 = c1 + 1f;
                return 1f + c3 * (float)Math.Pow(t - 1, 3) + c1 * (float)Math.Pow(t - 1, 2);
            }

            // 🔥 LA FÍSICA SPRING (Masa, Tensión y Fricción convertidas a curva de tiempo)
            public static float Spring(float t)
            {
                if (t == 0f || t == 1f) return t;
                // Fórmula de amortiguación 
                float c4 = (2f * (float)Math.PI) / 3f;
                return (float)(Math.Pow(2, -3 * t) * Math.Sin((t * 3f - 0.75f) * c4) + 1f);
            }
        }
    }
}