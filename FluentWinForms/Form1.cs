using FluentWinForms.Core;
using FluentWinForms.Custom_Buttons;
using FluentWinForms.Custom_Controls;
using FluentWinForms.Panel_Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            //AnimationManager.FrameTicked += dt =>
            //{
            //    Debug.WriteLine(AnimationManager.DumpDiagnostics());
            //};
            //var btnPrueba = FluentElement.Design("btnTest")
            //    .Layout(50, 50, 150, 45)
            //    .Content("¡Motor Listo!", "#fff", 12)
            //    .Font("Segoe UI", 12, bold: true, italic: true) // ¡El texto avanzado!
            //    .Background("#0078D4")
            //    .BorderRadius(8)
            //    .Ripple("#40FFFFFF") // El efecto onda al hacer clic
            //    .Hover(scale: 1.05, bg: "#1084E3")
            //    .Apply(this); // ¡Magia! Se dibuja solo.

            //btnPrueba.Click += (s, e) => MessageBox.Show("¡Sintaxis Dev-Friendly funcionando al 100%!");
        }       

        
    }
}
