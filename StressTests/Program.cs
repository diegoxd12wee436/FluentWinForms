#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FluentWinForms.Core;
using FluentWinForms.Custom_Controls;

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
                warm = Snapshot("inicio", 0);
                // Calentamiento: un ciclo completo (JIT + caches) y se descarta.
                RunCycle(0, extremes: true);
                Application.DoEvents();

                // El resize se prueba UNA vez. Después se asienta el proceso con
                // un par de ciclos vacíos + GC (sin EmptyWorkingSet: invalida WS
                // como métrica de fuga).
                RunResizeStress();
                warmFailures += RunSiblingOverlapCheck();
                warmFailures += RunGcCostCheck();
                warmFailures += RunPathCacheCheck();
                warmFailures += RunRippleFadeCheck();
                warmFailures += RunHitTestCheck();
                warmFailures += RunOverlayCheck();
                if (Environment.GetEnvironmentVariable("FWF_OVERLAY_PIXEL") == "1")
                    warmFailures += RunOverlayPixelCheck();
                warmFailures += RunDpiLayoutCheck();
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
            Console.WriteLine($"[stress] duración={sw.Elapsed.TotalSeconds:0.0}s fallos={failures}");
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
            form.Show();
            Application.DoEvents();

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
            using var form = new Form1();
            form.Show();
            Pump(900); // el backdrop se asigna async desde el worker

            object? manager = GetManager(form);
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
                bool durationOk = Math.Abs(b.RippleDuration - 900f) < 0.1f;
                Console.WriteLine($"[assert] {(durationOk ? "OK" : "FALLO")} ripple duration={b.RippleDuration:0}ms (900)");
                if (!durationOk) failures++;

                OnMouseDown?.Invoke(b, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 40, 24, 0) });
                Pump(450);

                var t = typeof(ModernControlBase);
                var nodeField = t.GetField("_rippleNode", BindingFlags.Instance | BindingFlags.NonPublic);
                var ripField = t.GetField("_isRippling", BindingFlags.Instance | BindingFlags.NonPublic);
                var radiusField = t.GetField("_rippleRadius", BindingFlags.Instance | BindingFlags.NonPublic);
                var maxField = t.GetField("_maxRippleRadius", BindingFlags.Instance | BindingFlags.NonPublic);
                float radius = radiusField?.GetValue(b) is float r ? r : -1f;
                float max = maxField?.GetValue(b) is float m && m > 0f
                    ? m : (float)Math.Sqrt(b.Width * b.Width + b.Height * b.Height);
                float ratio = max > 0f ? radius / max : 0f;
                bool timingOk = ratio >= 0.3f && ratio <= 0.8f;
                Console.WriteLine($"[assert] {(timingOk ? "OK" : "FALLO")} ripple timing: radio/max={ratio:0.00} a 450ms de 900 (0.30..0.80)");
                if (!timingOk) failures++;

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
                bool contentOnly = shadowExpansion <= 3;
                Console.WriteLine($"[assert] {(attached && behind && contentOnly && layeredOk && positionOk ? "OK" : "FALLO")} overlay Skia: adjunto={attached}, z-order={behind}, UpdateLayered={layeredOk} (err={ModernGlowOverlay.LastUpdateError}), posición={positionOk} {ModernGlowOverlay.LastOverlayScreen}, expansión superior={shadowExpansion}px (<=3)");
                if (!attached || !behind || !contentOnly || !layeredOk || !positionOk) failures++;

                // Un control añadido después también debe dejar el overlay al fondo.
                var b3 = FluentElement.Design("overlay3").Layout(300, 100, 140, 48)
                    .Glow("#00aaff", 14).Apply(form);
                Pump(200);
                bool behindAfter = ModernGlowOverlay.IsBehindControls(form);
                Console.WriteLine($"[assert] {(behindAfter ? "OK" : "FALLO")} overlay z-order tras alta: {behindAfter}");
                if (!behindAfter) failures++;

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

                using var shot = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
                using (var g = Graphics.FromImage(shot))
                    g.CopyFromScreen(form.PointToScreen(Point.Empty), Point.Empty, form.ClientSize);

                Rectangle lb = b.LogicalBounds;
                int px = lb.Left - 20, py = lb.Top + lb.Height / 2;
                Color halo = shot.GetPixel(px, py);
                Color far = shot.GetPixel(Math.Max(0, lb.Left - 55), py);
                bool haloOk = (halo.R > 10 || halo.B > 10) && halo.G < 80;
                bool farOk = far.R < 10 && far.G < 10 && far.B < 10;

                Console.WriteLine($"[assert] {(haloOk && farOk ? "OK" : "FALLO")} overlay pixel: halo({px},{py})={halo.R},{halo.G},{halo.B} (visible={haloOk}); lejos={far.R},{far.G},{far.B} (fondo={farOk}); dibujados={ModernGlowOverlay.LastGlowControls} alphaPx={ModernGlowOverlay.LastPaintedPixels} bounds={ModernGlowOverlay.LastOverlayBounds} screen={ModernGlowOverlay.LastOverlayScreen}");
                if (!(haloOk && farOk)) failures++;

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

                bool liveMove = h1 != h2;
                Console.WriteLine($"[assert] {(liveMove ? "OK" : "FALLO")} blur live (mover): hash {h1:X8} -> {h2:X8}");

                // Segunda prueba: ventana QUIETA y fondo cambiando. Es lo que
                // distingue "live" de una foto recapturada solo al mover.
                bool liveStatic = false;
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
                    cover.Close();
                    Pump(300);
                }

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
    }
}
