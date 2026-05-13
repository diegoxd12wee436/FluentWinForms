#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Collections.ObjectModel; // 🔥 INYECCIÓN: Para Reactividad del Árbol

namespace FluentWinForms.Core
{
    // ==========================================
    // ENUMS DE ESTILO (Comos CSS)
    // ==========================================
    public enum AnimationEasing
    {
        Linear, EaseInOut, EaseOutBack, Spring // 🔥 La nueva fisica (Animations)
    }
    public enum BorderStyle { Solid, Dashed, Dotted }          // Sólido, Discontinuo, Punteado
    public enum ImageFit { Fill, Contain, Cover, None }        // Llenar, Contener, Cubrir, Ninguno
    public enum TextDecoration { None, Underline, Strikethrough } // Ninguno, Subrayado, Tachado
    public enum LayoutStyle { Absolute, VerticalStack, HorizontalStack, AutoFitGrid }

    /// <summary>
    /// Nodo de render ultraligero y Zero-Allocation para el grafo de escena.
    /// Optimizado para 120 FPS. Los datos visuales son structs, evitando la presión sobre el Garbage Collector.
    /// / Ultra‑lightweight, Zero‑Allocation render node for the scene graph.
    /// Optimized for 120 FPS. Visual data is stored as structs to avoid GC pressure.
    /// </summary>
    [ToolboxItem(false)]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    [DebuggerDisplay("{Id,nq} ({Layout.Width}x{Layout.Height})")]
    public sealed class RenderNode
    {
        [Browsable(false)]
        public string Id { get; init; } = Guid.NewGuid().ToString("N");

        // 🔥 INYECCIÓN RESPONSIVE: Memoriza si el nodo debe estirarse dinámicamente
        // / Stores whether the node should stretch dynamically (like CSS 1fr)
        [Browsable(false)]
        public bool StretchX { get; set; } = false;

        [Browsable(false)]
        public bool StretchY { get; set; } = false;

        // --- Layout y Transformaciones --- // --- Layout & Transforms ---
        [Category("1. Layout")]
        [Description("Tamaño y posición absoluta (útil si no usa AutoLayout).\nAbsolute size and position (useful when not using AutoLayout).")]
        public RectangleF Layout { get; set; }

        [Category("1. Layout")]
        [Description("Espaciado interno del contenido.\nInternal content padding.")]
        [NotifyParentProperty(true)]
        public ModernPadding Padding { get; set; }

        [Category("1. Layout")]
        [Description("Radios de las esquinas para bordes redondeados.\nCorner radii for rounded borders.")]
        [NotifyParentProperty(true)]
        public CornerRadii Corners { get; set; }

        [Category("1. Layout")]
        [Description("Tamaño mínimo permitido para el nodo.\nMinimum allowed size for the node.")]
        [NotifyParentProperty(true)]
        public SizeF MinSize { get; set; } = new SizeF(0, 0);

        [Category("1. Layout")]
        [Description("Tamaño máximo permitido para el nodo (0 = sin límite).\nMaximum allowed size (0 = unlimited).")]
        [NotifyParentProperty(true)]
        public SizeF MaxSize { get; set; } = new SizeF(0, 0);

        [Category("2. Transformación")]
        [NotifyParentProperty(true)]
        public float ScaleX { get; set; } = 1.0f;

        [Category("2. Transformación")]
        [NotifyParentProperty(true)]
        public float ScaleY { get; set; } = 1.0f;

        [Category("2. Transformación")]
        [NotifyParentProperty(true)]
        public float Rotation { get; set; } = 0f;

        [Browsable(false)]
        public PointF TransformOrigin { get; set; } = new PointF(0.5f, 0.5f);

        [Category("3. Apariencia")]
        [Description("Opacidad del nodo (0..1).\nNode opacity (0..1).")]
        [NotifyParentProperty(true)]
        public float Opacity { get; set; } = 1.0f;

        [Category("3. Apariencia")]
        [Description("Indica si el nodo es visible.\nWhether the node is visible.")]
        [NotifyParentProperty(true)]
        public bool IsVisible { get; set; } = true;

