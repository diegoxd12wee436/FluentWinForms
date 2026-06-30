#nullable enable
using SkiaSharp;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace FluentWinForms.Core
{
    // ==========================================
    // ENUMS DE ESTILO (Como CSS)
    // ==========================================
    public enum AnimationEasing { Linear, EaseInOut, EaseOutBack, Spring }
    public enum FluentBorderStyle { Solid, Dashed, Dotted }
    public enum ImageFit { Fill, Contain, Cover, None }
    public enum TextDecoration { None, Underline, Strikethrough }
    public enum LayoutStyle { Absolute, VerticalStack, HorizontalStack, AutoFitGrid }
    public enum IconAlign { Center, Left, Right, Top, Bottom }
    public enum Align { Start, Center, End }
    public enum Justify { Start, Center, End, SpaceBetween, SpaceAround }

    [ToolboxItem(false)]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    [DebuggerDisplay("{Id,nq} ({Layout.Width}x{Layout.Height})")]
    public sealed class RenderNode
    {
        [Browsable(false)] public string Id { get; init; } = Guid.NewGuid().ToString("N");
        [Browsable(false)] public bool StretchX { get; set; } = false;
        [Browsable(false)] public bool StretchY { get; set; } = false;


        [Category("1. Layout")] public RectangleF Layout { get; set; }
        [Category("1. Layout")][NotifyParentProperty(true)] public ModernPadding Padding { get; set; }
        [Category("1. Layout")][NotifyParentProperty(true)] public ModernThickness Margin { get; set; }
        [Category("1. Layout")][NotifyParentProperty(true)] public CornerRadii Corners { get; set; }
        [Category("1. Layout")][Browsable(false)] public SizeF MinSize { get; set; } = new SizeF(0, 0);
        [Category("1. Layout")][Browsable(false)] public SizeF MaxSize { get; set; } = new SizeF(0, 0);

        [Category("2. Transformación")][NotifyParentProperty(true)] public float ScaleX { get; set; } = 1.0f;
        [Category("2. Transformación")][NotifyParentProperty(true)] public float ScaleY { get; set; } = 1.0f;
        [Category("2. Transformación")][NotifyParentProperty(true)] public float Rotation { get; set; } = 0f;
        [Category("2. Transformación")][NotifyParentProperty(true)] public float TranslateX { get; set; } = 0f;
        [Category("2. Transformación")][NotifyParentProperty(true)] public float TranslateY { get; set; } = 0f;
        [Browsable(false)] public PointF TransformOrigin { get; set; } = new PointF(0.5f, 0.5f);

        [Category("3. Apariencia")][NotifyParentProperty(true)] public float Opacity { get; set; } = 1.0f;
        [Category("3. Apariencia")][NotifyParentProperty(true)] public bool IsVisible { get; set; } = true;
        [Category("3. Apariencia")][NotifyParentProperty(true)] public bool Enabled { get; set; } = true;
        [Category("3. Apariencia")][NotifyParentProperty(true)] public AnimationEasing Easing { get; set; } = AnimationEasing.EaseInOut;

        [Category("4. Capas Visuales")][DesignerSerializationVisibility(DesignerSerializationVisibility.Content)][NotifyParentProperty(true)] public BorderData Border { get; set; }
        [Category("4. Capas Visuales")][DesignerSerializationVisibility(DesignerSerializationVisibility.Content)][NotifyParentProperty(true)] public BackgroundData Background { get; set; }
        [Category("4. Capas Visuales")][DesignerSerializationVisibility(DesignerSerializationVisibility.Content)][NotifyParentProperty(true)] public ShadowData Shadow { get; set; }
        [Category("4. Capas Visuales")][DesignerSerializationVisibility(DesignerSerializationVisibility.Content)][NotifyParentProperty(true)] public AcrylicData Acrylic { get; set; }
        [Category("4. Capas Visuales")][DesignerSerializationVisibility(DesignerSerializationVisibility.Content)][NotifyParentProperty(true)] public ContentData Content { get; set; }
        [Category("4. Capas Visuales")][DesignerSerializationVisibility(DesignerSerializationVisibility.Content)][NotifyParentProperty(true)] public RippleData Ripple { get; set; }
        [Category("4. Capas Visuales")][DesignerSerializationVisibility(DesignerSerializationVisibility.Content)][NotifyParentProperty(true)] public FilterData Filters { get; set; }
        
        [Category("4. Capas Visuales")] public SweepData Sweep { get; set; } = new SweepData { IsEnabled = false };
        [Browsable(false)] public SKPicture? SvgPicture { get; set; }
        [Browsable(false)] public Color SvgTintColor { get; set; } = Color.Empty;
        [Browsable(false)] public SizeF SvgSize { get; set; }
        [Browsable(false)] public IconAlign IconPosition { get; set; } = IconAlign.Center;
        [Browsable(false)] public float IconGap { get; set; } = 8f;
        [Browsable(false)] internal SKColorFilter? _cachedSvgTint;
        [Browsable(false)] internal Color _lastSvgTintColor = Color.Empty;
        [Category("4. Capas Visuales")][NotifyParentProperty(true)] public BadgeData Badge { get; set; }

        [Category("5. Contenedor")][NotifyParentProperty(true)] public LayoutStyle LayoutMode { get; set; } = LayoutStyle.Absolute;
        [Category("5. Contenedor")][NotifyParentProperty(true)] public float Spacing { get; set; } = 0f;
        [Category("5. Contenedor")][NotifyParentProperty(true)] public float GridMinColumnWidth { get; set; } = 200f;
        [Category("5. Contenedor")][NotifyParentProperty(true)] public Align AlignItems { get; set; } = Align.Start;
        [Category("5. Contenedor")][NotifyParentProperty(true)] public Justify JustifyContent { get; set; } = Justify.Start;

        [Browsable(false)] public ObservableCollection<RenderNode> Children { get; } = new ObservableCollection<RenderNode>();

        [Browsable(false)] public VisualStateOverrides HoverState;
        [Browsable(false)] public VisualStateOverrides PressState;
        [Browsable(false)] public VisualStateOverrides DisabledState;

        [Browsable(false)] public bool IsHovered { get; set; }
        [Browsable(false)] public bool IsPressed { get; set; }
        [Browsable(false)] public float HoverProgress { get; set; } = 0f;
        [Browsable(false)] public float PressProgress { get; set; } = 0f;
        [Browsable(false)] public float AnimatedScale { get; set; } = 1.0f;
        [Browsable(false)] internal SKImageFilter? _cachedShadowFilter;
        [Browsable(false)] internal SKPath? _cachedSweepClip;
        [Browsable(false)] internal SKPath? _cachedChildClip;
        [Browsable(false)] internal SKPath? _cachedAcrylicClip;
        [Browsable(false)] internal SKPaint? _cachedSweepPaint;
        [Browsable(false)] internal SKPaint? _cachedAlphaPaint;
        [Browsable(false)] internal SKPaint? _cachedTintPaint;
        [Browsable(false)] internal SKPaint? _cachedGlowPaint;
        [Browsable(false)] internal SKPaint? _cachedRipplePaint;
        [Browsable(false)] internal SKPath? _cachedRippleClip;
        [Browsable(false)] internal SKImageFilter? _cachedBlurFilter;
        [Browsable(false)] internal SKColorFilter? _cachedColorMatrixFilter;
        [Browsable(false)] internal SKImageFilter? _cachedComposedFilter;
        [Browsable(false)] internal SKImage? _cachedContentImage;
        [Browsable(false)] internal object? _lastContentImageRef;
        [Browsable(false)] internal float[]? _colorMatrixBuffer;
        [Browsable(false)] internal float _lastBlurValue = -1f, _lastGrayscale = -1f, _lastBrightness = -1f, _lastContrast = -1f;
        [Browsable(false)] internal float _lastSweepW = -1, _lastSweepH = -1;
        internal void ReleaseNativeResources()
        {
            _cachedShadowFilter?.Dispose();
            _cachedShadowFilter = null;
            _cachedContentImage?.Dispose(); _cachedContentImage = null; _lastContentImageRef = null;
            _cachedSweepClip?.Dispose(); _cachedSweepClip = null;
            _cachedChildClip?.Dispose(); _cachedChildClip = null;
            _cachedAcrylicClip?.Dispose(); _cachedAcrylicClip = null;
            _cachedSweepPaint?.Dispose(); _cachedSweepPaint = null;
            _cachedAlphaPaint?.Dispose(); _cachedAlphaPaint = null;
            _cachedTintPaint?.Dispose(); _cachedTintPaint = null;
            _cachedGlowPaint?.Dispose(); _cachedGlowPaint = null;
            _cachedRipplePaint?.Dispose(); _cachedRipplePaint = null;
            _cachedRippleClip?.Dispose(); _cachedRippleClip = null;
            _cachedBlurFilter?.Dispose(); _cachedBlurFilter = null;
            _cachedColorMatrixFilter?.Dispose(); _cachedColorMatrixFilter = null;
            _cachedComposedFilter?.Dispose(); _cachedComposedFilter = null;
            _cachedSvgTint?.Dispose();
            _cachedSvgTint = null;
            SvgPicture?.Dispose();
            SvgPicture = null;
            foreach (var child in Children) child.ReleaseNativeResources();
        }

        public void ClearSvg()
        {
            _cachedSvgTint?.Dispose();
            _cachedSvgTint = null;
            SvgPicture?.Dispose();
            SvgPicture = null;
        }

        // 🔥 CABLES CONECTADOS
        [Browsable(false)] public Action<RenderNode>? OnClickAction { get; set; }
        [Browsable(false)] public Action<RenderNode>? OnHoverAction { get; set; }
        [Browsable(false)] public Action<RenderNode>? OnHoverEnterAction { get; set; }
        [Browsable(false)] public Action<RenderNode>? OnHoverLeaveAction { get; set; }

        public RenderNode()
        {
            Padding = new ModernPadding(0);
            Corners = new CornerRadii(0);
            Border = new BorderData { Thickness = new ModernThickness(0), NormalColor = Color.Transparent, FocusColor = Color.Transparent, Style = FluentBorderStyle.Solid };
            Background = new BackgroundData { Color1 = Color.Transparent, Color2 = Color.Transparent, IsGradient = false };
            Shadow = new ShadowData { Color = Color.Transparent, Radius = 0, OffsetX = 0, OffsetY = 0 };
            Acrylic = new AcrylicData { TintColor = Color.FromArgb(40, 255, 255, 255), BlurRadius = 15, Downsample = 3 };
            Filters = new FilterData { Brightness = 1f, Contrast = 1f, Grayscale = 0f, Blur = 0f };
            Content = new ContentData { Text = string.Empty, FontSize = 12f, TextColor = Color.Black, FontFamily = "Segoe UI", ImageOpacity = 1f, HorizontalAlignment = StringAlignment.Center, VerticalAlignment = StringAlignment.Center, Decoration = TextDecoration.None, ImageFit = ImageFit.Cover };
            Ripple = new RippleData { Color = Color.FromArgb(60, 0, 0, 0) };
        }

        public int GetVisualCacheHash() { return RuntimeHelpers.GetHashCode(this); }
    }

    // ==========================================
    // 🧱 ESTRUCTURAS DE DATOS Y ESTADOS (Zero-Alloc)
    // ==========================================
    public struct VisualStateOverrides
    {
        public BackgroundData? Background;
        public BorderData? Border;
        public ShadowData? Shadow;
        public float? Scale;
        public float? Opacity;
        public Color? TextColor;   // 🆕 color de texto interpolado
        public float? TranslateX;  // 🆕 traslación en estado
        public float? TranslateY;  // 🆕
        public float? Grayscale;  // 🆕
        public float? Brightness; // 🆕
        public Color? IconColor;  // 🆕 tinte del ícono en hover/press
    }
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct BadgeData
    {
        [NotifyParentProperty(true)] public bool IsVisible { get; set; }
        [NotifyParentProperty(true)] public string Text { get; set; }
        [NotifyParentProperty(true)] public Color Background { get; set; }
        [NotifyParentProperty(true)] public Color TextColor { get; set; }
        [NotifyParentProperty(true)] public double Size { get; set; }
        [NotifyParentProperty(true)] public double OffsetX { get; set; }
        [NotifyParentProperty(true)] public double OffsetY { get; set; }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct FilterData
    {
        [NotifyParentProperty(true)] public float Brightness { get; set; }
        [NotifyParentProperty(true)] public float Contrast { get; set; }
        [NotifyParentProperty(true)] public float Grayscale { get; set; }
        [NotifyParentProperty(true)] public float Blur { get; set; }
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
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct BorderData
    {
        [NotifyParentProperty(true)] public ModernThickness Thickness { get; set; }
        [NotifyParentProperty(true)] public Color NormalColor { get; set; }
        [NotifyParentProperty(true)] public Color FocusColor { get; set; }
        [NotifyParentProperty(true)] public FluentBorderStyle Style { get; set; }
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
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct ShadowData
    {
        [NotifyParentProperty(true)] public Color Color { get; set; }
        [NotifyParentProperty(true)] public float Radius { get; set; }
        [NotifyParentProperty(true)] public float OffsetX { get; set; }
        [NotifyParentProperty(true)] public float OffsetY { get; set; }
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
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct ContentData
    {
        [NotifyParentProperty(true)] public string Text { get; set; }
        [NotifyParentProperty(true)] public float FontSize { get; set; }
        [NotifyParentProperty(true)] public Color TextColor { get; set; }
        [NotifyParentProperty(true)] public string FontFamily { get; set; }
        [NotifyParentProperty(true)] public bool IsBold { get; set; }
        [NotifyParentProperty(true)] public bool IsItalic { get; set; } // 🔥 EL FIX: se me habia olvidado XDDD
        [NotifyParentProperty(true)] public StringAlignment HorizontalAlignment { get; set; }
        [NotifyParentProperty(true)] public StringAlignment VerticalAlignment { get; set; }
        [NotifyParentProperty(true)] public bool WordWrap { get; set; }
        [NotifyParentProperty(true)] public bool Trimming { get; set; }
        [NotifyParentProperty(true)] public TextDecoration Decoration { get; set; }
        [Browsable(false)] public Image? Image { get; set; }
        [NotifyParentProperty(true)] public float ImageOpacity { get; set; }
        [NotifyParentProperty(true)] public ImageFit ImageFit { get; set; }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct RippleData
    {
        [NotifyParentProperty(true)] public Color Color { get; set; }
        [NotifyParentProperty(true)] public float Radius { get; set; }
        [NotifyParentProperty(true)] public float Opacity { get; set; }
        [Browsable(false)] public PointF Center { get; set; }
        [Browsable(false)] public bool IsActive { get; set; }
    }
    
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public struct SweepData
    {
        [NotifyParentProperty(true)] public bool IsEnabled { get; set; }
        [NotifyParentProperty(true)] public Color ThemeColor { get; set; }
        [NotifyParentProperty(true)] public Color TextHoverColor { get; set; }
    }
}