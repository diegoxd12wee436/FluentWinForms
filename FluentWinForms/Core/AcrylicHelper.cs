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

       
        // 🔥 CIRUGÍA PRO: Matemática de enteros rápida, Stride respetado y Tinte RGB aplicado mejora la version anterior.
        public static Bitmap ApplyTintAndBlur(Bitmap src, Color tint, int blurRadius)
        {
            if (src == null || src.Width == 0 || src.Height == 0) return new Bitmap(1, 1);

            int w = src.Width, h = src.Height;
            var rect = new Rectangle(0, 0, w, h);
            Bitmap final = new Bitmap(w, h, PixelFormat.Format32bppPArgb);

            BitmapData srcData = null!;
            BitmapData finalData = null!;

            byte[] pixels = null!;
            byte[] tmp = null!;

            try
            {
                srcData = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                int stride = Math.Abs(srcData.Stride);
                int bytes = stride * h;

                pixels = ArrayPool<byte>.Shared.Rent(bytes);
                tmp = ArrayPool<byte>.Shared.Rent(bytes);

                Marshal.Copy(srcData.Scan0, pixels, 0, bytes);

                // 1. Aplicamos el difuminado ultrarrápido
                StackBlurUltra.Blur(pixels, tmp, w, h, blurRadius);

                // 2. PREPARACIÓN DEL TINTE (Matemática de enteros para máxima velocidad)
                int ta = tint.A;
                int invTa = 255 - ta; // Lo que sobra del canal alpha para el fondo

                // Como usamos PArgb (Premultiplied Alpha), premultiplicamos el tinte antes del bucle
                int pmR = (tint.R * ta) / 255;
                int pmG = (tint.G * ta) / 255;
                int pmB = (tint.B * ta) / 255;

                // 3. BUCLE DE MEZCLA (Respetando el Stride y usando enteros)
                for (int y = 0; y < h; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int idx = rowOffset + (x * 4);

                        // Píxel del fondo ya difuminado (PArgb)
                        int bb = tmp[idx + 0];
                        int bg = tmp[idx + 1];
                        int br = tmp[idx + 2];
                        int ba = tmp[idx + 3];

                        // Mezcla Alpha Premultiplicada: Resultado = Tinte + Fondo * (1 - AlphaTinte)
                        int outB = pmB + ((bb * invTa) / 255);
                        int outG = pmG + ((bg * invTa) / 255);
                        int outR = pmR + ((br * invTa) / 255);
                        int outA = ta + ((ba * invTa) / 255);

                        tmp[idx + 0] = (byte)(outB > 255 ? 255 : outB);
                        tmp[idx + 1] = (byte)(outG > 255 ? 255 : outG);
                        tmp[idx + 2] = (byte)(outR > 255 ? 255 : outR);
                        tmp[idx + 3] = (byte)(outA > 255 ? 255 : outA);
                    }
                }

                finalData = final.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);

                // Aseguramos que el Stride de salida coincida (suele ser idéntico)
                if (Math.Abs(finalData.Stride) == stride)
                {
                    Marshal.Copy(tmp, 0, finalData.Scan0, bytes);
                }
                else
                {
                    // Fallback extremo si GDI+ decide usar un Stride diferente para la salida
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(tmp, y * stride, finalData.Scan0 + (y * Math.Abs(finalData.Stride)), w * 4);
                    }
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