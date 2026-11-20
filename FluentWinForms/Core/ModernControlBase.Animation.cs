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

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        public Rectangle LogicalBounds => _logicalBounds.IsEmpty ? this.Bounds : _logicalBounds;

        // 🔥 ESCUDO WINFORMS: Intercepta el tamaño real del programador
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            if (DpiTrace.Enabled)
                DpiTrace.Log($"SetBoundsCore {Name} in=({x},{y},{width},{height}) engineExpanding={_isEngineExpanding} cur={Bounds} logical={_logicalBounds} dpi={DeviceDpi} callers={DpiTrace.Callers()}");
            if (!_isEngineExpanding)
            {
                _logicalBounds = new Rectangle(x, y, width, height);
            }
            base.SetBoundsCore(x, y, width, height, specified);
            if (!_logicalBounds.IsEmpty) NotifyGlowOverlay();
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
        private RenderNode? _rippleNode;

        private float _rippleDuration = 600f;
        [Category("Modern -  Animations")]
        [Description("Duración del ripple en milisegundos (80–10000). Más alto = onda más lenta y visible.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public float RippleDuration
        {
            get => SanitizeRippleDuration(_rippleDuration);
            set => _rippleDuration = SanitizeRippleDuration(value);
        }

        private static float SanitizeRippleDuration(float value)
        {
            if (!float.IsFinite(value)) return 600f;
            return Math.Clamp(value, 80f, 10000f);
        }

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
            _isRippling = false;
            _rippleNode = null;
            AnimationManager.Unregister(this);
        }

        private void UpdateRippleBounds() => _maxRippleRadius = (float)Math.Sqrt(Width * Width + Height * Height);
        // =====================================================================
        // 🚀 EXPANSIÓN FÍSICA ZERO-ALLOCATION (CSS-LIKE BEHAVIOR)
        // =====================================================================
        /// <summary>Fuerza el recálculo de la expansión física (para el builder).</summary>
        internal void RefreshPhysicalBounds() => UpdatePhysicalBounds();

        protected virtual void UpdatePhysicalBounds()
        {
            if (_logicalBounds.IsEmpty || _visualNode == null) return;

            // En el camino VisualNode la escala del control NO se dibuja: solo
            // la del nodo. Incluirla aquí inflaba el HWND y tapaba hermanos.
            float maxScaleX = 1f, maxScaleY = 1f;
            float ctrlLeft = 0f, ctrlRight = 0f, ctrlTop = 0f, ctrlBottom = 0f;
            float nodeLeft = 0f, nodeRight = 0f, nodeTop = 0f, nodeBottom = 0f;
            float badgeLeft = 0f, badgeRight = 0f, badgeTop = 0f, badgeBottom = 0f;
            float shLeft = 0f, shRight = 0f, shTop = 0f, shBottom = 0f;

            // Translate del propio control (se aplica primero, en OnPaint) y
            // translate/escala del árbol (se aplica encima): son ADITIVOS, por
            // eso se acumulan por separado y se suman.
            if (TranslateX < 0f) ctrlLeft = Math.Max(ctrlLeft, -TranslateX);
            else if (TranslateX > 0f) ctrlRight = Math.Max(ctrlRight, TranslateX);
            if (TranslateY < 0f) ctrlTop = Math.Max(ctrlTop, -TranslateY);
            else if (TranslateY > 0f) ctrlBottom = Math.Max(ctrlBottom, TranslateY);

            CalculateBaseOverflow(_visualNode, ref maxScaleX, ref maxScaleY,
                ref nodeLeft, ref nodeRight, ref nodeTop, ref nodeBottom);

            // Overflow por hover/press scale + translate — solo durante animación
            if (_isAnimating || _isHoveringInternal || _isMouseDownInternal)
                CalculateMaxOverflow(_visualNode, ref maxScaleX, ref maxScaleY,
                    ref nodeLeft, ref nodeRight, ref nodeTop, ref nodeBottom);

            // Overflow por badge — SIEMPRE y en acumulador propio: el badge se
            // dibuja desde el borde ya trasladado y su alcance es el Offset
            // (no bHalf+Offset). Se suma al translate del nodo.
            CalculateBadgeOverflow(_visualNode,
                ref badgeLeft, ref badgeRight, ref badgeTop, ref badgeBottom);

            // Overflow por sombra/glow — SIEMPRE, se SUMA al contenido. Con el
            // overlay Skia, el halo de la raíz lo dibuja el overlay: no infla
            // el HWND; los nodos hijos siguen con su sombra en el canvas.
            if (UsesSkiaGlowOverlay)
            {
                for (int i = 0; i < _visualNode.Children.Count; i++)
                    CalculateShadowOverflow(_visualNode.Children[i],
                        ref shLeft, ref shRight, ref shTop, ref shBottom);
            }
            else
            {
                CalculateShadowOverflow(_visualNode, ref shLeft, ref shRight, ref shTop, ref shBottom);
            }

            float scaleExW = _logicalBounds.Width * (maxScaleX - 1f) / 2f;
            float scaleExH = _logicalBounds.Height * (maxScaleY - 1f) / 2f;

            float exLeft = scaleExW + ctrlLeft + nodeLeft + badgeLeft + shLeft + 1f;
            float exRight = scaleExW + ctrlRight + nodeRight + badgeRight + shRight + 1f;
            float exTop = scaleExH + ctrlTop + nodeTop + badgeTop + shTop + 1f;
            float exBottom = scaleExH + ctrlBottom + nodeBottom + badgeBottom + shBottom + 1f;

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

            Action update = () => {
                try
                {
                    _isEngineExpanding = true;
                    SetBounds(newBounds.X, newBounds.Y, newBounds.Width, newBounds.Height);
                    // 🔥 FIX FANTASMA: forzar rebuild inmediato antes del próximo WM_PAINT
                    if (Width > 0 && Height > 0) RebuildCanvas();
                }
                finally { _isEngineExpanding = false; }
            };
            try { if (InvokeRequired) BeginInvoke(update); else update(); }
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

        // Escanea Translate/Escala base del árbol (CSS transform estático)
        private void CalculateBaseOverflow(RenderNode node, ref float maxScaleX, ref float maxScaleY,
            ref float maxLeft, ref float maxRight, ref float maxTop, ref float maxBottom)
        {
            maxScaleX = Math.Max(maxScaleX, node.ScaleX);
            maxScaleY = Math.Max(maxScaleY, node.ScaleY);

            if (node.TranslateX < 0f) maxLeft = Math.Max(maxLeft, -node.TranslateX);
            else if (node.TranslateX > 0f) maxRight = Math.Max(maxRight, node.TranslateX);
            if (node.TranslateY < 0f) maxTop = Math.Max(maxTop, -node.TranslateY);
            else if (node.TranslateY > 0f) maxBottom = Math.Max(maxBottom, node.TranslateY);

            for (int i = 0; i < node.Children.Count; i++)
                CalculateBaseOverflow(node.Children[i], ref maxScaleX, ref maxScaleY,
                    ref maxLeft, ref maxRight, ref maxTop, ref maxBottom);
        }

        // Escanea badges SIEMPRE (necesitan espacio incluso en reposo).
        // El badge es un círculo de diámetro Size anclado arriba-derecha:
        // derecho = Right+OffsetX, superior = Top+OffsetY,
        // izquierdo = Right+OffsetX-Size, inferior = Top+OffsetY+Size.
        // El translate del nodo se acumula aparte en nodeLeft/Top.
        private void CalculateBadgeOverflow(RenderNode node,
            ref float maxLeft, ref float maxRight, ref float maxTop, ref float maxBottom)
        {
            if (node.Badge.IsVisible)
            {
                float ox = (float)node.Badge.OffsetX;
                float oy = (float)node.Badge.OffsetY;
                float size = (float)node.Badge.Size;
                float width = node.Layout.Width;
                float height = node.Layout.Height;

                maxRight = Math.Max(maxRight, Math.Max(0f, ox));
                maxTop = Math.Max(maxTop, Math.Max(0f, -oy));
                maxLeft = Math.Max(maxLeft, Math.Max(0f, size - width - ox));
                maxBottom = Math.Max(maxBottom, Math.Max(0f, size + oy - height));
            }
            for (int i = 0; i < node.Children.Count; i++)
                CalculateBadgeOverflow(node.Children[i],
                    ref maxLeft, ref maxRight, ref maxTop, ref maxBottom);
        }
        // Escanea la sombra/glow de cada nodo (Normal + Hover + Press + Disabled) SIEMPRE — no depende de animación
        private void CalculateShadowOverflow(RenderNode node, ref float maxLeft, ref float maxRight, ref float maxTop, ref float maxBottom)
        {
            AccumulateShadowReach(node.Shadow, ref maxLeft, ref maxRight, ref maxTop, ref maxBottom);
            if (node.HoverState.Shadow.HasValue) AccumulateShadowReach(node.HoverState.Shadow.Value, ref maxLeft, ref maxRight, ref maxTop, ref maxBottom);
            if (node.PressState.Shadow.HasValue) AccumulateShadowReach(node.PressState.Shadow.Value, ref maxLeft, ref maxRight, ref maxTop, ref maxBottom);
            if (node.DisabledState.Shadow.HasValue) AccumulateShadowReach(node.DisabledState.Shadow.Value, ref maxLeft, ref maxRight, ref maxTop, ref maxBottom);

            for (int i = 0; i < node.Children.Count; i++)
                CalculateShadowOverflow(node.Children[i], ref maxLeft, ref maxRight, ref maxTop, ref maxBottom);
        }

        private void AccumulateShadowReach(ShadowData sh, ref float maxLeft, ref float maxRight, ref float maxTop, ref float maxBottom)
        {
            if (sh.Radius <= 0 || sh.Color.A <= 0) return;

            // Revertido a la fórmula original (radio, no 2x): expandir a 2x
            // evitaba el recorte del bake pero solapaba los HWND de hermanos
            // cercanos (cada hijo pinta el fondo del padre en todo su rect y el
            // de arriba borra al de abajo). El recorte del glow en el borde es
            // el comportamiento previo; la solución completa exige recalcular
            // todos los hermanos en OnLayout y separar los acumuladores.
            float radius = S(sh.Radius);
            float ox = S(sh.OffsetX);
            float oy = S(sh.OffsetY);

            maxLeft = Math.Max(maxLeft, radius + Math.Max(0f, -ox));
            maxRight = Math.Max(maxRight, radius + Math.Max(0f, ox));
            maxTop = Math.Max(maxTop, radius + Math.Max(0f, -oy));
            maxBottom = Math.Max(maxBottom, radius + Math.Max(0f, oy));
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
            float rawDt = dt; // ripple en ms reales; hover/press usa el clamp
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

            // 5. Easing de Ripple (duración configurable en ms)
            if (_isRippling)
            {
                float duration = SanitizeRippleDuration(_rippleDuration);
                float startRadius = S(10f);
                float maxRadius = _maxRippleRadius;
                if (maxRadius <= startRadius)
                    maxRadius = (float)Math.Sqrt(Width * Width + Height * Height);
                maxRadius = Math.Max(maxRadius, startRadius);

                // Cap del dt real: un frame largo no debe completar el ripple
                // de golpe (destello). A 15-60 fps el desvío es mínimo.
                float rippleDt = Math.Min(rawDt, 50f);
                float rate = (maxRadius - startRadius) / duration;
                _rippleRadius = Math.Min(maxRadius, _rippleRadius + rippleDt * rate);

                // Fade SIEMPRE (también con el mouse apretado): si se mantiene
                // el clic, el círculo no deja el botón teñido para siempre.
                float fadeTime = _isMouseDownInternal ? duration * 1.4f : duration * 0.6f;
                _rippleOpacity = Math.Max(0f, _rippleOpacity - rippleDt / fadeTime);

                if (_rippleOpacity <= 0) { _isRippling = false; _rippleNode = null; }
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

            // La recolección periódica vive en AnimationManager (una por frame
            // global). Antes se ejecutaba aquí y N controles recolectaban en el
            // mismo frame.

            // 8. Gestión de Renderizado
            if (isMoving)
            {
                NotifyGlowOverlay();
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
        // 🔥 Convierte coords físicas del HWND (ya expandido) a coords lógicas del árbol visual
        protected PointF ToLocalPoint(Point p) => new PointF(p.X - EngineOffset.X - TranslateX, p.Y - EngineOffset.Y - TranslateY);


        // 🔥 FIX 1: BOUNDING BOX CULLING (Optimización extrema de CPU == Menos consumo RAM)
        private RenderNode? HitTest(RenderNode node, PointF pt)
        {
            if (!node.IsVisible) return null;

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
                var localPt = new PointF(e.Location.X - EngineOffset.X - this.TranslateX, e.Location.Y - EngineOffset.Y - this.TranslateY);
                var hit = HitTest(_visualNode, localPt);
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

                var localPt = ToLocalPoint(e.Location);

                if (_visualNode != null)
                {
                    var hit = HitTest(_visualNode, localPt);
                    if (hit != null)
                    {
                        _currentPressedNode = hit;
                        _currentPressedNode.IsPressed = true;
                    }
                }

                if (UseRipple && !_isRippling)
                {
                    _rippleCenter = localPt;
                    _rippleRadius = S(10f);
                    _rippleOpacity = 1f;
                    _isRippling = true;
                    _rippleNode = _currentPressedNode ?? _visualNode;
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
                    if (_currentPressedNode.Layout.Contains(ToLocalPoint(e.Location)))
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