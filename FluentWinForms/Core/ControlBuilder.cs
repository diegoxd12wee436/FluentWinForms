#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;

namespace FluentWinForms.Core
{
    /// <summary>
    /// Fluent builder para RenderNode. Diseñado para ergonomía suprema (Patrón Lambda y Atajos Directos).
    /// </summary>
    public sealed class ControlBuilder
    {
        private readonly RenderNode _node;
        internal Action<RenderNode>? OnApplied { get; set; }

        public ControlBuilder(string id = "")
        {
            _node = new RenderNode { Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id };
        }

        public static ControlBuilder Design(string id = "") => new ControlBuilder(id);
        public static ControlBuilder Create(string id = "") => new ControlBuilder(id);

        public ControlBuilder Layout(float x, float y, float width, float height)
        {
            _node.Layout = new RectangleF(x, y, Math.Max(0, width), Math.Max(0, height));
            _node.StretchX = false;
            _node.StretchY = false;
            return this;
        }
        public ControlBuilder Layout(int x, int y, int width, int height) => Layout((float)x, (float)y, (float)width, (float)height);

        // --- API de Tamaño Minimalista ---
        public ControlBuilder Width(float w) { _node.StretchX = false; _node.Layout = new RectangleF(_node.Layout.X, _node.Layout.Y, w, _node.Layout.Height); return this; }
        public ControlBuilder Width(int w) => Width((float)w);

        public ControlBuilder Height(float h) { _node.StretchY = false; _node.Layout = new RectangleF(_node.Layout.X, _node.Layout.Y, _node.Layout.Width, h); return this; }
        public ControlBuilder Height(int h) => Height((float)h);

        public ControlBuilder StretchWidth() { _node.StretchX = true; return this; }
        public ControlBuilder StretchHeight() { _node.StretchY = true; return this; }

        // --- API de Contenedor y Layout Automático ---
        public ControlBuilder VStack(float spacing = 0f) { _node.LayoutMode = LayoutStyle.VerticalStack; _node.Spacing = spacing; return this; }
        public ControlBuilder VStack(int spacing) => VStack((float)spacing);

        public ControlBuilder HStack(float spacing = 0f) { _node.LayoutMode = LayoutStyle.HorizontalStack; _node.Spacing = spacing; return this; }
        public ControlBuilder HStack(int spacing) => HStack((float)spacing);

        public ControlBuilder Grid(float minColWidth, float gap = 0f) { _node.LayoutMode = LayoutStyle.AutoFitGrid; _node.GridMinColumnWidth = Math.Max(1f, minColWidth); _node.Spacing = gap; return this; }
        public ControlBuilder Grid(int minColWidth, int gap = 0) => Grid((float)minColWidth, (float)gap);

        // 🔥 ATAJO: AddChild con Lambda inline
        public ControlBuilder AddChild(Action<ControlBuilder> configure)
        {
            var childBuilder = new ControlBuilder();
            configure(childBuilder);
            _node.Children.Add(childBuilder.Apply());
            return this;
        }

        public ControlBuilder AddChild(RenderNode child) { if (child != null) _node.Children.Add(child); return this; }

        public ControlBuilder Padding(float all) { _node.Padding = new ModernPadding(Math.Max(0, all)); return this; }
        public ControlBuilder Padding(int all) => Padding((float)all);
        public ControlBuilder Padding(float left, float top, float right, float bottom) { _node.Padding = new ModernPadding(Math.Max(0, left), Math.Max(0, top), Math.Max(0, right), Math.Max(0, bottom)); return this; }
        public ControlBuilder Padding(int left, int top, int right, int bottom) => Padding((float)left, (float)top, (float)right, (float)bottom);

        // --- API Minimalista para Restricciones ---
        public ControlBuilder Min(float width, float height) { _node.MinSize = new SizeF(Math.Max(0, width), Math.Max(0, height)); return this; }
        public ControlBuilder Min(int width, int height) => Min((float)width, (float)height);

