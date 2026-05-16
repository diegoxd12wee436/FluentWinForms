#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms; // 🔥 INYECCIÓN: Necesario para manejar Control y Application

namespace FluentWinForms.Core
{
    public sealed class ControlBuilder
    {
        private readonly RenderNode _node;
        private readonly Control? _hostControl; // 🔥 INYECCIÓN: Variable para guardar el control real de WinForms
        internal Action<RenderNode>? OnApplied { get; set; }

        // 🔥 INYECCIÓN: Constructor actualizado para recibir el control host
        public ControlBuilder(RenderNode existingNode, Control? hostControl = null)
        {
            _node = existingNode ?? throw new ArgumentNullException(nameof(existingNode));
            _hostControl = hostControl;
        }

        public ControlBuilder(string id = "") { _node = new RenderNode { Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id }; }

        

        public ControlBuilder Layout(float x, float y, float width, float height) { _node.Layout = new RectangleF(x, y, Math.Max(0, width), Math.Max(0, height)); _node.StretchX = false; _node.StretchY = false; return this; }
        public ControlBuilder Layout(int x, int y, int width, int height) => Layout((float)x, (float)y, (float)width, (float)height);

        public ControlBuilder Width(float w) { _node.StretchX = false; _node.Layout = new RectangleF(_node.Layout.X, _node.Layout.Y, w, _node.Layout.Height); return this; }
        public ControlBuilder Width(int w) => Width((float)w);
        public ControlBuilder Height(float h) { _node.StretchY = false; _node.Layout = new RectangleF(_node.Layout.X, _node.Layout.Y, _node.Layout.Width, h); return this; }
        public ControlBuilder Height(int h) => Height((float)h);
        public ControlBuilder StretchWidth() { _node.StretchX = true; return this; }
        public ControlBuilder StretchHeight() { _node.StretchY = true; return this; }

        public ControlBuilder VStack(float spacing = 0f) { _node.LayoutMode = LayoutStyle.VerticalStack; _node.Spacing = spacing; return this; }
        public ControlBuilder VStack(int spacing) => VStack((float)spacing);
        public ControlBuilder HStack(float spacing = 0f) { _node.LayoutMode = LayoutStyle.HorizontalStack; _node.Spacing = spacing; return this; }
        public ControlBuilder HStack(int spacing) => HStack((float)spacing);
        public ControlBuilder Grid(float minColWidth, float gap = 0f) { _node.LayoutMode = LayoutStyle.AutoFitGrid; _node.GridMinColumnWidth = Math.Max(1f, minColWidth); _node.Spacing = gap; return this; }

        // 🔥 MAGIA FLUENT: Agrega múltiples hijos
        public ControlBuilder AddChildren(params Action<ControlBuilder>[] configurations)
        {
            foreach (var config in configurations) AddChild(config);
            return this;
        }

        public ControlBuilder AddChild(Action<ControlBuilder> configure)
        {
            var childBuilder = new ControlBuilder();
            configure(childBuilder);
            _node.Children.Add(childBuilder.GetNode()); // 🔥 INYECCIÓN: Se cambia Apply() por GetNode() para uso interno
            return this;
        }

        public ControlBuilder AddChild(RenderNode child) { if (child != null) _node.Children.Add(child); return this; }

        public ControlBuilder Padding(float all) { _node.Padding = new ModernPadding(Math.Max(0, all)); return this; }
        public ControlBuilder Padding(float left, float top, float right, float bottom) { _node.Padding = new ModernPadding(Math.Max(0, left), Math.Max(0, top), Math.Max(0, right), Math.Max(0, bottom)); return this; }
        public ControlBuilder CornerRadius(float uniform) { _node.Corners = new CornerRadii(Math.Max(0, uniform)); return this; }
        public ControlBuilder CornerRadius(float tl, float tr, float br, float bl) { _node.Corners = new CornerRadii(Math.Max(0, tl), Math.Max(0, tr), Math.Max(0, br), Math.Max(0, bl)); return this; }

        public ControlBuilder Opacity(float opacity) { _node.Opacity = Math.Max(0f, Math.Min(1f, opacity)); return this; }
        public ControlBuilder Visible(bool visible = true) { _node.IsVisible = visible; return this; }
        public ControlBuilder Enabled(bool enabled = true) { _node.Enabled = enabled; return this; }

        // 🔥 INTERACCIONES
        public ControlBuilder OnClick(Action<RenderNode> action) { _node.OnClickAction = action; return this; }
        public ControlBuilder OnHoverEnter(Action<RenderNode> action) { _node.OnHoverEnterAction = action; return this; }
        public ControlBuilder OnHoverLeave(Action<RenderNode> action) { _node.OnHoverLeaveAction = action; return this; }

