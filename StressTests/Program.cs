#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FluentWinForms.Core;
using FluentWinForms.Custom_Controls;
using SkiaSharp;

namespace FluentWinForms.StressTests
{
    /// <summary>
    /// Harness de estrés para FluentWinForms. NO forma parte de la librería.
    ///
    /// Uso:
    ///   dotnet run --project StressTests -- [ciclos]
    ///
    /// Cubre: crear/destruir, hover/click REAL sobre FluentElement (verificado
    /// por reflexión), ripple, animación interrumpida, resize/minimize/restore,
    /// cambio de tema, valores extremos, cambio de DPI y una masa de controles.
    /// Falla (exit != 0) si la memoria crece por encima del 50 % o si el hover
    /// no se activa.
    /// </summary>
    internal static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern int GetGuiResources(IntPtr hProcess, int uiFlags);

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private const int WM_MOUSEMOVE = 0x0200;

        private static readonly MethodInfo? OnMouseEnter =
            typeof(Control).GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo? OnMouseLeave =
            typeof(Control).GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo? OnMouseMove =
            typeof(Control).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? FillPathField =
            typeof(ModernControlBase).GetField("_cachedFillPath", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? VisualNodeField =
            typeof(ModernControlBase).GetField("_visualNode", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? SweepClipField =
            typeof(RenderNode).GetField("_cachedSweepClip", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? ChildrenClipField =
            typeof(RenderNode).GetField("_cachedChildrenClip", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo? OnMouseDown =
            typeof(Control).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo? OnMouseUp =
            typeof(Control).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly List<(int Cycle, long Ws, long Private, long Managed, int Gdi, int User, int Threads, int Handles)> Samples = new();

        private static int _threadExceptions;
        private static int _skips;

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            SetProcessDPIAware();

            int cycles = 500;
            if (args.Length > 0 && int.TryParse(args[0], out int parsed) && parsed > 0)
                cycles = parsed;

            Application.ThreadException += (s, e) =>
            {
                _threadExceptions++;
                Console.WriteLine($"[fatal] ThreadException: {e.Exception}");
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Console.WriteLine($"[fatal] UnhandledException: {e.ExceptionObject}");

            Console.WriteLine($"[stress] ciclos={cycles} os={Environment.OSVersion.VersionString} " +
                              $"dpiMode={Application.HighDpiMode}");

            (int Cycle, long Ws, long Private, long Managed, int Gdi, int User, int Threads, int Handles) warm;
            int warmFailures = 0;
            try
            {
                bool visualStyles = Application.RenderWithVisualStyles;
                Console.WriteLine($"[assert] {(visualStyles ? "OK" : "FALLO")} visual styles activos: {visualStyles}");
                if (!visualStyles) warmFailures++;

                bool supportedOs = false;
                bool commonControls = false;
                bool manifestChecked = false;
                string manifestExe = "";
                try
                {
                    // Módulo activo real: el apphost del harness es el que lleva
                    // NUESTRO manifiesto. Si corre bajo dotnet.exe, no lo es.
                    try
                    {
                        using var proc = Process.GetCurrentProcess();
                        manifestExe = proc.MainModule?.FileName ?? "";
                    }
                    catch { }
                    if (manifestExe.Length == 0)
                    {
                        string apphost = System.IO.Path.Combine(AppContext.BaseDirectory,
                            "FluentWinForms.StressTests.exe");
                        manifestExe = System.IO.File.Exists(apphost)
                            ? apphost
                            : (Environment.ProcessPath ?? "");
                    }

                    if (manifestExe.Length > 0 && System.IO.File.Exists(manifestExe))
                    {
                        string manifest = System.Text.Encoding.UTF8.GetString(
                            System.IO.File.ReadAllBytes(manifestExe));
                        manifestChecked = true;
                        supportedOs = manifest.Contains("8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a");
                        commonControls = manifest.Contains("Microsoft.Windows.Common-Controls");
                    }
                }
                catch { }
                bool apphostActive = manifestExe.EndsWith("FluentWinForms.StressTests.exe",
                    StringComparison.OrdinalIgnoreCase);
                bool manifestOk = manifestChecked && apphostActive && supportedOs && commonControls;
                Console.WriteLine($"[assert] {(manifestOk ? "OK" : "FALLO")} manifiesto apphost: module={System.IO.Path.GetFileName(manifestExe)}, activo={apphostActive}, supportedOS={supportedOs}, CommonControls={commonControls}");
                if (!manifestOk) warmFailures++;

                warm = Snapshot("inicio", 0);
                // Calentamiento: un ciclo completo (JIT + caches) y se descarta.
                RunCycle(0, extremes: true);
                Application.DoEvents();

                // El resize se prueba UNA vez. Después se asienta el proceso con
                // un par de ciclos vacíos + GC (sin EmptyWorkingSet: invalida WS
                // como métrica de fuga).
                RunResizeStress();
                warmFailures += RunSiblingOverlapCheck();
                warmFailures += RunBadgeLayoutCheck();
                warmFailures += RunDetachedNodeCheck();
                warmFailures += RunControlChurnCheck();
                warmFailures += RunDisposedBeforeHandleCheck();
                warmFailures += RunGcCostCheck();
                warmFailures += RunPathCacheCheck();
                warmFailures += RunRippleFadeCheck();
                warmFailures += RunHitTestCheck();
                warmFailures += RunGlowOverlayAutoCheck();
                warmFailures += RunOverlayCheck();
                warmFailures += RunOverlayLoadCheck();
                warmFailures += RunOverlayBackdropCheck();
                warmFailures += RunAutoBackgroundCheck();
                warmFailures += RunDesignerBackgroundCheck();
                warmFailures += RunBackdropToggleCheck();
                warmFailures += RunBackdropThrottleCheck();
                if (Environment.GetEnvironmentVariable("FWF_OVERLAY_PIXEL") == "1")
                    warmFailures += RunOverlayPixelCheck();
                warmFailures += RunDpiLayoutCheck();
                if (Environment.GetEnvironmentVariable("FWF_OVERLAY_STRESS") == "1")
                    ModernGlowOverlay.Enabled = true;
                RunModernMassScenario();
                RunCycle(0, extremes: false);
                RunCycle(0, extremes: false);
                Application.DoEvents();

                warm = Snapshot("tras calentamiento", 1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[fatal] en calentamiento: {ex}");
                return 2;
            }

            // La línea base se toma cuando el proceso ya está estable (JIT y
            // caches de Skia/GDI creados), no en el primer ciclo.
            int baselineCycle = Math.Clamp(cycles / 10, 20, 50);
            var baseline = warm;

            int failures = warmFailures;
            string? firstError = null;
            var sw = Stopwatch.StartNew();

            for (int i = 1; i <= cycles; i++)
            {
                try
                {
                    RunCycle(i, extremes: i % 50 == 0);

                    if (i % 100 == 0)
                        InvokeDpiChange(i);

                    if (i % 250 == 0)
                        RunMassScenario();
                }
                catch (Exception ex)
                {
                    failures++;
                    firstError ??= $"ciclo {i}: {ex}";
                }

                if (i % 25 == 0)
                {
                    var s = Snapshot($"ciclo {i}", i);
                    if (i == baselineCycle)
                    {
                        baseline = s;
                        Console.WriteLine($"[stress] línea base fijada en el ciclo {i}");
                    }
                    Console.WriteLine($"[stress] {i}/{cycles} ({sw.Elapsed.TotalSeconds:0.0}s) ΔWS={s.Ws - baseline.Ws} MB ΔPriv={s.Private - baseline.Private} MB");
                }
            }

            Application.DoEvents();
            // Varios ciclos planos + settle para que el pico de 4000×3000
            // (buffers del backdrop) se libere antes de la medición final.
            for (int k = 0; k < 4; k++)
            {
                RunCycle(0, extremes: false);
                Pump(200);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            Application.DoEvents();
            var final = Snapshot("fin", cycles);
            sw.Stop();

            // El blur debe seguir siendo LIVE: se comprueba que el
            // BackgroundImage cambia al mover la ventana.
            failures += RunLiveBlurCheck();

            // ---- Aserciones de memoria ----
            // La línea base es el MÁXIMO de las muestras estables tempranas: así
            // un pico normal de caches (Skia/DWM) no cuenta como fuga. Lo que
            // delata una fuga es la pendiente sostenida.
            // El baseline de magnitud es el MÁXIMO de todas las muestras
            // intermedias (excluida la final): un pico transitorio (p. ej. el
            // resize 4000×3000) no cuenta como fuga, pero un crecimiento real
            // al final sí se detecta contra ese máximo. La pendiente sigue
            // siendo el detector principal de fugas.
            var stable = Samples.Where(s => s.Cycle > 0 && s.Cycle < cycles).ToList();
            if (stable.Count == 0) stable = Samples.Where(s => s.Cycle > 0).ToList();
            if (stable.Count == 0) stable = Samples;
            long baseWs = stable.Max(s => s.Ws);
            long basePrivate = stable.Max(s => s.Private);
            int baseGdi = stable.Max(s => s.Gdi);
            int baseUser = stable.Max(s => s.User);

            failures += AssertGrowth("WorkingSet", baseWs, final.Ws, 1.5);
            failures += AssertGrowth("Private", basePrivate, final.Private, 1.5);
            failures += AssertGrowth("GDI", baseGdi, final.Gdi, 1.5);
            // USER incluye handles de timers de diagnóstico que pueden quedar
            // pendientes en corridas cortas: solo se aserta con >=150 ciclos.
            if (cycles >= 150)
                failures += AssertGrowth("USER", baseUser, final.User, 1.5);
            else
                Console.WriteLine($"[assert] USER no evaluado en corrida corta ({cycles} ciclos)");

            double slopeWs = TrendPerCycle(s => s.Ws, cycles);
            double slopePrivate = TrendPerCycle(s => s.Private, cycles);
            Console.WriteLine($"[info] pendiente WS={slopeWs:0.0000} MB/ciclo  Private={slopePrivate:0.0000} MB/ciclo");
            // WorkingSet oscila con el recorte del SO: solo informativo.
            // Una fuga real se ve en Private (cada frame retenido son ~2.3 MB).
            if (cycles >= 150 && Samples.Count >= 6)
            {
                if (slopePrivate > 1.0)
                {
                    Console.WriteLine("[assert] FALLO: pendiente positiva sostenida en Private");
                    failures++;
                }
            }
            else
            {
                Console.WriteLine($"[assert] pendiente no evaluada: muestras={Samples.Count}, ciclos={cycles} (se requieren >=6 y >=150)");
            }

            failures += _threadExceptions;
            Console.WriteLine($"[stress] duración={sw.Elapsed.TotalSeconds:0.0}s fallos={failures} skips={_skips}");
            Console.WriteLine($"[stress] Δ vs calentamiento: WS={final.Ws - baseline.Ws} MB  Private={final.Private - baseline.Private} MB  " +
                              $"GDI={final.Gdi - baseline.Gdi}  USER={final.User - baseline.User}  Threads={final.Threads - baseline.Threads}  Handles={final.Handles - baseline.Handles}");
            if (firstError != null) Console.WriteLine($"[stress] primer error: {firstError}");

            return failures == 0 ? 0 : 1;
        }

        // ==================================================================
        // Ciclo principal
        // ==================================================================
        private static void RunCycle(int index, bool extremes)
        {
            using var form = new Form1();
            // Señal fresca por ciclo: sin reset, LastOverlayBounds podría ser
            // del Form1 anterior (mismo ClientSize) y dar un falso positivo.
            bool overlayStress = Environment.GetEnvironmentVariable("FWF_OVERLAY_STRESS") == "1" &&
                                 ModernGlowOverlay.Enabled;
            if (overlayStress) ResetOverlayStats();
            form.Show();
            Application.DoEvents();

            // Modo stress: el ciclo debe demostrar que Form1 adjunta y pinta
            // el overlay; si no, el "fallos=0" sería un falso positivo.
            if (overlayStress)
            {
                bool ready = PumpUntil(() =>
                    ModernGlowOverlay.IsAttached(form) &&
                    ModernGlowOverlay.LastUpdateSucceeded &&
                    ModernGlowOverlay.LastOverlayBounds.Width == form.ClientSize.Width &&
                    ModernGlowOverlay.LastOverlayBounds.Height == form.ClientSize.Height &&
                    ModernGlowOverlay.LastOverlayScreen == form.PointToScreen(Point.Empty), 1500);
                if (!ready)
                {
                    throw new InvalidOperationException(
                        $"overlay stress Form1: adjunto={ModernGlowOverlay.IsAttached(form)}, " +
                        $"UpdateLayered={ModernGlowOverlay.LastUpdateSucceeded}, " +
                        $"bounds={ModernGlowOverlay.LastOverlayBounds}, cliente={form.ClientSize}, " +
                        $"screen={ModernGlowOverlay.LastOverlayScreen} esperado={form.PointToScreen(Point.Empty)}");
                }
                if (index % 25 == 0)
                {
                    Console.WriteLine($"[info] overlay stress Form1 ciclo {index}: adjunto=True, bounds={ModernGlowOverlay.LastOverlayBounds}, cliente={form.ClientSize}, screen={ModernGlowOverlay.LastOverlayScreen}");
                }
            }

            var buttons = form.Controls.OfType<ModernControlBase>().ToList();
            if (buttons.Count == 0)
                throw new InvalidOperationException("Form1 no expone ningún ModernControlBase: cobertura nula.");

            // Hover + click REAL sobre el primer FluentElement (no el Label).
            var target = buttons[0];
            if (OnMouseEnter == null || OnMouseDown == null || OnMouseUp == null)
                throw new InvalidOperationException("No se pudieron resolver OnMouseEnter/OnMouseDown/OnMouseUp.");

            OnMouseEnter.Invoke(target, new object[] { EventArgs.Empty });
            Application.DoEvents();

            bool hovered = ReadBool(target, "_isHoveringInternal");
            if (!hovered)
                throw new InvalidOperationException($"{target.Name}: el hover no se activó (cobertura rota).");

            SendMessage(target.Handle, WM_MOUSEMOVE, IntPtr.Zero, Lp(10, 10));

            for (int k = 0; k < 3; k++)
            {
                OnMouseDown.Invoke(target, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0) });

                // Ripple: solo se activa si el control lo tiene configurado.
                bool rippling = ReadBool(target, "_isRippling");

                if (k == 2 && index % 7 == 0)
                {
                    // Animación interrumpida: se cierra con el botón apretado.
                    break;
                }

                OnMouseUp.Invoke(target, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0) });
                if (!rippling && index % 50 == 0)
                    Console.WriteLine($"[info] ciclo {index}: {target.Name} sin ripple configurado");
                Application.DoEvents();
            }

            // Click en un segundo botón (ripple/glow distinto) si existe.
            if (buttons.Count > 2 && index % 3 == 0)
            {
                var second = buttons[2];
                OnMouseEnter.Invoke(second, new object[] { EventArgs.Empty });
                OnMouseDown.Invoke(second, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0) });
                Application.DoEvents();
                OnMouseUp.Invoke(second, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0) });
            }

            if (index % 5 == 0)
            {
                AppTheme.ApplyTheme(index % 10 == 0 ? ThemePalette.Dark : ThemePalette.Light);
                Application.DoEvents();
            }

            if (index % 25 == 0)
            {
                form.WindowState = FormWindowState.Minimized;
                Application.DoEvents();
                form.WindowState = FormWindowState.Normal;
                form.Size = new Size(Math.Max(320, form.Width - 120), Math.Max(240, form.Height - 80));
                Application.DoEvents();
                form.Size = new Size(1116, 512);
                Application.DoEvents();
            }

            if (extremes)
            {
                object? manager = GetManager(form);
                if (manager == null)
                    throw new InvalidOperationException("No se encontró modernFormManager1: los valores extremos no se aplicaron.");

                Set(manager, "BorderRadius", index % 100 == 0 ? 9999 : 0);
                Set(manager, "BorderThickness", index % 100 == 0 ? 100f : 0f);
                Set(manager, "BlurAmount", index % 100 == 0 ? 40 : 1);
                Set(manager, "BackdropPerformanceMode", index % 100 != 0);

                // Verificación: el manager puede clampear los valores. Se leen
                // de vuelta y se registra la diferencia (no es un fallo del test).
                if (index % 100 == 0)
                {
                    Console.WriteLine(
                        $"[info] extremos: radius={Read(manager, "BorderRadius")} " +
                        $"border={Read(manager, "BorderThickness")} blur={Read(manager, "BlurAmount")}");
                }

                form.Opacity = 0;
                Application.DoEvents();
                form.Opacity = 1;
                Application.DoEvents();
            }

            // Tamaños extremos (WinForms los clampea; aquí solo se comprueba
            // que no explote ni deje recursos).
            if (index % 40 == 0)
            {
                form.Size = new Size(1, 1);
                Application.DoEvents();
                form.Size = new Size(4000, 3000);
                Application.DoEvents();
                form.Size = new Size(1116, 512);
                Application.DoEvents();
            }

            // Tras los extremos, el overlay debe seguir pintando en ESTE ciclo.
            if (overlayStress)
            {
                ResetOverlayStats(form);
                bool endReady = PumpUntil(() =>
                    ModernGlowOverlay.IsAttached(form) &&
                    ModernGlowOverlay.LastUpdateSucceeded &&
                    ModernGlowOverlay.LastOverlayBounds.Width == form.ClientSize.Width &&
                    ModernGlowOverlay.LastOverlayBounds.Height == form.ClientSize.Height &&
                    ModernGlowOverlay.LastOverlayScreen == form.PointToScreen(Point.Empty), 1500);
                if (!endReady)
                {
                    throw new InvalidOperationException(
                        $"overlay stress fin de ciclo: adjunto={ModernGlowOverlay.IsAttached(form)}, " +
                        $"UpdateLayered={ModernGlowOverlay.LastUpdateSucceeded}, " +
                        $"bounds={ModernGlowOverlay.LastOverlayBounds}, cliente={form.ClientSize}, " +
                        $"screen={ModernGlowOverlay.LastOverlayScreen} esperado={form.PointToScreen(Point.Empty)}");
                }
            }

            // Alternar Close+Dispose con Dispose-solo para cubrir ambos caminos.
            if (index % 10 == 0)
            {
                form.Dispose();
                form.Dispose(); // doble Dispose sin Close
                return;
            }

            form.Close();
            Application.DoEvents();
            form.Dispose();
            form.Dispose(); // doble Dispose a propósito
        }

        // ==================================================================
        // Escenarios especiales
        // ==================================================================
        private static void InvokeDpiChange(int cycle)
        {
            // Los overrides FWF_* ganan en ApplyModernEffects; este test fija
            // su propio BackdropType y no debe depender del entorno.
            using var envScope = new DiagnosticEnvScope();
            using var form = new Form1();
            // El Form1 del usuario puede estar en Backdrop=None: este test
            // necesita Blur para comprobar la cobertura del backdrop tras DPI.
            object? manager = GetManager(form);
            var backdrop = manager?.GetType().GetProperty("BackdropType");
            if (backdrop != null)
                backdrop.SetValue(manager, Enum.Parse(backdrop.PropertyType, "Blur"));
            manager?.GetType().GetProperty("AutoSetBlackBackground")?.SetValue(manager, true);
            form.Show();
            Pump(900); // el backdrop se asigna async desde el worker

            var method = manager?.GetType().GetMethod(
                "HandleDpiChange", BindingFlags.Instance | BindingFlags.NonPublic);
            if (manager == null || method == null)
                throw new InvalidOperationException("HandleDpiChange no disponible: DPI sin cubrir.");

            // Con el diseñador en Blur, el backdrop DEBE existir antes del cambio.
            if (form.BackgroundImage == null)
                throw new InvalidOperationException("Sin BackgroundImage antes del DPI: cobertura nula.");

            method.Invoke(manager, new object[] { 120, 144 });
            method.Invoke(manager, new object[] { 144, 120 });
            Pump(500);

            if (form.IsDisposed)
                throw new InvalidOperationException("El form quedó desposeído tras HandleDpiChange.");
            if (form.BackgroundImage == null)
                throw new InvalidOperationException("El backdrop desapareció tras HandleDpiChange.");

            form.Refresh();
            Application.DoEvents();

            form.Close();
            form.Dispose();
            if (cycle % 500 == 0) Console.WriteLine($"[info] ciclo {cycle}: DPI simulado 120<->144");
        }

        /// <summary>
        /// Arrastre de redimensionado real: ejercita CleanupBackdrop, la
        /// reasignación de buffers y la contabilidad de slots (lo que un move
        /// no toca).
        /// </summary>
        private static void RunResizeStress()
        {
            using var form = new Form1();
            form.Show();
            Pump(300);

            for (int i = 0; i < 80; i++)
            {
                int w = 700 + (i % 40) * 80;   // 700..3820
                int h = 500 + (i % 40) * 40;   // 500..2060
                form.Size = new Size(w, h);
                Application.DoEvents();
                System.Threading.Thread.Sleep(5);
            }

            form.Size = new Size(1116, 512);
            Pump(300);
            form.Close();
            form.Dispose();
            Console.WriteLine("[info] resize stress 80 pasos completado");
        }

        /// <summary>
        /// Los cuerpos lógicos de los controles no deben solaparse aunque los
        /// gaps sean 0/1/20 px. El margen de sombra puede solaparse: el glow
        /// vive dentro del HWND (comportamiento Skia actual, Tarea 7).
        /// </summary>
        private static int RunSiblingOverlapCheck()
        {
            int failures = 0;
            try
            {
                using var form = new Form { Size = new Size(760, 560) };

                var a = FluentElement.Design("gap0a").Layout(40, 40, 180, 48).Glow("#ff00ff", 14).Apply(form);
                var b = FluentElement.Design("gap0b").Layout(40, 88, 180, 48).Glow("#ff00ff", 14).Apply(form);
                var c = FluentElement.Design("gap1c").Layout(40, 137, 180, 48).Glow("#ff00ff", 14).Apply(form);
                var d = FluentElement.Design("gap20d").Layout(320, 40, 180, 48).Glow("#ff00ff", 14).Apply(form);
                var e = FluentElement.Design("gap20e").Layout(320, 108, 180, 48).Glow("#ff00ff", 14).Apply(form);

                form.Show();
                Pump(500);

                var list = new[] { a, b, c, d, e };
                int bodyOverlaps = 0, marginOverlaps = 0;
                for (int i = 0; i < list.Length; i++)
                {
                    for (int j = i + 1; j < list.Length; j++)
                    {
                        Rectangle la = list[i].LogicalBounds, lb = list[j].LogicalBounds;
                        if (la.IntersectsWith(lb))
                        {
                            bodyOverlaps++;
                            Console.WriteLine($"[assert] FALLO solape de botón {list[i].Name}/{list[j].Name}: {la} vs {lb}");
                        }

                        Rectangle ba = list[i].Bounds, bb = list[j].Bounds;
                        if (ba.IntersectsWith(bb)) marginOverlaps++;
                    }
                }

                // El glow ya no infla el HWND (vive en la capa del padre): los
                // cuerpos de los botones no se solapan ni con gap 0.
                // Los cuerpos (LogicalBounds) no se solapan; el margen de sombra
                // sí puede hacerlo porque el glow vive dentro del HWND (Tarea 7).
                Console.WriteLine($"[assert] {(bodyOverlaps == 0 ? "OK" : "FALLO")} botones sin solape (gaps 0/1/20); margen de sombra solapado en {marginOverlaps} par(es) (conocido)");
                failures += bodyOverlaps;

                form.Close();
                form.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO expansion sombra: {ex}");
                failures++;
            }
            return failures;
        }

        /// <summary>
        /// Mide el coste real de un GC.Collect y cuántas recolecciones provoca
        /// la animación al cruzar el umbral de 600 frames con varios controles.
        /// El motor recolecta UNA vez por frame global (no una por control).
        /// </summary>
        private static int RunGcCostCheck()
        {
            int failures = 0;
            try
            {
                // Coste de una recolección completa con el heap actual.
                GC.Collect();
                GC.WaitForPendingFinalizers();

                var sw = System.Diagnostics.Stopwatch.StartNew();
                GC.Collect();
                sw.Stop();
                double gcMs = sw.Elapsed.TotalMilliseconds;

                using var form = new Form { Size = new Size(700, 500) };
                var controls = new List<FluentElement>();
                for (int i = 0; i < 6; i++)
                {
                    var c = FluentElement.Design($"gc{i}").Layout(40, 40 + i * 60, 160, 44).Glow("#ff00ff", 10).Apply(form);
                    c.UseRipple = true; // el ripple mantiene la animación viva de forma continua
                    controls.Add(c);
                }

                form.Show();
                Pump(200);

                long frames0 = AnimationManager.Frames;
                int gen2Before = GC.CollectionCount(2);
                var timeout = System.Diagnostics.Stopwatch.StartNew();

                // Down/Up repetido: press + ripple animan sin depender del hit-test
                // del mouse real, así el loop global cruza el umbral de 600 frames.
                while (AnimationManager.Frames - frames0 < 700 && timeout.ElapsedMilliseconds < 90000)
                {
                    foreach (var c in controls)
                    {
                        OnMouseDown?.Invoke(c, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0) });
                        OnMouseUp?.Invoke(c, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0) });
                    }
                    Pump(80);
                }

                long framesCrossed = AnimationManager.Frames - frames0;
                int collects = GC.CollectionCount(2) - gen2Before;
                int expected = (int)(framesCrossed / 600);

                Console.WriteLine($"[info] GC.Collect coste={gcMs:0.00}ms");
                Console.WriteLine($"[info] {framesCrossed} frames ({timeout.Elapsed.TotalSeconds:0.0}s) con {controls.Count} controles: gen2=+{collects} (global, esperado={expected}; por control serian {expected * controls.Count})");

                if (framesCrossed < 600)
                {
                    Console.WriteLine("[assert] FALLO GC check: no se cruzó el umbral de 600 frames");
                    failures++;
                }
                else if (collects < expected || collects >= expected * controls.Count)
                {
                    Console.WriteLine($"[assert] FALLO GC check: gen2={collects} fuera de [{expected}, {expected * controls.Count})");
                    failures++;
                }
                else
                {
                    Console.WriteLine($"[assert] OK GC global: {collects} recolección(es) para {expected} umbral(es), no {expected * controls.Count} por control");
                }

                form.Close();
                form.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO GC check: {ex.Message}");
                failures++;
            }
            return failures;
        }

        // HISTÓRICO: la capa de glow GDI+ se revirtió (Skia-only). No se llama.
        /// <summary>
        /// Diagnóstico del glow: control con translate estático; comprueba que
        /// un píxel por encima del HWND (5 px fuera) muestra el halo y que uno
        /// mucho más lejos queda en el fondo.
        /// </summary>
        private static int RunGlowClipDiagnostic()
        {
            int failures = 0;
            try
            {
                using var form = new Form { Size = new Size(520, 320) };
                var c = FluentElement.Design("glowclip").Layout(160, 140, 180, 48).Glow("#ff00ff", 14).Translate(0, -10).Apply(form);
                form.Show();
                Pump(400);

                Rectangle lb = c.LogicalBounds, hb = c.Bounds;
                int exL = lb.Left - hb.Left, exR = hb.Right - lb.Right;
                int exT = lb.Top - hb.Top, exB = hb.Bottom - lb.Bottom;
                Console.WriteLine($"[info] glow logical={lb} hwnd={hb}");
                Console.WriteLine($"[info] glow expansion L={exL} R={exR} T={exT} B={exB} (content-only; el bake de 28px va en la capa del padre)");

                // Aserción: el glow debe existir FUERA del HWND del botón,
                // sobre la superficie del padre. Si el HWND lo recorta, el
                // píxel a 20px de distancia queda con el fondo (negro).
                using var host = new Form { Size = new Size(460, 300), BackColor = Color.Black };
                var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
                host.Controls.Add(panel);
                var g = FluentElement.Design("glowpix").Layout(150, 110, 140, 44).Glow("#ff00ff", 14).Translate(0, -10).Apply(panel);
                host.Show();
                Pump(350);

                using var shot = new Bitmap(panel.Width, panel.Height);
                panel.DrawToBitmap(shot, new Rectangle(0, 0, panel.Width, panel.Height));

                Rectangle gb = g.LogicalBounds;
                // 6px por encima del borde visual trasladado (fuera del HWND).
                int px = gb.Left + gb.Width / 2, py = gb.Top - 16;
                Color outside = shot.GetPixel(px, py);
                Color far = shot.GetPixel(px, Math.Max(0, gb.Top - 60));
                bool halo = outside.R > 12 || outside.G > 12 || outside.B > 12;
                bool background = far.R <= 12 && far.G <= 12 && far.B <= 12;

                Console.WriteLine($"[assert] {(halo && background ? "OK" : "FALLO")} glow fuera del HWND: pixel({px},{py})={outside.R},{outside.G},{outside.B} (halo={halo}); lejos={far.R},{far.G},{far.B} (fondo={background})");
                if (!(halo && background)) failures++;

                host.Close();
                host.Dispose();

                form.Close();
                form.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO glow diagnostic: {ex.Message}");
                failures++;
            }
            return failures;
        }

        private static void PaintOnce(Control c)
        {
            int w = Math.Max(1, c.Width), h = Math.Max(1, c.Height);
            using var bmp = new Bitmap(w, h);
            c.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
        }

        /// <summary>
        /// Cobertura de los cachés A1/A2/A4: misma referencia entre paints con
        /// geometría estable, referencia nueva tras resize, null tras Dispose.
        /// También cubre el doble Dispose de RenderNode.
        /// </summary>
        private static int RunPathCacheCheck()
        {
            int failures = 0;
            try
            {
                // Tarea 4: doble Dispose idempotente.
                var detached = new RenderNode();
                detached.Dispose();
                detached.Dispose();
                Console.WriteLine("[assert] OK RenderNode.Dispose doble (idempotente)");

                using var form = new Form { Size = new Size(520, 380) };

                // A) fillPath — camino sin VisualNode (DrawBaseEngine).
                var fill = new FluentElement
                {
                    Location = new Point(30, 30),
                    Size = new Size(140, 48),
                    BorderRadius = 12,
                    BorderThickness = 4
                };
                form.Controls.Add(fill);
                form.Show();
                Pump(300);

                PaintOnce(fill);
                object? f1 = FillPathField?.GetValue(fill);
                PaintOnce(fill);
                object? f2 = FillPathField?.GetValue(fill);
                bool fillCached = f1 != null && ReferenceEquals(f1, f2);

                fill.Width += 40; Pump(80); PaintOnce(fill);
                object? f3 = FillPathField?.GetValue(fill);
                bool fillRebuilt = f1 != null && !ReferenceEquals(f1, f3);

                form.Controls.Remove(fill);
                fill.Dispose();
                bool fillDisposed = FillPathField?.GetValue(fill) == null;

                Console.WriteLine($"[assert] {(fillCached && fillRebuilt && fillDisposed ? "OK" : "FALLO")} cache fillPath: estable={fillCached} rebuild={fillRebuilt} disposeNull={fillDisposed}");
                if (!(fillCached && fillRebuilt && fillDisposed)) failures++;

                // Translate estático: el HWND debe reservar el desplazamiento
                // para que el contenido no se recorte en reposo.
                var translated = FluentElement.Design("translated").Layout(200, 200, 120, 40)
                    .Translate(0, -10).Apply(form);
                Pump(200);
                int expTop = translated.LogicalBounds.Top - translated.Bounds.Top;
                Console.WriteLine($"[assert] {(Math.Abs(expTop - 11) <= 1 ? "OK" : "FALLO")} translate estático: expansión superior={expTop}px (esperado 11)");
                if (Math.Abs(expTop - 11) > 1) failures++;

                // Control + nodo hijo trasladados en la misma dirección: son
                // aditivos en el dibujo y deben sumarse en la expansión.
                var combo = FluentElement.Design("combo").Layout(300, 200, 120, 40)
                    .Translate(0, -10)
                    .AddChild(child => child.Translate(0, -10).Badge("5").BadgeOffset(-4, -6))
                    .Apply(form);
                Pump(200);
                int comboTop = combo.LogicalBounds.Top - combo.Bounds.Top;
                int comboLeft = combo.LogicalBounds.Left - combo.Bounds.Left;
                int comboBottom = combo.Bounds.Bottom - combo.LogicalBounds.Bottom;
                // top: control -10 + nodo -10 + badge -6 + 1 = 27
                // left: badge Size 18 - ancho 0 - OffsetX(-4) + 1 = 23
                Console.WriteLine($"[assert] {(comboTop >= 26 && comboTop <= 28 && comboLeft == 23 && comboBottom >= 12 ? "OK" : "FALLO")} translate control+nodo+badge: top={comboTop} (27) left={comboLeft} (23) bottom={comboBottom} (>=12)");
                if (comboTop < 26 || comboTop > 28 || comboLeft != 23 || comboBottom < 12) failures++;

                // B) childrenClip — nodo con hijos y esquinas redondeadas.
                var parent = FluentElement.Design("clipchildren").Layout(30, 110, 180, 80).BorderRadius(16)
                    .AddChild(child => child.Background("#ff0000").BorderRadius(8))
                    .Apply(form);
                Pump(200);
                PaintOnce(parent);
                var node = VisualNodeField?.GetValue(parent) as RenderNode;
                object? c1 = node != null ? ChildrenClipField?.GetValue(node) : null;
                PaintOnce(parent);
                object? c2 = node != null ? ChildrenClipField?.GetValue(node) : null;
                bool childCached = c1 != null && ReferenceEquals(c1, c2);

                parent.Width += 40; Pump(80); PaintOnce(parent);
                // El layout del nodo no sigue al HWND por diseño (el control no
                // estira el contenido); se cambia el rect del nodo para probar
                // el rebuild del clip.
                if (node != null)
                    node.Layout = new RectangleF(0, 0, node.Layout.Width + 40, node.Layout.Height);
                Pump(20); PaintOnce(parent);
                object? c3 = node != null ? ChildrenClipField?.GetValue(node) : null;
                bool childRebuilt = c1 != null && !ReferenceEquals(c1, c3);

                Console.WriteLine($"[assert] {(childCached && childRebuilt ? "OK" : "FALLO")} cache childrenClip: estable={childCached} rebuild={childRebuilt}");
                if (!(childCached && childRebuilt)) failures++;

                // C) sweepClip — hover con sweep activo.
                var sweep = FluentElement.Design("sweep").Layout(250, 110, 160, 48).BorderRadius(12)
                    .SweepHover("#ff00ff").Apply(form);
                Pump(200);
                OnMouseMove?.Invoke(sweep, new object[] { new MouseEventArgs(MouseButtons.None, 0, sweep.Width / 2, sweep.Height / 2, 0) });
                Pump(200);
                PaintOnce(sweep);
                var snode = VisualNodeField?.GetValue(sweep) as RenderNode;
                object? s1 = snode != null ? SweepClipField?.GetValue(snode) : null;
                PaintOnce(sweep);
                object? s2 = snode != null ? SweepClipField?.GetValue(snode) : null;
                bool sweepCached = s1 != null && ReferenceEquals(s1, s2);

                Console.WriteLine($"[assert] {(sweepCached ? "OK" : "FALLO")} cache sweepClip: estable={sweepCached} (hover)");
                if (!sweepCached) failures++;

                form.Close();
                form.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO path caches: {ex.Message}");
                failures++;
            }
            return failures;
        }

        // HISTÓRICO: la capa de glow GDI+ se revirtió (Skia-only). No se llama.
        /// <summary>
        /// Al quitar/disponer un control con glow, sus hermanos deben repintar
        /// su copia del halo (el padre no puede pintar debajo de HWND hijos).
        /// </summary>
        private static int RunGlowRemoveCheck()
        {
            int failures = 0;
            try
            {
                using var form = new Form { Size = new Size(500, 320) };
                var a = FluentElement.Design("glow-a").Layout(60, 60, 160, 48).Glow("#ff00ff", 14).Apply(form);
                var b = FluentElement.Design("glow-b").Layout(60, 120, 160, 48).Glow("#ff00ff", 14).Apply(form);
                form.Show();
                Pump(400);

                int paints = 0;
                a.Paint += (s, e) => paints++;
                form.Controls.Remove(b);
                Pump(400);
                Console.WriteLine($"[assert] {(paints > 0 ? "OK" : "FALLO")} glow remove: hermano repintado ({paints})");
                if (paints == 0) failures++;
                b.Dispose();

                var c = FluentElement.Design("glow-c").Layout(60, 120, 160, 48).Glow("#00ff00", 14).Apply(form);
                Pump(300);
                paints = 0;
                c.Dispose();
                Pump(400);
                Console.WriteLine($"[assert] {(paints > 0 ? "OK" : "FALLO")} glow dispose: hermano repintado ({paints})");
                if (paints == 0) failures++;

                form.Close();
                form.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO glow remove/dispose: {ex.Message}");
                failures++;
            }
            return failures;
        }

        /// <summary>
        /// Masa de controles REALES (VisualNode + Skia + glow). El escenario de
        /// 200 controles usa `new FluentElement` sin VisualNode y no ejercita
        /// este camino. Informativo: mide WS/Private/GDI antes y después.
        /// </summary>
        private static void RunModernMassScenario()
        {
            try
            {
                using var form = new Form { Size = new Size(980, 640) };
                var controls = new List<FluentElement>(60);
                for (int i = 0; i < 60; i++)
                {
                    int x = 20 + (i % 10) * 94;
                    int y = 20 + (i / 10) * 92;
                    controls.Add(FluentElement.Design($"modern{i}")
                        .Layout(x, y, 80, 36)
                        .Background("#202030")
                        .BorderRadius(8)
                        .Glow("#ff00ff", 8)
                        .Apply(form));
                }

                form.Show();
                Pump(600);
                using var p = Process.GetCurrentProcess();
                p.Refresh();
                long ws0 = p.WorkingSet64, priv0 = p.PrivateMemorySize64;

                for (int i = 0; i < controls.Count; i += 6)
                    OnMouseEnter?.Invoke(controls[i], new object[] { EventArgs.Empty });
                Pump(400);

                p.Refresh();
                Console.WriteLine($"[info] masa moderna 60 (Skia+glow): WS {ws0 / 1048576}->{p.WorkingSet64 / 1048576} MB, " +
                                  $"Private {priv0 / 1048576}->{p.PrivateMemorySize64 / 1048576} MB, " +
                                  $"GDI={GetGuiResources(p.Handle, 0)}");

                form.Close();
                form.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[info] masa moderna: {ex.Message}");
            }
        }

        /// <summary>
        /// Badge en el borde con offset negativo (clamp al parent), badge de un
        /// hijo 0x0 y escala del nodo en VisualNode.
        /// </summary>
        private static int RunBadgeLayoutCheck()
        {
            int failures = 0;
            using var overlayScope = new OverlayDisabledScope();
            try
            {
                using var form = new Form { Size = new Size(400, 260) };
                var edge = FluentElement.Design("badge-edge").Layout(330, 40, 120, 44)
                    .Glow("#ff00ff", 12).Badge("9").Apply(form);
                form.Show();
                Pump(250);

                var node = edge.VisualNode;
                if (node == null)
                    throw new InvalidOperationException("VisualNode nulo");
                var badge = node.Badge;
                badge.IsVisible = false;
                node.Badge = badge;

                var refresh = typeof(ModernControlBase).GetMethod("RefreshPhysicalBounds",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                refresh?.Invoke(edge, null);
                Pump(100);
                Rectangle withoutBadge = edge.Bounds;

                badge.IsVisible = true;
                badge.OffsetX = -30;
                badge.OffsetY = -8;
                node.Badge = badge;
                refresh?.Invoke(edge, null);
                Pump(120);
                Rectangle eb = edge.Bounds;

                bool badgeChanged = eb != withoutBadge;
                bool badgeDirection = eb.Top <= withoutBadge.Top;
                Rectangle logicalRect = new Rectangle(330, 40, 120, 44);
                bool logicalOk = edge.LogicalBounds == logicalRect;
                // Margen del glow (12) escalado por DPI + 1, con clamp al cliente.
                int shadowMargin = (int)Math.Ceiling(12 * form.DeviceDpi / 96f) + 1;
                Rectangle expectedWithout = Rectangle.FromLTRB(
                    Math.Max(0, logicalRect.Left - shadowMargin),
                    Math.Max(0, logicalRect.Top - shadowMargin),
                    Math.Min(form.ClientSize.Width, logicalRect.Right + shadowMargin),
                    Math.Min(form.ClientSize.Height, logicalRect.Bottom + shadowMargin));
                // OffsetY = -8 desplaza el badge (y el HWND) 8 px hacia arriba.
                Rectangle expectedWith = new Rectangle(
                    expectedWithout.X, expectedWithout.Y - 8,
                    expectedWithout.Width, expectedWithout.Height + 8);
                bool withoutOk = withoutBadge == expectedWithout;
                bool withOk = eb == expectedWith;
                bool clampOk = badgeChanged && badgeDirection && logicalOk &&
                               withoutOk && withOk &&
                               eb.Width > 20 && eb.Height > 20 &&
                               eb.Left >= 0 && eb.Top >= 0 &&
                               eb.Right <= form.ClientSize.Width &&
                               eb.Bottom <= form.ClientSize.Height;

                // Badge de un hijo 0x0: debe seguir dentro del nodo y del parent.
                var child = new RenderNode { Layout = new RectangleF(20, 20, 0, 0) };
                var childBadge = child.Badge;
                childBadge.IsVisible = true;
                childBadge.Size = 16;
                childBadge.Text = "1";
                childBadge.OffsetX = 8;
                childBadge.OffsetY = 8;
                child.Badge = childBadge;
                node.Children.Add(child);
                refresh?.Invoke(edge, null);
                Pump(120);
                var childBadgeRect = new RectangleF(
                    child.Layout.Right + (float)childBadge.OffsetX - (float)childBadge.Size,
                    child.Layout.Top + (float)childBadge.OffsetY,
                    (float)childBadge.Size, (float)childBadge.Size);
                bool childBadgeInside = childBadgeRect.Left >= 0 &&
                                        childBadgeRect.Top >= 0 &&
                                        childBadgeRect.Right <= node.Layout.Width &&
                                        childBadgeRect.Bottom <= node.Layout.Height;
                bool childOk = node.Children.Contains(child) &&
                               child.Layout.Width == 0 && child.Layout.Height == 0 &&
                               child.Badge.IsVisible && childBadgeInside &&
                               edge.Bounds.Right <= form.ClientSize.Width &&
                               edge.Bounds.Bottom <= form.ClientSize.Height;

                // Escala del nodo en VisualNode (no debe perderse).
                var scaled = FluentElement.Design("scaled").Layout(60, 150, 120, 44)
                    .Scale(1.2).Apply(form);
                Pump(150);
                var scaledNode = scaled.VisualNode;
                bool scaleSet = scaledNode != null &&
                                Math.Abs(scaledNode.ScaleX - 1.2f) < 0.01f &&
                                Math.Abs(scaledNode.ScaleY - 1.2f) < 0.01f;
                int scaleExpansion = scaled.LogicalBounds.Top - scaled.Bounds.Top;
                bool scaleExpanded = scaleExpansion >= 5 && scaleExpansion <= 7;

                bool ok = clampOk && childOk && scaleSet && scaleExpanded;
                Console.WriteLine($"[assert] {(ok ? "OK" : "FALLO")} badge/layout: badge={badgeChanged}/{badgeDirection} sin={withoutBadge} esperado={expectedWithout} con={eb} esperado={expectedWith} logical={edge.LogicalBounds}, hijo0x0={childOk} badgeHijo={childBadgeRect}, scale={scaleSet} expansion={scaleExpansion}px (5..7)");
                if (!ok) failures++;

                form.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO badge/layout: {ex.Message}");
                failures++;
            }
            return failures;
        }

        private static SKPicture MakePicture()
        {
            var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new SKRect(0, 0, 4, 4));
            var picture = recorder.EndRecording();
            canvas.Dispose();
            recorder.Dispose();
            return picture;
        }

        private static bool SubscribedContains(object? set, object node)
        {
            if (set is System.Collections.IEnumerable items)
                foreach (var item in items)
                    if (ReferenceEquals(item, node)) return true;
            return false;
        }

        private static int SubscribedCount(object? set)
            => set?.GetType().GetProperty("Count")?.GetValue(set) is int count ? count : -1;

        /// <summary>Nodos desprendidos: remove/reinsert con re-bake, promover hijo a root,
        /// VisualNode=null, remoción de nieto (suscripción recursiva) y SetItem
        /// con un nuevo item que era descendiente del viejo.</summary>
        private static int RunDetachedNodeCheck()
        {
            int failures = 0;
            try
            {
                using var form = new Form { Size = new Size(400, 300) };
                var control = FluentElement.Design("detached").Layout(80, 60, 160, 48)
                    .Glow("#00ff00", 12).Apply(form);
                form.Show();
                Pump(300);
                PaintOnce(control);

                var root = control.VisualNode;
                var paintField = typeof(RenderNode).GetField("_cachedRipplePaint",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var shadowField = typeof(RenderNode).GetField("_cachedShadowImage",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var subsField = typeof(ModernControlBase).GetField("_subscribedNodes",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (root == null || paintField == null || shadowField == null || subsField == null)
                    throw new InvalidOperationException("RenderNode/fields no disponibles");
                object NewPaint() => Activator.CreateInstance(paintField.FieldType)!;

                var removeNode = new RenderNode();
                paintField.SetValue(removeNode, NewPaint());
                root.Children.Add(removeNode);
                bool added = paintField.GetValue(removeNode) != null;
                root.Children.Remove(removeNode);
                bool removeOk = added && paintField.GetValue(removeNode) == null;

                var reusable = new RenderNode
                {
                    Layout = new RectangleF(10, 10, 60, 30),
                    Shadow = new ShadowData
                    {
                        Radius = 8,
                        Color = Color.FromArgb(200, 255, 0, 255)
                    }
                };
                root.Children.Add(reusable);
                PaintOnce(control);
                bool baked = shadowField.GetValue(reusable) != null;
                reusable.SvgPicture = MakePicture();
                root.Children.Remove(reusable);
                bool released = shadowField.GetValue(reusable) == null;
                bool svgRemoved = reusable.SvgPicture != null;
                root.Children.Add(reusable);
                PaintOnce(control);
                bool rebaked = shadowField.GetValue(reusable) != null;
                bool svgReadded = reusable.SvgPicture != null;
                bool rebakeOk = baked && released && rebaked && svgRemoved && svgReadded;

                var promoteChild = new RenderNode();
                paintField.SetValue(promoteChild, NewPaint());
                root.SvgPicture = MakePicture();
                promoteChild.SvgPicture = MakePicture();
                root.Children.Add(promoteChild);
                control.VisualNode = promoteChild;
                bool promoteOk = paintField.GetValue(promoteChild) != null &&
                                 root.SvgPicture == null &&
                                 promoteChild.SvgPicture != null;
                var promotedSubs = subsField.GetValue(control);
                promoteOk = promoteOk &&
                            SubscribedContains(promotedSubs, promoteChild) &&
                            !SubscribedContains(promotedSubs, root) &&
                            !SubscribedContains(promotedSubs, reusable);

                // Caso inverso: el nuevo root CONTIENE al viejo. El viejo no debe
                // liberarse (sigue en el árbol nuevo) y conserva su SVG.
                var ancestor = new RenderNode();
                ancestor.Children.Add(promoteChild);
                promoteChild.SvgPicture?.Dispose();
                promoteChild.SvgPicture = MakePicture();
                control.VisualNode = ancestor;
                bool ancestorOk = promoteChild.SvgPicture != null;
                PaintOnce(control);
                ancestorOk = ancestorOk && promoteChild.SvgPicture != null;

                // Cleanup explícito de los SVG del test que ya no pertenecen al control.
                reusable.SvgPicture?.Dispose();
                reusable.SvgPicture = null;
                root.SvgPicture?.Dispose();
                root.SvgPicture = null;

                // SetItem: el nuevo item era hijo del viejo -> sus cachés y SVG se conservan.
                var setParent = new RenderNode();
                var setOld = new RenderNode();
                var setNew = new RenderNode();
                paintField.SetValue(setNew, NewPaint());
                setOld.SvgPicture = MakePicture();
                setOld.Children.Add(setNew);
                setParent.Children.Add(setOld);
                setParent.Children[0] = setNew;
                bool setOk = paintField.GetValue(setNew) != null &&
                             setOld.SvgPicture != null;
                setOld.SvgPicture?.Dispose();
                setOld.SvgPicture = null;
                setParent.Dispose();

                // Builder: el SVG no debe destruirse al aplicar/reasignar el nodo.
                var svgBuilder = FluentElement.Design("svg-builder").Layout(20, 120, 140, 48)
                    .SvgIcon("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><rect width='16' height='16' fill='red'/></svg>")
                    .Apply(form);
                Pump(100);
                bool builderSvg = svgBuilder.VisualNode?.SvgPicture != null;
                PaintOnce(svgBuilder);
                builderSvg = builderSvg && svgBuilder.VisualNode?.SvgPicture != null;

                var svgChild = FluentElement.Design("svg-child").Layout(20, 180, 160, 60)
                    .AddChild(child => child.Layout(4, 4, 24, 24)
                        .SvgIcon("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='8' fill='red'/></svg>"))
                    .Apply(form);
                Pump(100);
                var childNode = svgChild.VisualNode?.Children.FirstOrDefault();
                bool childSvg = childNode?.SvgPicture != null;
                PaintOnce(svgChild);
                childSvg = childSvg && childNode?.SvgPicture != null;

                // API base (subclase directa de ModernControlBase): el builder
                // también debe pasar por VisualNode/suscripciones y conservar SVG.
                var baseControl = new TestModernControl { Name = "base-control" };
                baseControl.Design().Layout(20, 240, 140, 48)
                    .SvgIcon("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><rect width='16' height='16' fill='blue'/></svg>")
                    .Apply(form);
                Pump(100);
                bool baseSvg = baseControl.VisualNode?.SvgPicture != null;
                bool baseSubscribed = baseControl.VisualNode != null &&
                                      SubscribedContains(subsField.GetValue(baseControl),
                                          baseControl.VisualNode);
                PaintOnce(baseControl);
                baseSvg = baseSvg && baseControl.VisualNode?.SvgPicture != null;

                var pressedField = typeof(ModernControlBase).GetField("_currentPressedNode",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var hoverField = typeof(ModernControlBase).GetField("_currentHoveredNode",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                // VisualNode=null con un nodo pressed.
                bool nullOk;
                using (var nullForm = new Form { Size = new Size(300, 200) })
                {
                    var nc = FluentElement.Design("null-node").Layout(60, 60, 120, 48).Apply(nullForm);
                    nullForm.Show();
                    Pump(200);
                    var nroot = nc.VisualNode!;
                    int clicks = 0;
                    var nchild = new RenderNode
                    {
                        Layout = new RectangleF(10, 10, 60, 30),
                        OnClickAction = _ => clicks++
                    };
                    nroot.Children.Add(nchild);
                    nroot.SvgPicture = MakePicture();
                    Pump(50);
                    OnMouseDown?.Invoke(nc, new object[]
                        { new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0) });
                    bool pressed = ReferenceEquals(pressedField?.GetValue(nc), nchild);
                    nc.VisualNode = null;
                    bool cleared = pressedField?.GetValue(nc) == null &&
                                   hoverField?.GetValue(nc) == null;
                    bool nullSvg = nroot.SvgPicture == null;
                    bool nullSubs = SubscribedCount(subsField.GetValue(nc)) == 0;
                    OnMouseUp?.Invoke(nc, new object[]
                        { new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0) });
                    nullOk = pressed && cleared && clicks == 0 && nullSvg && nullSubs;
                    nullForm.Close();
                }

                // Nieto pressed removido: la suscripción recursiva debe limpiarlo.
                bool nestedOk;
                using (var nestedForm = new Form { Size = new Size(300, 200) })
                {
                    var nc = FluentElement.Design("nested-node").Layout(60, 60, 120, 48).Apply(nestedForm);
                    nestedForm.Show();
                    Pump(200);
                    var nroot = nc.VisualNode!;
                    int clicks = 0;
                    var mid = new RenderNode { Layout = new RectangleF(10, 10, 90, 40) };
                    var leaf = new RenderNode
                    {
                        Layout = new RectangleF(20, 20, 30, 20),
                        OnClickAction = _ => clicks++
                    };
                    mid.Children.Add(leaf);
                    nroot.Children.Add(mid);
                    Pump(50);
                    OnMouseDown?.Invoke(nc, new object[]
                        { new MouseEventArgs(MouseButtons.Left, 1, 40, 40, 0) });
                    bool pressed = ReferenceEquals(pressedField?.GetValue(nc), leaf);
                    mid.Children.Remove(leaf);
                    bool cleared = pressedField?.GetValue(nc) == null;
                    bool nestedSubsOk = !SubscribedContains(subsField.GetValue(nc), leaf);
                    OnMouseUp?.Invoke(nc, new object[]
                        { new MouseEventArgs(MouseButtons.Left, 1, 40, 40, 0) });
                    nestedOk = pressed && cleared && clicks == 0 && nestedSubsOk;
                    nestedForm.Close();
                }

                bool ok = removeOk && rebakeOk && promoteOk && setOk &&
                          ancestorOk && nullOk && nestedOk &&
                          builderSvg && childSvg && baseSvg && baseSubscribed;
                Console.WriteLine($"[assert] {(ok ? "OK" : "FALLO")} nodos desprendidos: remove={removeOk} rebake={rebakeOk} promote={promoteOk} ancestor={ancestorOk} setItem={setOk} builderSvg={builderSvg} childSvg={childSvg} baseSvg={baseSvg}/{baseSubscribed} null={nullOk} nieto={nestedOk}");
                if (!ok) failures++;
                form.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO nodos desprendidos: {ex.Message}");
                failures++;
            }
            return failures;
        }

        /// <summary>
        /// 100 ciclos de alta + paint + remove + dispose de controles con glow:
        /// comprueba que WS/GDI/handles no crecen (sin tocar el árbol visual).
        /// </summary>
        private static int RunControlChurnCheck()
        {
            int failures = 0;
            try
            {
                using var form = new Form { Size = new Size(420, 300) };
                form.Show();
                Pump(300);

                int gdi0 = 0, handles0 = 0;
                long ws0 = 0, priv0 = 0;
                bool baselineSet = false;
                var shadowField = typeof(RenderNode).GetField("_cachedShadowImage",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                bool bakeOk = true;
                bool releaseOk = true;
                for (int i = 0; i < 100; i++)
                {
                    var c = FluentElement.Design($"churn-glow{i}").Layout(80, 80, 160, 48)
                        .Glow("#00aaff", 12).Apply(form);
                    PaintOnce(c);
                    var node = c.VisualNode;
                    if (node == null || shadowField?.GetValue(node) == null)
                        bakeOk = false;
                    form.Controls.Remove(c);
                    c.Dispose();
                    if (node != null && shadowField?.GetValue(node) != null)
                        releaseOk = false;

                    // Baseline tras 10 ciclos de calentamiento **post-GC**:
                    // la caída de handles del settle no debe enmascarar leaks.
                    if (i == 10)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        Pump(100);
                        using var proc = Process.GetCurrentProcess();
                        proc.Refresh();
                        gdi0 = GetGuiResources(proc.Handle, 0);
                        handles0 = proc.HandleCount;
                        ws0 = proc.WorkingSet64;
                        priv0 = proc.PrivateMemorySize64;
                        baselineSet = true;
                    }
                }
                Pump(200);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                int gdi1, handles1;
                long ws1, priv1;
                using (var proc = Process.GetCurrentProcess())
                {
                    proc.Refresh();
                    gdi1 = GetGuiResources(proc.Handle, 0);
                    handles1 = proc.HandleCount;
                    ws1 = proc.WorkingSet64;
                    priv1 = proc.PrivateMemorySize64;
                }

                bool ok = baselineSet && bakeOk && releaseOk &&
                          gdi1 - gdi0 <= 4 &&
                          handles1 - handles0 <= 8 &&
                          ws1 - ws0 <= 12L * 1024 * 1024 &&
                          priv1 - priv0 <= 4L * 1024 * 1024;
                Console.WriteLine($"[assert] {(ok ? "OK" : "FALLO")} churn 100 controles glow: bake={bakeOk}, release={releaseOk}, baseline=10, GDI={gdi0}->{gdi1}, handles={handles0}->{handles1}, WS={ws0 / 1048576}->{ws1 / 1048576} MB, Private={priv0 / 1048576}->{priv1 / 1048576} MB");
                if (!ok) failures++;
                form.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO churn 100 controles: {ex.Message}");
                failures++;
            }
            return failures;
        }

        /// <summary>WeakReference: dispose antes de que el padre cree handle.</summary>
        private static int RunDisposedBeforeHandleCheck()
        {
            int failures = 0;
            bool wasEnabled = ModernGlowOverlay.Enabled;
            try
            {
                ModernGlowOverlay.Enabled = true;
                using var parent = new Form();
                var weak = CreateDisposedHookedControl(parent,
                    out bool installed, out bool cleared,
                    out bool subscribedBefore, out bool subscribedAfter);
                bool unsubscribed = subscribedBefore && !subscribedAfter;

                // Sin crear el handle del padre: si el hook siguiera suscrito,
                // el evento mantendría vivo al control y el WeakReference no moriría.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                bool collected = !weak.IsAlive;
                GC.KeepAlive(parent);

                // Segunda comprobación: crear el handle no debe adjuntar nada.
                _ = parent.Handle;
                Pump(100);
                bool notAttached = !ModernGlowOverlay.IsAttached(parent);

                bool ok = installed && cleared && unsubscribed &&
                          collected && notAttached;
                Console.WriteLine($"[assert] {(ok ? "OK" : "FALLO")} dispose antes de handle: instalado={installed}, limpio={cleared}, evento={subscribedBefore}->{subscribedAfter}, recolectado={collected}, sin_attach={notAttached}");
                if (!ok) failures++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO dispose antes de handle: {ex.Message}");
                failures++;
            }
            finally
            {
                ModernGlowOverlay.Enabled = wasEnabled;
            }
            return failures;
        }

        private static WeakReference CreateDisposedHookedControl(Form parent,
            out bool installed, out bool cleared,
            out bool subscribedBefore, out bool subscribedAfter)
        {
            var c = FluentElement.Design("weak-hook").Layout(30, 30, 100, 40)
                .Glow("#ff00ff", 8).Apply(parent);
            var handlerField = typeof(ModernControlBase).GetField("_overlayAttachHandler",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var parentField = typeof(ModernControlBase).GetField("_overlayAttachParent",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var hookHandler = handlerField?.GetValue(c) as Delegate;
            installed = hookHandler != null && parentField?.GetValue(c) != null;
            subscribedBefore = OverlayHookSubscribed(parent, hookHandler);
            c.Dispose();
            parent.Controls.Remove(c);
            cleared = handlerField?.GetValue(c) == null &&
                      parentField?.GetValue(c) == null;
            subscribedAfter = OverlayHookSubscribed(parent, hookHandler);
            return new WeakReference(c);
        }

        /// <summary>
        /// El ripple debe conservar el nodo después de MouseUp para dibujar el
        /// fade (antes _currentPressedNode=null lo cortaba de golpe).
        /// </summary>
        private static int RunRippleFadeCheck()
        {
            int failures = 0;
            try
            {
                using var form = new Form { Size = new Size(400, 240) };
                var b = FluentElement.Design("ripple").Layout(80, 80, 160, 48)
                    .Ripple("#40FF0000").RippleDuration(900).Apply(form);
                form.Show();
                Pump(300);
                bool useRipple = b.UseRipple;
                bool durationOk = Math.Abs(b.RippleDuration - 900f) < 0.1f && useRipple;
                Console.WriteLine($"[assert] {(durationOk ? "OK" : "FALLO")} ripple duration={b.RippleDuration:0}ms (900) useRipple={useRipple}");
                if (!durationOk) failures++;

                OnMouseDown?.Invoke(b, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 40, 24, 0) });
                Pump(200);

                var t = typeof(ModernControlBase);
                var nodeField = t.GetField("_rippleNode", BindingFlags.Instance | BindingFlags.NonPublic);
                var ripField = t.GetField("_isRippling", BindingFlags.Instance | BindingFlags.NonPublic);
                var radiusField = t.GetField("_rippleRadius", BindingFlags.Instance | BindingFlags.NonPublic);
                var maxField = t.GetField("_maxRippleRadius", BindingFlags.Instance | BindingFlags.NonPublic);
                var opacityField = t.GetField("_rippleOpacity", BindingFlags.Instance | BindingFlags.NonPublic);

                int ReadRippleAlpha()
                {
                    PaintOnce(b);
                    object? paintNode = nodeField?.GetValue(b);
                    var paintField = paintNode?.GetType().GetField("_cachedRipplePaint",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    object? paint = paintField?.GetValue(paintNode);
                    object? color = paint?.GetType().GetProperty("Color")?.GetValue(paint);
                    return color?.GetType().GetProperty("Alpha")?.GetValue(color) is byte alpha
                        ? alpha : -1;
                }

                float earlyFade = opacityField?.GetValue(b) is float ef ? ef : -1f;
                int alphaEarly = ReadRippleAlpha();
                Pump(250); // total 450ms

                float radius = radiusField?.GetValue(b) is float r ? r : -1f;
                float max = maxField?.GetValue(b) is float m && m > 0f
                    ? m : (float)Math.Sqrt(b.Width * b.Width + b.Height * b.Height);
                float ratio = max > 0f ? radius / max : 0f;
                bool timingOk = ratio >= 0.3f && ratio <= 0.8f;
                Console.WriteLine($"[assert] {(timingOk ? "OK" : "FALLO")} ripple timing: radio/max={ratio:0.00} a 450ms de 900 (0.30..0.80)");
                if (!timingOk) failures++;

                // Alpha por píxel: se compara con la opacidad REAL del fade, no
                // con un rango temporal fijo; un alpha constante o 255 falla.
                float lateFade = opacityField?.GetValue(b) is float lf ? lf : -1f;
                int alphaLate = ReadRippleAlpha();
                var rippleNode = nodeField?.GetValue(b) as RenderNode;
                float baseAlpha = rippleNode?.Ripple.Color.A ?? 0f;
                float rippleOpacity = rippleNode?.Ripple.Opacity ?? 1f;
                int ExpectedAlpha(float fade) =>
                    (int)Math.Clamp(baseAlpha * rippleOpacity * fade, 0f, 255f);
                int expectedEarly = ExpectedAlpha(earlyFade);
                int expectedLate = ExpectedAlpha(lateFade);
                bool alphaOk = earlyFade > lateFade &&
                               Math.Abs(alphaEarly - expectedEarly) <= 1 &&
                               Math.Abs(alphaLate - expectedLate) <= 1;
                Console.WriteLine($"[assert] {(alphaOk ? "OK" : "FALLO")} ripple alpha: early={alphaEarly} (esperado {expectedEarly}, fade {earlyFade:0.00}) late={alphaLate} (esperado {expectedLate}, fade {lateFade:0.00}); opaco=255 fallaria");
                if (!alphaOk) failures++;

                OnMouseUp?.Invoke(b, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 40, 24, 0) });
                Pump(60);

                object? node = nodeField?.GetValue(b);
                bool rippling = ripField?.GetValue(b) is true;

                Console.WriteLine($"[assert] {(rippling && node != null ? "OK" : "FALLO")} ripple fade: isRippling={rippling} nodo={node != null}");
                if (!(rippling && node != null)) failures++;

                Pump(700);
                bool finished = ripField?.GetValue(b) is false;
                Console.WriteLine($"[assert] {(finished ? "OK" : "FALLO")} ripple fin: isRippling={!finished} tras 900+540ms");
                if (!finished) failures++;

                // Clic mantenido: el fade debe correr igual y no dejar el botón
                // teñido para siempre.
                OnMouseDown?.Invoke(b, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 40, 24, 0) });
                Pump(1600);
                bool heldFinished = ripField?.GetValue(b) is false;
                Console.WriteLine($"[assert] {(heldFinished ? "OK" : "FALLO")} ripple mantenido: isRippling={!heldFinished} tras 1600ms");
                if (!heldFinished) failures++;
                OnMouseUp?.Invoke(b, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 40, 24, 0) });

                // Clics rápidos: el segundo Down no debe reiniciar la onda
                // (radio no vuelve al inicio).
                OnMouseDown?.Invoke(b, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 40, 24, 0) });
                Pump(90);
                float r1 = radiusField?.GetValue(b) is float rr1 ? rr1 : -1f;
                OnMouseDown?.Invoke(b, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0) });
                Pump(40);
                float r2 = radiusField?.GetValue(b) is float rr2 ? rr2 : -1f;
                bool noRestart = r2 >= r1;
                Console.WriteLine($"[assert] {(noRestart ? "OK" : "FALLO")} ripple clics rápidos: radio {r1:0.0} -> {r2:0.0} (sin reinicio)");
                if (!noRestart) failures++;
                OnMouseUp?.Invoke(b, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0) });
                Pump(400);

                form.Close();
                form.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO ripple fade: {ex.Message}");
                failures++;
            }
            return failures;
        }

        /// <summary>
        /// El anillo de sombra no debe ser clickeable: WM_NCHITTEST devuelve
        /// HTTRANSPARENT fuera del cuerpo y HTCLIENT dentro.
        /// </summary>
        private static int RunHitTestCheck()
        {
            int failures = 0;
            using var overlayScope = new OverlayDisabledScope();
            try
            {
                using var form = new Form { Size = new Size(500, 320) };
                var b = FluentElement.Design("hit").Layout(120, 100, 160, 48)
                    .Glow("#ff00ff", 14).Apply(form);
                form.Show();
                Pump(400);

                Rectangle lb = b.LogicalBounds;
                Point pShadow = form.PointToScreen(new Point(lb.Right + 5, lb.Top + lb.Height / 2));
                Point pBody = form.PointToScreen(new Point(lb.Left + 10, lb.Top + 10));

                IntPtr LParam(Point p) => (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));
                IntPtr shadow = SendMessage(b.Handle, 0x0084, IntPtr.Zero, LParam(pShadow));
                IntPtr body = SendMessage(b.Handle, 0x0084, IntPtr.Zero, LParam(pBody));

                bool ok = shadow.ToInt32() == -1 && body.ToInt32() == 1;
                Console.WriteLine($"[assert] {(ok ? "OK" : "FALLO")} hit-test sombra: anillo={shadow.ToInt32()} cuerpo={body.ToInt32()} (-1/1)");
                if (!ok) failures++;

                // Esquinas del cuerpo y anillo superior/inferior.
                Point pTL = form.PointToScreen(new Point(lb.Left + 2, lb.Top + 2));
                Point pTR = form.PointToScreen(new Point(lb.Right - 2, lb.Top + 2));
                Point pBL = form.PointToScreen(new Point(lb.Left + 2, lb.Bottom - 2));
                Point pBR = form.PointToScreen(new Point(lb.Right - 2, lb.Bottom - 2));
                Point pTop = form.PointToScreen(new Point(lb.Left + lb.Width / 2, lb.Top - 5));
                Point pBottom = form.PointToScreen(new Point(lb.Left + lb.Width / 2, lb.Bottom + 5));
                int hTL = SendMessage(b.Handle, 0x0084, IntPtr.Zero, LParam(pTL)).ToInt32();
                int hTR = SendMessage(b.Handle, 0x0084, IntPtr.Zero, LParam(pTR)).ToInt32();
                int hBL = SendMessage(b.Handle, 0x0084, IntPtr.Zero, LParam(pBL)).ToInt32();
                int hBR = SendMessage(b.Handle, 0x0084, IntPtr.Zero, LParam(pBR)).ToInt32();
                int hTop = SendMessage(b.Handle, 0x0084, IntPtr.Zero, LParam(pTop)).ToInt32();
                int hBottom = SendMessage(b.Handle, 0x0084, IntPtr.Zero, LParam(pBottom)).ToInt32();
                bool cornersOk = hTL == 1 && hTR == 1 && hBL == 1 && hBR == 1;
                bool ringOk = hTop == -1 && hBottom == -1;
                Console.WriteLine($"[assert] {(cornersOk && ringOk ? "OK" : "FALLO")} hit-test esquinas: tl={hTL}, tr={hTR}, bl={hBL}, br={hBR}, arriba={hTop}, abajo={hBottom} (cuerpo=1, anillo=-1)");
                if (!cornersOk || !ringOk) failures++;

                // Control Glow pegado al borde derecho/inferior: el HWND se
                // clampa al parent y el hit-test debe seguir distinguiendo
                // cuerpo (1) y anillo (-1) dentro del HWND recortado.
                var edge = FluentElement.Design("hit-clamped")
                    .Layout(form.ClientSize.Width - 50, form.ClientSize.Height - 40, 160, 48)
                    .Glow("#00ff00", 14).Apply(form);
                Pump(300);
                Rectangle edgeLogical = edge.LogicalBounds;
                Rectangle edgeBounds = edge.Bounds;
                int edgeMargin = (int)Math.Ceiling(14 * form.DeviceDpi / 96f) + 1;
                Rectangle expectedLogical = new Rectangle(
                    form.ClientSize.Width - 50, form.ClientSize.Height - 40, 160, 48);
                Rectangle expectedEdge = Rectangle.FromLTRB(
                    Math.Max(0, expectedLogical.Left - edgeMargin),
                    Math.Max(0, expectedLogical.Top - edgeMargin),
                    Math.Min(form.ClientSize.Width, expectedLogical.Right + edgeMargin),
                    Math.Min(form.ClientSize.Height, expectedLogical.Bottom + edgeMargin));
                bool clampedExact = edgeBounds == expectedEdge &&
                                    edgeLogical == expectedLogical &&
                                    (edgeLogical.Right > form.ClientSize.Width ||
                                     edgeLogical.Bottom > form.ClientSize.Height);
                // lParam usa coordenadas de pantalla: el WndProc del control las
                // convierte a cliente (convención del harness).
                Point pEdgeBody = form.PointToScreen(new Point(
                    edgeLogical.Left + 10, edgeLogical.Top + 10));
                Point pEdgeRing = form.PointToScreen(new Point(
                    edgeLogical.Left - 5, edgeLogical.Top + edgeLogical.Height / 2));
                Point pEdgeRight = form.PointToScreen(new Point(
                    form.ClientSize.Width - 2, edgeLogical.Top + 10));
                Point pEdgeBottom = form.PointToScreen(new Point(
                    edgeLogical.Left + 10, form.ClientSize.Height - 2));
                int hEdgeBody = SendMessage(edge.Handle, 0x0084, IntPtr.Zero,
                    LParam(pEdgeBody)).ToInt32();
                int hEdgeRing = SendMessage(edge.Handle, 0x0084, IntPtr.Zero,
                    LParam(pEdgeRing)).ToInt32();
                int hEdgeRight = SendMessage(edge.Handle, 0x0084, IntPtr.Zero,
                    LParam(pEdgeRight)).ToInt32();
                int hEdgeBottom = SendMessage(edge.Handle, 0x0084, IntPtr.Zero,
                    LParam(pEdgeBottom)).ToInt32();
                bool clampHitOk = clampedExact && hEdgeBody == 1 && hEdgeRing == -1 &&
                                  hEdgeRight == 1 && hEdgeBottom == 1;
                Console.WriteLine($"[assert] {(clampHitOk ? "OK" : "FALLO")} hit-test clamp: bounds={edgeBounds} esperado={expectedEdge} logical={edgeLogical} esperado={expectedLogical}, cuerpo={hEdgeBody}, anillo={hEdgeRing}, bordeDer={hEdgeRight}, bordeInf={hEdgeBottom}");
                if (!clampHitOk) failures++;

                form.Close();
                form.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO hit-test: {ex.Message}");
                failures++;
            }
            return failures;
        }

        /// <summary>
        /// Overlay Skia opt-in: se adjunta al padre, queda detrás de los
        /// controles y el HWND deja de expandirse por la sombra (la dibuja el
        /// overlay). No se puede validar el píxel con DrawToBitmap (layered).
        /// </summary>
        private static int RunOverlayCheck()
        {
            int failures = 0;
            ModernGlowOverlay.Enabled = true;
            try
            {
                using var form = new Form { Size = new Size(520, 320) };
                var b = FluentElement.Design("overlay").Layout(120, 100, 160, 48)
                    .Glow("#ff00ff", 14).Apply(form);
                form.Show();
                Pump(500);
                // Segundo control añadido DESPUÉS del overlay: debe quedar
                // delante (overlay al fondo) y no tapar el cuerpo.
                var b2 = FluentElement.Design("overlay2").Layout(120, 190, 160, 48)
                    .Glow("#00ff00", 14).Apply(form);
                Pump(300);

                bool attached = ModernGlowOverlay.IsAttached(form);
                bool behind = ModernGlowOverlay.IsBehindControls(form);
                bool layeredOk = ModernGlowOverlay.LastUpdateSucceeded;
                bool positionOk = ModernGlowOverlay.LastOverlayScreen == form.PointToScreen(Point.Empty);
                int shadowExpansion = b.LogicalBounds.Top - b.Bounds.Top;
                bool contentOnly = shadowExpansion == 0;
                Console.WriteLine($"[assert] {(attached && behind && contentOnly && layeredOk && positionOk ? "OK" : "FALLO")} overlay Skia: adjunto={attached}, z-order={behind}, UpdateLayered={layeredOk} (err={ModernGlowOverlay.LastUpdateError}), posición={positionOk} {ModernGlowOverlay.LastOverlayScreen}, expansión superior={shadowExpansion}px (==0)");
                if (!attached || !behind || !contentOnly || !layeredOk || !positionOk) failures++;

                // Un control añadido después también debe dejar el overlay al fondo.
                var b3 = FluentElement.Design("overlay3").Layout(300, 100, 140, 48)
                    .Glow("#00aaff", 14).Apply(form);
                Pump(200);
                bool behindAfter = ModernGlowOverlay.IsBehindControls(form);
                Console.WriteLine($"[assert] {(behindAfter ? "OK" : "FALLO")} overlay z-order tras alta: {behindAfter}");
                if (!behindAfter) failures++;

                // Enabled=false solo gatea altas nuevas: un overlay ya adjunto
                // sigue pintando y el control no debe reinflarse.
                ModernGlowOverlay.Enabled = false;
                typeof(ModernControlBase).GetMethod("RefreshPhysicalBounds",
                    BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(b, null);
                Pump(100);
                bool toggleAttached = ModernGlowOverlay.IsAttached(form);
                int toggleExpansion = b.LogicalBounds.Top - b.Bounds.Top;
                bool toggleContent = toggleExpansion == 0;
                ModernGlowOverlay.Enabled = true;
                Console.WriteLine($"[assert] {(toggleAttached && toggleContent ? "OK" : "FALLO")} overlay Enabled=false con overlay adjunto: adjunto={toggleAttached}, expansión={toggleExpansion}px (==0)");
                if (!toggleAttached || !toggleContent) failures++;

                // Adjuntado realmente tardío: form ya visible sin overlay; al
                // agregar el primer control Modern el overlay se adjunta después
                // y RefreshChildren debe devolver el HWND a tamaño de contenido.
                using (var lateForm = new Form { Size = new Size(420, 260) })
                {
                    lateForm.Show();
                    Pump(200);
                    bool noOverlayYet = !ModernGlowOverlay.IsAttached(lateForm);
                    var late = FluentElement.Design("late-attach").Layout(80, 60, 160, 48)
                        .Glow("#ff00ff", 14).Apply(lateForm);
                    Pump(300);
                    bool lateAttached = ModernGlowOverlay.IsAttached(lateForm);
                    int lateExpansion = late.LogicalBounds.Top - late.Bounds.Top;
                    bool lateContent = lateExpansion == 0;
                    Console.WriteLine($"[assert] {(noOverlayYet && lateAttached && lateContent ? "OK" : "FALLO")} overlay adjuntado tarde: sin-overlay-previo={noOverlayYet}, adjunto={lateAttached}, expansión={lateExpansion}px (==0)");
                    if (!noOverlayYet || !lateAttached || !lateContent) failures++;
                    lateForm.Close();
                    lateForm.Dispose();
                }

                // Reparentado a un padre que ya tiene overlay: ParentControlAdded
                // debe recomputar el tamaño de contenido sin otro attach.
                using (var host = new Form { Size = new Size(360, 240) })
                using (var other = new Form { Size = new Size(300, 200) })
                {
                    try
                    {
                        host.Show();
                        FluentElement.Design("host").Layout(60, 60, 140, 44).Glow("#00ff00", 14).Apply(host);
                        Pump(300);
                        bool hostAttached = ModernGlowOverlay.IsAttached(host);

                        ModernGlowOverlay.Enabled = false;
                        other.Show();
                        var moved = FluentElement.Design("moved").Layout(50, 50, 140, 44)
                            .Glow("#ff00ff", 14).Apply(other);
                        Pump(150);
                        bool otherAttached = ModernGlowOverlay.IsAttached(other);
                        ModernGlowOverlay.Enabled = true;

                        host.Controls.Add(moved);
                        Pump(300);
                        int movedExpansion = moved.LogicalBounds.Top - moved.Bounds.Top;
                        bool movedContent = movedExpansion == 0;
                        Console.WriteLine($"[assert] {(hostAttached && !otherAttached && movedContent ? "OK" : "FALLO")} overlay control reparentado: host={hostAttached}, otro={otherAttached}, expansión={movedExpansion}px (==0)");
                        if (!hostAttached || otherAttached || !movedContent) failures++;
                        host.Close();
                        host.Dispose();
                        other.Close();
                        other.Dispose();
                    }
                    finally
                    {
                        ModernGlowOverlay.Enabled = true;
                    }
                }

                // Reparentar FUERA de un padre con overlay: el control debe
                // recuperar el margen de sombra en su propio HWND.
                using (var source = new Form { Size = new Size(360, 240) })
                using (var bare = new Form { Size = new Size(360, 240) })
                {
                    try
                    {
                        source.Show();
                        var movedOut = FluentElement.Design("out").Layout(60, 60, 140, 44)
                            .Glow("#ff00ff", 14).Apply(source);
                        Pump(300);
                        int insideExpansion = movedOut.LogicalBounds.Top - movedOut.Bounds.Top;
                        ModernGlowOverlay.Enabled = false;
                        bare.Show();
                        bare.Controls.Add(movedOut);
                        Pump(300);
                        int outsideExpansion = movedOut.LogicalBounds.Top - movedOut.Bounds.Top;
                        bool ok = insideExpansion == 0 && outsideExpansion >= 10;
                        Console.WriteLine($"[assert] {(ok ? "OK" : "FALLO")} overlay reparentado fuera: dentro={insideExpansion}px (==0), fuera={outsideExpansion}px (>=10)");
                        if (!ok) failures++;
                    }
                    finally
                    {
                        ModernGlowOverlay.Enabled = true;
                    }
                }

                // Recrear el handle con un render pendiente no debe dejar el
                // overlay sin pintar (flag _renderQueuedFlag atascado).
                using (var rec = new Form { Size = new Size(420, 260) })
                {
                    FluentElement.Design("recreate").Layout(80, 60, 160, 48)
                        .Glow("#ff00ff", 14).Apply(rec);
                    ResetOverlayStats();
                    rec.Show();
                    Pump(300);
                    rec.Size = new Size(520, 320);
                    typeof(Control).GetMethod("RecreateHandle",
                        BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(rec, null);
                    Pump(500);
                    bool recAttached = ModernGlowOverlay.IsAttached(rec);
                    bool recBounds = ModernGlowOverlay.LastOverlayBounds.Width == rec.ClientSize.Width &&
                                     ModernGlowOverlay.LastOverlayBounds.Height == rec.ClientSize.Height &&
                                     ModernGlowOverlay.LastOverlayScreen == rec.PointToScreen(Point.Empty);
                    Console.WriteLine($"[assert] {(recAttached && recBounds ? "OK" : "FALLO")} overlay tras recrear handle: adjunto={recAttached}, bounds={ModernGlowOverlay.LastOverlayBounds} cliente={rec.ClientSize}, screen={ModernGlowOverlay.LastOverlayScreen} esperado={rec.PointToScreen(Point.Empty)}");
                    if (!recAttached || !recBounds) failures++;
                    rec.Close();
                    rec.Dispose();
                }

                // DPI: OnDpiChangedAfterParent debe liberar la superficie y
                // volver a pintar; sin el override la superficie sería la misma.
                using (var dpiForm = new Form { Size = new Size(420, 260) })
                {
                    FluentElement.Design("overlay-dpi").Layout(80, 60, 160, 48)
                        .Glow("#ff00ff", 14).Apply(dpiForm);
                    dpiForm.Show();
                    Pump(400);
                    Control overlayControl = dpiForm.Controls[dpiForm.Controls.Count - 1];
                    var surfaceField = overlayControl.GetType().GetField("_surface",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    object? surfaceBefore = surfaceField?.GetValue(overlayControl);
                    var dpiHandler = typeof(Control).GetMethod("OnDpiChangedAfterParent",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    int gdiBefore;
                    using (var proc = Process.GetCurrentProcess())
                        gdiBefore = GetGuiResources(proc.Handle, 0);
                    dpiHandler?.Invoke(overlayControl, new object[] { EventArgs.Empty });
                    Pump(400);
                    object? surfaceAfter = surfaceField?.GetValue(overlayControl);
                    int gdiAfter;
                    using (var proc = Process.GetCurrentProcess())
                        gdiAfter = GetGuiResources(proc.Handle, 0);
                    bool recreated = surfaceBefore != null && surfaceAfter != null &&
                                     !ReferenceEquals(surfaceBefore, surfaceAfter);
                    bool oldDisposed = surfaceBefore?.GetType().GetProperty("IsDisposed")
                        ?.GetValue(surfaceBefore) is true;
                    // SKSurface sobre DIB externo: en SkiaSharp IsDisposed puede
                    // quedar false tras Dispose; la recreación de la superficie
                    // y el GDI estable son la evidencia del camino DPI.
                    bool dpiOk = recreated && ModernGlowOverlay.IsAttached(dpiForm) &&
                                 ModernGlowOverlay.LastUpdateSucceeded &&
                                 ModernGlowOverlay.LastOverlayBounds.Width == dpiForm.ClientSize.Width &&
                                 ModernGlowOverlay.LastOverlayBounds.Height == dpiForm.ClientSize.Height &&
                                 ModernGlowOverlay.LastOverlayScreen == dpiForm.PointToScreen(Point.Empty) &&
                                 overlayControl.Bounds.Width == dpiForm.ClientSize.Width &&
                                 overlayControl.Bounds.Height == dpiForm.ClientSize.Height &&
                                 gdiAfter - gdiBefore <= 2;
                    Console.WriteLine($"[assert] {(dpiOk ? "OK" : "FALLO")} overlay DPI: superficie_recreada={recreated}, vieja_dispose={oldDisposed}, adjunto={ModernGlowOverlay.IsAttached(dpiForm)}, UpdateLayered={ModernGlowOverlay.LastUpdateSucceeded}, bounds={ModernGlowOverlay.LastOverlayBounds} cliente={dpiForm.ClientSize}, GDI={gdiBefore}->{gdiAfter}");
                    if (!dpiOk) failures++;

                    // 20 cambios de DPI más: las superficies viejas no deben
                    // quedar retenidas (Private acotado, sin depender de IsDisposed).
                    using (var proc = Process.GetCurrentProcess())
                    {
                        GC.Collect();
                        long priv0 = proc.PrivateMemorySize64;
                        for (int i = 0; i < 20; i++)
                        {
                            dpiHandler?.Invoke(overlayControl, new object[] { EventArgs.Empty });
                            Pump(120);
                        }
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        proc.Refresh();
                        long priv1 = proc.PrivateMemorySize64;
                        ResetOverlayStats(dpiForm);
                        bool dpiRenderOk = PumpUntil(() =>
                            ModernGlowOverlay.IsAttached(dpiForm) &&
                            ModernGlowOverlay.LastUpdateSucceeded &&
                            ModernGlowOverlay.LastOverlayBounds.Width == dpiForm.ClientSize.Width &&
                            ModernGlowOverlay.LastOverlayBounds.Height == dpiForm.ClientSize.Height &&
                            ModernGlowOverlay.LastOverlayScreen == dpiForm.PointToScreen(Point.Empty), 1500);
                        bool dpiLeakOk = priv1 - priv0 <= 6 * 1024 * 1024 && dpiRenderOk;
                        Console.WriteLine($"[assert] {(dpiLeakOk ? "OK" : "FALLO")} overlay DPI x20: Private={priv0 / 1048576}->{priv1 / 1048576} MB (delta<=6), render_final={dpiRenderOk}, bounds={ModernGlowOverlay.LastOverlayBounds} cliente={dpiForm.ClientSize}");
                        if (!dpiLeakOk) failures++;
                    }
                    dpiForm.Close();
                    dpiForm.Dispose();
                }

                // Reparentar a un form SIN overlay con Enabled=true: el nuevo
                // padre recibe overlay y el control queda a tamaño de contenido.
                using (var hostOn = new Form { Size = new Size(360, 240) })
                using (var fresh = new Form { Size = new Size(360, 240) })
                {
                    hostOn.Show();
                    var movedOn = FluentElement.Design("out-on").Layout(60, 60, 140, 44)
                        .Glow("#ff00ff", 14).Apply(hostOn);
                    Pump(300);
                    int insideOn = movedOn.LogicalBounds.Top - movedOn.Bounds.Top;
                    fresh.Show();
                    fresh.Controls.Add(movedOn);
                    Pump(400);
                    bool freshAttached = ModernGlowOverlay.IsAttached(fresh);
                    int outsideOn = movedOn.LogicalBounds.Top - movedOn.Bounds.Top;
                    bool ok = insideOn == 0 && freshAttached && outsideOn == 0;
                    Console.WriteLine($"[assert] {(ok ? "OK" : "FALLO")} overlay reparentado a form sin overlay (Enabled=true): nuevo_adjunto={freshAttached}, dentro={insideOn}px, fuera={outsideOn}px (==0)");
                    if (!ok) failures++;
                    hostOn.Close();
                    hostOn.Dispose();
                    fresh.Close();
                    fresh.Dispose();
                }

                // Dispose antes de que el padre cree handle: el hook no queda.
                using (var hookParent = new Form())
                {
                    var hooked = FluentElement.Design("hook").Layout(40, 40, 120, 40)
                        .Glow("#ff00ff", 10).Apply(hookParent);
                    var handlerField = typeof(ModernControlBase).GetField("_overlayAttachHandler",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    var hookParentField = typeof(ModernControlBase).GetField("_overlayAttachParent",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    object? handlerBefore = handlerField?.GetValue(hooked);
                    bool installed = handlerBefore != null &&
                                     hookParentField?.GetValue(hooked) != null;
                    bool subscribedBefore = OverlayHookSubscribed(hookParent, handlerBefore as Delegate);
                    hooked.Dispose();
                    hookParent.Controls.Remove(hooked);
                    bool cleared = handlerField?.GetValue(hooked) == null &&
                                   hookParentField?.GetValue(hooked) == null;

                    // Verificación real: antes del Dispose el delegate DEBE
                    // estar en el EventHandlerList de HandleCreated del padre,
                    // y después NO. Así también falla si la key cambiara.
                    bool subscribedAfter = OverlayHookSubscribed(hookParent, handlerBefore as Delegate);
                    bool unsubscribed = subscribedBefore && !subscribedAfter;

                    // Segunda comprobación: crear el handle del padre después
                    // del Dispose no debe adjuntar nada.
                    _ = hookParent.Handle;
                    Pump(100);
                    bool hookNotAttached = !ModernGlowOverlay.IsAttached(hookParent);
                    Console.WriteLine($"[assert] {(installed && cleared && unsubscribed && hookNotAttached ? "OK" : "FALLO")} overlay hook liberado al disponer sin handle: instalado={installed}, antes={subscribedBefore}, despues={subscribedAfter}, limpio={cleared}, sin_attach={hookNotAttached}");
                    if (!installed || !cleared || !unsubscribed || !hookNotAttached) failures++;
                }

                form.Close();
                form.Dispose();

                // Enabled=false no debe crear overlay.
                ModernGlowOverlay.Enabled = false;
                using var plain = new Form { Size = new Size(300, 200) };
                FluentElement.Design("plain").Layout(60, 60, 120, 40).Glow("#ff00ff", 10).Apply(plain);
                plain.Show();
                Pump(150);
                bool notAttached = !ModernGlowOverlay.IsAttached(plain);
                Console.WriteLine($"[assert] {(notAttached ? "OK" : "FALLO")} overlay desactivado: no se adjunta");
                if (!notAttached) failures++;
                plain.Close();
                plain.Dispose();

                // Variante borderless + Region redondeado (como Form1).
                try
                {
                    ModernGlowOverlay.Enabled = true;
                    using var borderless = new Form
                    {
                        FormBorderStyle = FormBorderStyle.None,
                        StartPosition = FormStartPosition.Manual,
                        Location = new Point(50, 50),
                        Size = new Size(420, 280),
                        BackColor = Color.Teal
                    };
                    using (var rp = new GraphicsPath())
                    {
                        int rr = 24;
                        rp.AddArc(0, 0, rr, rr, 180, 90);
                        rp.AddArc(420 - rr - 1, 0, rr, rr, 270, 90);
                        rp.AddArc(420 - rr - 1, 280 - rr - 1, rr, rr, 0, 90);
                        rp.AddArc(0, 280 - rr - 1, rr, rr, 90, 90);
                        rp.CloseFigure();
                        borderless.Region = new Region(rp);
                    }
                    FluentElement.Design("overlay-borderless").Layout(80, 60, 160, 44)
                        .Glow("#ff00ff", 14).Apply(borderless);
                    borderless.Show();
                    Pump(400);
                    bool bAttached = ModernGlowOverlay.IsAttached(borderless);
                    Console.WriteLine($"[assert] {(bAttached ? "OK" : "FALLO")} overlay borderless+region: adjunto={bAttached}");
                    if (!bAttached) failures++;
                    borderless.Close();
                    borderless.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[assert] FALLO overlay borderless+region: {ex.Message}");
                    failures++;
                }

                // Churn: 30 forms con overlay, altas/bajas y repintado.
                int churnOk = 0, churnFail = 0;
                using (var proc0 = Process.GetCurrentProcess())
                {
                    int gdi0 = GetGuiResources(proc0.Handle, 0);
                    int user0 = GetGuiResources(proc0.Handle, 1);
                    int handles0 = proc0.HandleCount;
                    for (int i = 0; i < 30; i++)
                    {
                        // Tamaño distinto por form: así LastOverlayBounds no
                        // puede "heredarse" del form anterior.
                        var churnSize = new Size(300 + i, 220 + (i % 7));
                        using (var churn = new Form { Size = churnSize })
                        {
                            FluentElement.Design($"churn{i}").Layout(70, 60, 150, 44)
                                .Glow("#00aaff", 12).Apply(churn);
                            ResetOverlayStats();
                            churn.Show();
                            bool ready = PumpUntil(() =>
                                ModernGlowOverlay.IsAttached(churn) &&
                                ModernGlowOverlay.LastUpdateSucceeded &&
                                ModernGlowOverlay.LastOverlayBounds.Width == churn.ClientSize.Width &&
                                ModernGlowOverlay.LastOverlayBounds.Height == churn.ClientSize.Height &&
                                ModernGlowOverlay.LastOverlayScreen == churn.PointToScreen(Point.Empty), 1500);
                            if (ready)
                                churnOk++;
                            else
                                churnFail++;
                            churn.Close();
                            churn.Dispose();
                        }
                    }
                    Pump(200);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    using var proc1 = Process.GetCurrentProcess();
                    int gdi1 = GetGuiResources(proc1.Handle, 0);
                    int user1 = GetGuiResources(proc1.Handle, 1);
                    int handles1 = proc1.HandleCount;
                    bool churnClean = handles1 - handles0 <= 30 && gdi1 - gdi0 <= 10 && user1 - user0 <= 10;
                    Console.WriteLine($"[assert] {(churnFail == 0 && churnClean ? "OK" : "FALLO")} overlay churn 30 forms: adjuntos={churnOk}, fallos={churnFail}, handles={handles0}->{handles1}, GDI={gdi0}->{gdi1}, USER={user0}->{user1}");
                    if (churnFail != 0 || !churnClean) failures++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO overlay: {ex.Message}");
                failures++;
            }
            finally
            {
                ModernGlowOverlay.Enabled = false;
            }
            return failures;
        }

        /// <summary>
        /// Carga con overlay activo: resize x80 + hover en 3 controles;
        /// mide CPU/tiempo y GDI, y comprueba que el overlay sigue al cliente.
        /// </summary>
        private static int RunOverlayLoadCheck()
        {
            int failures = 0;
            ModernGlowOverlay.Enabled = true;
            try
            {
                using var form = new Form { Size = new Size(620, 440) };
                var controls = new List<Control>();
                for (int i = 0; i < 6; i++)
                {
                    controls.Add(FluentElement.Design($"load{i}")
                        .Layout(60 + (i % 3) * 180, 60 + (i / 3) * 120, 160, 48)
                        .Glow("#00aaff", 14).Apply(form));
                }
                ResetOverlayStats();
                form.Show();
                Pump(500);

                int gdi0;
                using (var proc = Process.GetCurrentProcess())
                    gdi0 = GetGuiResources(proc.Handle, 0);

                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 80; i++)
                {
                    form.Size = (i % 2 == 0) ? new Size(700, 480) : new Size(620, 440);
                    Pump(15);
                }
                // Terminar en un tamaño distinto al inicial y exigir un render
                // fresco DESPUÉS de los resizes: si el overlay se congeló, falla.
                form.Size = new Size(700, 480);
                ResetOverlayStats(form);
                bool finalReady = PumpUntil(() =>
                    ModernGlowOverlay.IsAttached(form) &&
                    ModernGlowOverlay.LastUpdateSucceeded &&
                    ModernGlowOverlay.LastOverlayBounds.Width == form.ClientSize.Width &&
                    ModernGlowOverlay.LastOverlayBounds.Height == form.ClientSize.Height &&
                    ModernGlowOverlay.LastOverlayScreen == form.PointToScreen(Point.Empty), 1500);
                foreach (var c in controls.Take(3))
                    OnMouseEnter?.Invoke(c, new object[] { EventArgs.Empty });
                Pump(500);
                // Ejercita el render síncrono del overlay DENTRO del paint de un
                // control (regresión de reentrada de layout).
                PaintOnce(controls[0]);
                // También tras el hover: si el render se congela con la animación,
                // el estado fresco del resize no debe taparlo.
                ResetOverlayStats(form);
                bool hoverReady = PumpUntil(() =>
                    ModernGlowOverlay.IsAttached(form) &&
                    ModernGlowOverlay.LastUpdateSucceeded &&
                    ModernGlowOverlay.LastOverlayBounds.Width == form.ClientSize.Width &&
                    ModernGlowOverlay.LastOverlayBounds.Height == form.ClientSize.Height &&
                    ModernGlowOverlay.LastOverlayScreen == form.PointToScreen(Point.Empty), 1500);
                sw.Stop();

                int gdi1;
                using (var proc = Process.GetCurrentProcess())
                    gdi1 = GetGuiResources(proc.Handle, 0);

                var hoverField = typeof(ModernControlBase).GetField("_isHoveringInternal",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                bool hovered = OnMouseEnter != null && hoverField != null &&
                               controls.Take(3).All(c => hoverField.GetValue(c) is true);
                bool attached = ModernGlowOverlay.IsAttached(form);
                bool updated = ModernGlowOverlay.LastUpdateSucceeded;
                bool bounds = ModernGlowOverlay.LastOverlayBounds.Width == form.ClientSize.Width &&
                              ModernGlowOverlay.LastOverlayBounds.Height == form.ClientSize.Height &&
                              ModernGlowOverlay.LastOverlayScreen == form.PointToScreen(Point.Empty);
                bool gdiOk = gdi1 - gdi0 <= 4;
                bool loadOk = attached && updated && bounds && gdiOk && hovered && finalReady && hoverReady;
                Console.WriteLine($"[assert] {(loadOk ? "OK" : "FALLO")} overlay carga resize x80 + hover: {sw.ElapsedMilliseconds}ms, GDI={gdi0}->{gdi1}, final={finalReady}/{hoverReady}, hover={hovered}, adjunto={attached}, UpdateLayered={updated}, bounds={ModernGlowOverlay.LastOverlayBounds} cliente={form.ClientSize}");
                if (!loadOk) failures++;

                form.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO overlay carga: {ex.Message}");
                failures++;
            }
            finally
            {
                ModernGlowOverlay.Enabled = false;
            }
            return failures;
        }

        /// <summary>
        /// El overlay debe activarse solo: default true, se adjunta cuando el
        /// padre tiene un control con glow/sombra y no se adjunta sin glow.
        /// </summary>
        private static int RunGlowOverlayAutoCheck()
        {
            int failures = 0;
            bool wasEnabled = ModernGlowOverlay.Enabled;
            try
            {
                bool defaultOn = wasEnabled;

                using (var glowForm = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(60, 60),
                    Size = new Size(320, 200),
                    ShowInTaskbar = false
                })
                {
                    FluentElement.Design("auto-glow").Layout(40, 40, 160, 48)
                        .Glow("#ff00ff", 14).Apply(glowForm);
                    glowForm.Show();
                    bool attached = PumpUntil(() => ModernGlowOverlay.IsAttached(glowForm), 1500);
                    bool behind = ModernGlowOverlay.IsBehindControls(glowForm);
                    Console.WriteLine($"[assert] {(defaultOn && attached && behind ? "OK" : "FALLO")} overlay auto con glow: default={defaultOn}, adjunto={attached}, z-order={behind}");
                    if (!defaultOn || !attached || !behind) failures++;
                    glowForm.Close();
                }

                using (var plainForm = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(60, 280),
                    Size = new Size(320, 200),
                    ShowInTaskbar = false
                })
                {
                    FluentElement.Design("auto-plain").Layout(40, 40, 160, 48).Apply(plainForm);
                    plainForm.Show();
                    Pump(250);
                    bool notAttached = !ModernGlowOverlay.IsAttached(plainForm);
                    Console.WriteLine($"[assert] {(notAttached ? "OK" : "FALLO")} overlay auto sin glow: adjunto={!notAttached}");
                    if (!notAttached) failures++;
                    plainForm.Close();
                }

                ModernGlowOverlay.Enabled = false;
                using (var disabledForm = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(60, 500),
                    Size = new Size(320, 200),
                    ShowInTaskbar = false
                })
                {
                    FluentElement.Design("auto-off").Layout(40, 40, 160, 48)
                        .Glow("#ff00ff", 14).Apply(disabledForm);
                    disabledForm.Show();
                    Pump(250);
                    bool notAttached = !ModernGlowOverlay.IsAttached(disabledForm);
                    Console.WriteLine($"[assert] {(notAttached ? "OK" : "FALLO")} overlay opt-out: adjunto={!notAttached}");
                    if (!notAttached) failures++;
                    disabledForm.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO overlay auto: {ex.Message}");
                failures++;
            }
            finally
            {
                ModernGlowOverlay.Enabled = wasEnabled;
            }
            return failures;
        }

        /// <summary>
        /// AutoSetBlackBackground automático: negro en Blur (y Acrylic Win11),
        /// y restauración del fondo/controles originales al salir del backdrop.
        /// </summary>
        private static int RunAutoBackgroundCheck()
        {
            using var envScope = new DiagnosticEnvScope();
            int failures = 0;
            try
            {
                using var form = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(80, 300),
                    Size = new Size(360, 220),
                    BackColor = Color.Teal
                };
                form.Show();
                Pump(80);

                using var mgr = new ModernFormManager { TargetForm = form };
                var element = FluentElement.Design("auto-bg").Layout(60, 60, 160, 48).Apply(form);
                Pump(80);

                bool defaultAuto = mgr.AutoSetBlackBackground;

                mgr.BackdropType = FormBackdropType.Blur;
                Pump(500);
                bool blurBlack = form.BackColor.ToArgb() == Color.Black.ToArgb();
                bool childOpaque = element.BackColor.ToArgb() == Color.Black.ToArgb();

                mgr.BackdropType = FormBackdropType.None;
                Pump(200);
                bool noneRestored = form.BackColor.ToArgb() == Color.Teal.ToArgb();
                bool childRestored = element.BackColor.ToArgb() == Color.Transparent.ToArgb();

                mgr.BackdropType = FormBackdropType.Acrylic;
                Pump(250);
                bool win11 = WindowsVersionHelper.IsWindows11OrGreater();
                bool acrylicExpected = win11
                    ? form.BackColor.ToArgb() == Color.Black.ToArgb()
                    : form.BackColor.ToArgb() == Color.Teal.ToArgb();

                mgr.AutoSetBlackBackground = false;
                bool optOutRestored = form.BackColor.ToArgb() == Color.Teal.ToArgb();

                bool ok = defaultAuto && blurBlack && childOpaque &&
                          noneRestored && childRestored && acrylicExpected && optOutRestored;
                Console.WriteLine($"[assert] {(ok ? "OK" : "FALLO")} AutoBlack automático: default={defaultAuto}, Blur={blurBlack}, hijo_opaco={childOpaque}, None_restaura={noneRestored}/{childRestored}, Acrylic={acrylicExpected} (win11={win11}), opt-out={optOutRestored}");
                if (!ok) failures++;

                mgr.BackdropType = FormBackdropType.None;
                form.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO AutoBlack automático: {ex.Message}");
                failures++;
            }
            return failures;
        }

        /// <summary>
        /// Orden real del diseñador: TargetForm se asigna antes que BackColor
        /// y antes de crear el handle. En None debe conservar el BackColor
        /// definitivo (Teal) y al salir de Blur debe restaurarlo, no el color
        /// por defecto del Form.
        /// </summary>
        private static int RunDesignerBackgroundCheck()
        {
            using var envScope = new DiagnosticEnvScope();
            int failures = 0;
            try
            {
                using (var noneForm = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(80, 560),
                    Size = new Size(360, 220),
                    ShowInTaskbar = false
                })
                {
                    using var mgr = new ModernFormManager { TargetForm = noneForm };
                    noneForm.BackColor = Color.Teal;
                    noneForm.Show();
                    Pump(200);
                    bool noneKeepsTeal = noneForm.BackColor.ToArgb() == Color.Teal.ToArgb();
                    Console.WriteLine($"[assert] {(noneKeepsTeal ? "OK" : "FALLO")} fondo diseñador None: BackColor={noneForm.BackColor.Name} (esperado Teal)");
                    if (!noneKeepsTeal) failures++;
                    noneForm.Close();
                }

                using (var blurForm = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(80, 560),
                    Size = new Size(360, 220),
                    ShowInTaskbar = false
                })
                {
                    using var mgr = new ModernFormManager { TargetForm = blurForm };
                    mgr.BackdropType = FormBackdropType.Blur;
                    blurForm.BackColor = Color.Teal;
                    blurForm.Show();
                    Pump(500);
                    bool blurBlack = blurForm.BackColor.ToArgb() == Color.Black.ToArgb();
                    mgr.BackdropType = FormBackdropType.None;
                    Pump(250);
                    bool restoredTeal = blurForm.BackColor.ToArgb() == Color.Teal.ToArgb();
                    bool ok = blurBlack && restoredTeal;
                    Console.WriteLine($"[assert] {(ok ? "OK" : "FALLO")} fondo diseñador Blur→None: Blur={blurBlack}, None={restoredTeal} ({blurForm.BackColor.Name})");
                    if (!ok) failures++;
                    blurForm.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO fondo diseñador: {ex.Message}");
                failures++;
            }
            return failures;
        }

        /// <summary>
        /// Overlay + backdrop Blur/Acrylic con un ModernFormManager propio
        /// (sin depender de Form1). AutoSetBlackBackground se activa solo.
        /// </summary>
        private static int RunOverlayBackdropCheck()
        {
            using var envScope = new DiagnosticEnvScope();
            int failures = 0;
            ModernGlowOverlay.Enabled = true;
            try
            {
                foreach (string kind in new[] { "Blur", "Acrylic" })
                {
                    try
                    {
                        using var form = new Form
                        {
                            FormBorderStyle = FormBorderStyle.None,
                            StartPosition = FormStartPosition.Manual,
                            Location = new Point(80, 80),
                            // Tamaños que no colisionan con ningún otro test.
                            Size = kind == "Blur" ? new Size(430, 270) : new Size(470, 330),
                            BackColor = Color.Teal
                        };
                        using var mgr = new ModernFormManager { TargetForm = form };
                        mgr.BackdropType = (FormBackdropType)Enum.Parse(
                            typeof(FormBackdropType), kind);
                        FluentElement.Design($"backdrop-{kind}").Layout(100, 80, 160, 48)
                            .Glow("#ff00ff", 14).Apply(form);
                        ResetOverlayStats();
                        form.Show();
                        bool ready = PumpUntil(() =>
                            ModernGlowOverlay.IsAttached(form) &&
                            ModernGlowOverlay.LastUpdateSucceeded &&
                            ModernGlowOverlay.LastOverlayBounds.Width == form.ClientSize.Width &&
                            ModernGlowOverlay.LastOverlayBounds.Height == form.ClientSize.Height &&
                            ModernGlowOverlay.LastOverlayScreen == form.PointToScreen(Point.Empty), 2000);
                        if (kind == "Blur") Pump(500); // el worker del backdrop es async

                        bool attached = ModernGlowOverlay.IsAttached(form);
                        bool layeredOk = ModernGlowOverlay.LastUpdateSucceeded;
                        bool boundsOk = ModernGlowOverlay.LastOverlayBounds.Width == form.ClientSize.Width &&
                                        ModernGlowOverlay.LastOverlayBounds.Height == form.ClientSize.Height;
                        bool screenOk = ModernGlowOverlay.LastOverlayScreen == form.PointToScreen(Point.Empty);
                        bool autoBlack = mgr.AutoSetBlackBackground;
                        bool backdrop = form.BackgroundImage != null;

                        // Positivo en Win11 22H2+: DWM backdrop 3 para Acrylic
                        // y 1 (none) para Blur. En builds anteriores el
                        // atributo no existe: se valida el fallback y se omite.
                        int dwmBackdrop = -1;
                        int dwmHr = -1;
                        bool dwmSupported = WindowsVersionHelper.GetBuildNumber() >= 22621;
                        if (dwmSupported)
                            dwmHr = DwmGetWindowAttribute(form.Handle, 38, out dwmBackdrop, sizeof(int));
                        bool dwmOk = !dwmSupported ||
                                     (dwmHr == 0 && (kind == "Acrylic" ? dwmBackdrop == 3 : dwmBackdrop == 1));
                        string dwmInfo = dwmSupported ? $"{dwmHr}/{dwmBackdrop}" : $"skip(build {WindowsVersionHelper.GetBuildNumber()})";

                        // Blur usa bitmap CPU; Acrylic es DWM y no debe asignar
                        // BackgroundImage (si lo hace, el test falla).
                        bool kindOk = kind == "Blur" ? backdrop : !backdrop;
                        bool ok = ready && attached && layeredOk && boundsOk && screenOk &&
                                  autoBlack && kindOk && dwmOk;
                        Console.WriteLine($"[assert] {(ok ? "OK" : "FALLO")} overlay+{kind}: listo={ready}, adjunto={attached}, UpdateLayered={layeredOk}, bounds={ModernGlowOverlay.LastOverlayBounds} cliente={form.ClientSize}, screen={ModernGlowOverlay.LastOverlayScreen} esperado={form.PointToScreen(Point.Empty)}, AutoBlack={autoBlack}, BackgroundImage={backdrop}, DWM={dwmInfo}");
                        if (!ok) failures++;

                        form.Close();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[assert] FALLO overlay+{kind}: {ex.Message}");
                        failures++;
                    }
                }
            }
            finally
            {
                ModernGlowOverlay.Enabled = false;
            }
            return failures;
        }

        /// <summary>
        /// Toggle Blur→Acrylic→Blur en vivo: al desactivar el sink, los recursos
        /// GPU del backdrop deben liberarse (Stop + SetBackdropSink(null) en
        /// ModernFormManager) y al reactivar Blur el BackgroundImage debe
        /// volver a producirse. Mide handles/Private para detectar retención.
        /// </summary>
        private static int RunBackdropToggleCheck()
        {
            using var envScope = new DiagnosticEnvScope();
            int failures = 0;
            try
            {
                using var form = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(120, 120),
                    Size = new Size(430, 270),
                    BackColor = Color.Teal
                };
                using var mgr = new ModernFormManager { TargetForm = form };
                mgr.BackdropType = (FormBackdropType)Enum.Parse(typeof(FormBackdropType), "Blur");
                form.Show();
                Pump(1500); // el worker crea el device D3D11 + superficies y emite

                int h1; long p1;
                using (var proc = Process.GetCurrentProcess())
                {
                    proc.Refresh();
                    h1 = proc.HandleCount;
                    p1 = proc.PrivateMemorySize64;
                }

                // Blur → Acrylic: Stop() + SetBackdropSink(null) liberan el
                // contexto GPU del backdrop (device D3D11 incluido).
                mgr.BackdropType = (FormBackdropType)Enum.Parse(typeof(FormBackdropType), "Acrylic");
                Pump(900);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Pump(200);

                int h2; long p2;
                using (var proc = Process.GetCurrentProcess())
                {
                    proc.Refresh();
                    h2 = proc.HandleCount;
                    p2 = proc.PrivateMemorySize64;
                }

                // Acrylic → Blur: el backdrop vuelve a producirse.
                mgr.BackdropType = (FormBackdropType)Enum.Parse(typeof(FormBackdropType), "Blur");
                Pump(1200);
                bool backdropBack = form.BackgroundImage != null;

                // La liberación se ve sobre todo en handles (el device D3D11
                // abría ~60); Private puede no bajar por cachés del driver.
                bool released = h2 <= h1 + 4 && p2 <= p1 + 2L * 1024 * 1024;
                bool ok = released && backdropBack;
                Console.WriteLine($"[assert] {(ok ? "OK" : "FALLO")} backdrop toggle Blur→Acrylic→Blur: handles {h1}->{h2}, Private {p1 / 1048576}->{p2 / 1048576} MB, BackgroundImage de vuelta={backdropBack}");
                if (!ok) failures++;

                form.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO backdrop toggle: {ex.Message}");
                failures++;
            }
            return failures;
        }

        /// <summary>
        /// Throttle de arrastre del backdrop: con Blur activo, WM_ENTERSIZEMOVE
        /// baja BackdropIntervalMs a 33 (~30 fps) y WM_EXITSIZEMOVE lo restaura
        /// al refresco del monitor tras el timer de 350 ms.
        /// </summary>
        private static int RunBackdropThrottleCheck()
        {
            using var envScope = new DiagnosticEnvScope();
            int failures = 0;
            try
            {
                using var form = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(40, 40),
                    Size = new Size(430, 270),
                    BackColor = Color.Teal
                };
                using var mgr = new ModernFormManager { TargetForm = form };
                mgr.BackdropType = (FormBackdropType)Enum.Parse(typeof(FormBackdropType), "Blur");
                form.Show();
                Pump(1200);

                var helperField = typeof(ModernFormManager).GetField("_d2dBlurHelper",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var helper = helperField?.GetValue(mgr) as Direct2DBlurHelper;
                if (helper == null)
                {
                    Console.WriteLine("[assert] FALLO backdrop throttle: helper no accesible");
                    form.Close();
                    return 1;
                }

                int normal = helper.BackdropIntervalMs;
                SendMessage(form.Handle, 0x0231, IntPtr.Zero, IntPtr.Zero); // WM_ENTERSIZEMOVE
                int throttled = helper.BackdropIntervalMs;
                var inLoopField = typeof(ModernFormManager).GetField("_inMoveLoop",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                bool inLoopDuring = inLoopField != null && (bool)(inLoopField.GetValue(mgr) ?? false);
                SendMessage(form.Handle, 0x0232, IntPtr.Zero, IntPtr.Zero); // WM_EXITSIZEMOVE
                Pump(700); // timer de 350 ms + margen
                int restored = helper.BackdropIntervalMs;
                bool inLoopAfter = inLoopField != null && (bool)(inLoopField.GetValue(mgr) ?? false);

                // Sin exigir normal != 33: a 30 Hz normal ES 33 y el throttle
                // es un no-op válido (ENTER=33, EXIT=33).
                bool ok = throttled == 33 && restored == normal && inLoopDuring && !inLoopAfter;

                // Fallback watchdog: WM_MOVING sin ENTER/EXIT activa el throttle
                // (33 ms) y el watchdog lo restaura a los 350 ms sin actividad,
                // limpiando _inMoveLoop.
                SendMessage(form.Handle, 0x0216, IntPtr.Zero, IntPtr.Zero); // WM_MOVING
                Pump(60);
                SendMessage(form.Handle, 0x0216, IntPtr.Zero, IntPtr.Zero);
                Pump(60);
                int wdThrottled = helper.BackdropIntervalMs;
                Pump(700);
                int wdRestored = helper.BackdropIntervalMs;
                bool wdInLoop = inLoopField != null && (bool)(inLoopField.GetValue(mgr) ?? false);
                bool fallbackOk = wdThrottled == 33 && wdRestored == normal && !wdInLoop;
                ok = ok && fallbackOk;

                Console.WriteLine($"[assert] {(ok ? "OK" : "FALLO")} backdrop throttle arrastre: normal={normal}ms -> ENTER={throttled}ms (inLoop={inLoopDuring}) -> EXIT+700ms={restored}ms (inLoop={inLoopAfter}); fallback WM_MOVING: {wdThrottled}ms -> +700ms={wdRestored}ms (inLoop={wdInLoop})");
                if (!ok) failures++;

                form.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO backdrop throttle: {ex.Message}");
                failures++;
            }
            return failures;
        }

        /// <summary>
        /// Captura de pantalla opcional (FWF_OVERLAY_PIXEL=1) para validar que
        /// el overlay layered está en la posición correcta y el halo se ve
        /// fuera del HWND. No corre en la suite normal (depende del escritorio).
        /// </summary>
        private static int RunOverlayPixelCheck()
        {
            int failures = 0;
            ModernGlowOverlay.Enabled = true;
            ModernGlowOverlay.EnableRenderDiagnostics = true;
            try
            {
                using var form = new Form
                {
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(100, 100),
                    ClientSize = new Size(420, 280),
                    BackColor = Color.Black,
                    TopMost = true,
                    ShowInTaskbar = false
                };
                var b = FluentElement.Design("overlay-pixel").Layout(160, 120, 140, 44)
                    .Glow("#ff00ff", 30).Apply(form);
                form.Show();
                Pump(700);

                if (GetForegroundWindow() != form.Handle)
                {
                    _skips++;
                    Console.WriteLine("[assert] SKIP overlay pixel: la ventana de prueba no está al frente");
                    form.Close();
                    form.Dispose();
                    return 0;
                }

                using var shot = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
                using (var g = Graphics.FromImage(shot))
                    g.CopyFromScreen(form.PointToScreen(Point.Empty), Point.Empty, form.ClientSize);

                Rectangle lb = b.LogicalBounds;
                int px = lb.Left - 20, py = lb.Top + lb.Height / 2;
                Color halo = shot.GetPixel(px, py);
                Color far = shot.GetPixel(Math.Max(0, lb.Left - 55), py);
                // Antiguo "anillo de 1px": el pixel pegado al borde del botón
                // debe tener glow (no fondo). Con el margen del HWND volvía teal.
                Color edge = shot.GetPixel(lb.Left - 2, py);
                bool haloOk = (halo.R > 10 || halo.B > 10) && halo.G < 80;
                bool farOk = far.R < 10 && far.G < 10 && far.B < 10;
                // El glow magenta tiene G bajo; el fondo teal (G=128) NO debe
                // pasar como glow (regresión del anillo de 1px).
                bool edgeOk = (edge.R > 10 || edge.B > 10) && edge.G < 80;

                // Solo se omite si la ventana no está al frente; con la ventana
                // al frente, un fondo no negro es un FALLO (overlay opaco).
                Console.WriteLine($"[assert] {(haloOk && farOk && edgeOk ? "OK" : "FALLO")} overlay pixel: halo({px},{py})={halo.R},{halo.G},{halo.B} (visible={haloOk}); borde={edge.R},{edge.G},{edge.B} (glow={edgeOk}); lejos={far.R},{far.G},{far.B} (fondo={farOk}); dibujados={ModernGlowOverlay.LastGlowControls} alphaPx={ModernGlowOverlay.LastPaintedPixels} bounds={ModernGlowOverlay.LastOverlayBounds} screen={ModernGlowOverlay.LastOverlayScreen}");
                if (!(haloOk && farOk && edgeOk)) failures++;

                // Control transparente: el cuerpo no debe quedar con silueta
                // negra (DropShadowOnly deja transparente el interior).
                using (var ghostForm = new Form
                {
                    BackColor = Color.Teal,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(100, 100),
                    ClientSize = new Size(420, 280),
                    TopMost = true,
                    ShowInTaskbar = false
                })
                {
                    var ghost = FluentElement.Design("ghost-pixel").Layout(160, 120, 140, 44)
                        .Background("#00000000").Glow("#ff00ff", 30).Apply(ghostForm);
                    ghostForm.Show();
                    Pump(700);
                    if (GetForegroundWindow() != ghostForm.Handle)
                    {
                        _skips++;
                        Console.WriteLine("[assert] SKIP overlay pixel ghost: no al frente");
                    }
                    else
                    {
                        using var ghostShot = new Bitmap(ghostForm.ClientSize.Width, ghostForm.ClientSize.Height);
                        using (var g2 = Graphics.FromImage(ghostShot))
                            g2.CopyFromScreen(ghostForm.PointToScreen(Point.Empty), Point.Empty, ghostForm.ClientSize);
                        Rectangle glb = ghost.LogicalBounds;
                        Color bodyPx = ghostShot.GetPixel(glb.Left + 10, glb.Top + glb.Height / 2);
                        bool ghostOk = bodyPx.G > 100 && bodyPx.B > 100 && bodyPx.R < 60;
                        Console.WriteLine($"[assert] {(ghostOk ? "OK" : "FALLO")} overlay pixel ghost: cuerpo={bodyPx.R},{bodyPx.G},{bodyPx.B} (esperado teal, no negro)");
                        if (!ghostOk) failures++;
                    }
                    ghostForm.Close();
                }

                form.Close();
                form.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO overlay pixel: {ex.Message}");
                failures++;
            }
            finally
            {
                ModernGlowOverlay.Enabled = false;
                ModernGlowOverlay.EnableRenderDiagnostics = false;
            }
            return failures;
        }

        /// <summary>
        /// Verifica el helper de DPI: el Layout de los nodos escala por el
        /// factor real del monitor (160x48 → 128x38.4 en 125%→100%).
        /// El evento real WM_DPICHANGED lo valida el usuario.
        /// </summary>
        private static int RunDpiLayoutCheck()
        {
            int failures = 0;
            try
            {
                using var form = new Form { Size = new Size(520, 360) };
                var c = FluentElement.Design("dpi-scale").Layout(120, 100, 160, 48).Apply(form);
                form.Show();
                Pump(250);

                var t = typeof(ModernControlBase);
                var nodeField = t.GetField("_visualNode", BindingFlags.Instance | BindingFlags.NonPublic);
                var scaleMethod = t.GetMethod("ScaleNodeLayout", BindingFlags.Static | BindingFlags.NonPublic);
                var node = nodeField?.GetValue(c) as RenderNode;
                if (node == null || scaleMethod == null)
                {
                    Console.WriteLine("[assert] FALLO dpi layout: nodo/método no disponible");
                    failures++;
                }
                else
                {
                    scaleMethod.Invoke(null, new object[] { node, 0.8f });
                    bool shrunk = Math.Abs(node.Layout.Width - 128f) <= 0.2f &&
                                  Math.Abs(node.Layout.Height - 38.4f) <= 0.2f;
                    scaleMethod.Invoke(null, new object[] { node, 1.25f });
                    bool restored = Math.Abs(node.Layout.Width - 160f) <= 0.2f &&
                                    Math.Abs(node.Layout.Height - 48f) <= 0.2f;
                    Console.WriteLine($"[assert] {(shrunk && restored ? "OK" : "FALLO")} dpi layout: 160x48 -> 128x38.4 -> 160x48 (ok={shrunk && restored})");
                    if (!(shrunk && restored)) failures++;
                }

                form.Close();
                form.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO dpi layout: {ex.Message}");
                failures++;
            }
            return failures;
        }

        private static void RunMassScenario()
        {
            using var form = new Form { Text = "mass", Size = new Size(900, 600) };
            var panel = new Panel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);

            var controls = new List<ModernControlBase>(200);
            for (int i = 0; i < 200; i++)
            {
                var c = new FluentElement
                {
                    Location = new Point((i % 20) * 42, (i / 20) * 42),
                    Size = new Size(38, 38)
                };
                panel.Controls.Add(c);
                controls.Add(c);
            }

            form.Show();
            Application.DoEvents();

            for (int i = 0; i < controls.Count; i += 25)
                OnMouseEnter?.Invoke(controls[i], new object[] { EventArgs.Empty });

            Application.DoEvents();
            form.Close();
            form.Dispose();
        }

        // ==================================================================
        // Utilidades
        // ==================================================================
        private static object? GetManager(Form1 form)
            => typeof(Form1).GetField("modernFormManager1", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(form);

        private static object? Read(object target, string property)
            => target.GetType().GetProperty(property)?.GetValue(target);

        /// <summary>
        /// Comprueba que el backdrop del blur se actualiza en vivo: activa Blur,
        /// espera, mueve la ventana y verifica que el BackgroundImage cambió.
        /// </summary>
        private static int RunLiveBlurCheck()
        {
            using var envScope = new DiagnosticEnvScope();
            try
            {
                using var form = new Form1();
                object? mgr = GetManager(form);
                if (mgr == null)
                {
                    Console.WriteLine("[assert] FALLO blur live: no se encontró el manager");
                    return 1;
                }

                var backdrop = mgr.GetType().GetProperty("BackdropType");
                if (backdrop != null)
                    backdrop.SetValue(mgr, Enum.Parse(backdrop.PropertyType, "Blur"));
                mgr.GetType().GetProperty("AutoSetBlackBackground")?.SetValue(mgr, true);

                form.Show();
                Pump(900);
                form.Activate();
                form.BringToFront();
                Pump(200);

                var img1 = form.BackgroundImage;
                if (img1 == null)
                {
                    Console.WriteLine("[assert] FALLO blur live: sin BackgroundImage con Blur");
                    return 1;
                }
                long h1 = ImageHash(img1);

                // Sonda de fondo: garantiza que al mover el form la captura
                // cambie aunque el escritorio esté estático (el check de hash
                // era flaky en máquinas sin fondo cambiante).
                using var backgroundProbe = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    ShowInTaskbar = false,
                    BackColor = Color.FromArgb(35, 120, 200),
                    Bounds = new Rectangle(form.Left + 250, form.Top + 60, form.Width + 80, form.Height + 80)
                };
                backgroundProbe.Show();
                backgroundProbe.SendToBack();
                form.BringToFront();
                Pump(300);

                form.Location = new Point(form.Left + 250, form.Top + 60);
                Pump(900);

                var img2 = form.BackgroundImage;
                if (img2 == null)
                {
                    Console.WriteLine("[assert] FALLO blur live: BackgroundImage nulo tras mover");
                    return 1;
                }
                long h2 = ImageHash(img2);
                // Reintento por si el worker tarda en recalcular con la máquina
                // cargada; si el form no está al frente, es SKIP (no pase).
                for (int retry = 0; retry < 3 && h1 == h2; retry++)
                {
                    Pump(600);
                    var retryImg = form.BackgroundImage;
                    if (retryImg != null) h2 = ImageHash(retryImg);
                }

                bool liveMove = h1 != h2;
                if (!liveMove && GetForegroundWindow() != form.Handle)
                {
                    _skips++;
                    liveMove = true;
                    Console.WriteLine("[assert] SKIP blur live (mover): el form no está al frente");
                }
                else
                {
                    Console.WriteLine($"[assert] {(liveMove ? "OK" : "FALLO")} blur live (mover): hash {h1:X8} -> {h2:X8}");
                }

                // Segunda prueba: ventana QUIETA y fondo cambiando. Es lo que
                // distingue "live" de una foto recapturada solo al mover.
                bool liveStatic = false;
                bool skipped = false;
                using (var cover = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    ShowInTaskbar = false,
                    // Color neutro a propósito: este form es una SONDRA del test
                    // (tapa la ventana 1.2 s para verificar que el backdrop se
                    // actualiza con el fondo). Antes era Magenta y parecía un
                    // bug visual en las corridas del rompedor.
                    BackColor = Color.FromArgb(24, 24, 32),
                    Bounds = form.Bounds,
                    TopMost = true
                })
                {
                    cover.Show();
                    cover.BringToFront();
                    cover.Activate();
                    Pump(1200);

                    if (GetForegroundWindow() != cover.Handle)
                    {
                        // Sesión bloqueada/otra app al frente: la sonda no se
                        // captura; se marca SKIP, no se cuenta como validado.
                        skipped = true;
                        _skips++;
                        Console.WriteLine("[assert] SKIP blur live (quieto): la cobertura no está al frente");
                    }
                    else
                    {
                        var img3 = form.BackgroundImage;
                        if (img3 == null)
                        {
                            Console.WriteLine("[assert] FALLO blur live (quieto): BackgroundImage nulo");
                        }
                        else
                        {
                            long h3 = ImageHash(img3);
                            liveStatic = h3 != h2;
                            Console.WriteLine($"[assert] {(liveStatic ? "OK" : "FALLO")} blur live (quieto, fondo cambia): hash {h2:X8} -> {h3:X8}");
                        }
                    }
                    cover.Close();
                    Pump(300);
                }

                if (skipped) return liveMove ? 0 : 1;
                return liveMove && liveStatic ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[assert] FALLO blur live: {ex}");
                return 1;
            }
        }

        private static void Pump(int ms)
        {
            long until = Environment.TickCount64 + ms;
            while (Environment.TickCount64 < until)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(15);
            }
        }

        /// <summary>Procesa mensajes hasta que la condición se cumpla o venza.</summary>
        private static bool PumpUntil(Func<bool> condition, int timeoutMs)
        {
            long until = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < until)
            {
                if (condition()) return true;
                Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }
            return condition();
        }

        /// <summary>
        /// Desactiva temporalmente el overlay para tests de geometría/hit-test
        /// que miden la expansión del HWND sin la capa layered.
        /// </summary>
        private sealed class OverlayDisabledScope : IDisposable
        {
            private readonly bool _wasEnabled;

            public OverlayDisabledScope()
            {
                _wasEnabled = ModernGlowOverlay.Enabled;
                ModernGlowOverlay.Enabled = false;
            }

            public void Dispose() => ModernGlowOverlay.Enabled = _wasEnabled;
        }

        /// <summary>
        /// Limpia temporalmente las variables de diagnóstico del manager para
        /// que un test fije su propio BackdropType sin que FWF_BACKDROP lo pise.
        /// </summary>
        private sealed class DiagnosticEnvScope : IDisposable
        {
            private static readonly string[] Keys =
            {
                "FWF_BACKDROP", "FWF_BORDER_THICKNESS",
                "FWF_FAKE_DPI", "FWF_BLUR_AMOUNT"
            };

            private readonly (string Key, string? Value)[] _saved;

            public DiagnosticEnvScope()
            {
                _saved = new (string, string?)[Keys.Length];
                for (int i = 0; i < Keys.Length; i++)
                {
                    _saved[i] = (Keys[i], Environment.GetEnvironmentVariable(Keys[i]));
                    Environment.SetEnvironmentVariable(Keys[i], null);
                }
            }

            public void Dispose()
            {
                foreach (var (key, value) in _saved)
                    Environment.SetEnvironmentVariable(key, value);
            }
        }

        /// <summary>
        /// Limpia las estadísticas estáticas del overlay para que un test no
        /// pueda pasar con el render de un form anterior.
        /// </summary>
        private static void ResetOverlayStats(Control? parent = null)
        {
            var type = typeof(ModernGlowOverlay);
            var stats = type.GetMethod("SetRenderStats",
                BindingFlags.Static | BindingFlags.NonPublic);
            var update = type.GetMethod("SetUpdateResult",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (stats == null || update == null)
                throw new InvalidOperationException("No se pudo resetear el estado del overlay.");

            bool freeze = parent != null &&
                          Environment.GetEnvironmentVariable("FWF_FREEZE_OVERLAY") == "1";
            if (freeze)
            {
                Control? overlay = null;
                for (int i = parent!.Controls.Count - 1; i >= 0; i--)
                {
                    if (parent.Controls[i].GetType().Name == "ModernSkiaGlowOverlay")
                    {
                        overlay = parent.Controls[i];
                        break;
                    }
                }
                if (overlay == null)
                    throw new InvalidOperationException(
                        "FWF_FREEZE_OVERLAY: no se encontró el overlay en el padre.");
                var flagField = overlay.GetType().GetField("_renderQueuedFlag",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (flagField == null)
                    throw new InvalidOperationException(
                        "FWF_FREEZE_OVERLAY: no se encontró _renderQueuedFlag.");

                // Primero drenar el BeginInvoke pendiente; si no, el callback
                // corre durante PumpUntil, resetea el flag y pinta igual.
                flagField.SetValue(overlay, 1);
                Pump(60);
                flagField.SetValue(overlay, 1);
            }

            stats.Invoke(null, new object[] { 0, 0L, Rectangle.Empty, Point.Empty });
            update.Invoke(null, new object[] { false, 0 });
            if (freeze) return;

            if (parent != null)
            {
                // Sin un render nuevo, el reset dejaría bounds vacíos para siempre.
                type.GetMethod("Request", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.Invoke(null, new object[] { parent });
            }
        }

        /// <summary>
        /// ¿El delegate está suscrito al HandleCreated real del padre? Lee el
        /// EventHandlerList privado; si no puede, devuelve false (el test falla).
        /// </summary>
        private static bool OverlayHookSubscribed(Control parent, Delegate? handler)
        {
            if (handler == null) return false;
            try
            {
                var keyField = typeof(Control).GetField("s_handleCreatedEvent",
                    BindingFlags.Static | BindingFlags.NonPublic);
                var key = keyField?.GetValue(null);
                var eventsProp = typeof(System.ComponentModel.Component).GetProperty("Events",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                object? events = eventsProp?.GetValue(parent);
                var itemProp = events?.GetType().GetProperty("Item",
                    BindingFlags.Instance | BindingFlags.Public);
                object? current = itemProp?.GetValue(events, new[] { key! });
                return current is Delegate currentDelegate &&
                       currentDelegate.GetInvocationList().Contains(handler);
            }
            catch
            {
                return false;
            }
        }

        private static long ImageHash(Image image)
        {
            using var bmp = new Bitmap(image);
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int total = data.Stride * bmp.Height;
                int step = Math.Max(4, total / 512) & ~3;
                long hash = 17;
                for (int i = 0; i < total; i += step)
                    hash = hash * 31 + Marshal.ReadByte(data.Scan0, i);
                return hash;
            }
            finally { bmp.UnlockBits(data); }
        }

        private static void Set(object target, string property, object value)
        {
            var prop = target.GetType().GetProperty(property);
            if (prop == null || !prop.CanWrite)
                throw new InvalidOperationException($"La propiedad '{property}' no existe o no es escribible.");

            try
            {
                prop.SetValue(target, value);
            }
            catch (Exception ex)
            {
                // No se oculta: el valor extremo no se aplicó y el stack importa.
                throw new InvalidOperationException($"'{property}' lanzó al asignar {value}: {ex}", ex);
            }
        }

        private static bool ReadBool(object target, string field)
        {
            for (Type? t = target.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f?.GetValue(target) is bool b) return b;
            }
            return false;
        }

        private static IntPtr Lp(int x, int y) => (IntPtr)((y << 16) | (x & 0xFFFF));

        private static int AssertGrowth(string name, long baseline, long final, double factor)
        {
            if (baseline <= 0 || final <= 0)
            {
                Console.WriteLine($"[assert] FALLO {name}: medición inválida (baseline={baseline}, final={final})");
                return 1;
            }
            double ratio = final / (double)baseline;
            if (ratio > factor)
            {
                Console.WriteLine($"[assert] FALLO {name}: {baseline} -> {final} ({ratio:0.00}x, límite {factor:0.00}x)");
                return 1;
            }
            Console.WriteLine($"[assert] OK {name}: {baseline} -> {final} ({ratio:0.00}x)");
            return 0;
        }

        private static double TrendPerCycle(Func<(int Cycle, long Ws, long Private, long Managed, int Gdi, int User, int Threads, int Handles), long> selector, int cycles)
        {
            // Se excluye el último ciclo (puede incluir DPI/extremos del cierre)
            // y la muestra previa a crear ventanas.
            var tail = Samples
                .Where(s => s.Cycle > 0 && s.Cycle < cycles)
                .Skip(Math.Max(0, Samples.Count(s => s.Cycle > 0 && s.Cycle < cycles) - 6))
                .ToList();
            if (tail.Count < 2) return 0;

            double mx = tail.Average(s => (double)s.Cycle);
            double my = tail.Average(s => (double)selector(s));
            double num = 0, den = 0;
            foreach (var s in tail)
            {
                double dx = s.Cycle - mx;
                num += dx * (selector(s) - my);
                den += dx * dx;
            }
            return den == 0 ? 0 : num / den;
        }

        private static (int Cycle, long Ws, long Private, long Managed, int Gdi, int User, int Threads, int Handles) Snapshot(string tag, int cycle)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            using var p = Process.GetCurrentProcess();
            p.Refresh();
            long ws = p.WorkingSet64 / 1024 / 1024;
            long priv = p.PrivateMemorySize64 / 1024 / 1024;
            long managed = GC.GetTotalMemory(true) / 1024 / 1024;
            int gdi = GetGuiResources(p.Handle, 0);
            int user = GetGuiResources(p.Handle, 1);
            int threads = p.Threads.Count;
            int handles = p.HandleCount;

            Console.WriteLine(
                $"[mem] {tag,-22} WS={ws,4} MB  Private={priv,4} MB  Managed={managed,3} MB  " +
                $"GDI={gdi,4}  USER={user,4}  Threads={threads,2}  Handles={handles,4}");

            Samples.Add((cycle, ws, priv, managed, gdi, user, threads, handles));
            return (cycle, ws, priv, managed, gdi, user, threads, handles);
        }
        private sealed class TestModernControl : ModernControlBase { }
    }
}
