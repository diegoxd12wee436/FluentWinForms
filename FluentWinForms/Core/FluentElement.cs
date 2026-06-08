using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using FluentWinForms.Core;

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

            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            // Skia por defecto — antialiasing real, bordes perfectos
            UseSkiaGraphics = true;
        }

        /// <summary>
        /// Crea el control al vuelo y abre la Fluent API.
        /// </summary>
        public static ControlBuilder<FluentElement> Design(string name = "")
        {
            var element = new FluentElement { Name = name };
            return element.Design();
        }

        public ControlBuilder<FluentElement> Design()
        {
            if (VisualNode == null)
                VisualNode = new RenderNode
                {
                    Id = string.IsNullOrEmpty(Name) ? "fe_" + GetHashCode() : Name
                };

            var builder = new ControlBuilder<FluentElement>(VisualNode, this);

            builder.OnApplied = node =>
            {
                VisualNode = null;
                VisualNode = node;
            };

            return builder;
        }
    }
}