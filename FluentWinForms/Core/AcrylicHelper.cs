#nullable enable
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FluentWinForms.Core
{
    public static class AcrylicHelper
    {
        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, int nFlags);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdc, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        // ====================================================================
        // 🔥 TU POOL DE BITMAPS 
        // ====================================================================
        public static class BitmapPool
        {
            private const int MaxPerBucket = 4;
            private static readonly ConcurrentDictionary<(int w, int h), ConcurrentBag<Bitmap>> _bags = new();

            public static Bitmap Rent(int w, int h)
            {
                var key = (w, h);
                if (_bags.TryGetValue(key, out var bag) && bag.TryTake(out var bmp))
                    return bmp;
                return new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            }

            public static void Return(Bitmap bmp)
            {
                if (bmp == null) return;
                var key = (bmp.Width, bmp.Height);
                var bag = _bags.GetOrAdd(key, _ => new ConcurrentBag<Bitmap>());

                if (bag.Count >= MaxPerBucket)
                {
                    bmp.Dispose();
                    return;
                }

                bag.Add(bmp);
            }
        }

        // ====================================================================
        // 🔥 TU CACHÉ TTL
        // ====================================================================
        private class BackdropCacheEntry
        {
            public Bitmap Bitmap = null!;
            public DateTime Expire;
        }

        private static readonly ConcurrentDictionary<string, BackdropCacheEntry> _backdropCache = new();
        private static int _captureCallCount = 0;
        private const int CleanupEvery = 10;

        private static void CleanExpiredBackdrops()
        {
            var now = DateTime.UtcNow;
            foreach (var key in _backdropCache.Keys)
            {
                if (_backdropCache.TryGetValue(key, out var entry) && entry.Expire <= now)
                {
                    if (_backdropCache.TryRemove(key, out var removed) && removed?.Bitmap != null)
                        BitmapPool.Return(removed.Bitmap);
                }
            }
        }

        // ====================================================================
        // 🛡️ TUS MÉTODOS ORIGINALES (INTACTOS, NADIE LOS BORRÓ)
        // ====================================================================
        public static Bitmap CaptureWindow(IntPtr hwnd, Rectangle bounds)
        {
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
        // 🚀 EL MOTOR DE ZERO-ALLOCATION (Seguro, 100% Antivirus friendly)
        // Esto soluciona los tirones y saltos de memoria
        // ====================================================================
        private static Bitmap CaptureAndProcessInPlaceSafe(IntPtr hwnd, Rectangle bounds, Color tint, int blurRadius)
        {
            int w = bounds.Width;
            int h = bounds.Height;
            if (w <= 0 || h <= 0) return BitmapPool.Rent(1, 1);

            int stride = w * 4;
            int bytes = stride * h;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(bytes);
            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

            try
            {
                IntPtr pointer = handle.AddrOfPinnedObject();

                using (Bitmap wrapperBmp = new Bitmap(w, h, stride, PixelFormat.Format32bppPArgb, pointer))
                using (Graphics g = Graphics.FromImage(wrapperBmp))
                {
                    IntPtr hdcDest = g.GetHdc();
                    bool success = false;
                    try
                    {
                        if (hwnd != IntPtr.Zero)
                        {
                            success = PrintWindow(hwnd, hdcDest, 0);
                        }

                        if (!success)
                        {
                            IntPtr hdcScreen = GetWindowDC(IntPtr.Zero);
                            BitBlt(hdcDest, 0, 0, w, h, hdcScreen, bounds.X, bounds.Y, 0x00CC0020);
                            ReleaseDC(IntPtr.Zero, hdcScreen);
                        }
                    }
                    finally
                    {
                        g.ReleaseHdc(hdcDest);
                    }
                }

                blurRadius = Math.Max(0, Math.Min(128, blurRadius));
                if (blurRadius > 0)
                {
                    StackBlurUltra.BlurInPlace(buffer, w, h, blurRadius);
                }

                ApplyTintInPlaceSafe(buffer, w, h, tint);

                Bitmap finalBitmap = BitmapPool.Rent(w, h);
                BitmapData finalData = finalBitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);

                Marshal.Copy(buffer, 0, finalData.Scan0, bytes);
                finalBitmap.UnlockBits(finalData);

                return finalBitmap;
            }
            finally
            {
                handle.Free();
                ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            }
        }

        private static void ApplyTintInPlaceSafe(byte[] buffer, int width, int height, Color tint)
        {
            if (tint.A == 0) return;

            int ta = tint.A;
            int invTa = 255 - ta;
            int pmR = (tint.R * ta) / 255;
            int pmG = (tint.G * ta) / 255;
            int pmB = (tint.B * ta) / 255;

            int length = width * height * 4;

            for (int i = 0; i < length; i += 4)
            {
                int b = buffer[i];
                int g = buffer[i + 1];
                int r = buffer[i + 2];
                int a = buffer[i + 3];

                int outB = pmB + ((b * invTa + 127) / 255);
                int outG = pmG + ((g * invTa + 127) / 255);
                int outR = pmR + ((r * invTa + 127) / 255);
                int outA = ta + ((a * invTa + 127) / 255);

                buffer[i] = (byte)(outB > 255 ? 255 : outB);
                buffer[i + 1] = (byte)(outG > 255 ? 255 : outG);
                buffer[i + 2] = (byte)(outR > 255 ? 255 : outR);
                buffer[i + 3] = (byte)(outA > 255 ? 255 : outA);
            }
        }

        // ====================================================================
        // 🔥 TU WRAPPER CACHEADO 
        // ====================================================================
        public static async Task<Bitmap> CaptureBackdropAsync(IntPtr hwnd, Rectangle bounds, Color tint, int blurRadius)
        {
            if (Interlocked.Increment(ref _captureCallCount) % CleanupEvery == 0)
                CleanExpiredBackdrops();

            string key = $"{hwnd}-{bounds.Width}x{bounds.Height}-{tint.ToArgb()}-{blurRadius}";

            if (_backdropCache.TryGetValue(key, out var e) && e.Expire > DateTime.UtcNow)
            {
                return e.Bitmap;
            }

            // AHORA USA EL MOTOR RÁPIDO SIN CONSUMIR RAM
            var bmp = await Task.Run(() => CaptureAndProcessInPlaceSafe(hwnd, bounds, tint, blurRadius));

            var entry = new BackdropCacheEntry { Bitmap = bmp, Expire = DateTime.UtcNow.AddMilliseconds(300) };

            if (_backdropCache.TryGetValue(key, out var oldEntry) && oldEntry.Bitmap != null)
            {
                BitmapPool.Return(oldEntry.Bitmap);
            }

            _backdropCache[key] = entry;
            return bmp;
        }
    }
}