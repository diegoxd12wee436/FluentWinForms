#nullable enable
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace FluentWinForms.Core
{
    public static class AcrylicHelper
    {
        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, int nFlags);

        // ====================================================================
        // 🔥 INYECCIÓN: POOL DE BITMAPS (Cero Asignaciones de GDI Objects repetidas)
        // ====================================================================
        public static class BitmapPool
        {
            private static readonly ConcurrentDictionary<(int w, int h), ConcurrentBag<Bitmap>> _bags = new();

            public static Bitmap Rent(int w, int h)
            {
                var key = (w, h);
                if (_bags.TryGetValue(key, out var bag) && bag.TryTake(out var bmp))
                {
                    return bmp;
                }
                return new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            }

            public static void Return(Bitmap bmp)
            {
                if (bmp == null) return;
                var key = (bmp.Width, bmp.Height);
                var bag = _bags.GetOrAdd(key, _ => new ConcurrentBag<Bitmap>());
                bag.Add(bmp);
            }
        }

        // ====================================================================
        // 🔥 INYECCIÓN: CACHÉ TTL (Time-To-Live de 300ms)
        // ====================================================================
        private class BackdropCacheEntry
        {
            public Bitmap Bitmap = null!;
            public DateTime Expire;
        }

        private static readonly ConcurrentDictionary<string, BackdropCacheEntry> _backdropCache = new();

        public static Bitmap CaptureWindow(IntPtr hwnd, Rectangle bounds)
        {
            // Usamos el Pool en lugar de crear uno nuevo siempre
            var bmp = BitmapPool.Rent(bounds.Width, bounds.Height);
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
            var bmp = BitmapPool.Rent(bounds.Width, bounds.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }

        public static Bitmap ApplyTintAndBlur(Bitmap src, Color tint, int blurRadius)
        {
            if (src == null || src.Width == 0 || src.Height == 0) return new Bitmap(1, 1);

            int w = src.Width, h = src.Height;
            var rect = new Rectangle(0, 0, w, h);

            // Rentamos el bitmap de salida del Pool
            Bitmap final = BitmapPool.Rent(w, h);

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

                // Límite estricto de seguridad
                blurRadius = Math.Max(0, Math.Min(128, blurRadius));

                if (blurRadius > 0)
                {
                    StackBlurUltra.Blur(pixels, tmp, w, h, blurRadius);
                }
                else
                {
                    Buffer.BlockCopy(pixels, 0, tmp, 0, bytes);
                }

                int ta = tint.A;
                int invTa = 255 - ta;
                int pmR = (tint.R * ta) / 255;
                int pmG = (tint.G * ta) / 255;
                int pmB = (tint.B * ta) / 255;

                for (int y = 0; y < h; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int idx = rowOffset + (x * 4);

                        int bb = tmp[idx + 0];
                        int bg = tmp[idx + 1];
                        int br = tmp[idx + 2];
                        int ba = tmp[idx + 3];

                        int outB = pmB + ((bb * invTa + 127) / 255);
                        int outG = pmG + ((bg * invTa + 127) / 255);
                        int outR = pmR + ((br * invTa + 127) / 255);
                        int outA = ta + ((ba * invTa + 127) / 255);

                        tmp[idx + 0] = (byte)(outB > 255 ? 255 : outB);
                        tmp[idx + 1] = (byte)(outG > 255 ? 255 : outG);
                        tmp[idx + 2] = (byte)(outR > 255 ? 255 : outR);
                        tmp[idx + 3] = (byte)(outA > 255 ? 255 : outA);
                    }
                }

                finalData = final.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);

                int outStride = Math.Abs(finalData.Stride);
                IntPtr destBase = finalData.Scan0;
                for (int y = 0; y < h; y++)
                {
                    IntPtr destRow = IntPtr.Add(destBase, y * outStride);
                    Marshal.Copy(tmp, y * stride, destRow, w * 4);
                }
            }
            finally
            {
                if (finalData != null) final.UnlockBits(finalData);
                if (srcData != null) src.UnlockBits(srcData);
                if (pixels != null) { ArrayPool<byte>.Shared.Return(pixels, false); pixels = null!; }
                if (tmp != null) { ArrayPool<byte>.Shared.Return(tmp, false); tmp = null!; }
            }

            return final;
        }

        // ====================================================================
        // 🔥 INYECCIÓN: WRAPPER CACHEADO (El salvador de RAM en los clics rápidos)
        // ==========================================
        public static async Task<Bitmap> CaptureBackdropAsync(IntPtr hwnd, Rectangle bounds, Color tint, int blurRadius)
        {
            string key = $"{hwnd}-{bounds.Width}x{bounds.Height}-{tint.ToArgb()}-{blurRadius}";

            // Si piden la imagen y pasaron menos de 300ms, damos la copia en caché (0 coste de CPU/RAM)
            if (_backdropCache.TryGetValue(key, out var e) && e.Expire > DateTime.UtcNow)
            {
                return e.Bitmap;
            }

            var bmp = await Task.Run(() =>
            {
                Bitmap? captured = null;
                try
                {
                    try { captured = CaptureWindow(hwnd, bounds); }
                    catch { captured = CaptureScreen(bounds); }

                    return ApplyTintAndBlur(captured, tint, blurRadius);
                }
                finally
                {
                    if (captured != null)
                    {
                        BitmapPool.Return(captured); // Regresamos el original al pool
                    }
                }
            });

            // Guardamos el nuevo resultado en caché por 300 milisegundos
            var entry = new BackdropCacheEntry { Bitmap = bmp, Expire = DateTime.UtcNow.AddMilliseconds(300) };

            // Limpiamos el caché viejo si existía y lo mandamos al pool
            if (_backdropCache.TryGetValue(key, out var oldEntry) && oldEntry.Bitmap != null)
            {
                BitmapPool.Return(oldEntry.Bitmap);
            }

            _backdropCache[key] = entry;
            return bmp;
        }
    }
}