        public ControlBuilder Content(string text, string hexColor = "#000000", float fontSize = 12f)
        {
            var ct = _node.Content;
            ct.Text = text ?? string.Empty;
            try { ct.TextColor = ColorTranslator.FromHtml(hexColor); } catch { ct.TextColor = Color.Black; }
            ct.FontSize = Math.Max(1f, fontSize);
            _node.Content = ct;
            return this;
        }

        public ControlBuilder Background(string hex)
        {
            var bg = _node.Background;
            try { bg.Color1 = ColorTranslator.FromHtml(hex); } catch { bg.Color1 = Color.Transparent; }
            bg.IsGradient = false;
            _node.Background = bg;
            return this;
        }

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
            if (elevation > 0) sh.Color = Color.FromArgb(30, 0, 0, 0);
            _node.Shadow = sh;
            return this;
        }

        // 🔥 INYECCIÓN 1A: Lector de colores con Transparencia (Alpha)
        private Color ParseAlphaHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Color.Transparent;
            hex = hex.Trim('#');
            try
            {
                if (hex.Length == 8) // Formato #AARRGGBB
                {
                    int a = Convert.ToInt32(hex.Substring(0, 2), 16);
                    int r = Convert.ToInt32(hex.Substring(2, 2), 16);
                    int g = Convert.ToInt32(hex.Substring(4, 2), 16);
                    int b = Convert.ToInt32(hex.Substring(6, 2), 16);
                    return Color.FromArgb(a, r, g, b);
                }
                return ColorTranslator.FromHtml("#" + hex);
            }
            catch { return Color.Transparent; }
        }

        // 🔥 INYECCIÓN 1B: Comando Glassmorphism blindado para STRUCTS
        public ControlBuilder Glass(string tintHexColor = "#80000000") // Negro al 50% por defecto
        {
            // Regla de C#: Copiamos el struct a una variable, lo modificamos y lo reasignamos.
            var ac = _node.Acrylic;
            ac.IsEnabled = true;
            ac.TintColor = ParseAlphaHex(tintHexColor);
            _node.Acrylic = ac;

            var bg = _node.Background;
            bg.Color1 = Color.Transparent;
            bg.IsGradient = false;
            _node.Background = bg;

            return this;
        }
        public ControlBuilder Border(string hexColor, float width = 1f)
        {
            var bd = _node.Border;
            bd.Thickness = new ModernThickness(Math.Max(0f, width));
            try { bd.NormalColor = ColorTranslator.FromHtml(hexColor); } catch { bd.NormalColor = Color.Transparent; }
            _node.Border = bd;
            return this;
        }

        public ControlBuilder StateHover(Action<StateBuilder> configure)
        {
            var state = _node.HoverState;
            var builder = new StateBuilder(state);
            configure(builder);
            _node.HoverState = builder.GetState();
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

        // 🔥 INYECCIÓN: Método interno para devolver el nodo sin intentar agregarlo a la ventana
        internal RenderNode GetNode()
        {
            OnApplied?.Invoke(_node);
            return _node;
        }

        // 🔥 INYECCIÓN: EL APPLY INTELIGENTE FINAL
        public ModernControlBase Apply(Control? parent = null)
        {
            OnApplied?.Invoke(_node);

            if (_hostControl == null)
                throw new InvalidOperationException("Este Builder no está asociado a un control. Usa GetNode() para nodos internos.");

            if (parent != null)
            {
                // El desarrollador eligió el contenedor: btn.Apply(panel1)
                parent.Controls.Add(_hostControl);
            }
            else
            {
                // El desarrollador lo dejó vacío: btn.Apply() -> Va al Formulario abierto por defecto
                if (Application.OpenForms.Count > 0)
                {
                    Application.OpenForms[0].Controls.Add(_hostControl);
                }
            }

            // Devuelve el objeto base real para poder guardarlo (var btn = ...)
            return (ModernControlBase)_hostControl;
        }

        public sealed class StateBuilder
        {
            private VisualStateOverrides _state;
            internal StateBuilder(VisualStateOverrides state) { _state = state; }
            public StateBuilder Background(string hex) { try { _state.Background = new BackgroundData { Color1 = ColorTranslator.FromHtml(hex), IsGradient = false }; } catch { } return this; }
            public StateBuilder Scale(float scale) { _state.Scale = scale; return this; }
            internal VisualStateOverrides GetState() => _state;
        }
    }
}