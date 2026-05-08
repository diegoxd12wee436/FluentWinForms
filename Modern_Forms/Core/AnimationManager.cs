#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Modern_Forms.Core
{
    // -------------------------------------------------------------------------
    // AnimationManager vFinalPlus (Enterprise / WinUI Level)
    // - Alta resolución (Stopwatch) & Zero-alloc snapshot.
    // - Hybrid frame pacing (Task.Delay + short spin).
    // - Métricas Avanzadas: p95, p99, Forced Frames, y monitor de latencia.
    // - CPU/Batería monitorizadas Out-Of-Band (Hilo secundario dedicado).
    // - Lifecycle Público (Start/Stop) y Debounce en ForceFrame.
    // - SceneInvalidator vía SynchronizationContext (UI Thread seguro sin Task.Run).
    // -------------------------------------------------------------------------
    public static class AnimationManager
    {
        public enum FpsMode { Fps60 = 60, Fps120 = 120, Unlocked = 0 }

        // Suscriptores (WeakReferences)
        private static readonly List<WeakReference<ModernControlBase>> _subs = new List<WeakReference<ModernControlBase>>();
        private static readonly List<ModernControlBase> _snapshotBuffer = new List<ModernControlBase>(256);
        private static readonly object _lock = new object();

        // Control del loop
        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;
        private static volatile bool _isRunning = false;
        private static TimeSpan _targetInterval = TimeSpan.Zero;

        // Housekeeping
        private const int CompactEveryTicks = 120;
        private static int _tickCounter = 0;

        // Adaptive behavior
        private static bool _adaptiveEnabled = true;
        private static double _adaptiveCpuThreshold = 0.70;
        private static int _adaptiveLowFps = 30;
        private static int _adaptiveVeryLowFps = 15;
        private static bool _pauseWhenAllHidden = true;

        // Thread tuning
        private static ThreadPriority _loopThreadPriority = ThreadPriority.AboveNormal;
        private static bool _setThreadPriority = true;

        // Allow host to force high FPS on battery globally
        private static bool _allowHighFpsOnBattery = false;

        // Wake mechanism: cancel token para interrumpir Task.Delay
        private static CancellationTokenSource _wakeCts = new CancellationTokenSource();
        private static long _lastForceFrameTicks = 0; // Para el Debounce
        private static long _forcedFrames = 0;

        // Métricas
        private static long _frames = 0;
        private static double _accDt = 0;
        private static long _frameDrops = 0;
        private static double _maxDt = 0;
        private static readonly object _metricsLock = new object();
        private static readonly Stopwatch _uptime = Stopwatch.StartNew();

        // Búfer circular para P95 y P99
        private const int FrameHistorySize = 1000;
        private static readonly double[] _frameTimes = new double[FrameHistorySize];
        private static int _frameTimeIndex = 0;

        // Evento para invalidación coalesced (escena)
        public static event Action<double>? FrameTicked; // dt en ms

        // ---------------------------------------------------------------------
        // Extensiones: proveedor de métricas
        // ---------------------------------------------------------------------
        public interface ISystemMetricsProvider
        {
            double GetCpuLoad();
            bool IsOnBattery();
        }

        private class DefaultMetricsProvider : ISystemMetricsProvider
        {
            public double GetCpuLoad() => 0.0;
            public bool IsOnBattery() => false;
        }

        private static ISystemMetricsProvider _metricsProvider = new DefaultMetricsProvider();

        public static void SetSystemMetricsProvider(ISystemMetricsProvider provider)
        {
            _metricsProvider = provider ?? new DefaultMetricsProvider();
        }

        public static bool AllowHighFpsOnBattery
        {
            get => _allowHighFpsOnBattery;
            set => _allowHighFpsOnBattery = value;
        }

        public interface IHighFpsPreference
        {
            bool PreferHighFpsOnBattery { get; }
        }

        // ---------------------------------------------------------------------
        // API pública de configuración y estado
        // ---------------------------------------------------------------------
        public static FpsMode Mode
        {
            get => _targetInterval == TimeSpan.Zero ? FpsMode.Unlocked : (FpsMode)Math.Round(1000.0 / _targetInterval.TotalMilliseconds);
            set
            {
                if (value == FpsMode.Unlocked) _targetInterval = TimeSpan.Zero;
                else
                {
                    if (!Enum.IsDefined(typeof(FpsMode), value)) throw new ArgumentOutOfRangeException(nameof(value));
                    _targetInterval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / (int)value);
                }
            }
        }

        public static bool AdaptiveEnabled { get => _adaptiveEnabled; set => _adaptiveEnabled = value; }
        public static bool PauseWhenAllHidden { get => _pauseWhenAllHidden; set => _pauseWhenAllHidden = value; }

        public static void ConfigureThreadPriority(bool enable, ThreadPriority priority = ThreadPriority.AboveNormal)
        {
            _setThreadPriority = enable;
            _loopThreadPriority = priority;
        }

        public static void ConfigureAdaptive(double cpuThreshold, int lowFps = 30, int veryLowFps = 15)
        {
            _adaptiveCpuThreshold = Math.Max(0.0, Math.Min(1.0, cpuThreshold));
            _adaptiveLowFps = Math.Max(1, lowFps);
            _adaptiveVeryLowFps = Math.Max(1, veryLowFps);
        }

        public static bool IsRunning => _isRunning;

        /// <summary>
        /// Forzar un frame inmediato (interrumpe Task.Delay sin bloquear hilos).
        /// Incluye Debounce de 1 milisegundo para evitar Wake-Spam y saturación de GC.
        /// </summary>
        public static void ForceFrame()
        {
            long nowTicks = Stopwatch.GetTimestamp();

            // Debounce: Si pasaron menos de 1ms desde el último ForceFrame, lo ignoramos.
            if (nowTicks - Interlocked.Read(ref _lastForceFrameTicks) < (Stopwatch.Frequency / 1000))
                return;

            Interlocked.Exchange(ref _lastForceFrameTicks, nowTicks);
            Interlocked.Increment(ref _forcedFrames);

            try
            {
                var prev = Interlocked.Exchange(ref _wakeCts, new CancellationTokenSource());
                try { prev?.Cancel(); } catch { }
                try { prev?.Dispose(); } catch { }
            }
            catch { /* swallow */ }
        }

        // ---------------------------------------------------------------------
        // Lifecycle Explícito (Host Control) y Registro
        // ---------------------------------------------------------------------

        /// <summary>Arranca explícitamente el bucle de animación.</summary>
        public static void Start()
        {
            if (_isRunning) return;
            _cts = new CancellationTokenSource();
            var oldWake = Interlocked.Exchange(ref _wakeCts, new CancellationTokenSource());
            try { oldWake?.Cancel(); oldWake?.Dispose(); } catch { }
            _isRunning = true;
            _loopTask = Task.Factory.StartNew(() => LoopAsync(_cts.Token), _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        }

        /// <summary>Detiene explícitamente el bucle de animación (espera graciosa de 500ms).</summary>
        public static void Stop()
        {
            if (!_isRunning) return;
            try
            {
                _cts?.Cancel();
                try { _wakeCts?.Cancel(); } catch { }
                _loopTask?.Wait(500);
            }
            catch { /* swallow */ }
            finally
            {
                try { _cts?.Dispose(); } catch { }
                try { _wakeCts?.Dispose(); } catch { }
                _cts = null!;
                _wakeCts = new CancellationTokenSource();
                _loopTask = null!;
                _isRunning = false;
            }
        }

        public static void Register(ModernControlBase c)
        {
            if (c == null) return;
            lock (_lock)
            {
                for (int i = 0; i < _subs.Count; i++)
                {
                    if (_subs[i].TryGetTarget(out var t) && ReferenceEquals(t, c)) return;
                }
                _subs.Add(new WeakReference<ModernControlBase>(c));
                if (!_isRunning) Start();
            }
        }

        public static void Unregister(ModernControlBase c)
        {
            if (c == null) return;
            lock (_lock)
            {
                for (int i = _subs.Count - 1; i >= 0; i--)
                {
                    if (!_subs[i].TryGetTarget(out var t) || ReferenceEquals(t, c))
                    {
                        _subs.RemoveAt(i);
                    }
                }
                if (_subs.Count == 0) Stop();
            }
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                Stop();
                _subs.Clear();
            }
        }

        // ---------------------------------------------------------------------
        // Loop interno (Corazón de Rendering GPU-Ready)
        // ---------------------------------------------------------------------
        private static async Task LoopAsync(CancellationToken ct)
        {
            try { if (_setThreadPriority) Thread.CurrentThread.Priority = _loopThreadPriority; } catch { }

            var sw = Stopwatch.StartNew();
            long lastTicks = sw.ElapsedTicks;
            _tickCounter = 0;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    long nowTicks = sw.ElapsedTicks;
                    double dt = (nowTicks - lastTicks) * 1000.0 / Stopwatch.Frequency;
                    lastTicks = nowTicks;

                    // Snapshot sin allocations
                    _snapshotBuffer.Clear();
                    lock (_lock)
                    {
                        if (_subs.Count == 0)
                        {
                            _isRunning = false;
                            return;
                        }
                        for (int i = 0; i < _subs.Count; i++)
                        {
                            if (_subs[i].TryGetTarget(out var t) && t != null) _snapshotBuffer.Add(t);
                        }
                    }

                    // Pause cuando todo está oculto
                    if (_pauseWhenAllHidden)
                    {
                        bool anyVisible = false;
                        for (int i = 0; i < _snapshotBuffer.Count; i++)
                        {
                            try { var c = _snapshotBuffer[i]; if (c != null && c.Visible && c.IsHandleCreated) { anyVisible = true; break; } }
                            catch { }
                        }
                        if (!anyVisible)
                        {
                            try
                            {
                                var currentWakeCts = _wakeCts;
                                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, currentWakeCts.Token))
                                {
                                    await Task.Delay(250, linked.Token).ConfigureAwait(false);
                                }
                            }
                            catch (OperationCanceledException) { /* wake o shutdown */ }
                            catch (ObjectDisposedException) { /* CTS swap */ }

                            if (ct.IsCancellationRequested) break;
                            continue;
                        }
                    }

                    // Decisión de FPS adaptativo
                    TimeSpan effectiveTarget = _targetInterval;
                    if (_adaptiveEnabled && _targetInterval > TimeSpan.Zero)
                    {
                        double cpuLoad = _metricsProvider?.GetCpuLoad() ?? 0.0;
                        bool onBattery = _metricsProvider?.IsOnBattery() ?? false;

                        if (onBattery && !_allowHighFpsOnBattery)
                        {
                            bool anyControlPrefersHigh = false;
                            for (int i = 0; i < _snapshotBuffer.Count; i++)
                            {
                                try
                                {
                                    var c = _snapshotBuffer[i];
                                    if (c is IHighFpsPreference pref && pref.PreferHighFpsOnBattery)
                                    {
                                        anyControlPrefersHigh = true;
                                        break;
                                    }
                                }
                                catch { }
                            }

                            if (!anyControlPrefersHigh)
                            {
                                effectiveTarget = TimeSpan.FromMilliseconds(1000.0 / Math.Max(30, (int)Mode));
                            }
                        }
                        else if (onBattery && _allowHighFpsOnBattery)
                        {
                            // mantener Mode
                        }
                        else
                        {
                            if (cpuLoad >= _adaptiveCpuThreshold)
                            {
                                effectiveTarget = TimeSpan.FromMilliseconds(1000.0 / _adaptiveLowFps);
                                if (cpuLoad > Math.Min(1.0, _adaptiveCpuThreshold + 0.15))
                                    effectiveTarget = TimeSpan.FromMilliseconds(1000.0 / _adaptiveVeryLowFps);
                            }
                        }
                    }

                    // Notificar controles
                    for (int i = 0; i < _snapshotBuffer.Count; i++)
                    {
                        var c = _snapshotBuffer[i];
                        if (c == null) continue;
                        try { c.AnimationTick((float)dt); } catch { }
                    }

                    // Hook coalesced para invalidación global
                    try { FrameTicked?.Invoke(dt); } catch { }

                    // Métricas
                    Interlocked.Increment(ref _frames);
                    lock (_metricsLock)
                    {
                        _accDt += dt;
                        if (dt > _maxDt) _maxDt = dt;

                        _frameTimes[_frameTimeIndex] = dt;
                        _frameTimeIndex = (_frameTimeIndex + 1) % FrameHistorySize;
                    }

                    // Compactación in-place ocasional
                    _tickCounter++;
                    if (_tickCounter >= CompactEveryTicks)
                    {
                        lock (_lock)
                        {
                            int write = 0;
                            for (int read = 0; read < _subs.Count; read++)
                            {
                                if (_subs[read].TryGetTarget(out var t))
                                {
                                    if (write != read) _subs[write] = _subs[read];
                                    write++;
                                }
                            }
                            if (write < _subs.Count) _subs.RemoveRange(write, _subs.Count - write);
                        }
                        _tickCounter = 0;
                    }

                    // Frame pacing híbrido
                    if (effectiveTarget > TimeSpan.Zero)
                    {
                        long targetTicks = effectiveTarget.Ticks;
                        long elapsedForFrameTicks = sw.ElapsedTicks - nowTicks;
                        long waitTicks = targetTicks - elapsedForFrameTicks;

                        if (waitTicks > 0)
                        {
                            if (waitTicks > TimeSpan.TicksPerMillisecond * 2)
                            {
                                int ms = (int)(waitTicks / TimeSpan.TicksPerMillisecond) - 1;
                                try
                                {
                                    var currentWakeCts = _wakeCts;
                                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, currentWakeCts.Token))
                                    {
                                        await Task.Delay(ms, linked.Token).ConfigureAwait(false);
                                    }
                                }
                                catch (OperationCanceledException) { }
                                catch (ObjectDisposedException) { }
                            }

                            while ((sw.ElapsedTicks - nowTicks) < targetTicks)
                            {
                                if (ct.IsCancellationRequested) break;
                                if (_wakeCts.IsCancellationRequested) break;
                                Thread.SpinWait(1);
                            }
                        }
                        else
                        {
                            Interlocked.Increment(ref _frameDrops);
                            await Task.Yield();
                        }
                    }
                    else
                    {
                        try
                        {
                            var currentWakeCts = _wakeCts;
                            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, currentWakeCts.Token))
                            {
                                await Task.Delay(1, linked.Token).ConfigureAwait(false);
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch (ObjectDisposedException) { }
                        await Task.Yield();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { try { Trace.TraceError($"[AnimationManager] Loop error: {ex}"); } catch { } }
            finally
            {
                _isRunning = false;
                sw.Stop();
            }
        }

        #region Diagnostics & Metrics API

        public static long Frames => Interlocked.Read(ref _frames);
        public static long ForcedFrames => Interlocked.Read(ref _forcedFrames);
        public static long FrameDrops => Interlocked.Read(ref _frameDrops);
        public static TimeSpan Uptime => _uptime.Elapsed;

        public static double AvgDtMs
        {
            get { lock (_metricsLock) { var f = Interlocked.Read(ref _frames); return f == 0 ? 0.0 : _accDt / f; } }
        }

        public static double MaxDtMs { get { lock (_metricsLock) { return _maxDt; } } }

        public static double P95FrameTimeMs => GetPercentile(0.95);
        public static double P99FrameTimeMs => GetPercentile(0.99);

        private static double GetPercentile(double percentile)
        {
            lock (_metricsLock)
            {
                int count = (int)Math.Min(_frames, FrameHistorySize);
                if (count == 0) return 0.0;

                // 🔥 INYECCIÓN PRO: Zero-Allocation. Tomamos prestada la memoria y la devolvemos.
                double[] temp = System.Buffers.ArrayPool<double>.Shared.Rent(count);
                try
                {
                    Array.Copy(_frameTimes, temp, count);
                    Array.Sort(temp, 0, count);

                    int index = (int)Math.Ceiling(percentile * count) - 1;
                    return temp[Math.Max(0, index)];
                }
                finally
                {
                    System.Buffers.ArrayPool<double>.Shared.Return(temp);
                }
            }
        }

        public static int SubscriberCount
        {
            get { lock (_lock) { int c = 0; foreach (var wr in _subs) if (wr.TryGetTarget(out var t)) c++; return c; } }
        }

        public static void ResetMetrics()
        {
            lock (_metricsLock)
            {
                _frames = 0;
                _accDt = 0;
                _frameDrops = 0;
                _maxDt = 0;
                _forcedFrames = 0;
                Array.Clear(_frameTimes, 0, FrameHistorySize);
                _uptime.Restart();
            }
        }

        public static string DumpDiagnostics()
        {
            return $"AnimMgr Running={IsRunning} Mode={Mode} Subs={SubscriberCount} Frames={Frames} " +
                   $"AvgDt={AvgDtMs:0.00}ms P95={P95FrameTimeMs:0.00}ms P99={P99FrameTimeMs:0.00}ms " +
                   $"MaxDt={MaxDtMs:0.00}ms Drops={FrameDrops} Forced={ForcedFrames} Uptime={Uptime.TotalSeconds:0}s";
        }

        #endregion
    }

    // -------------------------------------------------------------------------
    // SceneInvalidator (UI Thread Safe)
    // - Usa SynchronizationContext para evitar Task.Run() innecesarios.
    // - Coalesce automático (batching) garantizando ejecución en el hilo correcto.
    // -------------------------------------------------------------------------
    public sealed class SceneInvalidator : IDisposable
    {
        private readonly Action _invalidateAction;
        private int _pending = 0;
        private bool _disposed = false;

        // 🔥 MEJORA: Captura el contexto de UI automáticamente
        private readonly SynchronizationContext _syncContext;

        public SceneInvalidator(Action invalidateAction)
        {
            _invalidateAction = invalidateAction ?? throw new ArgumentNullException(nameof(invalidateAction));
            _syncContext = SynchronizationContext.Current!; // Captura el hilo UI actual
            AnimationManager.FrameTicked += OnFrameTicked;
        }

        private void OnFrameTicked(double dt)
        {
            if (Interlocked.Exchange(ref _pending, 1) == 0)
            {
                if (_syncContext != null)
                {
                    // Ejecución segura y directa en el Hilo de la UI (Cero Cross-Thread Exceptions)
                    _syncContext.Post(_ =>
                    {
                        try { _invalidateAction(); }
                        catch { /* swallow */ }
                        finally { Interlocked.Exchange(ref _pending, 0); }
                    }, null);
                }
                else
                {
                    // Fallback por si fue inicializado fuera de un hilo UI
                    Task.Run(() =>
                    {
                        try { _invalidateAction(); }
                        catch { /* swallow */ }
                        finally { Interlocked.Exchange(ref _pending, 0); }
                    });
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            AnimationManager.FrameTicked -= OnFrameTicked;
            _disposed = true;
        }
    }

    // -------------------------------------------------------------------------
    // WindowsSystemMetricsProvider (Off-Thread Monitor)
    // - Cachea la CPU usando System.Threading.Timer para NUNCA bloquear el loop.
    // -------------------------------------------------------------------------
    public sealed class WindowsSystemMetricsProvider : AnimationManager.ISystemMetricsProvider, IDisposable
    {
        private PerformanceCounter _cpuCounter;
        private bool _disposed = false;

        // Cachés asíncronos
        private double _cachedCpuLoad = 0.0;
        private bool _cachedBattery = false;
        private System.Threading.Timer _timer;

        public WindowsSystemMetricsProvider()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
                _ = _cpuCounter.NextValue();
            }
            catch
            {
                _cpuCounter = null!;
            }

            // 🔥 MEJORA: Muestreo en hilo secundario cada 1 segundo exacto. Cero carga al Frame Loop.
            _timer = new System.Threading.Timer(UpdateMetrics!, null, 1000, 1000);
        }

        private void UpdateMetrics(object state)
        {
            if (_disposed) return;

            // Actualiza CPU
            try
            {
                if (_cpuCounter != null)
                {
                    float val = _cpuCounter.NextValue();
                    if (!float.IsNaN(val) && !float.IsInfinity(val))
                        _cachedCpuLoad = Math.Max(0.0, Math.Min(1.0, val / 100.0));
                }
            }
            catch { }

            // Actualiza Batería
            try
            {
                SYSTEM_POWER_STATUS sps = new SYSTEM_POWER_STATUS();
                if (GetSystemPowerStatus(out sps))
                {
                    _cachedBattery = (sps.ACLineStatus == 0);
                }
            }
            catch { }
        }

        public double GetCpuLoad() => _cachedCpuLoad;

        public bool IsOnBattery() => _cachedBattery;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            try { _cpuCounter?.Dispose(); } catch { }
            _cpuCounter = null!;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte Reserved1;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);
    }
}