        public ControlBuilder Max(float width, float height) { _node.MaxSize = new SizeF(Math.Max(0, width), Math.Max(0, height)); return this; }
        public ControlBuilder Max(int width, int height) => Max((float)width, (float)height);

        public ControlBuilder CornerRadius(float uniform) { _node.Corners = new CornerRadii(Math.Max(0, uniform)); return this; }
        public ControlBuilder CornerRadius(int uniform) => CornerRadius((float)uniform);
        public ControlBuilder CornerRadius(float tl, float tr, float br, float bl) { _node.Corners = new CornerRadii(Math.Max(0, tl), Math.Max(0, tr), Math.Max(0, br), Math.Max(0, bl)); return this; }
        public ControlBuilder CornerRadius(int tl, int tr, int br, int bl) => CornerRadius((float)tl, (float)tr, (float)br, (float)bl);

        public ControlBuilder Transform(float scaleX = 1f, float scaleY = 1f, float rotation = 0f) { _node.ScaleX = scaleX; _node.ScaleY = scaleY; _node.Rotation = rotation; return this; }
        public ControlBuilder Transform(double scaleX, double scaleY, int rotation = 0) => Transform((float)scaleX, (float)scaleY, (float)rotation);

        public ControlBuilder Opacity(float opacity) { _node.Opacity = Math.Max(0f, Math.Min(1f, opacity)); return this; }
        public ControlBuilder Opacity(double opacity) => Opacity((float)opacity);

        public ControlBuilder Visible(bool visible = true) { _node.IsVisible = visible; return this; }
        public ControlBuilder Enabled(bool enabled = true) { _node.Enabled = enabled; return this; }

        public ControlBuilder OnClick(Action<RenderNode> action) { _node.OnClickAction = action; return this; }
        public ControlBuilder OnHover(Action<RenderNode> action) { _node.OnHoverAction = action; return this; }


        // ==========================================
        // 🔥 ATAJOS DIRECTOS (SIMPLIFICACIÓN MÁXIMA)
        // ==========================================

        // Atajo: Content simple
        public ControlBuilder Content(string text, string hexColor = "#000000", float fontSize = 12f)
        {
            var ct = _node.Content;
            ct.Text = text ?? string.Empty;
            try { ct.TextColor = ColorTranslator.FromHtml(hexColor); } catch { ct.TextColor = Color.Black; }
            ct.FontSize = Math.Max(1f, fontSize);
            _node.Content = ct;
            return this;
        }

        // Atajo: Background sólido
        public ControlBuilder Background(string hex)
        {
            var bg = _node.Background;
            try { bg.Color1 = ColorTranslator.FromHtml(hex); } catch { bg.Color1 = Color.Transparent; }
            bg.IsGradient = false;
            _node.Background = bg;
            return this;
        }

        // Atajo: Shadow por nivel de elevación
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
            if (elevation > 0) sh.Color = Color.FromArgb(30, 0, 0, 0); // Color sutil por defecto
            _node.Shadow = sh;
            return this;
        }

        // Atajo: Border simple
        public ControlBuilder Border(string hexColor, float width = 1f)
        {
            var bd = _node.Border;
            bd.Thickness = new ModernThickness(Math.Max(0f, width));
            try { bd.NormalColor = ColorTranslator.FromHtml(hexColor); } catch { bd.NormalColor = Color.Transparent; }
            _node.Border = bd;
            return this;
        }

        // Atajos: Estados (Hover y Press)
        public ControlBuilder HoverBackground(string hex)
        {
            var hover = _node.HoverState;
            try { hover.Background = new BackgroundData { Color1 = ColorTranslator.FromHtml(hex), IsGradient = false }; } catch { }
            _node.HoverState = hover;
            return this;
        }

        public ControlBuilder PressScale(float scale)
        {
            var press = _node.PressState;
            press.Scale = scale;
            _node.PressState = press;
            return this;
        }
        public ControlBuilder PressScale(double scale) => PressScale((float)scale);


