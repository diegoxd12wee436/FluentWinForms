using System;
using System.Windows.Forms;
using System.Drawing;
namespace FluentWinForms
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            modernFormManager1 = new FluentWinForms.Core.ModernFormManager(components);
            SuspendLayout();
            // 
            // modernFormManager1
            // 
            modernFormManager1.AutoSetBlackBackground = true;
            modernFormManager1.BackdropType = Core.FormBackdropType.Blur;
            modernFormManager1.BlurAmount = 2;
            modernFormManager1.BorderColor = Color.FromArgb(40, 255, 255, 255);
            modernFormManager1.BorderRadius = 10;
            modernFormManager1.BorderThickness = 0F;
            modernFormManager1.DragControl = this;
            modernFormManager1.DragOpacity = 1D;
            modernFormManager1.EnableDrag = true;
            modernFormManager1.ForceDarkModeTitleBar = false;
            modernFormManager1.TargetForm = this;
            modernFormManager1.TransparentStyle = false;
            modernFormManager1.UseModernRoundedCorners = true;
            modernFormManager1.UseSkia = true;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.LightSeaGreen;
            BackgroundImageLayout = ImageLayout.Stretch;
            ClientSize = new Size(1116, 512);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None;
            Name = "Form1";
            Text = "Form1";
            Load += Form1_Load;
            ResumeLayout(false);
        }

        #endregion
        private Core.ModernFormManager modernFormManager1;
    }
}
