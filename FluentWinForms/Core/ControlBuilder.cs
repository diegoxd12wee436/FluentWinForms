#nullable enable
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace FluentWinForms.Core
{
    /// <summary>
    /// Fluent API de FluentWinForms — Minimalista y Dev-Friendly.
    /// </summary>
    // 🔥 EL TRUCO: La clase entera ahora sabe qué control está construyendo (T)
    public sealed class ControlBuilder<T> where T : ModernControlBase
    {
        private readonly RenderNode _node;
        private readonly T? _hostControl; // Guarda el control real (ej: FluentElement)
        internal Action<RenderNode>? OnApplied { get; set; }

        public ControlBuilder(RenderNode node, T? host = null)
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
        // 1. LAYOUT
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder<T> Layout(int x, int y, int width, int height)
            => Layout((float)x, (float)y, (float)width, (float)height);
        public ControlBuilder<T> Layout(float x, float y, float width, float height)
        {
            _node.Layout = new RectangleF(x, y, Math.Max(0, width), Math.Max(0, height));
            _node.StretchX = false;
            _node.StretchY = false;
            return this;
        }

        public ControlBuilder<T> Width(int w) => Width((float)w);
        public ControlBuilder<T> Width(float w)
        { _node.Layout = new RectangleF(_node.Layout.X, _node.Layout.Y, Math.Max(0, w), _node.Layout.Height); _node.StretchX = false; return this; }

        public ControlBuilder<T> Height(int h) => Height((float)h);
        public ControlBuilder<T> Height(float h)
        { _node.Layout = new RectangleF(_node.Layout.X, _node.Layout.Y, _node.Layout.Width, Math.Max(0, h)); _node.StretchY = false; return this; }

        public ControlBuilder<T> Size(int width, int height) => Size((float)width, (float)height);
        public ControlBuilder<T> Size(float width, float height)
        { Width(width); Height(height); return this; }

        public ControlBuilder<T> StretchWidth() { _node.StretchX = true; return this; }
        public ControlBuilder<T> StretchHeight() { _node.StretchY = true; return this; }
        public ControlBuilder<T> Fill() { StretchWidth(); StretchHeight(); return this; }

        public ControlBuilder<T> MinSize(double width, double height)
        { _node.MinSize = new SizeF((float)width, (float)height); return this; }
        public ControlBuilder<T> MaxSize(double width, double height)
        { _node.MaxSize = new SizeF((float)width, (float)height); return this; }
        public ControlBuilder<T> Rotate(double degrees)
        { _node.Rotation = (float)degrees; return this; }
        public ControlBuilder<T> Scale(double scaleX, double scaleY)
        { _node.ScaleX = (float)scaleX; _node.ScaleY = (float)scaleY; return this; }
        public ControlBuilder<T> Scale(double uniformScale) => Scale(uniformScale, uniformScale);
        public ControlBuilder<T> TransformOrigin(double x, double y)
        { _node.TransformOrigin = new PointF((float)x, (float)y); return this; }

        // ── Contenedores ───────────────────────────────────────────────
        public ControlBuilder<T> VStack(int spacing = 0) => VStack((float)spacing);
        public ControlBuilder<T> VStack(float spacing = 0)
        { _node.LayoutMode = LayoutStyle.VerticalStack; _node.Spacing = spacing; return this; }

        public ControlBuilder<T> HStack(int spacing = 0) => HStack((float)spacing);
        public ControlBuilder<T> HStack(float spacing = 0)
        { _node.LayoutMode = LayoutStyle.HorizontalStack; _node.Spacing = spacing; return this; }

        public ControlBuilder<T> Grid(int minColWidth, int gap = 0) => Grid((float)minColWidth, (float)gap);
        public ControlBuilder<T> Grid(float minColWidth, float gap = 0)
        { _node.LayoutMode = LayoutStyle.AutoFitGrid; _node.GridMinColumnWidth = Math.Max(1, minColWidth); _node.Spacing = gap; return this; }

        // ── Hijos ──────────────────────────────────────────────────────
        public ControlBuilder<T> AddChild(Action<ControlBuilder<ModernControlBase>> configure)
        {
            var child = new ControlBuilder<ModernControlBase>();
            configure(child);
            _node.Children.Add(child.GetNode());
            return this;
        }
        public ControlBuilder<T> AddChild(RenderNode child)
        { if (child != null) _node.Children.Add(child); return this; }
        public ControlBuilder<T> AddChildren(params Action<ControlBuilder<ModernControlBase>>[] cfgs)
        { foreach (var c in cfgs) AddChild(c); return this; }

        // ══════════════════════════════════════════════════════════════
        // 2. ESPACIADO Y FORMA
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder<T> Padding(int all) => Padding((float)all);
        public ControlBuilder<T> Padding(float all)
        { _node.Padding = new ModernPadding(Math.Max(0, all)); return this; }
        public ControlBuilder<T> Padding(float horizontal, float vertical)
        { _node.Padding = new ModernPadding(Math.Max(0, horizontal), Math.Max(0, vertical), Math.Max(0, horizontal), Math.Max(0, vertical)); return this; }
        public ControlBuilder<T> Padding(int horizontal, int vertical) => Padding((float)horizontal, (float)vertical);
        public ControlBuilder<T> Padding(int l, int t, int r, int b) => Padding((float)l, (float)t, (float)r, (float)b);
        public ControlBuilder<T> Padding(float l, float t, float r, float b)
        { _node.Padding = new ModernPadding(Math.Max(0, l), Math.Max(0, t), Math.Max(0, r), Math.Max(0, b)); return this; }

        public ControlBuilder<T> BorderRadius(int r) => BorderRadius((float)r);
        public ControlBuilder<T> BorderRadius(float r)
        { _node.Corners = new CornerRadii(Math.Max(0, r)); return this; }
        public ControlBuilder<T> BorderRadius(int tl, int tr, int br, int bl) => BorderRadius((float)tl, (float)tr, (float)br, (float)bl);
        public ControlBuilder<T> BorderRadius(float tl, float tr, float br, float bl)
        { _node.Corners = new CornerRadii(Math.Max(0, tl), Math.Max(0, tr), Math.Max(0, br), Math.Max(0, bl)); return this; }

        // ══════════════════════════════════════════════════════════════
        // 3. VISIBILIDAD, ANIMACIÓN Y EVENTOS
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder<T> Opacity(double o) => Opacity((float)o);
        public ControlBuilder<T> Opacity(float o)
        { _node.Opacity = Math.Max(0f, Math.Min(1f, o)); return this; }
        public ControlBuilder<T> Visible(bool v = true) { _node.IsVisible = v; return this; }
        public ControlBuilder<T> Enabled(bool e = true) { _node.Enabled = e; return this; }
        public ControlBuilder<T> UseSkia(bool enabled = true)
        {
            if (_hostControl != null) _hostControl.UseSkiaGraphics = enabled;
            return this;
        }

        public ControlBuilder<T> Animate(AnimationEasing easing)
        { _node.Easing = easing; return this; }
        public ControlBuilder<T> AnimationSpeed(int ms) => AnimationSpeed((float)ms);
        public ControlBuilder<T> AnimationSpeed(float ms)
        {
            if (_hostControl != null) _hostControl.AnimationSpeed = Math.Max(10f, ms);
            return this;
        }
        public ControlBuilder<T> AnimateSpring(int ms = 150) => Animate(AnimationEasing.Spring).AnimationSpeed(ms);
        public ControlBuilder<T> AnimateEase(int ms = 150) => Animate(AnimationEasing.EaseInOut).AnimationSpeed(ms);

        public ControlBuilder<T> OnClick(Action<RenderNode> a) { _node.OnClickAction = a; return this; }
        public ControlBuilder<T> OnHoverEnter(Action<RenderNode> a) { _node.OnHoverEnterAction = a; return this; }
        public ControlBuilder<T> OnHoverLeave(Action<RenderNode> a) { _node.OnHoverLeaveAction = a; return this; }

        public ControlBuilder<T> Ripple(string hexColor = "#40000000", double opacity = 1.0)
        {
            var rp = _node.Ripple;
            try { rp.Color = ParseHex(hexColor); } catch { }
            rp.Opacity = (float)opacity;
            _node.Ripple = rp;
            return this;
        }

        // ══════════════════════════════════════════════════════════════
        // 4. CONTENIDO Y TEXTO
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder<T> Content(string text, string? hexColor = null, float? fontSize = null)
        {
            var ct = _node.Content;
            ct.Text = text ?? string.Empty;
            if (fontSize.HasValue) ct.FontSize = Math.Max(1f, fontSize.Value);
            if (!string.IsNullOrWhiteSpace(hexColor)) { try { ct.TextColor = ParseHex(hexColor!); } catch { } }
            _node.Content = ct;
            return this;
        }

        public ControlBuilder<T> Text(string text)
        { var ct = _node.Content; ct.Text = text ?? string.Empty; _node.Content = ct; return this; }
        public ControlBuilder<T> TextColor(string hex)
        { var ct = _node.Content; try { ct.TextColor = ParseHex(hex); } catch { } _node.Content = ct; return this; }
        public ControlBuilder<T> TextColor(Color color)
        { var ct = _node.Content; ct.TextColor = color; _node.Content = ct; return this; }

        public ControlBuilder<T> Font(string family, int size, bool bold = false, bool italic = false)
            => Font(family, (float)size, bold, italic);
        public ControlBuilder<T> Font(string family, float size, bool bold = false, bool italic = false)
        {
            var ct = _node.Content;
            if (!string.IsNullOrWhiteSpace(family)) ct.FontFamily = family;
            ct.FontSize = Math.Max(1f, size);
            ct.IsBold = bold;
            ct.IsItalic = italic; // 🔥 FIX DE ITALIC APLICADO
            _node.Content = ct;
            return this;
        }

        public ControlBuilder<T> TextAlign(StringAlignment align)
        { var ct = _node.Content; ct.HorizontalAlignment = align; _node.Content = ct; return this; }
        public ControlBuilder<T> TextAlignV(StringAlignment align)
        { var ct = _node.Content; ct.VerticalAlignment = align; _node.Content = ct; return this; }
        public ControlBuilder<T> WordWrap(bool wrap = true)
        { var ct = _node.Content; ct.WordWrap = wrap; _node.Content = ct; return this; }
        public ControlBuilder<T> TextDecoration(TextDecoration decoration)
        { var ct = _node.Content; ct.Decoration = decoration; _node.Content = ct; return this; }
        public ControlBuilder<T> TextTrimming(bool enable = true)
        { var ct = _node.Content; ct.Trimming = enable; _node.Content = ct; return this; }

        public ControlBuilder<T> Image(Image img, ImageFit fit = ImageFit.Cover, double opacity = 1.0)
        {
            var ct = _node.Content;
            ct.Image = img;
            ct.ImageFit = fit;
            ct.ImageOpacity = (float)opacity;
            _node.Content = ct;
            return this;
        }
        public ControlBuilder<T> ImageFitMode(ImageFit fit)
        { var ct = _node.Content; ct.ImageFit = fit; _node.Content = ct; return this; }

        // ══════════════════════════════════════════════════════════════
        // 5. FONDO Y BORDE
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder<T> Background(string hex)
        {
            var bg = _node.Background;
            bg.Color1 = ParseHex(hex);
            bg.IsGradient = false;
            _node.Background = bg;
            return this;
        }
        public ControlBuilder<T> Background(Color color)
        {
            var bg = _node.Background;
            bg.Color1 = color;
            bg.IsGradient = false;
            _node.Background = bg;
            return this;
        }
        public ControlBuilder<T> Gradient(string hexFrom, string hexTo, int angle = 0) => Gradient(hexFrom, hexTo, (float)angle);
        public ControlBuilder<T> Gradient(string hexFrom, string hexTo, float angle = 0)
        {
            var bg = _node.Background;
            bg.Color1 = ParseHex(hexFrom);
            bg.Color2 = ParseHex(hexTo);
            bg.IsGradient = true;
            bg.GradientAngle = angle;
            _node.Background = bg;
            return this;
        }

        public ControlBuilder<T> Border(string hex, int width = 1) => Border(hex, (float)width);
        public ControlBuilder<T> Border(string hex, float width = 1)
        {
            var bd = _node.Border;
            bd.Thickness = new ModernThickness(Math.Max(0, width));
            try { bd.NormalColor = ParseHex(hex); } catch { bd.NormalColor = Color.Transparent; }
            _node.Border = bd;
            return this;
        }
        public ControlBuilder<T> Border(string hex, double width, FluentBorderStyle style)
        {
            var bd = _node.Border;
            bd.Thickness = new ModernThickness((float)width);
            bd.Style = style;
            try { bd.NormalColor = ParseHex(hex); } catch { }
            _node.Border = bd;
            return this;
        }
        public ControlBuilder<T> Border(string hex, double top, double right, double bottom, double left, FluentBorderStyle style = FluentBorderStyle.Solid)
        {
            var bd = _node.Border;
            bd.Thickness = new ModernThickness((float)top, (float)right, (float)bottom, (float)left);
            bd.Style = style;
            try { bd.NormalColor = ParseHex(hex); } catch { }
            _node.Border = bd;
            return this;
        }

        public ControlBuilder<T> Shadow(int elevation = 2)
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
            // 🔥 FIX #3: Invalidar cache
            _node._cachedShadowFilter?.Dispose();
            _node._cachedShadowFilter = null;
            return this;
        }
        public ControlBuilder<T> Shadow(string hex, int offsetX = 0, int offsetY = 4, int blur = 8)
            => Shadow(hex, (float)offsetX, (float)offsetY, (float)blur);
        public ControlBuilder<T> Shadow(string hex, float offsetX = 0, float offsetY = 4, float blur = 8)
        {
            var sh = _node.Shadow;
            sh.Color = ParseHex(hex);
            sh.OffsetX = offsetX;
            sh.OffsetY = offsetY;
            sh.Radius = Math.Max(0, blur);
            _node.Shadow = sh;
            return this;
        }

        public ControlBuilder<T> Glass(string tint = "#40FFFFFF")
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
        // 6. ESTADOS HOVER Y PRESS
        // ══════════════════════════════════════════════════════════════

        public ControlBuilder<T> StateHover(Action<StateBuilder> cfg)
        {
            var sb = new StateBuilder(_node.HoverState);
            cfg(sb);
            _node.HoverState = sb.Build();
            return this;
        }

        public ControlBuilder<T> StatePress(Action<StateBuilder> cfg)
        {
            var sb = new StateBuilder(_node.PressState);
            cfg(sb);
            _node.PressState = sb.Build();
            return this;
        }

        public ControlBuilder<T> Hover(string? bg = null, double? scale = null, double? opacity = null, string? border = null, string? shadow = null)
        {
            return StateHover(s => {
                if (!string.IsNullOrWhiteSpace(bg)) s.Background(bg);
                if (scale.HasValue) s.Scale((float)scale.Value);
                if (opacity.HasValue) s.Opacity((float)opacity.Value);
                if (!string.IsNullOrWhiteSpace(border)) s.Border(border);
                if (!string.IsNullOrWhiteSpace(shadow)) s.Shadow(shadow);
            });
        }

        public ControlBuilder<T> Press(string? bg = null, double? scale = null, double? opacity = null, string? border = null, string? shadow = null)
        {
            return StatePress(s => {
                if (!string.IsNullOrWhiteSpace(bg)) s.Background(bg);
                if (scale.HasValue) s.Scale((float)scale.Value);
                if (opacity.HasValue) s.Opacity((float)opacity.Value);
                if (!string.IsNullOrWhiteSpace(border)) s.Border(border);
                if (!string.IsNullOrWhiteSpace(shadow)) s.Shadow(shadow);
            });
        }

        // ══════════════════════════════════════════════════════════════
        // 7. APPLY — Normalito y Tuani
        // ══════════════════════════════════════════════════════════════

        // 🔥 MIRA ESTO: El Apply devuelve "T" directamente. Adiós a poner genéricos en tu formulario.
        public T Apply(Control? parent = null)
        {
            if (_hostControl == null)
                throw new InvalidOperationException("Usa FluentElement.Design() para crear el control primero.");

            if (_node.Layout.Width > 0 && _node.Layout.Height > 0)
            {
                _hostControl.Location = new Point((int)_node.Layout.X, (int)_node.Layout.Y);
                _hostControl.Size = new Size((int)_node.Layout.Width, (int)_node.Layout.Height);
                _node.Layout = new RectangleF(0, 0, _node.Layout.Width, _node.Layout.Height);
            }

            OnApplied?.Invoke(_node);

            if (parent != null && !parent.Controls.Contains(_hostControl))
                parent.Controls.Add(_hostControl);
            else if (parent == null && _hostControl.Parent == null && Application.OpenForms.Count > 0)
                Application.OpenForms[0]!.Controls.Add(_hostControl);

            return _hostControl;
        }

        internal RenderNode GetNode() { OnApplied?.Invoke(_node); return _node; }

        private static Color ParseHex(string hex)
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

        public sealed class StateBuilder
        {
            private VisualStateOverrides _s;
            internal StateBuilder(VisualStateOverrides s) { _s = s; }

            public StateBuilder Background(string hex) { try { _s.Background = new BackgroundData { Color1 = ParseHex(hex), IsGradient = false }; } catch { } return this; }
            public StateBuilder Border(string hex) { try { _s.Border = new BorderData { NormalColor = ParseHex(hex), Thickness = new ModernThickness(1) }; } catch { } return this; }
            public StateBuilder Shadow(string hex) { try { _s.Shadow = new ShadowData { Color = ParseHex(hex), Radius = 8, OffsetY = 4 }; } catch { } return this; }
            public StateBuilder Scale(float scale) { _s.Scale = scale; return this; }
            public StateBuilder Opacity(float opacity) { _s.Opacity = Math.Max(0f, Math.Min(1f, opacity)); return this; }
            internal VisualStateOverrides Build() => _s;
        }
    }
}