        // ==========================================
        // 🔥 SUB-BUILDERS CON PATRÓN LAMBDA (Avanzados)
        // ==========================================

        public ControlBuilder Background(Action<BackgroundBuilder> configure) { configure(new BackgroundBuilder(_node)); return this; }
        public ControlBuilder Shadow(Action<ShadowBuilder> configure) { configure(new ShadowBuilder(_node)); return this; }
        public ControlBuilder Content(Action<ContentBuilder> configure) { configure(new ContentBuilder(_node)); return this; }
        public ControlBuilder Border(Action<BorderBuilder> configure) { configure(new BorderBuilder(_node)); return this; }
        public ControlBuilder Acrylic(Action<AcrylicBuilder> configure) { configure(new AcrylicBuilder(_node)); return this; }
        public ControlBuilder Ripple(Action<RippleBuilder> configure) { configure(new RippleBuilder(_node)); return this; }
        public ControlBuilder Filters(Action<FilterBuilder> configure) { configure(new FilterBuilder(_node)); return this; }

        // 🔥 INYECCIÓN PRO: StateHover y StatePress funcionan correctamente con structs.
        // Crean una copia del estado, permiten modificarla, y la reasignan al nodo.
        public ControlBuilder StateHover(Action<StateBuilder> configure)
        {
            var state = _node.HoverState;            // copia del struct
            var builder = new StateBuilder(state);
            configure(builder);
            _node.HoverState = builder.GetState();   // reasignar struct modificado
            return this;
        }

        public ControlBuilder StatePress(Action<StateBuilder> configure)
        {
            var state = _node.PressState;
            var builder = new StateBuilder(state);
            configure(builder);
            _node.PressState = builder.GetState();
            return this;
        }

        public RenderNode Apply()
        {
            OnApplied?.Invoke(_node);
            return _node;
        }

        // ==========================================
        // CLASES DE SUB-BUILDERS
        // ==========================================

        public sealed class FilterBuilder
        {
            private readonly RenderNode _node;
            internal FilterBuilder(RenderNode node) { _node = node; }

            public FilterBuilder Blur(float radius) { var f = _node.Filters; f.Blur = Math.Max(0, radius); _node.Filters = f; return this; }
            public FilterBuilder Blur(int radius) => Blur((float)radius);

            public FilterBuilder Brightness(float amt) { var f = _node.Filters; f.Brightness = Math.Max(0, amt); _node.Filters = f; return this; }
            public FilterBuilder Brightness(double amt) => Brightness((float)amt);

            public FilterBuilder Grayscale(float amt) { var f = _node.Filters; f.Grayscale = Math.Max(0f, Math.Min(1f, amt)); _node.Filters = f; return this; }
            public FilterBuilder Grayscale(double amt) => Grayscale((float)amt);
        }

        public sealed class StateBuilder
        {
            private VisualStateOverrides _state; // 🔥 campo no readonly para modificar el struct

            internal StateBuilder(VisualStateOverrides state)
            {
                _state = state;
            }

            public StateBuilder Background(string hex)
            {
                try { _state.Background = new BackgroundData { Color1 = ColorTranslator.FromHtml(hex), IsGradient = false }; } catch { }
                return this;
            }

            public StateBuilder Border(string hex, float width = 1)
            {
                try { _state.Border = new BorderData { NormalColor = ColorTranslator.FromHtml(hex), Thickness = new ModernThickness(width), Style = BorderStyle.Solid }; } catch { }
                return this;
            }
            public StateBuilder Border(string hex, int width) => Border(hex, (float)width);

            public StateBuilder Scale(float scale) { _state.Scale = scale; return this; }
            public StateBuilder Scale(double scale) => Scale((float)scale);

            public StateBuilder Opacity(float opacity) { _state.Opacity = opacity; return this; }

            public StateBuilder Opacity(double opacity) => Opacity((float)opacity);

