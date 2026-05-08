#nullable enable
using System;
using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FluentWinForms.Core
{
    public static class AcrylicHelper
    {
        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, int nFlags);

        public static Bitmap CaptureWindow(IntPtr hwnd, Rectangle bounds)
        {
            var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = IntPtr.Zero;
                try
                {
                    hdc = g.GetHdc();
                    PrintWindow(hwnd, hdc, 0);
                }
                catch
                {
                    g.Clear(Color.Transparent);
                }
                finally
                {
                    if (hdc != IntPtr.Zero)
                    {
                        g.ReleaseHdc(hdc);
                    }
                }
            }
            return bmp;
        }

        public static Bitmap CaptureScreen(Rectangle bounds)
        {
            var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }

        // 🔥 CIRUGÍA: ArrayPool inyectado y Try/Finally para proteger los LockBits
        public static Bitmap ApplyTintAndBlur(Bitmap src, Color tint, int blurRadius)
        {
            if (src == null) return new Bitmap(1, 1); // Fallback seguro anti-crashes

            int w = src.Width, h = src.Height;
            var rect = new Rectangle(0, 0, w, h);
            Bitmap final = new Bitmap(w, h, PixelFormat.Format32bppPArgb);

            BitmapData srcData = null!;
            BitmapData finalData = null!;

            int bytes = 0;
            byte[] pixels = null!;
            byte[] tmp = null!;

            try
            {
                srcData = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                bytes = Math.Abs(srcData.Stride) * h;

                pixels = ArrayPool<byte>.Shared.Rent(bytes);
                tmp = ArrayPool<byte>.Shared.Rent(bytes);

                Marshal.Copy(srcData.Scan0, pixels, 0, bytes);

                // 🔥 Reemplazado por StackBlur de alto rendimiento
                StackBlurUltra.Blur(pixels, tmp, w, h, blurRadius);

                float ta = tint.A / 255f;
                for (int i = 0; i < w * h; i++)
                {
                    int idx = i * 4;
                    float b = tmp[idx + 0];
                    float g = tmp[idx + 1];
                    float r = tmp[idx + 2];
                    float a = tmp[idx + 3] / 255f;
                    float outA = a * ta;

                    tmp[idx + 0] = (byte)Math.Min(255f, b * outA);
                    tmp[idx + 1] = (byte)Math.Min(255f, g * outA);
                    tmp[idx + 2] = (byte)Math.Min(255f, r * outA);
                    tmp[idx + 3] = (byte)Math.Min(255f, outA * 255f);
                }

                finalData = final.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                // Dentro de ApplyTintAndBlur, antes del Marshal.Copy final:
                if (tmp != null && finalData != null)
                {
                    // Aseguramos que el buffer 'tmp' contiene datos válidos antes de pasarlos a GDI
                    Marshal.Copy(tmp, 0, finalData.Scan0, bytes);
                }
            }
            finally
            {
                if (finalData != null) final.UnlockBits(finalData);
                if (srcData != null) src.UnlockBits(srcData);
                if (pixels != null) ArrayPool<byte>.Shared.Return(pixels, false);
                if (tmp != null) ArrayPool<byte>.Shared.Return(tmp, false);
            }

            return final;
        }

        public static async Task<Bitmap> CaptureBackdropAsync(IntPtr hwnd, Rectangle bounds, Color tint, int blurRadius)
        {
            return await Task.Run(() =>
            {
                Bitmap captured;
                try { captured = CaptureWindow(hwnd, bounds); }
                catch { captured = CaptureScreen(bounds); }
                var blurred = ApplyTintAndBlur(captured, tint, blurRadius);
                captured.Dispose();
                return blurred;
            });
        }
    }
}