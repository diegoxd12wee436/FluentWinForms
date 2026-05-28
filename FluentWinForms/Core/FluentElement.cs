#nullable enable
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using FluentWinForms.Core;

// =====================================================================
// FluentElement + ControlBuilder — VERSIÓN FINAL
// =====================================================================
// FluentElement → namespace FluentWinForms.Custom_Controls
// ControlBuilder → namespace FluentWinForms.Core
//
// 
// =====================================================================

namespace FluentWinForms.Custom_Controls
{
    [ToolboxItem(true)]
    [DesignTimeVisible(true)]
    [Description("Control Fluent universal. Usa .Design() para configurarlo con la Fluent API.")]
    public class FluentElement : ModernControlBase
    {
        public FluentElement()
        {
            SetStyle(ControlStyles.StandardDoubleClick, false);
            SetStyle(ControlStyles.StandardClick, true);
            // Skia por defecto — antialiasing real, bordes perfectos
            UseSkiaGraphics = true;
        }

        /// <summary>
        /// Punto de entrada de la Fluent API.
        /// Encadena métodos y termina con .Apply() para renderizar.
        /// </summary>
        public ControlBuilder Design()
        {
            if (VisualNode == null)
                VisualNode = new RenderNode
                {
                    Id = string.IsNullOrEmpty(Name) ? "fe_" + GetHashCode() : Name
                };

            var builder = new ControlBuilder(VisualNode, this);

            builder.OnApplied = node =>
            {
                // Forzar setter aunque sea el mismo objeto
                // El setter del base corre ComputeLayout + RefreshVisuals
                VisualNode = null;
                VisualNode = node;
            };

            return builder;
        }
    }
}