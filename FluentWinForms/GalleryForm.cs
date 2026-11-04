// ═══════════════════════════════════════════════════════════════════════
// GalleryForm.cs — ARCHIVO NUEVO (no modifica nada del core)
// Banco de pruebas: botones congelados + golden images + medición.
//   F5 = guardar referencia (golden)   F6 = comparar contra la referencia
// Ponlo junto a Program.cs (carpeta FluentWinForms/FluentWinForms/)
// ═══════════════════════════════════════════════════════════════════════
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FluentWinForms.Core;
using FluentWinForms.Custom_Controls;

namespace FluentWinForms
{
    public sealed class GalleryForm : Form
    {
        // Zona de pantalla que se captura (coordenadas del cliente, en píxeles)
        private static readonly Rectangle CaptureRect = new(0, 0, 340, 520);
        private static readonly string GoldenDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluentWinForms", "golden");
        private const string GoldenFile = "gallery.png";
        private const int Tolerance = 2; // diferencia por canal que se ignora (ruido)

        private readonly Label _status = new()
        {
            Location = new Point(360, 20),
            Size = new Size(320, 480),
            ForeColor = Color.White,
            Font = new Font("Consolas", 9f)
        };
        private readonly Stopwatch _boot = Stopwatch.StartNew();
        private readonly double _buildMs;
        private bool _busy;

        public GalleryForm()
        {
            AutoScaleMode = AutoScaleMode.None;   // sin reescalado: base estable
            Text = "Fluent Gallery  |  F5 guardar golden  |  F6 comparar";
            ClientSize = new Size(700, 540);
            BackColor = Color.Teal;               // mismo fondo que Form1
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            Controls.Add(_status);

            var sw = Stopwatch.StartNew();
            BuildButtons();
            _buildMs = sw.Elapsed.TotalMilliseconds;

            Application.Idle += OnFirstIdle;
        }

        // ── Los mismos 6 botones de Form1, CONGELADOS como referencia ──
        private void BuildButtons()
        {
            FluentElement.Design("btn-primary")
                .Layout(40, 40, 180, 48).Background("#0055ff").BorderRadius(12)
                .Text("Guardar").Bold().FontSize(14).TextColor("#fff")
                .Shadow(2).Glow("#0055ff", 14)
                .Hover(bg: "#0044dd", scale: 1.02, translateY: -2)
                .Press(scale: 0.96).Tooltip("Guardar cambios")
                .Apply(this);

            FluentElement.Design("btn-outline")
                .Layout(40, 108, 180, 48).Background("#00000000").BorderRadius(12)
                .Border("#0055ff", 1.5)
                .Text("Cancelar").FontSize(14).TextColor("#0055ff")
                .Hover(bg: "#0055ff15", border: "#0044dd", textColor: "#0044dd")
                .Press(scale: 0.97).HandCursor().AnimateEase(180)
                .Apply(this);

            FluentElement.Design("btn-danger")
                .Layout(40, 176, 180, 48).Background("#e53935").BorderRadius(12)
                .Text("Eliminar").Bold().FontSize(14).TextColor("#fff")
                .Ripple("#ff000040").Shadow(2)
                .Hover(bg: "#c62828", scale: 1.02)
                .Press(scale: 0.95).HandCursor().AnimateEase(200)
                .Tooltip("Esta acción no se puede deshacer")
                .Apply(this);

            FluentElement.Design("btn-glass")
                .Layout(40, 244, 180, 48).Glass("#40ffffff").BorderRadius(14)
                .Border("#ffffff55", 1)
                .Text("Continuar").FontSize(14).TextColor("#fff")
                .Shadow(1)
                .Hover(scale: 1.03, opacity: 0.9)
                .Press(scale: 0.97).NoRipple().HandCursor().AnimateEase(200)
                .Apply(this);

            FluentElement.Design("btn-gradient")
                .Layout(40, 312, 180, 48)
                .Gradient("#7c3aed", "#db2777", angle: 135).BorderRadius(24)
                .Text("Notificaciones").Bold().FontSize(13).TextColor("#fff")
                .Shadow(3).Glow("#7c3aed", 16)
                .Hover(gradient: ("#6d28d9", "#be185d", 135), scale: 1.03, translateX: 22)
                .Press(scale: 0.95)
                .Badge("5").BadgeOffset(-4, -6)
                .Tooltip("5 notificaciones nuevas").AnimateSpring(180)
                .Apply(this);

            FluentElement.Design("btnEstatico")
                .Layout(50, 412, 180, 48)
                .Content("Estático", "#fff", 12).Background("#333").Shadow(2)
                .Apply(this);
        }

