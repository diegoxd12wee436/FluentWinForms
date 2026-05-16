using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Drawing;
using System.Windows.Forms;

namespace FluentWinForms.Core
{
    /// <summary>
    /// StackBlur Ultra: versión altamente optimizada para BGRA interleaved (4 bytes/pixel).
    /// - ArrayPool para temporales
    /// - Tabla de divisores cacheada por radius (byte[] para mínima presión de caché de CPU)
    /// - Soporta src == dst (in-place) con mínima asignación
    /// - Blur, BlurInPlace y BlurAlpha
    /// - Caché de tablas con límite automático para evitar crecimiento descontrolado
    /// - Pasadas unificadas para reducir duplicación de código
    /// </summary>
    public static class StackBlurUltra
    {
        // Tabla de divisores por radio (byte[] porque los valores nunca superan 255)
        private static readonly ConcurrentDictionary<int, byte[]> _divTableCache = new ConcurrentDictionary<int, byte[]>();
        private const int MaxCachedTables = 16;

        private static void TrimCacheIfNeeded()
        {
            if (_divTableCache.Count <= MaxCachedTables) return;                       
            // Reconstruir una pequeña tabla es más rápido que generar basura en la RAM en .NET 4.8.
            _divTableCache.Clear();
        }

        public static void Blur(byte[] src, byte[] dst, int w, int h, int radius)
        {
            ValidateBuffers(src, dst, w, h);
            radius = Math.Max(0, Math.Min(128, radius)); // 🔥 FIX PASO 2: Límite estricto de seguridad

            if (radius < 1)
            {
                if (!ReferenceEquals(src, dst)) Buffer.BlockCopy(src, 0, dst, 0, w * h * 4);
                return;
            }

            int bytes = w * h * 4;
            byte[] temp = ArrayPool<byte>.Shared.Rent(bytes);
            try
            {
                byte[] divTable = GetDivTable(radius);
                HorizontalPass(src, temp, w, h, radius, divTable);
                VerticalPass(temp, dst, w, h, radius, divTable);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temp, clearArray: false);
            }
        }

        public static void BlurInPlace(byte[] buffer, int w, int h, int radius)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            int bytes = w * h * 4;
            if (buffer.Length < bytes) throw new ArgumentException("Buffer too small for dimensions", nameof(buffer));
            if (radius < 1) return;

