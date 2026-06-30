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
        // =====================================================================
        // 🛡️ SISTEMA DE DOBLE REALIDAD (LOGICAL vs PHYSICAL BOUNDS)
        // =====================================================================
        protected Rectangle _logicalBounds = Rectangle.Empty;
        protected bool _isEngineExpanding = false;
        private Rectangle _pendingBounds;
        private Action? _cachedBoundsUpdate;

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        public Rectangle LogicalBounds => _logicalBounds.IsEmpty ? this.Bounds : _logicalBounds;

        // 🔥 ESCUDO WINFORMS: Intercepta el tamaño real del programador
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            if (!_isEngineExpanding)
            {
                _logicalBounds = new Rectangle(x, y, width, height);
            }
            else
            {
                // 🛡️ HWND expandido: pasar los bounds físicos a WinForms
                // pero NO dejar que WinForms luego restaure _logicalBounds con el tamaño inflado
                base.SetBoundsCore(x, y, width, height, specified);
                return; // Salir sin el segundo base.SetBoundsCore
            }
            base.SetBoundsCore(x, y, width, height, specified);
        }

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
        // =====================================================================
        // 🚀 EXPANSIÓN FÍSICA ZERO-ALLOCATION (CSS-LIKE BEHAVIOR)
        // =====================================================================
        protected virtual void UpdatePhysicalBounds()
        {
            if (_logicalBounds.IsEmpty || _visualNode == null) return;

            float maxScaleX = 1f, maxScaleY = 1f;
            float maxLeft = 0f, maxRight = 0f, maxTop = 0f, maxBottom = 0f;

            // Overflow por hover/press scale + translate — solo durante animación
            if (_isAnimating || _isHoveringInternal || _isMouseDownInternal)
                CalculateMaxOverflow(_visualNode, ref maxScaleX, ref maxScaleY,
                    ref maxLeft, ref maxRight, ref maxTop, ref maxBottom);

            // Overflow por badge — SIEMPRE (visible en reposo también)
            CalculateBadgeOverflow(_visualNode, ref maxRight, ref maxTop);

            float scaleExW = _logicalBounds.Width * (maxScaleX - 1f) / 2f;
            float scaleExH = _logicalBounds.Height * (maxScaleY - 1f) / 2f;

            float exLeft = scaleExW + maxLeft + 1f;
            float exRight = scaleExW + maxRight + 1f;
            float exTop = scaleExH + maxTop + 1f;
            float exBottom = scaleExH + maxBottom + 1f;

            Rectangle newBounds = new Rectangle(
                (int)Math.Floor(_logicalBounds.X - exLeft),
                (int)Math.Floor(_logicalBounds.Y - exTop),
                (int)Math.Ceiling(_logicalBounds.Width + exLeft + exRight),
                (int)Math.Ceiling(_logicalBounds.Height + exTop + exBottom)
            );

            // Clamp al parent — EngineOffset compensa automáticamente via logicalBounds.X - this.Left
            var p = this.Parent;
            if (p != null)
            {
                newBounds = Rectangle.FromLTRB(
                    Math.Max(0, newBounds.Left),
                    Math.Max(0, newBounds.Top),
                    Math.Min(p.ClientSize.Width, newBounds.Right),
                    Math.Min(p.ClientSize.Height, newBounds.Bottom));
            }

            if (this.Bounds == newBounds) return;
            if (!IsHandleCreated || IsDisposed || DesignMode) return;

            _pendingBounds = newBounds;
            _cachedBoundsUpdate ??= () =>
            {
                try
                {
                    _isEngineExpanding = true;
                    SetBounds(_pendingBounds.X, _pendingBounds.Y, _pendingBounds.Width, _pendingBounds.Height);
                    if (Width > 0 && Height > 0) RebuildCanvas();
                }
                finally { _isEngineExpanding = false; }
            };
            try { if (InvokeRequired) BeginInvoke(_cachedBoundsUpdate); else _cachedBoundsUpdate(); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }


        // Búsqueda en Stack (Cero Garbage Collector)
        // Escanea Scale + Translate de hover/press — solo durante animación
        private void CalculateMaxOverflow(RenderNode node, ref float maxScaleX, ref float maxScaleY,
            ref float maxLeft, ref float maxRight, ref float maxTop, ref float maxBottom)
        {
            if (node.HoverState.Scale.HasValue) { maxScaleX = Math.Max(maxScaleX, node.HoverState.Scale.Value); maxScaleY = Math.Max(maxScaleY, node.HoverState.Scale.Value); }
            if (node.PressState.Scale.HasValue) { maxScaleX = Math.Max(maxScaleX, node.PressState.Scale.Value); maxScaleY = Math.Max(maxScaleY, node.PressState.Scale.Value); }

            if (node.HoverState.TranslateX.HasValue) { float tx = node.HoverState.TranslateX.Value; if (tx < 0) maxLeft = Math.Max(maxLeft, -tx); else maxRight = Math.Max(maxRight, tx); }
            if (node.HoverState.TranslateY.HasValue) { float ty = node.HoverState.TranslateY.Value; if (ty < 0) maxTop = Math.Max(maxTop, -ty); else maxBottom = Math.Max(maxBottom, ty); }
            if (node.PressState.TranslateX.HasValue) { float tx = node.PressState.TranslateX.Value; if (tx < 0) maxLeft = Math.Max(maxLeft, -tx); else maxRight = Math.Max(maxRight, tx); }
            if (node.PressState.TranslateY.HasValue) { float ty = node.PressState.TranslateY.Value; if (ty < 0) maxTop = Math.Max(maxTop, -ty); else maxBottom = Math.Max(maxBottom, ty); }

            for (int i = 0; i < node.Children.Count; i++)
                CalculateMaxOverflow(node.Children[i], ref maxScaleX, ref maxScaleY,
                    ref maxLeft, ref maxRight, ref maxTop, ref maxBottom);
        }

        // Escanea badges SIEMPRE (necesitan espacio incluso en reposo)
        private void CalculateBadgeOverflow(RenderNode node, ref float maxRight, ref float maxTop)
        {
            if (node.Badge.IsVisible)
            {
                float bHalf = (float)(node.Badge.Size * 0.5);
                maxRight = Math.Max(maxRight, bHalf + Math.Max(0f, (float)node.Badge.OffsetX));
                maxTop = Math.Max(maxTop, bHalf + Math.Max(0f, -(float)node.Badge.OffsetY));
            }
            for (int i = 0; i < node.Children.Count; i++)
                CalculateBadgeOverflow(node.Children[i], ref maxRight, ref maxTop);
        }

        private void StartAnimation()
        {
            if (!_isAnimating && !DesignMode)
            {
                _isAnimating = true;
                UpdatePhysicalBounds();
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

            // 🔥 (Adiós enumerador, hola optimización)
            for (int i = 0; i < node.Children.Count; i++)
            {
                if (UpdateNodeAnimations(node.Children[i], step)) moving = true;
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
                UpdatePhysicalBounds();
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
                UpdatePhysicalBounds();
                AnimationManager.Unregister(this);
            }
        }

        protected virtual bool CustomAnimationLoop(float dt, float step) => false;


        // 🔥 Convierte coords HWND → espacio de layout Skia (descuenta EngineOffset + TranslateX/Y del control)
        private PointF ToLayoutPoint(Point hwndPt)
        {
            var eo = EngineOffset;
            // TranslateX/Y sólo se aplica al canvas cuando _logicalBounds no es vacío
            float tx = _logicalBounds.IsEmpty ? 0f : this.TranslateX;
            float ty = _logicalBounds.IsEmpty ? 0f : this.TranslateY;
            return new PointF(hwndPt.X - eo.X - tx, hwndPt.Y - eo.Y - ty);
        }

        // 🔥 HIT-TEST TRANSFORM-AWARE: inverse-transforma el punto por el translate+scale
        // animado de cada nodo (espeja exactamente lo que hace RenderNodeTree en el canvas).
        // Los layouts son absolutos → al deshacer la transformación del padre, el punto queda
        // en el mismo espacio que usan los layouts de sus hijos.
        private RenderNode? HitTest(RenderNode node, PointF pt)
        {
            if (!node.IsVisible) return null;

            // ── Recalcula los valores animados actuales (igual que RenderNodeTree) ──
            float transX = node.TranslateX;
            float transY = node.TranslateY;
            float scaleX = node.ScaleX;
            float scaleY = node.ScaleY;

            if (node.HoverProgress > 0f)
            {
                float eH = Easing.Calculate(node.Easing, node.HoverProgress);
                if (node.HoverState.TranslateX.HasValue)
                    transX += (node.HoverState.TranslateX.Value - transX) * eH;
                if (node.HoverState.TranslateY.HasValue)
                    transY += (node.HoverState.TranslateY.Value - transY) * eH;
                if (node.HoverState.Scale.HasValue)
                {
                    scaleX += (node.HoverState.Scale.Value - scaleX) * eH;
                    scaleY += (node.HoverState.Scale.Value - scaleY) * eH;
                }
            }

            if (node.PressProgress > 0f)
            {
                float eP = Easing.Calculate(node.Easing, node.PressProgress);
                if (node.PressState.TranslateX.HasValue)
                    transX += (node.PressState.TranslateX.Value - transX) * eP;
                if (node.PressState.TranslateY.HasValue)
                    transY += (node.PressState.TranslateY.Value - transY) * eP;
                if (node.PressState.Scale.HasValue)
                {
                    scaleX += (node.PressState.Scale.Value - scaleX) * eP;
                    scaleY += (node.PressState.Scale.Value - scaleY) * eP;
                }
            }

            // ── Inverse-transform: deshace translate y luego scale alrededor del TransformOrigin ──
            float ix = pt.X - transX;
            float iy = pt.Y - transY;

            if (scaleX != 1f || scaleY != 1f)
            {
                float cx = node.Layout.Left + node.Layout.Width * node.TransformOrigin.X;
                float cy = node.Layout.Top + node.Layout.Height * node.TransformOrigin.Y;
                if (scaleX != 0f) ix = (ix - cx) / scaleX + cx;
                if (scaleY != 0f) iy = (iy - cy) / scaleY + cy;
            }

            var localPt = new PointF(ix, iy);

            // 🛡️ ESCUDO: bounding-box cull en coordenadas locales (pre-transform)
            if (!node.Layout.Contains(localPt)) return null;

            // Revisamos hijos de arriba hacia abajo (Z-Index), pasando el punto LOCAL
            // (los layouts hijos son absolutos en el mismo espacio pre-transform del padre)
            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                var hit = HitTest(node.Children[i], localPt);
                if (hit != null) return hit;
            }

            return node;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!Enabled) return;

            if (_visualNode != null)
            {
                var pt = ToLayoutPoint(e.Location);
                var hit = HitTest(_visualNode, pt);
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
                    var pt = ToLayoutPoint(e.Location);
                    var hit = HitTest(_visualNode, pt);
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
                    // 🔥 FIX: convertir a layout-space y usar HitTest para respetar transforms
                    var upPt = ToLayoutPoint(e.Location);
                    if (HitTest(_currentPressedNode, upPt) != null)
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