        // ── Medición (paso 2): sin tocar el motor ──
        private void OnFirstIdle(object? s, EventArgs e)
        {
            Application.Idle -= OnFirstIdle;
            _status.Text =
                $"Build de 6 botones : {_buildMs:F1} ms\r\n" +
                $"Ctor -> 1er idle   : {_boot.Elapsed.TotalMilliseconds:F0} ms\r\n\r\n" +
                "F5 = guardar golden\r\nF6 = comparar\r\n\r\n" +
                "Carpeta:\r\n" + GoldenDir;
        }

        // ── Golden images (paso 1) ──
        protected override async void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_busy || (e.KeyCode != Keys.F5 && e.KeyCode != Keys.F6)) return;
            _busy = true;
            try { await RunAsync(save: e.KeyCode == Keys.F5); }
            catch (Exception ex) { _status.Text = "ERROR: " + ex.Message; }
            finally { _busy = false; }
        }

        private async Task RunAsync(bool save)
        {
            Cursor.Position = PointToScreen(new Point(520, 520)); // mouse lejos de los botones
            _status.Text = "Esperando que las animaciones terminen...";
            bool settled = await WaitIdleAsync(3000);
            string note = settled ? "estable" : "NO se estabilizó en 3 s";

            using var shot = Grab();
            Directory.CreateDirectory(GoldenDir);
            string golden = Path.Combine(GoldenDir, GoldenFile);

            if (save || !File.Exists(golden))
            {
                shot.Save(golden, ImageFormat.Png);
                _status.Text = $"Golden guardado ({note})\r\n{golden}";
                return;
            }

            using var gold = new Bitmap(golden);
            if (gold.Size != shot.Size) { _status.Text = "Tamaño distinto. Guarda otro golden con F5."; return; }

            byte[] a = Bytes(gold, out int stride);
            byte[] b = Bytes(shot, out _);
            byte[] diff = (byte[])b.Clone();
            int w = shot.Width, h = shot.Height, bad = 0;

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * stride + x * 4;
                    int d = Math.Max(Math.Abs(a[i] - b[i]),
                            Math.Max(Math.Abs(a[i + 1] - b[i + 1]), Math.Abs(a[i + 2] - b[i + 2])));
                    if (d > Tolerance) { bad++; diff[i] = 255; diff[i + 1] = 0; diff[i + 2] = 255; }
                    diff[i + 3] = 255;
                }

            if (bad == 0) { _status.Text = $"IGUAL al golden ({note})"; return; }

            using (var dbmp = FromBytes(diff, w, h))
                dbmp.Save(Path.Combine(GoldenDir, "diff.png"), ImageFormat.Png);
            _status.Text = $"DISTINTO: {bad} px ({100.0 * bad / (w * h):F2} %) ({note})\r\n" +
                           $"Mira diff.png (magenta = cambio) en:\r\n{GoldenDir}";
        }

        // Espera a que ningún control esté animando (4 sondeos seguidos en calma)
        private static async Task<bool> WaitIdleAsync(int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            int calm = 0;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                calm = AnimationManager.SubscriberCount == 0 ? calm + 1 : 0;
                if (calm >= 4) return true;
                await Task.Delay(50);
            }
            return false;
        }

        private Bitmap Grab()
        {
            var bmp = new Bitmap(CaptureRect.Width, CaptureRect.Height, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(PointToScreen(CaptureRect.Location), Point.Empty, CaptureRect.Size);
            return bmp;
        }

        private static byte[] Bytes(Bitmap bmp, out int stride)
        {
            var d = bmp.LockBits(new Rectangle(Point.Empty, bmp.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            stride = d.Stride;
            var arr = new byte[stride * bmp.Height];
            Marshal.Copy(d.Scan0, arr, 0, arr.Length);
            bmp.UnlockBits(d);
            return arr;
        }

        private static Bitmap FromBytes(byte[] data, int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(data, 0, d.Scan0, Math.Min(data.Length, d.Stride * h));
            bmp.UnlockBits(d);
            return bmp;
        }
    }
}