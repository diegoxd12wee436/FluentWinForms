#nullable enable
using System;
using System.ComponentModel;
using System.Drawing;

namespace FluentWinForms.Core
{
    public struct ModernTheme
    {
        public Color Background, Border, Focus, Checked, Checked2;
    }

    public abstract partial class ModernControlBase
    {
        #region 🔵 Visual State Manager
        [Category("Modern - States")]
        [Description("Habilita o deshabilita el efecto de hover para este control.\nEnable or disable the hover effect for this control.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color DisabledColor { get; set; } = Color.FromArgb(200, 200, 200);

        [Category("Modern - States")]
        [Description("El color del texto cuando el control está deshabilitado.\nThe color of the text when the control is disabled.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color DisabledTextColor { get; set; } = Color.FromArgb(130, 130, 130);
        [Category("Modern - States")]
        [Description("Habilita o deshabilita el efecto de hover para este control.\nEnable or disable the hover effect for this control.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]

        public Color HoverColor { get; set; } = Color.FromArgb(240, 240, 240);
        [Category("Modern - States")]
        [Description("El segundo color para el efecto de hover (si se usa un gradiente).\nThe second color for the hover effect (if using a gradient).")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color HoverColor2 { get; set; } = Color.FromArgb(220, 220, 220);
        [Category("Modern - States")]
        [Description("El color del control cuando está presionado.\nThe color of the control when pressed.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color PressColor { get; set; } = Color.FromArgb(220, 220, 220);
        [Category("Modern - States")]
        [Description("El segundo color para el efecto de presionado (si se usa un gradiente).\nThe second color for the pressed effect (if using a gradient).")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color PressColor2 { get; set; } = Color.FromArgb(200, 200, 200);

        private bool _isChecked = false;
        [Category("Modern - States")]
        [DefaultValue(false)]
        [Description("Indica si el control esta selecionado o no.\nIndicates whether the control is Checked or not.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; OnCheckedChanged(EventArgs.Empty); RefreshVisuals(); } }
        }
        [Category("Modern - States")]
        [Description("El color del control cuando está seleccionado.\nThe color of the control when checked.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color CheckedColor { get; set; } = Color.FromArgb(0, 120, 215);
        [Category("Modern - States")]
        [Description("El segundo color para el efecto de seleccionado (si se usa un gradiente).\nThe second color for the checked effect (if using a gradient).")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color CheckedColor2 { get; set; } = Color.FromArgb(0, 90, 180);

        [Category("Modern - States")]
        [Description("Indica si el control tiene el foco.\nIndicates whether the control is focused.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public bool IsFocusedControl { get; private set; } = false;
        [Category("Modern - States")]
        [Description("El color del control cuando tiene el foco.\nThe color of the control when focused.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color FocusColor { get; set; } = Color.FromArgb(0, 120, 215);
        [Category("Modern - States")]
        [Description("El grosor del borde del control cuando tiene el foco.\nThe thickness of the control's border when focused.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public float FocusThickness { get; set; } = 2;

        public event EventHandler? CheckedChanged;
        public event EventHandler? FocusChanged;

        protected virtual void OnCheckedChanged(EventArgs e) => CheckedChanged?.Invoke(this, e);
        protected virtual void OnFocusChanged(EventArgs e) => FocusChanged?.Invoke(this, e);
        #endregion

        // Gestión de Foco
        protected override void OnGotFocus(EventArgs e) { IsFocusedControl = true; OnFocusChanged(EventArgs.Empty); ClearCaches(); RefreshVisuals(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { IsFocusedControl = false; OnFocusChanged(EventArgs.Empty); ClearCaches(); RefreshVisuals(); base.OnLostFocus(e); }

        #region 🔵 Pipeline de Colores (Interpolación)
        // 🔥 CORRECCIÓN: 3 parámetros out (Fondo 1, Fondo 2 y Borde)
        protected void CalculateStateColors(out Color c1, out Color c2, out Color border)
        {
            c1 = BackgroundColor;
            c2 = BackgroundColor2;
            border = IsFocusedControl ? FocusColor : BorderColor;

            if (!Enabled) { c1 = DisabledColor; c2 = DisabledColor; border = DisabledColor; return; }
            if (IsChecked) { c1 = CheckedColor; c2 = CheckedColor2; return; }

            // Aplicar Hover (EaseInOut)
            float hEase = Easing.EaseInOutQuad(_hoverProgress);
            if (EnableHover && hEase > 0)
            {
                c1 = LerpColor(c1, HoverColor, hEase);
                c2 = LerpColor(c2, HoverColor2, hEase);
            }

            // Aplicar Press (EaseInOut)
            float pEase = Easing.EaseInOutQuad(_pressProgress);
            if (EnablePressEffect && pEase > 0)
            {
                c1 = LerpColor(c1, PressColor, pEase);
                c2 = LerpColor(c2, PressColor2, pEase);
            }
        }

        protected static Color LerpColor(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t)
            );
        }
        #endregion
    }
}