        [Category("3. Apariencia")]
        [Description("Indica si el nodo está habilitado.\nWhether the node is enabled.")]
        [NotifyParentProperty(true)]
        public bool Enabled { get; set; } = true;
        [Category("3. Apariencia")]
        [Description("Curva matemática o física de la animación.\nMathematical or physical curve of the animation.")]
        [NotifyParentProperty(true)]
        public AnimationEasing Easing { get; set; } = AnimationEasing.EaseInOut;

        // --- Capas Visuales (Campos Struct = 0 Heap Allocations) --- // --- Visual Layers (Struct fields = 0 heap allocs) ---
        [Category("4. Capas Visuales")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        [NotifyParentProperty(true)]
        public BorderData Border { get; set; }

        [Category("4. Capas Visuales")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        [NotifyParentProperty(true)]
        public BackgroundData Background { get; set; }

        [Category("4. Capas Visuales")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        [NotifyParentProperty(true)]
        public ShadowData Shadow { get; set; }

        [Category("4. Capas Visuales")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        [NotifyParentProperty(true)]
        public AcrylicData Acrylic { get; set; }

        [Category("4. Capas Visuales")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        [NotifyParentProperty(true)]
        public ContentData Content { get; set; }

        [Category("4. Capas Visuales")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        [NotifyParentProperty(true)]
        public RippleData Ripple { get; set; }

        // INYECCIÓN: Filtros CSS // / CSS Filters
        [Category("4. Capas Visuales")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        [NotifyParentProperty(true)]
        public FilterData Filters { get; set; }

        [Category("5. Contenedor")]
        [Description("Modo de distribución automática de los hijos.\nAutomatic child distribution mode.")]
        [NotifyParentProperty(true)]
        public LayoutStyle LayoutMode { get; set; } = LayoutStyle.Absolute;

        [Category("5. Contenedor")]
        [Description("Espacio en píxeles entre cada hijo.\nPixel spacing between children.")]
        [NotifyParentProperty(true)]
        public float Spacing { get; set; } = 0f;

        [Category("5. Contenedor")]
        [Description("Ancho mínimo de las columnas (Equivalente al 200px de minmax en CSS).\nMinimum column width (equivalent to CSS minmax 200px).")]
        [NotifyParentProperty(true)]
        public float GridMinColumnWidth { get; set; } = 200f;

        // 🔥 FIX REACTIVIDAD: ObservableCollection avisa automáticamente si añaden/quitan hijos
        // / ObservableCollection automatically notifies when children are added/removed
        [Browsable(false)]
        public ObservableCollection<RenderNode> Children { get; } = new ObservableCollection<RenderNode>();

        // 🔥 INYECCIÓN PRO: Estados Visuales Dinámicos como structs para Zero‑Allocation real
        // / Dynamic Visual States stored as structs for true Zero‑Allocation
        [Browsable(false)] public VisualStateOverrides HoverState;
        [Browsable(false)] public VisualStateOverrides PressState;

        // --- Estado en Tiempo de Ejecución (Zero-Allocation) --- // --- Runtime State (Zero‑Alloc) ---
        [Browsable(false)] public bool IsHovered { get; set; }
        [Browsable(false)] public bool IsPressed { get; set; }
        [Browsable(false)] public float HoverProgress { get; set; } = 0f;
        [Browsable(false)] public float PressProgress { get; set; } = 0f;
        [Browsable(false)] public float AnimatedScale { get; set; } = 1.0f;

        // --- Callbacks de Interacción --- // --- Interaction Callbacks ---
        [Browsable(false)] public Action<RenderNode>? OnClickAction { get; set; }
        [Browsable(false)] public Action<RenderNode>? OnHoverAction { get; set; }

        public RenderNode()
        {
            Padding = new ModernPadding(0);
            Corners = new CornerRadii(0);
            Border = new BorderData { Thickness = new ModernThickness(0), NormalColor = Color.Transparent, FocusColor = Color.Transparent, Style = BorderStyle.Solid };
            Background = new BackgroundData { Color1 = Color.Transparent, Color2 = Color.Transparent, IsGradient = false };
            Shadow = new ShadowData { Color = Color.Transparent, Radius = 0, OffsetX = 0, OffsetY = 0 };
            Acrylic = new AcrylicData { TintColor = Color.FromArgb(40, 255, 255, 255), BlurRadius = 15, Downsample = 3 };
            Filters = new FilterData { Brightness = 1f, Contrast = 1f, Grayscale = 0f, Blur = 0f };

            Content = new ContentData
            {
                Text = string.Empty,
                FontSize = 12f,
                TextColor = Color.Black,
                FontFamily = "Segoe UI",
                ImageOpacity = 1f,
                HorizontalAlignment = StringAlignment.Center,
                VerticalAlignment = StringAlignment.Center,
                Decoration = TextDecoration.None,
                ImageFit = ImageFit.Cover
            };

            Ripple = new RippleData { Color = Color.FromArgb(60, 0, 0, 0) };

            // Los structs VisualStateOverrides se inicializan automáticamente a sus valores por defecto.
        }

        public int GetVisualCacheHash()
        {
            var hash = new HashCode();

            hash.Add(Layout);
            hash.Add(MinSize);
            hash.Add(MaxSize);
            hash.Add(Corners);
            hash.Add(Opacity);
            hash.Add(ScaleX);
            hash.Add(Rotation);

            hash.Add(Background.Color1.ToArgb());
            hash.Add(Background.Color2.ToArgb());
            hash.Add(Background.IsGradient);

            hash.Add(Border.Thickness.Top);
            hash.Add(Border.Thickness.Right);
            hash.Add(Border.Thickness.Bottom);
            hash.Add(Border.Thickness.Left);
            hash.Add(Border.NormalColor.ToArgb());
            hash.Add(Border.Style);

            hash.Add(Shadow.Color.ToArgb());
            hash.Add(Shadow.Radius);
            hash.Add(Shadow.OffsetX);
            hash.Add(Shadow.OffsetY);

            hash.Add(Content.Decoration);
            hash.Add(Content.ImageFit);

            if (Acrylic.IsEnabled)
            {
                hash.Add(Acrylic.TintColor.ToArgb());
                hash.Add(Acrylic.BlurRadius);
                hash.Add(Acrylic.Downsample);
            }

            // Filtros CSS
            hash.Add(Filters.Brightness);
            hash.Add(Filters.Contrast);
            hash.Add(Filters.Grayscale);
            hash.Add(Filters.Blur);

            hash.Add(LayoutMode);
            hash.Add(Spacing);
            hash.Add(GridMinColumnWidth);

            return hash.ToHashCode();
        }
    }

    // ==========================================
    // ESTRUCTURAS DE DATOS Y ESTADOS
    // ==========================================

    // 🔥 INYECCIÓN PRO: Convertido de clase a struct. Ahora vive dentro del RenderNode sin presión sobre el GC.
    // / Converted from class to struct. Now lives inside RenderNode without GC pressure.
    public struct VisualStateOverrides
    {
        public BackgroundData? Background;
        public BorderData? Border;
        public ShadowData? Shadow;
        public float? Scale;
        public float? Opacity;
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct FilterData
    {
        [NotifyParentProperty(true)] public float Brightness { get; set; }
        [NotifyParentProperty(true)] public float Contrast { get; set; }
        [NotifyParentProperty(true)] public float Grayscale { get; set; }
        [NotifyParentProperty(true)] public float Blur { get; set; }

        public override string ToString() => $"Filtros CSS (Brillo/Brightness: {Brightness})";
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct ModernThickness
    {
        [NotifyParentProperty(true)] public float Top { get; set; }
        [NotifyParentProperty(true)] public float Right { get; set; }
        [NotifyParentProperty(true)] public float Bottom { get; set; }
        [NotifyParentProperty(true)] public float Left { get; set; }

        public ModernThickness(float all) { Top = Right = Bottom = Left = all; }
        public ModernThickness(float vertical, float horizontal) { Top = Bottom = vertical; Left = Right = horizontal; }
        public ModernThickness(float top, float right, float bottom, float left) { Top = top; Right = right; Bottom = bottom; Left = left; }

        public override string ToString() => $"{Top}, {Right}, {Bottom}, {Left}";
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct ModernPadding
    {
        [NotifyParentProperty(true)] public float Left { get; set; }
        [NotifyParentProperty(true)] public float Top { get; set; }
        [NotifyParentProperty(true)] public float Right { get; set; }
        [NotifyParentProperty(true)] public float Bottom { get; set; }

        public ModernPadding(float all) { Left = Top = Right = Bottom = all; }
        public ModernPadding(float l, float t, float r, float b) { Left = l; Top = t; Right = r; Bottom = b; }

        public override string ToString() => $"{Left}, {Top}, {Right}, {Bottom}";
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct CornerRadii
    {
        [NotifyParentProperty(true)] public float TopLeft { get; set; }
        [NotifyParentProperty(true)] public float TopRight { get; set; }
        [NotifyParentProperty(true)] public float BottomRight { get; set; }
        [NotifyParentProperty(true)] public float BottomLeft { get; set; }

        public CornerRadii(float all) { TopLeft = TopRight = BottomRight = BottomLeft = all; }
        public CornerRadii(float tl, float tr, float br, float bl) { TopLeft = tl; TopRight = tr; BottomRight = br; BottomLeft = bl; }

        public override string ToString() => $"{TopLeft}, {TopRight}, {BottomRight}, {BottomLeft}";
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct BorderData
    {
        [NotifyParentProperty(true)] public ModernThickness Thickness { get; set; }
        [NotifyParentProperty(true)] public Color NormalColor { get; set; }
        [NotifyParentProperty(true)] public Color FocusColor { get; set; }
        [NotifyParentProperty(true)] public BorderStyle Style { get; set; }

        public override string ToString() => $"Borde ({Style}, {NormalColor.Name})";
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct BackgroundData
    {
        [NotifyParentProperty(true)] public Color Color1 { get; set; }
        [NotifyParentProperty(true)] public Color Color2 { get; set; }
        [NotifyParentProperty(true)] public Color HoverColor { get; set; }
        [NotifyParentProperty(true)] public Color PressColor { get; set; }
        [NotifyParentProperty(true)] public bool IsGradient { get; set; }
        [NotifyParentProperty(true)] public float GradientAngle { get; set; }

        public override string ToString() => IsGradient ? "Gradiente" : $"Sólido ({Color1.Name})";
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct ShadowData
    {
        [NotifyParentProperty(true)] public Color Color { get; set; }
        [NotifyParentProperty(true)] public float Radius { get; set; }
        [NotifyParentProperty(true)] public float OffsetX { get; set; }
        [NotifyParentProperty(true)] public float OffsetY { get; set; }

        public override string ToString() => Radius > 0 ? $"Sombra {Radius}px" : "Sin Sombra";
    }

    public enum AcrylicQuality { Auto = 0, High = 1, Low = 2 }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct AcrylicData
    {
        [NotifyParentProperty(true)] public bool IsEnabled { get; set; }
        [NotifyParentProperty(true)] public Color TintColor { get; set; }
        [NotifyParentProperty(true)] public int BlurRadius { get; set; }
        [NotifyParentProperty(true)] public int Downsample { get; set; }
        [NotifyParentProperty(true)] public AcrylicQuality Quality { get; set; }

        public override string ToString() => IsEnabled ? $"Acrílico ({BlurRadius}px)" : "Desactivado";
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct ContentData
    {
        [NotifyParentProperty(true)] public string Text { get; set; }
        [NotifyParentProperty(true)] public float FontSize { get; set; }
        [NotifyParentProperty(true)] public Color TextColor { get; set; }
        [NotifyParentProperty(true)] public string FontFamily { get; set; }
        [NotifyParentProperty(true)] public bool IsBold { get; set; }

        [NotifyParentProperty(true)] public StringAlignment HorizontalAlignment { get; set; }
        [NotifyParentProperty(true)] public StringAlignment VerticalAlignment { get; set; }
        [NotifyParentProperty(true)] public bool WordWrap { get; set; }
        [NotifyParentProperty(true)] public bool Trimming { get; set; }

        [NotifyParentProperty(true)] public TextDecoration Decoration { get; set; }

        [Browsable(false)] public Image? Image { get; set; }
        [NotifyParentProperty(true)] public float ImageOpacity { get; set; }
        [NotifyParentProperty(true)] public ImageFit ImageFit { get; set; }

        public override string ToString() => string.IsNullOrEmpty(Text) ? "Sin texto" : Text;
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct RippleData
    {
        [NotifyParentProperty(true)] public Color Color { get; set; }
        [NotifyParentProperty(true)] public float Radius { get; set; }
        [NotifyParentProperty(true)] public float Opacity { get; set; }

        [Browsable(false)] public PointF Center { get; set; }
        [Browsable(false)] public bool IsActive { get; set; }

        public override string ToString() => "Efecto Ripple";
    }
}