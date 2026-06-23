#nullable enable
using System.Windows.Forms;

namespace FluentWinForms.Core
{
    /// <summary>
    /// EN: Semantic node factory. Add "using static FluentWinForms.Core.F;" once per file to use without prefix.
    /// ES: Fábrica semántica de nodos. Agrega "using static FluentWinForms.Core.F;" una vez por archivo para usarla sin prefijo.
    /// </summary>
    public static class F
    {
        /// <summary>EN: Generic node. ES: Nodo genérico.</summary>
        public static ControlBuilder<ModernControlBase> Node(string id = "")
            => new ControlBuilder<ModernControlBase>(id);

        /// <summary>EN: Container node. ES: Contenedor visual.</summary>
        public static ControlBuilder<ModernControlBase> Panel(string id = "")
            => new ControlBuilder<ModernControlBase>(id);

        /// <summary>EN: Text label node. ES: Etiqueta de texto.</summary>
        public static ControlBuilder<ModernControlBase> Label(string id = "")
            => new ControlBuilder<ModernControlBase>(id);

        /// <summary>
        /// EN: Button node — pre-configured with HandCursor, Ripple, Press scale and spring animation.
        /// ES: Nodo botón — pre-configurado con cursor de mano, ripple, escala al presionar y animación spring.
        /// </summary>
        public static ControlBuilder<ModernControlBase> Button(string id = "")
            => new ControlBuilder<ModernControlBase>(id)
                .HandCursor()                
                .Press(scale: 0.97)
                .AnimateEase(200);

        /// <summary>
        /// EN: Icon node — square by default, sized for SVG icons.
        /// ES: Nodo ícono — cuadrado por defecto, dimensionado para íconos SVG.
        /// </summary>
        public static ControlBuilder<ModernControlBase> Icon(string id = "", double size = 24)
            => new ControlBuilder<ModernControlBase>(id)
                .Size(size, size);
    }
}