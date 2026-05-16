#nullable enable
using FluentWinForms.Core;
using System.ComponentModel;
using System.Windows.Forms;

namespace FluentWinForms.Custom_Controls
{
    [ToolboxItem(true)]
    public class FluentElement : ModernControlBase
    {
        public FluentElement()
        {
            SetStyle(ControlStyles.StandardDoubleClick, false);
            SetStyle(ControlStyles.StandardClick, true);
        }
        
        
    }
}