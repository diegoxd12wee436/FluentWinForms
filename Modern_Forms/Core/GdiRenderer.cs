#nullable enable
#pragma warning disable CA1416 // Silencia la advertencia de compatibilidad cross-platform de System.Drawing
#pragma warning disable IDE0090 // Silencia sugerencias de simplificar 'new'
#pragma warning disable IDE0028 // Silencia sugerencias de inicialización de colecciones
#pragma warning disable CA1051 // Silencia sugerencias de campos visibles en clases

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Modern_Forms.Core
{
    public readonly struct ShadowKey : IEquatable<ShadowKey>
    {
        public readonly string PathHash;
        public readonly int Radius;
        public readonly int OffsetX;
        public readonly int OffsetY;
        public readonly int A, R, G, B;
        public readonly int DpiNormalized;

        public ShadowKey(string hash, int rad, int ox, int oy, int a, int r, int g, int b, int dpi)
        {
            PathHash = hash; Radius = rad; OffsetX = ox; OffsetY = oy;
            A = a; R = r; G = g; B = b; DpiNormalized = dpi;
        }

        public bool Equals(ShadowKey other) =>
            PathHash == other.PathHash && Radius == other.Radius && OffsetX == other.OffsetX &&
            OffsetY == other.OffsetY && A == other.A && R == other.R && G == other.G &&
            B == other.B && DpiNormalized == other.DpiNormalized;

        public override bool Equals(object? obj) => obj is ShadowKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (PathHash != null ? PathHash.GetHashCode() : 0);
                hash = hash * 31 + Radius.GetHashCode();
                hash = hash * 31 + OffsetX.GetHashCode();
                hash = hash * 31 + OffsetY.GetHashCode();
                hash = hash * 31 + A.GetHashCode();
                hash = hash * 31 + R.GetHashCode();
                hash = hash * 31 + G.GetHashCode();
                hash = hash * 31 + B.GetHashCode();
                hash = hash * 31 + DpiNormalized.GetHashCode();
                return hash;
            }
        }

        // 🔥 FIX PRO: Implementación de operadores de igualdad sugerida por el compilador
        public static bool operator ==(ShadowKey left, ShadowKey right) => left.Equals(right);
        public static bool operator !=(ShadowKey left, ShadowKey right) => !(left == right);
    }

    public class CachedShadowRef : IDisposable
    {
        public Bitmap Image;
        public long SizeInBytes;
        private int _refs = 1;

        public CachedShadowRef(Bitmap bmp, long size)
        {
            Image = bmp;
            SizeInBytes = size;
        }

        public void AddRef() => Interlocked.Increment(ref _refs);

        public void Release()
        {
            if (Interlocked.Decrement(ref _refs) == 0)
                Dispose();
        }

        public void Dispose()
        {
            if (Image != null)
            {
                try { Image.Dispose(); } catch { /* swallow */ }
                Image = null!;
            }
            // 🔥 FIX PRO: Suprimir la finalización recomendada por el analizador
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// GdiRenderer (final, production-ready)
    /// - Same public class name and signatures as before (no renames)
    /// - Bounded worker queue to avoid Task.Run storms
    /// - Bucketed buffer reuse with per-bucket limits
    /// - AddToCache atomic with _isClearing checks
    /// - Metrics extended: SemaphoreTimeouts, Evictions, BucketDrops
    /// - Robust GDI resource disposal and race protections
    /// </summary>
    public static class GdiRenderer
    {
        #region Metrics
        public static class Metrics
        {
            public static int CacheHits = 0;
            public static int CacheMisses = 0;
            public static int Errors = 0;
            public static long TotalGenTimeMs = 0;
            internal static int CurrentGenerating => _currentGenerating;
            public static int CurrentCacheItems => _shadowCache.Count;

            public static double CurrentCacheMB => _currentCacheBytes / 1048576.0;

#if NETFRAMEWORK
            public static string DumpCacheStats() =>
                $"Hits: {CacheHits} | Misses: {CacheMisses} | Errors: {Errors} | GenAct: {_currentGenerating} | Mem: {CurrentCacheMB:0.00} MB / 30.00 MB | AvgGenMs: {(CacheMisses == 0 ? 0 : TotalGenTimeMs / CacheMisses)} | Timeouts: {SemaphoreTimeouts} | Evictions: {Evictions} | BucketDrops: {BucketDrops}";
#else
            public static string DumpCacheStats() =>
                $"Hits: {CacheHits} | Misses: {CacheMisses} | Errors: {Errors} | GenAct: {_currentGenerating} | Mem: {CurrentCacheMB:0.00} MB / 50.00 MB | AvgGenMs: {(CacheMisses == 0 ? 0 : TotalGenTimeMs / CacheMisses)} | Timeouts: {SemaphoreTimeouts} | Evictions: {Evictions} | BucketDrops: {BucketDrops}";
#endif

            // New metrics
            public static int SemaphoreTimeouts = 0;
            public static int Evictions = 0;
            public static int BucketDrops = 0;
        }
        #endregion

        #region Cache and concurrency

        // 🔥 FIX PRO: Límite estricto de LOH para .NET 4.8.
#if NETFRAMEWORK
        private const long MAX_CACHE_BYTES = 30L * 1024 * 1024; // 30 MB en .NET 4.8
#else
        private const long MAX_CACHE_BYTES = 50L * 1024 * 1024; // 50 MB en .NET 8/9
#endif
        private static long _currentCacheBytes = 0;

        private static readonly Dictionary<ShadowKey, CachedShadowRef> _shadowCache = new Dictionary<ShadowKey, CachedShadowRef>();
        private static readonly LinkedList<ShadowKey> _lruList = new LinkedList<ShadowKey>();
        private static readonly ConcurrentDictionary<ShadowKey, bool> _isGenerating = new ConcurrentDictionary<ShadowKey, bool>();
        private static readonly object _cacheLock = new object();

        private static readonly SemaphoreSlim _generationSemaphore = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount - 1));

        private static volatile bool _isClearing = false;
        private static int _currentGenerating = 0;
        #endregion

        #region Worker queue and buffer buckets

        private class GenerationRequest
        {
            public ShadowKey Key;
            public GraphicsPath? Path;
            public Color ShadowColor;
            public int BlurRadius;
            public float DpiScale;
            public float OffsetX;
            public float OffsetY;
            public Control? Owner;
            public Rectangle Bounds;
            public FillMode FillMode;
        }

        private static readonly ConcurrentQueue<GenerationRequest> _workQueue = new ConcurrentQueue<GenerationRequest>();
        private static readonly SemaphoreSlim _workAvailable = new SemaphoreSlim(0);
        private static readonly int _workerCount = Math.Max(1, Environment.ProcessorCount - 1);
        private static readonly List<Task> _workers = new List<Task>();
        private static volatile bool _workersRunning = false;

        // Buffer buckets: normalize sizes to power-of-two buckets to reduce fragmentation
        private static readonly int[] _bucketSizes = new[] { 1024, 4 * 1024, 16 * 1024, 64 * 1024, 256 * 1024, 1024 * 1024, 4 * 1024 * 1024 };
        private static readonly ConcurrentDictionary<int, ConcurrentBag<byte[]>> _bufferBuckets = new ConcurrentDictionary<int, ConcurrentBag<byte[]>>();
        private const int MAX_BUCKET_ITEMS = 4; // limit per bucket to avoid unbounded growth

        private static int GetBucketSize(int bytes)
        {
            foreach (var b in _bucketSizes)
            {
                if (bytes <= b) return b;
            }
            return _bucketSizes[_bucketSizes.Length - 1];
        }

        // Rent buffer: small buffers use ArrayPool, large buffers use bucketed bags
        private static byte[] RentBucketedBuffer(int bytes, out bool fromPool)
        {
            if (bytes <= 16 * 1024)
            {
                fromPool = true;
                return ArrayPool<byte>.Shared.Rent(bytes);
            }

            fromPool = false;
            int bucket = GetBucketSize(bytes);
            var bag = _bufferBuckets.GetOrAdd(bucket, _ => new ConcurrentBag<byte[]>());
            if (bag.TryTake(out var buf) && buf.Length >= bytes) return buf;
            return new byte[bucket];
        }

        // Return buffer: respect bucket limits; small buffers go back to ArrayPool
        private static void ReturnBucketedBuffer(byte[] buf, bool fromPool)
        {
            if (buf == null) return;

            if (fromPool)
            {
                try { ArrayPool<byte>.Shared.Return(buf, clearArray: false); } catch { }
                return;
            }

            int bucket = GetBucketSize(buf.Length);
            var bag = _bufferBuckets.GetOrAdd(bucket, _ => new ConcurrentBag<byte[]>());

            // Use bag.Count as heuristic; it's O(n) but acceptable for small counts
            try
            {
                if (bag.Count >= MAX_BUCKET_ITEMS)
                {
                    // drop buffer to avoid unbounded growth
                    Interlocked.Increment(ref Metrics.BucketDrops);
                    return;
                }
                bag.Add(buf);
            }
            catch
            {
                // If anything goes wrong, drop buffer
                Interlocked.Increment(ref Metrics.BucketDrops);
            }
        }

        private static void EnsureWorkersRunning()
        {
            if (_workersRunning) return;
            lock (_workers)
            {
                if (_workersRunning) return;
                _workersRunning = true;
                for (int i = 0; i < _workerCount; i++)
                {
                    var t = Task.Factory.StartNew(WorkerLoop, TaskCreationOptions.LongRunning);
                    _workers.Add(t);
                }
            }
        }

        private static async Task WorkerLoop()
        {
            while (_workersRunning)
            {
                try
                {
                    await _workAvailable.WaitAsync().ConfigureAwait(false);
                }
                catch
                {
                    continue;
                }

                if (!_workersRunning) break;

                if (_workQueue.TryDequeue(out var req))
                {
                    if (_isClearing)
                    {
                        _isGenerating.TryRemove(req.Key, out _);
                        try { req.Path?.Dispose(); } catch { }
                        continue;
                    }

                    bool acquired = false;
                    try
                    {
                        acquired = await _generationSemaphore.WaitAsync(3000).ConfigureAwait(false);
                        if (!acquired)
                        {
                            Interlocked.Increment(ref Metrics.SemaphoreTimeouts);
                            _isGenerating.TryRemove(req.Key, out _);
                            try { req.Path?.Dispose(); } catch { }
                            continue;
                        }
                    }
                    catch
                    {
                        Interlocked.Increment(ref Metrics.SemaphoreTimeouts);
                        _isGenerating.TryRemove(req.Key, out _);
                        try { req.Path?.Dispose(); } catch { }
                        continue;
                    }

                    Interlocked.Increment(ref _currentGenerating);
                    try
                    {
                        if (_isClearing) continue;

                        try
                        {
                            Bitmap newShadow = null!;
                            int finalStride = 0, finalHeight = 0;
                            var sw = Stopwatch.StartNew();

                            try
                            {
                                // 🔥 FIX PRO: Aislamiento del Path para prevenir fugas si falla el constructor
                                if (req.Path != null && req.Path.PathData.Points != null && req.Path.PathData.Types != null)
                                {
                                    using (var bgPath = new GraphicsPath(req.Path.PathData.Points, req.Path.PathData.Types) { FillMode = req.FillMode })
                                    {
                                        newShadow = GenerateShadowBitmap(bgPath, req.ShadowColor, req.BlurRadius, req.DpiScale, out finalStride, out finalHeight);
                                    }
                                }
                            }
                            catch (Exception pathEx)
                            {
                                Trace.TraceError($"[GdiRenderer.Worker] Path instantiation error: {pathEx}");
                            }

                            sw.Stop();
                            Interlocked.Add(ref Metrics.TotalGenTimeMs, sw.ElapsedMilliseconds);

                            if (newShadow != null)
                            {
                                AddToCache(req.Key, newShadow, finalStride, finalHeight);
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref Metrics.Errors);
                            Trace.TraceError($"[GdiRenderer.Worker] Shadow generation error: {ex}");
                        }

                        if (req.Owner != null && !req.Owner.IsDisposed && req.Owner.IsHandleCreated && !_isClearing)
                        {
                            try
                            {
                                var invalidRect = req.Bounds;
                                req.Owner.BeginInvoke(new Action(() =>
                                {
                                    if (!req.Owner.IsDisposed) req.Owner.Invalidate(invalidRect);
                                }));
                            }
                            catch { /* swallow */ }
                        }
                    }
                    finally
                    {
                        // Always release semaphore and cleanup
                        try { _generationSemaphore.Release(); } catch { }
                        _isGenerating.TryRemove(req.Key, out _);
                        Interlocked.Decrement(ref _currentGenerating);
                        try { req.Path?.Dispose(); } catch { }
                    }
                }
            }
        }

        public static void ShutdownWorkers()
        {
            _workersRunning = false;
            for (int i = 0; i < _workerCount; i++) _workAvailable.Release();
            try { Task.WaitAll(_workers.ToArray(), 2000); } catch { }
            _workers.Clear();
        }

        #endregion

        #region Public API

        public static void ClearCache()
        {
            lock (_cacheLock) { _isClearing = true; }

            var sw = Stopwatch.StartNew();
            while (Interlocked.CompareExchange(ref _currentGenerating, 0, 0) != 0 && sw.ElapsedMilliseconds < 2000)
            {
                Thread.Sleep(10);
            }

            lock (_cacheLock)
            {
                foreach (var item in _shadowCache.Values)
                {
                    try { item.Release(); } catch { /* swallow */ }
                }

                _shadowCache.Clear();
                _lruList.Clear();
                _currentCacheBytes = 0;
                _isClearing = false;
            }
        }

        public static void DrawSoftShadowAsync(Control owner, Graphics g, GraphicsPath path, Color shadowColor, float blurRadius, float offsetX, float offsetY, float dpiScale = 1.0f)
        {
            if (blurRadius <= 0 || shadowColor.A == 0 || path.PointCount == 0) return;

            int dpiNorm = (int)Math.Round(dpiScale * 100f);
            var key = new ShadowKey(ComputePathHash(path), (int)blurRadius, (int)offsetX, (int)offsetY, shadowColor.A, shadowColor.R, shadowColor.G, shadowColor.B, dpiNorm);

            CachedShadowRef? localRef = null;

            lock (_cacheLock)
            {
                if (!_isClearing && _shadowCache.TryGetValue(key, out var refItem))
                {
                    Interlocked.Increment(ref Metrics.CacheHits);
                    localRef = refItem;
                    localRef.AddRef();
                    try { _lruList.Remove(key); } catch { }
                    _lruList.AddFirst(key);
                }
            }

            if (localRef != null)
            {
                try
                {
                    if (localRef.Image != null)
                        DrawBitmapShadow(g, localRef.Image, path, blurRadius, offsetX, offsetY, dpiScale);
                }
                finally
                {
                    localRef.Release();
                }
                return;
            }

            Interlocked.Increment(ref Metrics.CacheMisses);

            if (_isClearing) return;
            if (!_isGenerating.TryAdd(key, true)) return;

            var bounds = Rectangle.Ceiling(path.GetBounds());
            int padding = (int)Math.Ceiling(blurRadius * 3 * dpiScale);
            var invalidRect = new Rectangle(
                bounds.Left - padding + (int)offsetX,
                bounds.Top - padding + (int)offsetY,
                Math.Max(1, (int)(bounds.Width + padding * 2)),
                Math.Max(1, (int)(bounds.Height + padding * 2)));

            var req = new GenerationRequest
            {
                Key = key,
                Path = (GraphicsPath)path.Clone(),
                ShadowColor = shadowColor,
                BlurRadius = (int)blurRadius,
                DpiScale = dpiScale,
                OffsetX = offsetX,
                OffsetY = offsetY,
                Owner = owner,
                Bounds = invalidRect,
                FillMode = path.FillMode
            };

            EnsureWorkersRunning();
            _workQueue.Enqueue(req);
            _workAvailable.Release();
        }

        #endregion

        #region Cache management
        private static void AddToCache(ShadowKey key, Bitmap bmp, int stride, int height)
        {
            long sizeInBytes = Math.Abs(stride) * (long)height;
            if (sizeInBytes >= MAX_CACHE_BYTES)
            {
                try { bmp.Dispose(); } catch { }
                return;
            }

            var cachedItem = new CachedShadowRef(bmp, sizeInBytes);

            lock (_cacheLock)
            {
                if (_isClearing) { try { bmp.Dispose(); } catch { } return; }

                while (_currentCacheBytes + sizeInBytes > MAX_CACHE_BYTES && _lruList.Count > 0)
                {
                    var oldest = _lruList.Last!.Value;
                    if (_shadowCache.TryGetValue(oldest, out var oldItem))
                    {
                        _currentCacheBytes -= oldItem.SizeInBytes;
                        oldItem.Release();
                        _shadowCache.Remove(oldest);
                        Interlocked.Increment(ref Metrics.Evictions);
                    }
                    _lruList.RemoveLast();
                }

                if (_isClearing) { try { bmp.Dispose(); } catch { } return; }

                _shadowCache[key] = cachedItem;
                _lruList.AddFirst(key);
                _currentCacheBytes += sizeInBytes;
            }
        }
        #endregion

        #region Path hashing & drawing helpers

        private static string ComputePathHash(GraphicsPath path)
        {
            var pd = path.PathData;
            uint hash = 2166136261;
            foreach (var pt in pd.Points!)
            {
                hash = (hash ^ (uint)pt.X.GetHashCode()) * 16777619;
                hash = (hash ^ (uint)pt.Y.GetHashCode()) * 16777619;
            }
            foreach (var type in pd.Types!) hash = (hash ^ type) * 16777619;
            hash = (hash ^ (uint)path.FillMode) * 16777619;
            return hash.ToString("X");
        }

        public static void ApplyHighQuality(Graphics g)
        {
            g.CompositingMode = CompositingMode.SourceOver;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        }

        private static void DrawBitmapShadow(Graphics g, Bitmap shadowBmp, GraphicsPath path, float blurRadius, float offsetX, float offsetY, float dpiScale)
        {
            if (shadowBmp == null) return;

            var bounds = Rectangle.Ceiling(path.GetBounds());
            int padding = (int)Math.Ceiling(blurRadius * 3 * dpiScale);
            var destRect = new Rectangle(bounds.Left - padding + (int)offsetX, bounds.Top - padding + (int)offsetY, shadowBmp.Width, shadowBmp.Height);

            var oldMode = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceOver;
            using (var imgAttr = new ImageAttributes())
            {
                imgAttr.SetWrapMode(WrapMode.Clamp);
                try
                {
                    g.DrawImage(shadowBmp, destRect, 0, 0, shadowBmp.Width, shadowBmp.Height, GraphicsUnit.Pixel, imgAttr);
                }
                catch (ArgumentException) { /* Ignorar: purgado de cache */ }
                catch (ObjectDisposedException) { /* Ignorar: liberado concurrentemente */ }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref Metrics.Errors);
                    Trace.TraceError($"[GdiRenderer] DrawBitmapShadow error: {ex}");
                }
            }
            g.CompositingMode = oldMode;
        }

        #endregion

        #region Shadow bitmap generation
        private static Bitmap GenerateShadowBitmap(GraphicsPath path, Color shadowColor, int blurRadius, float dpiScale, out int outStride, out int outHeight)
        {
            var bounds = Rectangle.Ceiling(path.GetBounds());
            int padding = (int)Math.Ceiling(blurRadius * 3 * dpiScale);
            int w = Math.Max(1, bounds.Width + padding * 2);
            int h = Math.Max(1, bounds.Height + padding * 2);
            outHeight = h;

            Bitmap maskBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            try
            {
                using (var bg = Graphics.FromImage(maskBmp))
                {
                    ApplyHighQuality(bg);
                    bg.Clear(Color.Transparent);
                    bg.TranslateTransform(padding - bounds.Left, padding - bounds.Top);
                    using (var brush = new SolidBrush(Color.White)) bg.FillPath(brush, path);
                }

                var rect = new Rectangle(0, 0, w, h);
                var data = maskBmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try
                {
                    outStride = data.Stride;
                    int bytesCount = Math.Abs(data.Stride) * h;

                    byte[] pixels = RentBucketedBuffer(bytesCount, out bool fromPool);
                    try
                    {
                        Marshal.Copy(data.Scan0, pixels, 0, bytesCount);

                        // Use alpha-only blur in-place (StackBlurUltra must be present)
                        StackBlurUltra.BlurAlpha(pixels, pixels, w, h, blurRadius);

                        float finalAlphaMult = shadowColor.A / 255f;
                        float r = shadowColor.R, gC = shadowColor.G, b = shadowColor.B;

                        for (int i = 0; i < w * h; i++)
                        {
                            int idx = i * 4;
                            float a = pixels[idx + 3] * finalAlphaMult;
                            int ai = (int)Math.Round(a);
                            if (ai < 0) ai = 0; else if (ai > 255) ai = 255;

                            pixels[idx + 0] = (byte)((b * ai) / 255);
                            pixels[idx + 1] = (byte)((gC * ai) / 255);
                            pixels[idx + 2] = (byte)((r * ai) / 255);
                            pixels[idx + 3] = (byte)ai;
                        }

                        var finalBmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                        var finalData = finalBmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                        try
                        {
                            int finalStride = Math.Abs(finalData.Stride);
                            int srcStride = Math.Abs(data.Stride);

                            if (finalStride == srcStride)
                            {
                                Marshal.Copy(pixels, 0, finalData.Scan0, bytesCount);
                            }
                            else
                            {
                                IntPtr dstPtr = finalData.Scan0;
                                for (int rowY = 0; rowY < h; rowY++)
                                {
                                    IntPtr dstRowPtr = IntPtr.Add(dstPtr, rowY * finalData.Stride);
                                    int srcOffset = rowY * srcStride;
                                    Marshal.Copy(pixels, srcOffset, dstRowPtr, Math.Min(srcStride, finalStride));
                                }
                            }
                        }
                        finally
                        {
                            finalBmp.UnlockBits(finalData);
                        }
                        return finalBmp;
                    }
                    finally
                    {
                        ReturnBucketedBuffer(pixels, fromPool);
                    }
                }
                finally
                {
                    maskBmp.UnlockBits(data);
                }
            }
            finally
            {
                maskBmp.Dispose();
            }
        }
        #endregion

        #region Geometry helpers
        public static GraphicsPath CreateRoundedRectPath(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float minDim = Math.Min(rect.Width, rect.Height);
            if (radius <= 0 || minDim <= 0) { path.AddRectangle(rect); return path; }
            float d = Math.Min(radius * 2, minDim);

            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void DrawInnerShadow(Graphics g, RectangleF rect, float radius, Color shadowColor, float thickness)
        {
            using (var path = CreateRoundedRectPath(rect, radius))
            {
                var state = g.Save();
                g.SetClip(path);
                using (var pen = new Pen(shadowColor, thickness * 2)) g.DrawPath(pen, path);
                g.Restore(state);
            }
        }

        public static void FillLinearGradient(Graphics g, RectangleF rect, float radius, Color c1, Color c2, LinearGradientMode mode = LinearGradientMode.Horizontal)
        {
            using var path = CreateRoundedRectPath(rect, radius);
            using var lg = new LinearGradientBrush(rect, c1, c2, mode);
            g.FillPath(lg, path);
        }

        public static void FillRadialGlow(Graphics g, RectangleF rect, Color centerColor, Color edgeColor)
        {
            using var path = new GraphicsPath();
            path.AddEllipse(rect);
            using var pgb = new PathGradientBrush(path)
            {
                CenterColor = centerColor,
                SurroundColors = new[] { edgeColor }
            };
            g.FillPath(pgb, path);
        }

        public static Color LerpColor(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            float aF = a.A + (b.A - a.A) * t;
            float rF = a.R + (b.R - a.R) * t;
            float gF = a.G + (b.G - a.G) * t;
            float bF = a.B + (b.B - a.B) * t;
            return Color.FromArgb((int)aF, (int)rF, (int)gF, (int)bF);
        }
        #endregion
    }
}