            // 🔥 INYECCIÓN PRO: Permite recuperar el struct modificado
            internal VisualStateOverrides GetState() => _state;
        }

        public sealed class BackgroundBuilder
        {
            private readonly RenderNode _node;
            internal BackgroundBuilder(RenderNode node) { _node = node; }

            public BackgroundBuilder Solid(string hex) { var bg = _node.Background; try { bg.Color1 = ColorTranslator.FromHtml(hex); } catch { bg.Color1 = Color.Transparent; } bg.IsGradient = false; _node.Background = bg; return this; }
            public BackgroundBuilder LinearGradient(string hex1, string hex2, float angle = 0f) { var bg = _node.Background; try { bg.Color1 = ColorTranslator.FromHtml(hex1); bg.Color2 = ColorTranslator.FromHtml(hex2); } catch { } bg.GradientAngle = angle; bg.IsGradient = true; _node.Background = bg; return this; }
            public BackgroundBuilder LinearGradient(string hex1, string hex2, int angle) => LinearGradient(hex1, hex2, (float)angle);
            public BackgroundBuilder HoverColor(string hex) { var bg = _node.Background; try { bg.HoverColor = ColorTranslator.FromHtml(hex); } catch { } _node.Background = bg; return this; }
            public BackgroundBuilder PressColor(string hex) { var bg = _node.Background; try { bg.PressColor = ColorTranslator.FromHtml(hex); } catch { } _node.Background = bg; return this; }
        }

        public sealed class ShadowBuilder
        {
            private readonly RenderNode _node;
            internal ShadowBuilder(RenderNode node) { _node = node; }

            public ShadowBuilder Color(string hex) { var sh = _node.Shadow; try { sh.Color = ColorTranslator.FromHtml(hex); } catch { } _node.Shadow = sh; return this; }

            public ShadowBuilder Radius(float radius) { var sh = _node.Shadow; sh.Radius = Math.Max(0f, radius); _node.Shadow = sh; return this; }
            public ShadowBuilder Radius(int radius) => Radius((float)radius);

            public ShadowBuilder Offset(float x, float y) { var sh = _node.Shadow; sh.OffsetX = x; sh.OffsetY = y; _node.Shadow = sh; return this; }
            public ShadowBuilder Offset(int x, int y) => Offset((float)x, (float)y);
        }

        public sealed class ContentBuilder
        {
            private readonly RenderNode _node;
            internal ContentBuilder(RenderNode node) { _node = node; }

            public ContentBuilder Text(string text) { var ct = _node.Content; ct.Text = text ?? string.Empty; _node.Content = ct; return this; }

            public ContentBuilder FontSize(float size) { var ct = _node.Content; ct.FontSize = Math.Max(1f, size); _node.Content = ct; return this; }
            public ContentBuilder FontSize(int size) => FontSize((float)size);

            public ContentBuilder Color(string hex) { var ct = _node.Content; try { ct.TextColor = ColorTranslator.FromHtml(hex); } catch { ct.TextColor = System.Drawing.Color.Black; } _node.Content = ct; return this; }
            public ContentBuilder Bold(bool isBold = true) { var ct = _node.Content; ct.IsBold = isBold; _node.Content = ct; return this; }

            public ContentBuilder Align(StringAlignment horizontal, StringAlignment vertical = StringAlignment.Center) { var ct = _node.Content; ct.HorizontalAlignment = horizontal; ct.VerticalAlignment = vertical; _node.Content = ct; return this; }
            public ContentBuilder Wrap(bool wrap = true, bool trimming = true) { var ct = _node.Content; ct.WordWrap = wrap; ct.Trimming = trimming; _node.Content = ct; return this; }

            public ContentBuilder Image(Image img, float opacity = 1.0f) { var ct = _node.Content; ct.Image = img; ct.ImageOpacity = Math.Max(0f, Math.Min(1f, opacity)); _node.Content = ct; return this; }
            public ContentBuilder Image(Image img, double opacity) => Image(img, (float)opacity);
        }