            byte[] temp = ArrayPool<byte>.Shared.Rent(bytes);
            try
            {
                byte[] divTable = GetDivTable(radius);
                HorizontalPass(buffer, temp, w, h, radius, divTable);
                VerticalPass(temp, buffer, w, h, radius, divTable);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temp, clearArray: false);
            }
        }

        public static void BlurAlpha(byte[] src, byte[] dst, int w, int h, int radius)
        {
            ValidateBuffers(src, dst, w, h);
            if (radius < 1) { if (!ReferenceEquals(src, dst)) Buffer.BlockCopy(src, 0, dst, 0, w * h * 4); return; }

            int bytes = w * h * 4;
            byte[] temp = ArrayPool<byte>.Shared.Rent(bytes);
            try
            {
                byte[] divTable = GetDivTable(radius);
                HorizontalPassAlpha(src, temp, w, h, radius, divTable);
                VerticalPassAlpha(temp, dst, w, h, radius, divTable);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temp, clearArray: false);
            }
        }

        // ---------- Helpers ----------

        private static void ValidateBuffers(byte[] src, byte[] dst, int w, int h)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (dst == null) throw new ArgumentNullException(nameof(dst));
            if (w <= 0 || h <= 0) throw new ArgumentOutOfRangeException(nameof(w), "Width and height must be greater than zero.");
            int bytes = w * h * 4;
            if (src.Length < bytes) throw new ArgumentException($"Source buffer too small: expected at least {bytes} bytes, got {src.Length}.", nameof(src));
            if (dst.Length < bytes) throw new ArgumentException($"Destination buffer too small: expected at least {bytes} bytes, got {dst.Length}.", nameof(dst));
        }

        private static byte[] GetDivTable(int radius)
        {
            var table = _divTableCache.GetOrAdd(radius, r =>
            {
                int window = r * 2 + 1;
                byte[] t = new byte[256 * window];
                for (int i = 0; i < t.Length; i++)
                    t[i] = (byte)(i / window);
                TrimCacheIfNeeded();
                return t;
            });
            return table;
        }

        // Pasada horizontal genérica (para blur completo)
        private static void HorizontalPass(byte[] src, byte[] dst, int w, int h, int radius, byte[] divTable)
        {
            int stride = w * 4;
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                int bSum = 0, gSum = 0, rSum = 0, aSum = 0;

                // init window
                for (int i = -radius; i <= radius; i++)
                {
                    int x = Clamp(i, 0, w - 1);
                    int idx = row + x * 4;
                    bSum += src[idx];
                    gSum += src[idx + 1];
                    rSum += src[idx + 2];
                    aSum += src[idx + 3];
                }

                int outIdx = row;
                for (int x = 0; x < w; x++)
                {
                    dst[outIdx] = divTable[bSum];
                    dst[outIdx + 1] = divTable[gSum];
                    dst[outIdx + 2] = divTable[rSum];
                    dst[outIdx + 3] = divTable[aSum];

                    int removeX = x - radius;
                    int addX = x + radius + 1;
                    int removeIdx = row + Clamp(removeX, 0, w - 1) * 4;
                    int addIdx = row + Clamp(addX, 0, w - 1) * 4;

                    bSum += src[addIdx] - src[removeIdx];
                    gSum += src[addIdx + 1] - src[removeIdx + 1];
                    rSum += src[addIdx + 2] - src[removeIdx + 2];
                    aSum += src[addIdx + 3] - src[removeIdx + 3];

                    outIdx += 4;
                }
            }
        }

        // Pasada vertical genérica (para blur completo)
        private static void VerticalPass(byte[] src, byte[] dst, int w, int h, int radius, byte[] divTable)
        {
            int stride = w * 4;
            for (int x = 0; x < w; x++)
            {
                int colOffset = x * 4;
                int bSum = 0, gSum = 0, rSum = 0, aSum = 0;

                // init window
                for (int i = -radius; i <= radius; i++)
                {
                    int y = Clamp(i, 0, h - 1);
                    int idx = y * stride + colOffset;
                    bSum += src[idx];
                    gSum += src[idx + 1];
                    rSum += src[idx + 2];
                    aSum += src[idx + 3];
                }

                int outIdx = colOffset;
                for (int y = 0; y < h; y++)
                {
                    dst[outIdx] = divTable[bSum];
                    dst[outIdx + 1] = divTable[gSum];
                    dst[outIdx + 2] = divTable[rSum];
                    dst[outIdx + 3] = divTable[aSum];

                    int removeY = y - radius;
                    int addY = y + radius + 1;
                    int removeIdx = Clamp(removeY, 0, h - 1) * stride + colOffset;
                    int addIdx = Clamp(addY, 0, h - 1) * stride + colOffset;

                    bSum += src[addIdx] - src[removeIdx];
                    gSum += src[addIdx + 1] - src[removeIdx + 1];
                    rSum += src[addIdx + 2] - src[removeIdx + 2];
                    aSum += src[addIdx + 3] - src[removeIdx + 3];

                    outIdx += stride;
                }
            }
        }

        // Pasada horizontal solo alfa
        private static void HorizontalPassAlpha(byte[] src, byte[] dst, int w, int h, int radius, byte[] divTable)
        {
            int stride = w * 4;
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                int sum = 0;
                for (int i = -radius; i <= radius; i++)
                {
                    int x = Clamp(i, 0, w - 1);
                    sum += src[row + x * 4 + 3];
                }

                int outIdx = row + 3;
                for (int x = 0; x < w; x++)
                {
                    dst[outIdx] = divTable[sum];

                    int removeX = x - radius;
                    int addX = x + radius + 1;
                    int removeIdx = row + Clamp(removeX, 0, w - 1) * 4 + 3;
                    int addIdx = row + Clamp(addX, 0, w - 1) * 4 + 3;

                    sum += src[addIdx] - src[removeIdx];
                    outIdx += 4;
                }
            }
        }

        // Pasada vertical solo alfa
        private static void VerticalPassAlpha(byte[] src, byte[] dst, int w, int h, int radius, byte[] divTable)
        {
            int stride = w * 4;
            for (int x = 0; x < w; x++)
            {
                int col = x * 4 + 3;
                int sum = 0;
                for (int i = -radius; i <= radius; i++)
                {
                    int y = Clamp(i, 0, h - 1);
                    sum += src[y * stride + col];
                }

                int outIdx = col;
                for (int y = 0; y < h; y++)
                {
                    dst[outIdx] = divTable[sum];

                    int removeY = y - radius;
                    int addY = y + radius + 1;
                    int removeIdx = Clamp(removeY, 0, h - 1) * stride + col;
                    int addIdx = Clamp(addY, 0, h - 1) * stride + col;

                    sum += src[addIdx] - src[removeIdx];
                    outIdx += stride;
                }
            }
        }

        // Función de clamping rápido
        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}