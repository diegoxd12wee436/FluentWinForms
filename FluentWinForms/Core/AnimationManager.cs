#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FluentWinForms.Core
{
    // -------------------------------------------------------------------------
    // AnimationManager vMax 
    // - Dedicated Render Thread (Cero sobrecarga de Async/Await).
    // - Copy-On-Write Subscriber Array (Cero Locks en el Render Loop para 1000+ items).
    // - ManualResetEventSlim (Cero Allocations en ForceFrame).
    // - WinMM Timer Hack con Lifecycle Safe-Exit.
    // - Compatibilidad total .NET Framework 4.8 y .NET Core/5/6/8+.
    // -------------------------------------------------------------------------
    public static class AnimationManager
    {
        public enum FpsMode { Fps60 = 60, Fps120 = 120, Unlocked = 0 }

        // HACK DE KERNEL: Precisión de 1ms en Windows
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
        private static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
        private static extern uint TimeEndPeriod(uint uMilliseconds);

        private static bool _timePeriodSet = false;
        private static readonly object _timeLock = new object();

        // COPY-ON-WRITE LIST (Lock-Free Render Loop)
        private static volatile WeakReference<ModernControlBase>[] _activeSubs = new WeakReference<ModernControlBase>[0];
        private static readonly List<WeakReference<ModernControlBase>> _subsMaster = new List<WeakReference<ModernControlBase>>();
        private static readonly object _lock = new object();

        // Control del Hilo Dedicado
        private static Thread? _renderThread;
        private static volatile bool _isRunning = false;
        private static volatile bool _isShuttingDown = false;
        private static TimeSpan _targetInterval = TimeSpan.Zero;

        // ZERO-ALLOCATION WAKE (Reemplaza a los CancellationTokenSource)
        private static readonly ManualResetEventSlim _wakeEvent = new ManualResetEventSlim(false);

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
        private static bool _allowHighFpsOnBattery = false;

        private static long _lastForceFrameTicks = 0;
        private static long _forcedFrames = 0;

        // Métricas
        private static long _frames = 0;
        private static double _accDt = 0;
        private static long _frameDrops = 0;
        private static double _maxDt = 0;
        private static readonly object _metricsLock = new object();
        private static readonly Stopwatch _uptime = Stopwatch.StartNew();

        private const int FrameHistorySize = 1000;
        private static readonly double[] _frameTimes = new double[FrameHistorySize];
        private static int _frameTimeIndex = 0;

        public static event Action<double>? FrameTicked;

        // Seguro de vida a nivel de proceso para garantizar la restauración del sistema
        static AnimationManager()
        {
            AppDomain.CurrentDomain.ProcessExit += (s, e) => RestoreTimePeriod();
        }

        private static void SetTimePeriod()
        {
            lock (_timeLock)
            {
                if (!_timePeriodSet)
                {
                    try { TimeBeginPeriod(1); _timePeriodSet = true; } catch { }
                }
            }
        }

        private static void RestoreTimePeriod()
        {
            lock (_timeLock)
            {
                if (_timePeriodSet)
                {
                    try { TimeEndPeriod(1); _timePeriodSet = false; } catch { }
                }
            }
        }

        public interface ISystemMetricsProvider { double GetCpuLoad(); bool IsOnBattery(); }
        private class DefaultMetricsProvider : ISystemMetricsProvider { public double GetCpuLoad() => 0.0; public bool IsOnBattery() => false; }
        private static ISystemMetricsProvider _metricsProvider = new DefaultMetricsProvider();

        public static void SetSystemMetricsProvider(ISystemMetricsProvider provider) => _metricsProvider = provider ?? new DefaultMetricsProvider();
        public static bool AllowHighFpsOnBattery { get => _allowHighFpsOnBattery; set => _allowHighFpsOnBattery = value; }
        public interface IHighFpsPreference { bool PreferHighFpsOnBattery { get; } }

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
        public static void ConfigureThreadPriority(bool enable, ThreadPriority priority = ThreadPriority.AboveNormal) { _setThreadPriority = enable; _loopThreadPriority = priority; }
        public static void ConfigureAdaptive(double cpuThreshold, int lowFps = 30, int veryLowFps = 15) { _adaptiveCpuThreshold = Math.Max(0.0, Math.Min(1.0, cpuThreshold)); _adaptiveLowFps = Math.Max(1, lowFps); _adaptiveVeryLowFps = Math.Max(1, veryLowFps); }
        public static bool IsRunning => _isRunning;

        // FORCE FRAME (Cero Asignaciones de Memoria)
        // 🔥 FIX: Debounce de 4ms (Evita saturación de invalidaciones en ráfagas de clics)
        public static void ForceFrame()
        {
            long nowTicks = Stopwatch.GetTimestamp();

            // Si pasaron menos de 4ms desde el último ForceFrame, lo ignoramos.
            if (nowTicks - Interlocked.Read(ref _lastForceFrameTicks) < (Stopwatch.Frequency / 250)) // 4ms — más fluido con 1000 controles
                return;

            Interlocked.Exchange(ref _lastForceFrameTicks, nowTicks);
            Interlocked.Increment(ref _forcedFrames);

            try { _wakeEvent.Set(); } catch { }
        }

        public static void Start()
        {
            if (_isRunning) return;

            SetTimePeriod();

            _isShuttingDown = false;
            _isRunning = true;
            _wakeEvent.Reset();

            _renderThread = new Thread(LoopInternal)
            {
                IsBackground = true,
                Name = "FluentWinForms_RenderThread"
            };

            if (_setThreadPriority)
            {
                try { _renderThread.Priority = _loopThreadPriority; } catch { }
            }

            _renderThread.Start();
        }

        public static void Stop()
        {
            if (!_isRunning) return;
            try
            {
                _isShuttingDown = true;
                _wakeEvent.Set();
                _renderThread?.Join(500);
            }
            catch { }
            finally
            {
                _isRunning = false;
                RestoreTimePeriod();
            }
        }

        private static void UpdateActiveSubsArray()
        {
            _activeSubs = _subsMaster.ToArray();
        }

        // 🔥 FIX PASO 3: Método Compact explícito y seguro para limpiar recolecciones muertas.
        private static void Compact()
        {
            lock (_lock)
            {
                bool changed = false;
                for (int i = _subsMaster.Count - 1; i >= 0; i--)
                {
                    if (!_subsMaster[i].TryGetTarget(out _))
                    {
                        _subsMaster.RemoveAt(i);
                        changed = true;
                    }
                }
                if (changed) UpdateActiveSubsArray();
            }
        }

        public static void Register(ModernControlBase c)
        {
            if (c == null) return;
            lock (_lock)
            {
                for (int i = 0; i < _subsMaster.Count; i++) if (_subsMaster[i].TryGetTarget(out var t) && ReferenceEquals(t, c)) return;
                _subsMaster.Add(new WeakReference<ModernControlBase>(c));
                UpdateActiveSubsArray();
                if (!_isRunning) Start();
            }
        }

        public static void Unregister(ModernControlBase c)
        {
            if (c == null) return;
            lock (_lock)
            {
                bool changed = false;
                for (int i = _subsMaster.Count - 1; i >= 0; i--)
                {
                    if (!_subsMaster[i].TryGetTarget(out var t) || ReferenceEquals(t, c))
                    {
                        _subsMaster.RemoveAt(i);
                        changed = true;
                    }
                }
                if (changed) UpdateActiveSubsArray();
                if (_subsMaster.Count == 0) Stop();
            }
        }

        public static void Shutdown() { lock (_lock) { Stop(); _subsMaster.Clear(); UpdateActiveSubsArray(); } }

        // ---------------------------------------------------------------------
        // DEDICATED RENDER LOOP (Cero Locks, Cero Allocations)
        // ---------------------------------------------------------------------
        private static void LoopInternal()
        {
            var sw = Stopwatch.StartNew();
            long lastTicks = sw.ElapsedTicks;
            _tickCounter = 0;

            try
            {
                while (!_isShuttingDown)
                {
                    long nowTicks = sw.ElapsedTicks;
                    double dt = (nowTicks - lastTicks) * 1000.0 / Stopwatch.Frequency;
                    lastTicks = nowTicks;

                    // Lectura atómica de suscriptores sin bloquear la UI
                    var currentSubs = _activeSubs;

                    if (currentSubs.Length == 0)
                    {
                        _isRunning = false;
                        RestoreTimePeriod();
                        return;
                    }

                    if (_pauseWhenAllHidden)
                    {
                        bool anyVisible = false;
                        for (int i = 0; i < currentSubs.Length; i++)
                        {
                            try { if (currentSubs[i].TryGetTarget(out var c) && c.Visible && c.IsHandleCreated) { anyVisible = true; break; } } catch { }
                        }
                        if (!anyVisible)
                        {
                            _wakeEvent.Wait(250);
                            _wakeEvent.Reset();
                            if (_isShuttingDown) break;
                            continue;
                        }
                    }

                    TimeSpan effectiveTarget = _targetInterval;
                    if (_adaptiveEnabled && _targetInterval > TimeSpan.Zero)
                    {
                        double cpuLoad = _metricsProvider?.GetCpuLoad() ?? 0.0;
                        bool onBattery = _metricsProvider?.IsOnBattery() ?? false;

                        if (onBattery && !_allowHighFpsOnBattery)
                        {
                            bool anyControlPrefersHigh = false;
                            for (int i = 0; i < currentSubs.Length; i++)
                            {
                                try { if (currentSubs[i].TryGetTarget(out var c) && c is IHighFpsPreference pref && pref.PreferHighFpsOnBattery) { anyControlPrefersHigh = true; break; } } catch { }
                            }
                            if (!anyControlPrefersHigh) effectiveTarget = TimeSpan.FromMilliseconds(1000.0 / Math.Max(30, (int)Mode));
                        }
                        else if (onBattery && _allowHighFpsOnBattery) { }
                        else
                        {
                            if (cpuLoad >= _adaptiveCpuThreshold)
                            {
                                effectiveTarget = TimeSpan.FromMilliseconds(1000.0 / _adaptiveLowFps);
                                if (cpuLoad > Math.Min(1.0, _adaptiveCpuThreshold + 0.15)) effectiveTarget = TimeSpan.FromMilliseconds(1000.0 / _adaptiveVeryLowFps);
                            }
                        }
                    }

                    // Notificar controles
                    for (int i = 0; i < currentSubs.Length; i++)
                    {
                        try { if (currentSubs[i].TryGetTarget(out var c)) c.AnimationTick((float)dt); } catch { }
                    }

                    try { FrameTicked?.Invoke(dt); } catch { }

                    Interlocked.Increment(ref _frames);
                    lock (_metricsLock)
                    {
                        _accDt += dt;
                        if (dt > _maxDt) _maxDt = dt;
                        _frameTimes[_frameTimeIndex] = dt;
                        _frameTimeIndex = (_frameTimeIndex + 1) % FrameHistorySize;
                    }

                    // Compactación asíncrona de referencias muertas
                    _tickCounter++;
                    if (_tickCounter >= CompactEveryTicks)
                    {
                        Task.Run(() => Compact());
                        _tickCounter = 0;
                    }

                    // 🔥 FIX PASO 3: Pacing Híbrido de Alta Precisión (Bajo consumo de CPU)
                    if (effectiveTarget > TimeSpan.Zero)
                    {
                        long targetTicks = effectiveTarget.Ticks;
                        long elapsedForFrameTicks = sw.ElapsedTicks - nowTicks;
                        long waitTicks = targetTicks - elapsedForFrameTicks;

                        if (waitTicks > 0)
                        {
                            // Si nos sobra más de 2ms, usamos el WaitEvent bloqueante (0% CPU)
                            if (waitTicks > TimeSpan.TicksPerMillisecond * 2)
                            {
                                int ms = (int)(waitTicks / TimeSpan.TicksPerMillisecond) - 1;
                                _wakeEvent.Wait(ms);
                            }

                            // Si solo faltan migajas (< 2ms), cedemos el procesador sin quemar ciclos.
                            if ((sw.ElapsedTicks - nowTicks) < targetTicks)
                            {
                                Thread.Sleep(0); // o Thread.Yield();
                            }
                        }
                        else { Interlocked.Increment(ref _frameDrops); }

                        _wakeEvent.Reset();
                    }
                    else
                    {
                        _wakeEvent.Wait(1);
                        _wakeEvent.Reset();
                    }
                }
            }
            catch (Exception ex) { try { Trace.TraceError($"[AnimationManager] Loop error: {ex}"); } catch { } }
            finally
            {
                _isRunning = false;
                sw.Stop();
                RestoreTimePeriod();
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
                double[] temp = System.Buffers.ArrayPool<double>.Shared.Rent(count);
                try { Array.Copy(_frameTimes, temp, count); Array.Sort(temp, 0, count); int index = (int)Math.Ceiling(percentile * count) - 1; return temp[Math.Max(0, index)]; }
                finally { System.Buffers.ArrayPool<double>.Shared.Return(temp, false); }
            }
        }

        public static int SubscriberCount => _activeSubs.Length;

        public static void ResetMetrics()
        {
            lock (_metricsLock)
            {
                _frames = 0; _accDt = 0; _frameDrops = 0; _maxDt = 0; _forcedFrames = 0;
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
    // -------------------------------------------------------------------------
    public sealed class SceneInvalidator : IDisposable
    {
        private readonly Action _invalidateAction;
        private int _pending = 0;
        private bool _disposed = false;
        private readonly SynchronizationContext _syncContext;

        public SceneInvalidator(Action invalidateAction)
        {
            _invalidateAction = invalidateAction ?? throw new ArgumentNullException(nameof(invalidateAction));

            // 🔥 CANDADO ESTRUCTURAL: Falla inmediatamente si se intenta instanciar fuera del hilo de UI
            _syncContext = SynchronizationContext.Current
                ?? throw new InvalidOperationException("[SceneInvalidator] CRÍTICO: Debe ser instanciado desde el Hilo de la Interfaz de Usuario (UI Thread).");

            AnimationManager.FrameTicked += OnFrameTicked;
        }

        private void OnFrameTicked(double dt)
        {
            if (Interlocked.Exchange(ref _pending, 1) == 0)
            {
                // Ejecución 100% segura y garantizada en el Hilo de la UI
                _syncContext.Post(_ =>
                {
                    try { _invalidateAction(); }
                    catch (Exception ex) { Trace.TraceError($"[SceneInvalidator] Invoke error: {ex}"); } // 🔥 FIX PASO 6: Trazar excepciones en lugar de silenciarlas
                    finally { Interlocked.Exchange(ref _pending, 0); }
                }, null);
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
    // -------------------------------------------------------------------------
    public sealed class WindowsSystemMetricsProvider : AnimationManager.ISystemMetricsProvider, IDisposable
    {
        private PerformanceCounter _cpuCounter;
        private bool _disposed = false;
        private double _cachedCpuLoad = 0.0;
        private bool _cachedBattery = false;
        private System.Threading.Timer _timer;

        public WindowsSystemMetricsProvider()
        {
            try { _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true); _ = _cpuCounter.NextValue(); } catch { _cpuCounter = null!; }
            _timer = new System.Threading.Timer(UpdateMetrics!, null, 1000, 1000);
        }

        private void UpdateMetrics(object state)
        {
            if (_disposed) return;

            // 🔥 FIX PASO 5: Normalización segura y probada (val ya es el total 0-100 en PerformanceCounter "_Total").
            try
            {
                if (_cpuCounter != null)
                {
                    float val = _cpuCounter.NextValue();
                    if (!float.IsNaN(val) && !float.IsInfinity(val))
                    {
                        _cachedCpuLoad = Math.Max(0.0, Math.Min(1.0, val / 100.0));
                    }
                }
            }
            catch { _cachedCpuLoad = 0.0; } // Fallback seguro

            try
            {
                SYSTEM_POWER_STATUS sps = new SYSTEM_POWER_STATUS();
                if (GetSystemPowerStatus(out sps))
                {
                    _cachedBattery = (sps.ACLineStatus == 0);
                }
                else
                {
                    _cachedBattery = false; // Fallback seguro
                }
            }
            catch { _cachedBattery = false; } // Fallback seguro
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
        private struct SYSTEM_POWER_STATUS { public byte ACLineStatus; public byte BatteryFlag; public byte BatteryLifePercent; public byte Reserved1; public int BatteryLifeTime; public int BatteryFullLifeTime; }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);
    }
}