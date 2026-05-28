#nullable enable
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace FluentWinForms.Core
{
    /// <summary>
    /// Fluent API de FluentWinForms — construye controles con código legible.
    /// Compatible: .NET 4.8 → .NET 10 | Windows 7 → Windows 11
    /// Zero extra allocations en hot path.
    /// </summary>
    public sealed class ControlBuilder
    {
        private readonly RenderNode _node;
        private readonly Control? _hostControl;
        internal Action<RenderNode>? OnApplied { get; set; }

        // ── Constructores ──────────────────────────────────────────────
        public ControlBuilder(RenderNode node, Control? host = null)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));
            _hostControl = host;
        }

        public ControlBuilder(string id = "")
        {
            _node = new RenderNode
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id
            };
        }

        // ══════════════════════════════════════════════════════════════
        // 1. LAYOUT — Posición, tamaño y estiramiento
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder Layout(int x, int y, int width, int height)
            => Layout((float)x, (float)y, (float)width, (float)height);
        public ControlBuilder Layout(float x, float y, float width, float height)
        {
            _node.Layout = new RectangleF(x, y, Math.Max(0, width), Math.Max(0, height));
            _node.StretchX = false;
            _node.StretchY = false;
            return this;
        }

        public ControlBuilder Width(int w) => Width((float)w);
        public ControlBuilder Width(float w)
        { _node.Layout = new RectangleF(_node.Layout.X, _node.Layout.Y, Math.Max(0, w), _node.Layout.Height); _node.StretchX = false; return this; }

        public ControlBuilder Height(int h) => Height((float)h);
        public ControlBuilder Height(float h)
        { _node.Layout = new RectangleF(_node.Layout.X, _node.Layout.Y, _node.Layout.Width, Math.Max(0, h)); _node.StretchY = false; return this; }

        /// <summary>Define ancho y alto simultáneamente.</summary>
        public ControlBuilder Size(int width, int height) => Size((float)width, (float)height);
        public ControlBuilder Size(float width, float height)
        { Width(width); Height(height); return this; }

        public ControlBuilder StretchWidth() { _node.StretchX = true; return this; }
        public ControlBuilder StretchHeight() { _node.StretchY = true; return this; }

        /// <summary>Hace que el control ocupe todo el ancho y alto disponible.</summary>
        public ControlBuilder Fill()
        { StretchWidth(); StretchHeight(); return this; }

        // ── Contenedores de layout ─────────────────────────────────────
        public ControlBuilder VStack(int spacing = 0) => VStack((float)spacing);
        public ControlBuilder VStack(float spacing = 0)
        { _node.LayoutMode = LayoutStyle.VerticalStack; _node.Spacing = spacing; return this; }

        public ControlBuilder HStack(int spacing = 0) => HStack((float)spacing);
        public ControlBuilder HStack(float spacing = 0)
        { _node.LayoutMode = LayoutStyle.HorizontalStack; _node.Spacing = spacing; return this; }

        public ControlBuilder Grid(int minColWidth, int gap = 0) => Grid((float)minColWidth, (float)gap);
        public ControlBuilder Grid(float minColWidth, float gap = 0)
        { _node.LayoutMode = LayoutStyle.AutoFitGrid; _node.GridMinColumnWidth = Math.Max(1, minColWidth); _node.Spacing = gap; return this; }

        // ── Hijos ──────────────────────────────────────────────────────
        public ControlBuilder AddChild(Action<ControlBuilder> configure)
        {
            var child = new ControlBuilder();
            configure(child);
            _node.Children.Add(child._node);
            return this;
        }
        public ControlBuilder AddChild(RenderNode child)
        { if (child != null) _node.Children.Add(child); return this; }
        public ControlBuilder AddChildren(params Action<ControlBuilder>[] cfgs)
        { foreach (var c in cfgs) AddChild(c); return this; }

        // ══════════════════════════════════════════════════════════════
        // 2. ESPACIADO Y FORMA
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder Padding(int all) => Padding((float)all);
        public ControlBuilder Padding(float all)
        { _node.Padding = new ModernPadding(Math.Max(0, all)); return this; }

        public ControlBuilder Padding(int l, int t, int r, int b)
            => Padding((float)l, (float)t, (float)r, (float)b);
        public ControlBuilder Padding(float l, float t, float r, float b)
        { _node.Padding = new ModernPadding(Math.Max(0, l), Math.Max(0, t), Math.Max(0, r), Math.Max(0, b)); return this; }

        /// <summary>Radio uniforme de bordes (Estilo CSS).</summary>
        public ControlBuilder BorderRadius(int r) => BorderRadius((float)r);
        public ControlBuilder BorderRadius(float r)
        { _node.Corners = new CornerRadii(Math.Max(0, r)); return this; }

        /// <summary>Radio por borde: TopLeft, TopRight, BottomRight, BottomLeft.</summary>
        public ControlBuilder BorderRadius(int tl, int tr, int br, int bl)
            => BorderRadius((float)tl, (float)tr, (float)br, (float)bl);
        public ControlBuilder BorderRadius(float tl, float tr, float br, float bl)
        { _node.Corners = new CornerRadii(Math.Max(0, tl), Math.Max(0, tr), Math.Max(0, br), Math.Max(0, bl)); return this; }

        // ══════════════════════════════════════════════════════════════
        // 3. VISIBILIDAD Y ESTADO
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder Opacity(float o)
        { _node.Opacity = Math.Max(0f, Math.Min(1f, o)); return this; }
        public ControlBuilder Visible(bool v = true) { _node.IsVisible = v; return this; }
        public ControlBuilder Enabled(bool e = true) { _node.Enabled = e; return this; }
        public ControlBuilder UseSkia(bool enabled = true)
        {
            if (_hostControl is ModernControlBase mcb)
                mcb.UseSkiaGraphics = enabled;
            return this;
        }

        // ══════════════════════════════════════════════════════════════
        // 4. ANIMACIÓN
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder Animate(AnimationEasing easing)
        { _node.Easing = easing; return this; }

        public ControlBuilder AnimationSpeed(int ms) => AnimationSpeed((float)ms);
        public ControlBuilder AnimationSpeed(float ms)
        {
            if (_hostControl is ModernControlBase mcb)
                mcb.AnimationSpeed = Math.Max(10f, ms);
            return this;
        }

        /// <summary>Atajo Dev-Friendly: Curva Spring con velocidad.</summary>
        public ControlBuilder AnimateSpring(int ms = 150)
            => Animate(AnimationEasing.Spring).AnimationSpeed(ms);

        /// <summary>Atajo Dev-Friendly: Curva EaseInOut con velocidad.</summary>
        public ControlBuilder AnimateEase(int ms = 150)
            => Animate(AnimationEasing.EaseInOut).AnimationSpeed(ms);

        // ══════════════════════════════════════════════════════════════
        // 5. EVENTOS
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder OnClick(Action<RenderNode> a)
        { _node.OnClickAction = a; return this; }
        public ControlBuilder OnHoverEnter(Action<RenderNode> a)
        { _node.OnHoverEnterAction = a; return this; }
        public ControlBuilder OnHoverLeave(Action<RenderNode> a)
        { _node.OnHoverLeaveAction = a; return this; }

        // ══════════════════════════════════════════════════════════════
        // 6. CONTENIDO / TEXTO
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder Content(string text, string? hexColor = null, float? fontSize = null)
        {
            var ct = _node.Content;
            ct.Text = text ?? string.Empty;

            if (fontSize.HasValue)
                ct.FontSize = Math.Max(1f, fontSize.Value);

            if (!string.IsNullOrWhiteSpace(hexColor))
            {
                try { ct.TextColor = ParseHex(hexColor!); } catch { }
            }

            _node.Content = ct;
            return this;
        }

        public ControlBuilder Text(string text)
        { var ct = _node.Content; ct.Text = text ?? string.Empty; _node.Content = ct; return this; }

        public ControlBuilder TextColor(string hex)
        {
            var ct = _node.Content;
            try { ct.TextColor = ParseHex(hex); } catch { }
            _node.Content = ct;
            return this;
        }

        public ControlBuilder Font(string family, int size, bool bold = false, bool italic = false)
            => Font(family, (float)size, bold, italic);
        public ControlBuilder Font(string family, float size, bool bold = false, bool italic = false)
        {
            var ct = _node.Content;
            if (!string.IsNullOrWhiteSpace(family)) ct.FontFamily = family;
            ct.FontSize = Math.Max(1f, size);
            ct.IsBold = bold;
            _node.Content = ct;
            return this;
        }

        public ControlBuilder TextAlign(StringAlignment align)
        { var ct = _node.Content; ct.HorizontalAlignment = align; _node.Content = ct; return this; }
        public ControlBuilder TextAlignV(StringAlignment align)
        { var ct = _node.Content; ct.VerticalAlignment = align; _node.Content = ct; return this; }
        public ControlBuilder WordWrap(bool wrap = true)
        { var ct = _node.Content; ct.WordWrap = wrap; _node.Content = ct; return this; }

        // ══════════════════════════════════════════════════════════════
        // 7. FONDO
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder Background(string hex)
        {
            var bg = _node.Background;
            bg.Color1 = ParseHex(hex);
            bg.IsGradient = false;
            _node.Background = bg;
            return this;
        }

        public ControlBuilder Gradient(string hexFrom, string hexTo, int angle = 0)
            => Gradient(hexFrom, hexTo, (float)angle);
        public ControlBuilder Gradient(string hexFrom, string hexTo, float angle = 0)
        {
            var bg = _node.Background;
            bg.Color1 = ParseHex(hexFrom);
            bg.Color2 = ParseHex(hexTo);
            bg.IsGradient = true;
            bg.GradientAngle = angle;
            _node.Background = bg;
            return this;
        }

        // ══════════════════════════════════════════════════════════════
        // 8. BORDE
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder Border(string hex, int width = 1) => Border(hex, (float)width);
        public ControlBuilder Border(string hex, float width = 1)
        {
            var bd = _node.Border;
            bd.Thickness = new ModernThickness(Math.Max(0, width));
            try { bd.NormalColor = ColorTranslator.FromHtml(hex); }
            catch { bd.NormalColor = Color.Transparent; }
            _node.Border = bd;
            return this;
        }

        // ══════════════════════════════════════════════════════════════
        // 9. SOMBRA
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder Shadow(int elevation = 2)
        {
            var sh = _node.Shadow;
            switch (elevation)
            {
                case 1: sh.OffsetX = 0; sh.OffsetY = 2; sh.Radius = 4; break;
                case 2: sh.OffsetX = 0; sh.OffsetY = 4; sh.Radius = 8; break;
                case 3: sh.OffsetX = 0; sh.OffsetY = 8; sh.Radius = 16; break;
                case 4: sh.OffsetX = 0; sh.OffsetY = 12; sh.Radius = 24; break;
                case 5: sh.OffsetX = 0; sh.OffsetY = 16; sh.Radius = 32; break;
                default: sh.OffsetX = 0; sh.OffsetY = 0; sh.Radius = 0; break;
            }
            if (elevation > 0) sh.Color = Color.FromArgb(50, 0, 0, 0);
            _node.Shadow = sh;
            return this;
        }

        public ControlBuilder Shadow(string hex, int offsetX = 0, int offsetY = 4, int blur = 8)
            => Shadow(hex, (float)offsetX, (float)offsetY, (float)blur);
        public ControlBuilder Shadow(string hex, float offsetX = 0, float offsetY = 4, float blur = 8)
        {
            var sh = _node.Shadow;
            sh.Color = ParseHex(hex);
            sh.OffsetX = offsetX;
            sh.OffsetY = offsetY;
            sh.Radius = Math.Max(0, blur);
            _node.Shadow = sh;
            return this;
        }

        // ══════════════════════════════════════════════════════════════
        // 10. FILTROS CSS-LIKE
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder Blur(float amount)
        { var f = _node.Filters; f.Blur = Math.Max(0, amount); _node.Filters = f; return this; }
        public ControlBuilder Blur(int amount) => Blur((float)amount);

        public ControlBuilder Brightness(float value)
        { var f = _node.Filters; f.Brightness = Math.Max(0, value); _node.Filters = f; return this; }

        public ControlBuilder Contrast(float value)
        { var f = _node.Filters; f.Contrast = Math.Max(0, value); _node.Filters = f; return this; }

        public ControlBuilder Grayscale(float amount)
        { var f = _node.Filters; f.Grayscale = Math.Max(0f, Math.Min(1f, amount)); _node.Filters = f; return this; }

        // ══════════════════════════════════════════════════════════════
        // 11. GLASS — SOLO ModernForm y ModernGlassPanel
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder Glass(string tint = "#40FFFFFF")
        {
            var ac = _node.Acrylic;
            ac.IsEnabled = true;
            ac.TintColor = ParseHex(tint);
            _node.Acrylic = ac;
            var bg = _node.Background;
            bg.Color1 = Color.Transparent; bg.IsGradient = false;
            _node.Background = bg;
            return this;
        }

        // ══════════════════════════════════════════════════════════════
        // 12. ESTADOS INTERACTIVOS — Hover y Press (Optimizados)
        // ══════════════════════════════════════════════════════════════

        /// <summary>Configuración avanzada del estado Hover mediante expresión lambda.</summary>
        public ControlBuilder StateHover(Action<StateBuilder> cfg)
        {
            var sb = new StateBuilder(_node.HoverState);
            cfg(sb);
            _node.HoverState = sb.Build();
            return this;
        }

        /// <summary>Configuración avanzada del estado Press mediante expresión lambda.</summary>
        public ControlBuilder StatePress(Action<StateBuilder> cfg)
        {
            var sb = new StateBuilder(_node.PressState);
            cfg(sb);
            _node.PressState = sb.Build();
            return this;
        }

        /// <summary>
        /// Atajo Ultra-Friendly: Configura el estado Hover completo usando parámetros opcionales con Cero Lambdas.
        /// Uso: .Hover(bg: "#106EBE", scale: 1.05f)
        /// </summary>
        public ControlBuilder Hover(string? bg = null, float? scale = null, float? opacity = null, string? border = null, string? shadow = null)
        {
            return StateHover(s => {
                if (!string.IsNullOrWhiteSpace(bg)) s.Background(bg);
                if (scale.HasValue) s.Scale(scale.Value);
                if (opacity.HasValue) s.Opacity(opacity.Value);
                if (!string.IsNullOrWhiteSpace(border)) s.Border(border);
                if (!string.IsNullOrWhiteSpace(shadow)) s.Shadow(shadow);
            });
        }

        /// <summary>
        /// Atajo Ultra-Friendly: Configura el estado Press completo usando parámetros opcionales con Cero Lambdas.
        /// Uso: .Press(bg: "#005A9E", scale: 0.96f)
        /// </summary>
        public ControlBuilder Press(string? bg = null, float? scale = null, float? opacity = null, string? border = null, string? shadow = null)
        {
            return StatePress(s => {
                if (!string.IsNullOrWhiteSpace(bg)) s.Background(bg);
                if (scale.HasValue) s.Scale(scale.Value);
                if (opacity.HasValue) s.Opacity(opacity.Value);
                if (!string.IsNullOrWhiteSpace(border)) s.Border(border);
                if (!string.IsNullOrWhiteSpace(shadow)) s.Shadow(shadow);
            });
        }

        // --- Atajos individuales directos para Hover (Chaining Seguro) ---
        public ControlBuilder HoverBackground(string hex) => StateHover(s => s.Background(hex));
        public ControlBuilder HoverScale(float scale) => StateHover(s => s.Scale(scale));
        public ControlBuilder HoverOpacity(float opacity) => StateHover(s => s.Opacity(opacity));
        public ControlBuilder HoverBorder(string hex) => StateHover(s => s.Border(hex));
        public ControlBuilder HoverShadow(string hex) => StateHover(s => s.Shadow(hex));

        // --- Atajos individuales directos para Press (Chaining Seguro) ---
        public ControlBuilder PressBackground(string hex) => StatePress(s => s.Background(hex));
        public ControlBuilder PressScale(float scale) => StatePress(s => s.Scale(scale));
        public ControlBuilder PressOpacity(float opacity) => StatePress(s => s.Opacity(opacity));
        public ControlBuilder PressBorder(string hex) => StatePress(s => s.Border(hex));
        public ControlBuilder PressShadow(string hex) => StatePress(s => s.Shadow(hex));

        // ══════════════════════════════════════════════════════════════
        // 13. APPLY — El último método de la cadena
        // ══════════════════════════════════════════════════════════════

        public ModernControlBase Apply(Control? parent = null)
        {
            if (_hostControl == null)
                throw new InvalidOperationException(
                    "Usa .Design() sobre un FluentElement para obtener un ControlBuilder válido.");

            if (_node.Layout.Width > 0 && _node.Layout.Height > 0)
            {
                _hostControl.Location = new Point((int)_node.Layout.X, (int)_node.Layout.Y);
                _hostControl.Size = new Size((int)_node.Layout.Width, (int)_node.Layout.Height);
                _node.Layout = new RectangleF(0, 0, _node.Layout.Width, _node.Layout.Height);
            }

            OnApplied?.Invoke(_node);

            if (parent != null && !parent.Controls.Contains(_hostControl))
                parent.Controls.Add(_hostControl);
            else if (parent == null
                  && _hostControl.Parent == null
                  && Application.OpenForms.Count > 0)
                Application.OpenForms[0]!.Controls.Add(_hostControl);

            return (ModernControlBase)_hostControl;
        }

        internal RenderNode GetNode() { OnApplied?.Invoke(_node); return _node; }

        // ── Helpers privados ──────────────────────────────────────────
        private Color ParseHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Color.Transparent;
            hex = hex.TrimStart('#');
            try
            {
                if (hex.Length == 8)
                    return Color.FromArgb(
                        Convert.ToInt32(hex.Substring(0, 2), 16),
                        Convert.ToInt32(hex.Substring(2, 2), 16),
                        Convert.ToInt32(hex.Substring(4, 2), 16),
                        Convert.ToInt32(hex.Substring(6, 2), 16));
                return ColorTranslator.FromHtml("#" + hex);
            }
            catch { return Color.Transparent; }
        }

        private static string ToHex(Color c)
            => string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);

        private static string Lighten(Color c, int amt)
            => ToHex(Color.FromArgb(c.A,
                Math.Max(0, Math.Min(255, c.R + amt)),
                Math.Max(0, Math.Min(255, c.G + amt)),
                Math.Max(0, Math.Min(255, c.B + amt))));

        // ══════════════════════════════════════════════════════════════
        // StateBuilder — Configura estados hover y press
        // ══════════════════════════════════════════════════════════════
        public sealed class StateBuilder
        {
            private VisualStateOverrides _s;
            internal StateBuilder(VisualStateOverrides s) { _s = s; }

            public StateBuilder Background(string hex)
            {
                try
                {
                    _s.Background = new BackgroundData
                    { Color1 = ColorTranslator.FromHtml(hex), IsGradient = false };
                }
                catch { }
                return this;
            }

            public StateBuilder Gradient(string hexFrom, string hexTo, float angle = 0)
            {
                try
                {
                    _s.Background = new BackgroundData
                    {
                        Color1 = ColorTranslator.FromHtml(hexFrom),
                        Color2 = ColorTranslator.FromHtml(hexTo),
                        IsGradient = true,
                        GradientAngle = angle
                    };
                }
                catch { }
                return this;
            }
            public StateBuilder Gradient(string hexFrom, string hexTo, int angle)
                => Gradient(hexFrom, hexTo, (float)angle);

            public StateBuilder Border(string hex)
            {
                try
                {
                    _s.Border = new BorderData
                    {
                        NormalColor = ColorTranslator.FromHtml(hex),
                        Thickness = new ModernThickness(1)
                    };
                }
                catch { }
                return this;
            }

            public StateBuilder Scale(float scale)
            { _s.Scale = scale; return this; }

            public StateBuilder Opacity(float opacity)
            { _s.Opacity = Math.Max(0f, Math.Min(1f, opacity)); return this; }

            public StateBuilder Shadow(string hex)
            {
                try
                {
                    _s.Shadow = new ShadowData
                    { Color = ColorTranslator.FromHtml(hex), Radius = 8, OffsetY = 4 };
                }
                catch { }
                return this;
            }

            internal VisualStateOverrides Build() => _s;
        }
    }
}