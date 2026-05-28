using FluentWinForms.Core;
using FluentWinForms.Custom_Buttons;
using FluentWinForms.Custom_Controls;
using FluentWinForms.Panel_Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace FluentWinForms
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();            

        }       

        private void Form1_Load(object sender, EventArgs e)
        {
            var btn = new FluentElement { Name = "btnGuardar" };
            btn.Design()
               .Layout(20, 20, 160, 44)
               .Content("Guardar", "#FFF", 14)
               .Glass("#40FFFFFF")              
               .BorderRadius(12)
               .Hover(scale: 1.1f)
               
               //demo it works but i want to make it super devfriendly
               .Apply(this);           
        }       

        
    }
}
