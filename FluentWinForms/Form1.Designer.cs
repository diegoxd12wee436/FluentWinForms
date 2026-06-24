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
            pictureBox1 = new PictureBox();
            modernFormManager1 = new FluentWinForms.Core.ModernFormManager(components);
            modernGlassPanel1 = new FluentWinForms.Panel_Controls.ModernGlassPanel();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            SuspendLayout();
            // 
            // pictureBox1
            // 
            pictureBox1.Image = Properties.Resources.Copilot_20260514_173627;
            pictureBox1.Location = new Point(436, 41);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(641, 344);
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox1.TabIndex = 1;
            pictureBox1.TabStop = false;
            // 
            // modernFormManager1
            // 
            modernFormManager1.AutoSetBlackBackground = false;
            modernFormManager1.BackdropType = Core.FormBackdropType.None;
            modernFormManager1.BlurAmount = 20;
            modernFormManager1.BorderColor = Color.FromArgb(40, 255, 255, 255);
            modernFormManager1.BorderRadius = 12;
            modernFormManager1.BorderThickness = 0F;
            modernFormManager1.DragControl = null;
            modernFormManager1.DragOpacity = 1D;
            modernFormManager1.EnableDrag = true;
            modernFormManager1.ForceDarkModeTitleBar = false;
            modernFormManager1.TargetForm = this;
            modernFormManager1.TransparentStyle = false;
            modernFormManager1.UseModernRoundedCorners = true;
            modernFormManager1.UseSkia = true;
            // 
            // modernGlassPanel1
            // 
            modernGlassPanel1.AcrylicTintColor = Color.FromArgb(40, 255, 255, 255);
            modernGlassPanel1.AnimationSpeed = 150F;
            modernGlassPanel1.AnimationType = Core.AnimationEasing.EaseInOut;
            modernGlassPanel1.BackColor = Color.Transparent;
            modernGlassPanel1.BackdropStyle = Panel_Controls.PanelBackdropStyle.Glass;
            modernGlassPanel1.BackgroundColor = Color.White;
            modernGlassPanel1.BackgroundColor2 = Color.LightGray;
            modernGlassPanel1.BackgroundImg = null;
            modernGlassPanel1.BackgroundImgLayout = ImageLayout.Zoom;
            modernGlassPanel1.BlurAmount = 25F;
            modernGlassPanel1.BorderColor = Color.FromArgb(120, 255, 255, 255);
            modernGlassPanel1.BorderRadius = 15F;
            modernGlassPanel1.BorderThickness = 0F;
            modernGlassPanel1.CheckedColor = Color.FromArgb(0, 120, 215);
            modernGlassPanel1.CheckedColor2 = Color.FromArgb(0, 90, 180);
            modernGlassPanel1.DisabledColor = Color.FromArgb(200, 200, 200);
            modernGlassPanel1.DisabledTextColor = Color.FromArgb(130, 130, 130);
            modernGlassPanel1.EnableHover = false;
            modernGlassPanel1.EnablePressEffect = true;
            modernGlassPanel1.FocusColor = Color.FromArgb(0, 120, 215);
            modernGlassPanel1.FontFamily = "Segoe UI";
            modernGlassPanel1.FontSize = 12F;
            modernGlassPanel1.FontWeightBold = false;
            modernGlassPanel1.GlassTint = Color.FromArgb(40, 255, 255, 255);
            modernGlassPanel1.GradientAngle = 45F;
            modernGlassPanel1.HoverColor = Color.FromArgb(240, 240, 240);
            modernGlassPanel1.HoverColor2 = Color.FromArgb(220, 220, 220);
            modernGlassPanel1.ImageOpacity = 1F;
            modernGlassPanel1.Location = new Point(607, 108);
            modernGlassPanel1.Name = "modernGlassPanel1";
            modernGlassPanel1.Opacity = 1F;
            modernGlassPanel1.PressColor = Color.FromArgb(220, 220, 220);
            modernGlassPanel1.PressColor2 = Color.FromArgb(200, 200, 200);
            modernGlassPanel1.RippleColor = Color.FromArgb(60, 0, 0, 0);
            modernGlassPanel1.Rotation = 0F;
            modernGlassPanel1.ScaleX = 1F;
            modernGlassPanel1.ScaleY = 1F;
            modernGlassPanel1.ShadowBlur = 8F;
            modernGlassPanel1.ShadowColor = Color.FromArgb(50, 0, 0, 0);
            modernGlassPanel1.ShadowOffsetX = 0F;
            modernGlassPanel1.ShadowOffsetY = 4F;
            modernGlassPanel1.ShadowOpacity = 0.5F;
            modernGlassPanel1.Size = new Size(327, 177);
            modernGlassPanel1.TabIndex = 2;
            modernGlassPanel1.Text = "modernGlassPanel1";
            modernGlassPanel1.TextAlignment = StringAlignment.Center;
            modernGlassPanel1.TextColor = Color.Black;
            modernGlassPanel1.TranslateX = 0F;
            modernGlassPanel1.TranslateY = 0F;
            modernGlassPanel1.UseGradient = false;
            modernGlassPanel1.UseRipple = false;
            modernGlassPanel1.UseShadow = false;
            modernGlassPanel1.UseSkiaGraphics = true;
            modernGlassPanel1.VerticalAlignment = StringAlignment.Center;
            modernGlassPanel1.WordWrap = false;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.Teal;
            BackgroundImageLayout = ImageLayout.Stretch;
            ClientSize = new Size(1116, 512);
            Controls.Add(modernGlassPanel1);
            Controls.Add(pictureBox1);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None;
            Name = "Form1";
            Text = "Form1";
            Load += Form1_Load;
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private PictureBox pictureBox1;
        private Core.ModernFormManager modernFormManager1;
        private Panel_Controls.ModernGlassPanel modernGlassPanel1;
    }
}
