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
using static FluentWinForms.Core.F;

namespace FluentWinForms
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            // ── Botón 1: Primario clásico con glow ──
            var btn1 = FluentElement.Design("btn-primary")
                .Layout(40, 40, 180, 48)
                .Background("#0055ff")
                .BorderRadius(12)
                .Text("Guardar").Bold().FontSize(14).TextColor("#fff")
                .Shadow(2).Glow("#0055ff", 14)
                .Hover(bg: "#0044dd", scale: 1.02, translateY: -2)
                .Press(scale: 0.96)
                .Tooltip("Guardar cambios")
                .Apply(this);

            // ── Botón 2: Outlined / Ghost ──
            var btn2 = FluentElement.Design("btn-outline")
                .Layout(40, 108, 180, 48)
                .Background("#00000000")
                .BorderRadius(12)
                .Border("#0055ff", 1.5)
                .Text("Cancelar").FontSize(14).TextColor("#0055ff")
                .Hover(bg: "#0055ff15", border: "#0044dd", textColor: "#0044dd")
                .Press(scale: 0.97)
                .HandCursor().AnimateEase(180)
                .Apply(this);

            // ── Botón 3: Peligro con ripple rojo ──
            var btn3 = FluentElement.Design("btn-danger")
                .Layout(40, 176, 180, 48)
                .Background("#e53935")
                .BorderRadius(12)
                .Text("Eliminar").Bold().FontSize(14).TextColor("#fff")
                .Ripple("#ff000040")
                .Shadow(2)
                .Hover(bg: "#c62828", scale: 1.02)
                .Press(scale: 0.95)
                .HandCursor().AnimateSpring(160)
                .Tooltip("Esta acción no se puede deshacer")
                .Apply(this);

            // ── Botón 4: Glass / Blur ──
            var btn4 = FluentElement.Design("btn-glass")
                .Layout(40, 244, 180, 48)
                .Glass("#40ffffff")
                .BorderRadius(14)
                .Border("#ffffff55", 1)
                .Text("Continuar").FontSize(14).TextColor("#fff")
                .Shadow(1)
                .Hover(scale: 1.03, opacity: 0.9)
                .Press(scale: 0.97)
                .NoRipple()
                .HandCursor().AnimateEase(200)
                .Apply(this);

            // ── Botón 5: Degradado animado con badge ──
            var btn5 = FluentElement.Design("btn-gradient")
                .Layout(40, 312, 180, 48)
                .Gradient("#7c3aed", "#db2777", angle: 135)
                .BorderRadius(24)
                .Text("Notificaciones").Bold().FontSize(13).TextColor("#fff")
                .Shadow(3).Glow("#7c3aed", 16)
                .Hover(gradient: ("#6d28d9", "#be185d", 135), scale: 1.03, translateY: -2)
                .Press(scale: 0.95)
                .Badge("5").BadgeOffset(-4, -6)
                .Tooltip("5 notificaciones nuevas")
                .AnimateSpring(180)
                .Apply(this);


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