        public sealed class BorderBuilder
        {
            private readonly RenderNode _node;
            internal BorderBuilder(RenderNode node) { _node = node; }

            public BorderBuilder Thickness(float all) { var bd = _node.Border; bd.Thickness = new ModernThickness(Math.Max(0f, all)); _node.Border = bd; return this; }
            public BorderBuilder Thickness(int all) => Thickness((float)all);

            public BorderBuilder Thickness(float vertical, float horizontal) { var bd = _node.Border; bd.Thickness = new ModernThickness(Math.Max(0f, vertical), Math.Max(0f, horizontal)); _node.Border = bd; return this; }
            public BorderBuilder Thickness(int vertical, int horizontal) => Thickness((float)vertical, (float)horizontal);

            public BorderBuilder Thickness(float top, float right, float bottom, float left) { var bd = _node.Border; bd.Thickness = new ModernThickness(Math.Max(0f, top), Math.Max(0f, right), Math.Max(0f, bottom), Math.Max(0f, left)); _node.Border = bd; return this; }
            public BorderBuilder Thickness(int top, int right, int bottom, int left) => Thickness((float)top, (float)right, (float)bottom, (float)left);

            public BorderBuilder Width(float all) => Thickness(all);
            public BorderBuilder Width(int all) => Thickness((float)all);

            public BorderBuilder Color(string hex) { var bd = _node.Border; try { bd.NormalColor = ColorTranslator.FromHtml(hex); } catch { } _node.Border = bd; return this; }
            public BorderBuilder FocusColor(string hex) { var bd = _node.Border; try { bd.FocusColor = ColorTranslator.FromHtml(hex); } catch { } _node.Border = bd; return this; }
        }

        public sealed class AcrylicBuilder
        {
            private readonly RenderNode _node;
            internal AcrylicBuilder(RenderNode node) { _node = node; }

            public AcrylicBuilder Enable(bool enabled = true) { var ac = _node.Acrylic; ac.IsEnabled = enabled; _node.Acrylic = ac; return this; }
            public AcrylicBuilder Tint(string hex) { var ac = _node.Acrylic; try { ac.TintColor = ColorTranslator.FromHtml(hex); } catch { } _node.Acrylic = ac; return this; }

            public AcrylicBuilder Blur(int radius) { var ac = _node.Acrylic; ac.BlurRadius = Math.Max(0, radius); _node.Acrylic = ac; return this; }

            public AcrylicBuilder Downsample(int ds) { var ac = _node.Acrylic; ac.Downsample = Math.Max(1, Math.Min(8, ds)); _node.Acrylic = ac; return this; }
            public AcrylicBuilder Quality(AcrylicQuality q) { var ac = _node.Acrylic; ac.Quality = q; _node.Acrylic = ac; return this; }
        }

        public sealed class RippleBuilder
        {
            private readonly RenderNode _node;
            internal RippleBuilder(RenderNode node) { _node = node; }

            public RippleBuilder Color(string hex) { var rp = _node.Ripple; try { rp.Color = ColorTranslator.FromHtml(hex); } catch { } _node.Ripple = rp; return this; }

            public RippleBuilder Radius(float r) { var rp = _node.Ripple; rp.Radius = Math.Max(0f, r); _node.Ripple = rp; return this; }
            public RippleBuilder Radius(int r) => Radius((float)r);

            public RippleBuilder Opacity(float o) { var rp = _node.Ripple; rp.Opacity = Math.Max(0f, Math.Min(1f, o)); _node.Ripple = rp; return this; }
            public RippleBuilder Opacity(double o) => Opacity((float)o);
        }
        // Pon esto junto a tus otros métodos rápidos como "Opacity" o "Visible"
        public ControlBuilder Easing(AnimationEasing ease)
        {
            _node.Easing = ease;
            return this;
        }
    }
}