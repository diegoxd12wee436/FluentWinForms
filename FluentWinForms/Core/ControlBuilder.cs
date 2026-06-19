#nullable enable
using Svg.Skia;
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
        // 🛡️ Mantiene el ToolTip enlazado al control sin usar el "Tag" y evita fugas de memoria
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<T, ToolTip> _tooltips = new();
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

        // ═════════════════════════════════════════════════════════════════════
        // 1. LAYOUT
        // ═════════════════════════════════════════════════════════════════════

        public ControlBuilder<T> Layout(int x, int y, int width, int height)
            => Layout((double)x, (double)y, (double)width, (double)height);
        public ControlBuilder<T> Layout(double x, double y, double width, double height)
        {
            _node.Layout = new RectangleF((float)x, (float)y, (float)Math.Max(0, width), (float)Math.Max(0, height));
            _node.StretchX = false;
            _node.StretchY = false;
            return this;
        }
        // ═════════════════════════════════════════════════════════════════════
        // 🔥 TRANSFORMACIONES (ATAJO DEV-FRIENDLY CSS)
        // ═════════════════════════════════════════════════════════════════════
        public ControlBuilder<T> Translate(double x, double y)
        {
            if (_hostControl != null)
            {
                // Root: mueve el HWND físico → dispara UpdatePhysicalBounds
                _hostControl.TranslateX = (float)x;
                _hostControl.TranslateY = (float)y;
            }
            else
            {
                // Child node: traslación pura en Skia, sin tocar WinForms
                _node.TranslateX = (float)x;
                _node.TranslateY = (float)y;
            }
            return this;
        }

        public ControlBuilder<T> Width(double w)
        { _node.Layout = new RectangleF(_node.Layout.X, _node.Layout.Y, (float)Math.Max(0, w), _node.Layout.Height); _node.StretchX = false; return this; }

        public ControlBuilder<T> Height(double h)
        { _node.Layout = new RectangleF(_node.Layout.X, _node.Layout.Y, _node.Layout.Width, (float)Math.Max(0, h)); _node.StretchY = false; return this; }

        public ControlBuilder<T> Size(double width, double height)
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
        public ControlBuilder<T> VStack(int spacing = 0) => VStack((double)spacing);
        public ControlBuilder<T> VStack(double spacing = 0, Align align = Align.Start, Justify justify = Justify.Start)
        { _node.LayoutMode = LayoutStyle.VerticalStack; _node.Spacing = (float)spacing; _node.AlignItems = align; _node.JustifyContent = justify; return this; }
        public ControlBuilder<T> AlignChildren(Align align) { _node.AlignItems = align; return this; }
        public ControlBuilder<T> JustifyContent(Justify justify) { _node.JustifyContent = justify; return this; }

        public ControlBuilder<T> HStack(int spacing = 0) => HStack((double)spacing);
        public ControlBuilder<T> HStack(double spacing = 0, Align align = Align.Start, Justify justify = Justify.Start)
        { _node.LayoutMode = LayoutStyle.HorizontalStack; _node.Spacing = (float)spacing; _node.AlignItems = align; _node.JustifyContent = justify; return this; }

        public ControlBuilder<T> Grid(int minColWidth, int gap = 0) => Grid((double)minColWidth, (double)gap);
        public ControlBuilder<T> Grid(double minColWidth, double gap = 0)
        { _node.LayoutMode = LayoutStyle.AutoFitGrid; _node.GridMinColumnWidth = (float)Math.Max(1, minColWidth); _node.Spacing = (float)gap; return this; }

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

        // ═════════════════════════════════════════════════════════════════════
        // 2. ESPACIADO Y FORMA (100% ESTILO CSS CON DOUBLE)
        // ═════════════════════════════════════════════════════════════════════

        // ── PADDING (Espacio interno) ──

        public ControlBuilder<T> Padding(double all)
        { _node.Padding = new ModernPadding((float)Math.Max(0, all)); return this; }

        public ControlBuilder<T> Padding(double vertical, double horizontal)
        {
            // Constructor de ModernPadding asume: Left, Top, Right, Bottom
            _node.Padding = new ModernPadding((float)Math.Max(0, horizontal), (float)Math.Max(0, vertical), (float)Math.Max(0, horizontal), (float)Math.Max(0, vertical));
            return this;
        }

        public ControlBuilder<T> Padding(double top, double right, double bottom, double left) // ⏱️ EL RELOJ CSS
        {
            // Constructor de ModernPadding asume: Left, Top, Right, Bottom
            _node.Padding = new ModernPadding((float)Math.Max(0, left), (float)Math.Max(0, top), (float)Math.Max(0, right), (float)Math.Max(0, bottom));
            return this;
        }

        // ── MARGIN (Espacio externo) ──

        public ControlBuilder<T> Margin(double all)
        { _node.Margin = new ModernThickness((float)Math.Max(0, all)); return this; }

        public ControlBuilder<T> Margin(double vertical, double horizontal)
        {
            // Constructor de ModernThickness asume: Top, Right, Bottom, Left
            _node.Margin = new ModernThickness((float)Math.Max(0, vertical), (float)Math.Max(0, horizontal), (float)Math.Max(0, vertical), (float)Math.Max(0, horizontal));
            return this;
        }

        public ControlBuilder<T> Margin(double top, double right, double bottom, double left) // ⏱️ EL RELOJ CSS
        {
            // Constructor de ModernThickness asume: Top, Right, Bottom, Left
            _node.Margin = new ModernThickness((float)Math.Max(0, top), (float)Math.Max(0, right), (float)Math.Max(0, bottom), (float)Math.Max(0, left));
            return this;
        }

        // ── BORDES (Radio de las esquinas) ──

        public ControlBuilder<T> BorderRadius(double r)
        { _node.Corners = new CornerRadii((float)Math.Max(0, r)); return this; }

        public ControlBuilder<T> BorderRadius(double tl, double tr, double br, double bl) // ⏱️ EL RELOJ CSS (Esquinas)
        {
            // tl = TopLeft, tr = TopRight, br = BottomRight, bl = BottomLeft
            _node.Corners = new CornerRadii((float)Math.Max(0, tl), (float)Math.Max(0, tr), (float)Math.Max(0, br), (float)Math.Max(0, bl));
            return this;
        }
        public ControlBuilder<T> BorderRadius(int tl, int tr, int br, int bl) => BorderRadius((double)tl, (double)tr, (double)br, (double)bl);

        // ═════════════════════════════════════════════════════════════════════
        // 3. VISIBILIDAD, ANIMACIÓN Y EVENTOS
        // ═════════════════════════════════════════════════════════════════════

        public ControlBuilder<T> Opacity(double o)
        { _node.Opacity = (float)Math.Max(0.0, Math.Min(1.0, o)); return this; }
        public ControlBuilder<T> Visible(bool v = true) { _node.IsVisible = v; return this; }
        public ControlBuilder<T> Enabled(bool e = true) { _node.Enabled = e; return this; }
        public ControlBuilder<T> UseSkia(bool enabled = true)
        {
            if (_hostControl != null) _hostControl.UseSkiaGraphics = enabled;
            return this;
        }
        public ControlBuilder<T> Cursor(Cursor cursor)
        { if (_hostControl != null) _hostControl.Cursor = cursor; return this; }
        public ControlBuilder<T> HandCursor() => Cursor(Cursors.Hand);
        
        public ControlBuilder<T> Tooltip(string text, int autoPopMs = 5000, int delayMs = 500)
        {
            if (_hostControl == null) return this;

            Action setTooltip = () =>
            {
                // 1. Si ya existe, lo REUTILIZAMOS. Así evitamos crear múltiples eventos Disposed
                if (_tooltips.TryGetValue(_hostControl, out var existing))
                {
                    existing.AutoPopDelay = autoPopMs;
                    existing.InitialDelay = delayMs;
                    existing.SetToolTip(_hostControl, text);
                    return; // 🚀 Salimos aquí para no suscribir eventos duplicados
                }

                // 2. Si no existe, creamos el dedicado
                var newTooltip = new ToolTip
                {
                    AutoPopDelay = autoPopMs,
                    InitialDelay = delayMs,
                    ReshowDelay = 200,
                    ShowAlways = true
                };
                newTooltip.SetToolTip(_hostControl, text);

                _tooltips.Add(_hostControl, newTooltip);

                // 3. Suscripción UNA SOLA VEZ a Disposed para limpiar memoria nativa
                _hostControl.Disposed += (s, e) =>
                {
                    if (_tooltips.TryGetValue(_hostControl, out var tt))
                    {
                        tt.Dispose();
                        _tooltips.Remove(_hostControl);
                    }
                };
            };

            if (!_hostControl.IsHandleCreated) setTooltip();
            else if (_hostControl.InvokeRequired) _hostControl.Invoke(setTooltip);
            else setTooltip();

            return this;
        }

        public ControlBuilder<T> Animate(AnimationEasing easing)
        { _node.Easing = easing; return this; }
        public ControlBuilder<T> AnimationSpeed(int ms) => AnimationSpeed((double)ms);
        public ControlBuilder<T> AnimationSpeed(double ms)
        {
            if (_hostControl != null) _hostControl.AnimationSpeed = (float)Math.Max(10.0, ms);
            return this;
        }
        public ControlBuilder<T> AnimateSpring(int ms = 150) => Animate(AnimationEasing.Spring).AnimationSpeed(ms);
        public ControlBuilder<T> AnimateEase(int ms = 150) => Animate(AnimationEasing.EaseInOut).AnimationSpeed(ms);

        public ControlBuilder<T> OnClick(Action a) { _node.OnClickAction = _ => a(); return this; }
        public ControlBuilder<T> OnClick(Action<RenderNode> a) { _node.OnClickAction = a; return this; }
        public ControlBuilder<T> OnHoverEnter(Action a) { _node.OnHoverEnterAction = _ => a(); return this; }
        public ControlBuilder<T> OnHoverEnter(Action<RenderNode> a) { _node.OnHoverEnterAction = a; return this; }
        public ControlBuilder<T> OnHoverLeave(Action a) { _node.OnHoverLeaveAction = _ => a(); return this; }
        public ControlBuilder<T> OnHoverLeave(Action<RenderNode> a) { _node.OnHoverLeaveAction = a; return this; }

        public ControlBuilder<T> Ripple(string hexColor = "#40000000", double opacity = 1.0)
        {
            var rp = _node.Ripple;
            try { rp.Color = ParseHex(hexColor); } catch { }
            rp.Opacity = (float)opacity;
            _node.Ripple = rp;
            return this;
        }
        /// <summary>
        /// Aplica el efecto animado Sweep (Círculo expansivo) en Hover.
        /// </summary>
        public ControlBuilder<T> SweepHover(string themeHex = "#0077ff", string textHoverHex = "#FFFFFF")
        {
            var swp = _node.Sweep;
            swp.IsEnabled = true;

            // Usamos tu método interno ParseHex para convertir los strings a colores
            swp.ThemeColor = ParseHex(themeHex);
            swp.TextHoverColor = ParseHex(textHoverHex);

            _node.Sweep = swp;
            return this;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 4. CONTENIDO Y TEXTO
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// EN: Loads and caches a vector icon from SVG markup. Stays sharp at any size/DPI.
        /// ES: Carga y cachea un ícono vectorial desde código SVG. Se ve nítido en cualquier tamaño/DPI.
        /// </summary>
        public ControlBuilder<T> IconSvg(string svgXml, double width = 24, double height = 24, string? color = null)
        {
            _node.ClearSvg();
            using var svg = new SKSvg();
            using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgXml));
            var picture = svg.Load(stream);

            if (picture != null)
            {
                _node.SvgPicture = picture;
                _node.SvgSize = new SizeF((float)width, (float)height);
                if (!string.IsNullOrWhiteSpace(color)) _node.SvgTintColor = ParseHex(color!);
            }
            return this;
        }
        public ControlBuilder<T> IconPosition(IconAlign align, double gap = 8)
        { _node.IconPosition = align; _node.IconGap = (float)gap; return this; }

        /// <summary>
        /// EN: Loads a vector icon from an .svg file on disk.
        /// ES: Carga un ícono vectorial desde un archivo .svg en disco.
        /// </summary>
        public ControlBuilder<T> IconSvgFile(string filePath, double width = 24, double height = 24, string? color = null)
        {
            _node.ClearSvg();
            using var svg = new SKSvg();
            var picture = svg.Load(filePath);

            if (picture != null)
            {
                _node.SvgPicture = picture;
                _node.SvgSize = new SizeF((float)width, (float)height);
                if (!string.IsNullOrWhiteSpace(color)) _node.SvgTintColor = ParseHex(color!);
            }
            return this;
        }

        public ControlBuilder<T> Content(string text, string? hexColor = null, double? fontSize = null)
        {
            var ct = _node.Content;
            ct.Text = text ?? string.Empty;
            if (fontSize.HasValue) ct.FontSize = (float)Math.Max(1.0, fontSize.Value);
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
            => Font(family, (double)size, bold, italic);
        public ControlBuilder<T> Font(string family, double size, bool bold = false, bool italic = false)
        {
            var ct = _node.Content;
            if (!string.IsNullOrWhiteSpace(family)) ct.FontFamily = family;
            ct.FontSize = (float)Math.Max(1.0, size);
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
        public ControlBuilder<T> Bold(bool value = true)
        { var ct = _node.Content; ct.IsBold = value; _node.Content = ct; return this; }
        public ControlBuilder<T> Italic(bool value = true)
        { var ct = _node.Content; ct.IsItalic = value; _node.Content = ct; return this; }
        public ControlBuilder<T> FontSize(double size)
        { var ct = _node.Content; ct.FontSize = (float)Math.Max(1.0, size); _node.Content = ct; return this; }
        public ControlBuilder<T> FontSize(int size) => FontSize((double)size);

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

        // ═════════════════════════════════════════════════════════════════════
        // 5. FONDO Y BORDE
        // ═════════════════════════════════════════════════════════════════════

        //Themes
        // ── TOKENS DE TEMA ──────────────────────────────────────────
        public ControlBuilder<T> Background(Color color)
        { var bg = _node.Background; bg.Color1 = color; bg.IsGradient = false; _node.Background = bg; return this; }

        public ControlBuilder<T> Primary() => Background(AppTheme.Primary);
        public ControlBuilder<T> Surface() => Background(AppTheme.Surface);
        public ControlBuilder<T> SurfaceAlt() => Background(AppTheme.SurfaceAlt);
        public ControlBuilder<T> Background(string hex)
        {
            var bg = _node.Background;
            bg.Color1 = ParseHex(hex);
            bg.IsGradient = false;
            _node.Background = bg;
            return this;
        }
       
        public ControlBuilder<T> ThemeAware(Action<ControlBuilder<T>> reconfigure)
        {
            if (_hostControl == null) return this;
            _hostControl.WatchTheme(() =>
            {
                reconfigure(new ControlBuilder<T>(_node, _hostControl));
                _hostControl.Invalidate();
            });
            return this;
        }
        public ControlBuilder<T> Gradient(string hexFrom, string hexTo, int angle = 0) => Gradient(hexFrom, hexTo, (double)angle);
        public ControlBuilder<T> Gradient(string hexFrom, string hexTo, double angle = 0)
        {
            var bg = _node.Background;
            bg.Color1 = ParseHex(hexFrom);
            bg.Color2 = ParseHex(hexTo);
            bg.IsGradient = true;
            bg.GradientAngle = (float)angle;
            _node.Background = bg;
            return this;
        }

        public ControlBuilder<T> Border(string hex, int width = 1) => Border(hex, (double)width);
        public ControlBuilder<T> Border(string hex, double width = 1)
        {
            var bd = _node.Border;
            bd.Thickness = new ModernThickness((float)Math.Max(0, width));
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
            => Shadow(hex, (double)offsetX, (double)offsetY, (double)blur);
        public ControlBuilder<T> Shadow(string hex, double offsetX = 0, double offsetY = 4, double blur = 8)
        {
            var sh = _node.Shadow;
            sh.Color = ParseHex(hex);
            sh.OffsetX = (float)offsetX;
            sh.OffsetY = (float)offsetY;
            sh.Radius = (float)Math.Max(0, blur);
            _node.Shadow = sh;
            return this;
        }
        public ControlBuilder<T> Glow(string hex, double radius = 12) => Shadow(hex, 0, 0, radius);
        // ═════════════════════════════════════════════════════════════════════
        // 5b. FILTROS COMO CSS
        // ═════════════════════════════════════════════════════════════════════
        public ControlBuilder<T> Filter(double brightness = 1, double contrast = 1,
                                         double grayscale = 0, double blur = 0)
        {
            var f = _node.Filters;
            f.Brightness = (float)brightness;
            f.Contrast = (float)contrast;
            f.Grayscale = (float)grayscale;
            f.Blur = (float)blur;
            _node.Filters = f;
            return this;
        }
        public ControlBuilder<T> Blur(double radius) => Filter(blur: radius);
        public ControlBuilder<T> Grayscale(double amount = 1) => Filter(grayscale: amount);
        public ControlBuilder<T> Dim(double amount = 0.6) => Filter(brightness: amount);
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
        public ControlBuilder<T> Badge(string text, string bg = "#E53935", string textColor = "#FFFFFF", double size = 18)
        {
            var b = _node.Badge;
            b.IsVisible = true;
            b.Text = text;
            b.Size = size;
            try { b.Background = ParseHex(bg); } catch { }
            try { b.TextColor = ParseHex(textColor); } catch { }
            _node.Badge = b;
            return this;
        }
        public ControlBuilder<T> Badge(string bg = "#E53935", double size = 10)
        {
            var b = _node.Badge;
            b.IsVisible = true;
            b.Text = string.Empty;
            b.Size = size;
            try { b.Background = ParseHex(bg); } catch { }
            _node.Badge = b;
            return this;
        }
        public ControlBuilder<T> BadgeOffset(double x, double y)
        {
            var b = _node.Badge;
            b.OffsetX = x;
            b.OffsetY = y;
            _node.Badge = b;
            return this;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 6. ESTADOS HOVER Y PRESS
        // ═════════════════════════════════════════════════════════════════════

        public ControlBuilder<T> StateHover(Action<StateBuilder> cfg)
        {
            var sb = new StateBuilder(_node.HoverState);
            cfg(sb);
            _node.HoverState = sb.Build();
            return this;
        }
        public ControlBuilder<T> StateDisabled(Action<StateBuilder> cfg)
        {
            var sb = new StateBuilder(_node.DisabledState);
            cfg(sb);
            _node.DisabledState = sb.Build();
            _node.Enabled = false;
            return this;
        }
        public ControlBuilder<T> Disabled(float opacity = 0.4f, string? textColor = null, string? bg = null)
        {
            return StateDisabled(s => {
                s.Opacity(opacity);
                if (!string.IsNullOrWhiteSpace(textColor)) s.TextColor(textColor!);
                if (!string.IsNullOrWhiteSpace(bg)) s.Background(bg!);
            });
        }
        public ControlBuilder<T> StatePress(Action<StateBuilder> cfg)
        {
            var sb = new StateBuilder(_node.PressState);
            cfg(sb);
            _node.PressState = sb.Build();
            return this;
        }

        public ControlBuilder<T> Hover(string? bg = null, double? scale = null, double? opacity = null,
                                string? border = null, string? shadow = null, string? textColor = null, string? iconColor = null)
        {
            return StateHover(s => {
                if (!string.IsNullOrWhiteSpace(bg)) s.Background(bg);
                if (scale.HasValue) s.Scale(scale.Value);
                if (opacity.HasValue) s.Opacity(opacity.Value);
                if (!string.IsNullOrWhiteSpace(border)) s.Border(border);
                if (!string.IsNullOrWhiteSpace(shadow)) s.Shadow(shadow);
                if (!string.IsNullOrWhiteSpace(textColor)) s.TextColor(textColor);
                if (!string.IsNullOrWhiteSpace(iconColor)) s.IconColor(iconColor); // 🆕
            });
        }

        public ControlBuilder<T> Press(string? bg = null, double? scale = null, double? opacity = null,
                                        string? border = null, string? shadow = null, string? textColor = null, string? iconColor = null)
        {
            return StatePress(s => {
                if (!string.IsNullOrWhiteSpace(bg)) s.Background(bg);
                if (scale.HasValue) s.Scale(scale.Value);
                if (opacity.HasValue) s.Opacity(opacity.Value);
                if (!string.IsNullOrWhiteSpace(border)) s.Border(border);
                if (!string.IsNullOrWhiteSpace(shadow)) s.Shadow(shadow);
                if (!string.IsNullOrWhiteSpace(textColor)) s.TextColor(textColor);
                if (!string.IsNullOrWhiteSpace(iconColor)) s.IconColor(iconColor); // 🆕
            });
        }
        // ═════════════════════════════════════════════════════════════════════
        // 7. UTILIDADES
        // ═════════════════════════════════════════════════════════════════════

        public ControlBuilder<T> When(
            bool condition,
            string? bg = null,
            string? textColor = null,
            string? border = null,
            string? shadow = null,
            string? glow = null,
            double? scale = null,
            double? opacity = null,
            double? dim = null,
            double? grayscale = null,
            double? blur = null,
            double? borderRadius = null, // 🆕 cambiar radio condicionalmente es muy común
            bool? bold = null,   // 🆕 estado activo/seleccionado suele poner bold
            bool? visible = null)   // 🆕 visibilidad condicional sin romper la cadena
        {
            if (!condition) return this;
            if (!string.IsNullOrWhiteSpace(bg)) Background(bg!);
            if (!string.IsNullOrWhiteSpace(textColor)) TextColor(textColor!);
            if (!string.IsNullOrWhiteSpace(border)) Border(border!, 1f);
            if (!string.IsNullOrWhiteSpace(shadow)) Shadow(shadow!, 0f, 4f, 8f);
            if (!string.IsNullOrWhiteSpace(glow)) Glow(glow!);
            if (scale.HasValue) Scale(scale.Value);
            if (opacity.HasValue) Opacity(opacity.Value);
            if (dim.HasValue) Dim(dim.Value);
            if (grayscale.HasValue) Grayscale(grayscale.Value);
            if (blur.HasValue) Blur(blur.Value);
            if (borderRadius.HasValue) BorderRadius(borderRadius.Value); // 🆕
            if (bold.HasValue) Bold(bold.Value); // 🆕
            if (visible.HasValue) Visible(visible.Value); // 🆕
            return this;
        }
        public ControlBuilder<T> When(bool condition, Action<ControlBuilder<T>> cfg)
        { if (condition) cfg(this); return this; }
        // ═════════════════════════════════════════════════════════════════════
        // 7. APPLY — Normalito y Tuani
        // ═════════════════════════════════════════════════════════════════════

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
            public StateBuilder Scale(double scale) { _s.Scale = (float)scale; return this; }
            public StateBuilder Opacity(double opacity) { _s.Opacity = (float)Math.Max(0.0, Math.Min(1.0, opacity)); return this; }
            public StateBuilder TextColor(string hex) { try { _s.TextColor = ParseHex(hex); } catch { } return this; }
            public StateBuilder TextColor(Color color) { _s.TextColor = color; return this; }
            public StateBuilder IconColor(string hex) { try { _s.IconColor = ParseHex(hex); } catch { } return this; }
            public StateBuilder Translate(double x, double y) { _s.TranslateX = (float)x; _s.TranslateY = (float)y; return this; }
            internal VisualStateOverrides Build() => _s;
        }
    }
}
