# FluentWinForms — Auditoría técnica y plan de estabilización

> **Documento de continuidad para agentes IA y colaboradores.**
> Léelo completo antes de tocar código. Si algo de aquí contradice al código actual, **gana el código**: este documento describe intenciones y evidencia, no reemplaza la lectura del repositorio.

| Metadato | Valor |
|---|---|
| Última verificación | **2026-11-19** |
| Commit verificado | `5dc14cb` (docs) — código en `feature/estabilizacion` |
| Rama | `main` (referencia) · trabajo en `feature/estabilizacion` |
| Estado del build | **Compila: 0 errores, 0 warnings** |
| Estado de los bugs A/B/C | A: **VERIFIED** por el usuario. B: corregido. C: límite de arquitectura aceptado (CPU 15–17 fps; Acrylic sigue disponible). |

> **Estado actual:** ver `NOTES.md` → "ESTADO ACTUAL (2026-11-19)". Tareas 2/4/5 aprobadas
> (sin commitear); glow CSS en capa GDI+ **revertido**; decisión **Skia-only**.

> **Avance del día a día:** ver `PROGRESS.md` (qué se implementó, evidencia y qué falta probar).

---

## 0. Cómo usar este documento

1. Lee las **reglas no negociables** (§2). No son sugerencias.
2. Verifica el **estado del repo** (§3) antes de asumir que sigue igual.
3. Revisa la **arquitectura** (§4) y los **hallazgos** (§5) con evidencia `archivo:línea`.
4. Trabaja por **fases** (§7), no por ocurrencias.
5. Reporta cada cambio con el **formato obligatorio** (§11).

### Leyenda de estados

| Estado | Significado |
|---|---|
| **CONFIRMADO (código)** | La ruta existe y se leyó en el checkout actual. No implica síntoma visible. |
| **REPRODUCIDO** | Se observó el fallo en runtime con pasos documentados. |
| **HIPÓTESIS** | Explicación plausible sin reproducción todavía. No presentar como hecho. |
| **LIMITACIÓN** | Comportamiento inherente al diseño actual, no un bug puntual. |
| **PENDIENTE** | Falta información del entorno del usuario. |

> Los números de línea corresponden a `ae1f469`. Si no coinciden al abrir el archivo, **busca por símbolo** (`UpdateFormBackground`, `RefreshLayer`, etc.), no confíes en el número.

---

## 1. Próximo paso (TL;DR)

**A está `VERIFIED` por el usuario.** B (overlay 25% más grande que el form) y C (8 repintados
del form por frame) están corregidos y medidos; falta la prueba visual del usuario.

El cuello de botella restante son ~15 ms por frame: pintar el `BackgroundImage` y componer
8 HWND hijos. Para bajar de ahí hace falta composición en una sola superficie (Fase 6), no
micro-optimizaciones.

Orden restante: **validación visual de B y C → DPI en caliente → threads/lifecycle → refactor.**

---

## 2. Reglas no negociables

1. **Nunca hacer `commit` ni `push` sin autorización explícita del usuario.** El 2026-11-18 el usuario autorizó un commit local del fix A+B en `feature/estabilizacion`; **`main` no se toca** y no se hace push.
2. **No cambiar la API `FluentElement.Design(...)`**: ni nombres, ni firmas, ni el estilo de encadenado, ni el orden de los métodos.
3. No hacer reescrituras grandes sin **causa raíz demostrada**. Cambios pequeños, medibles, reversibles.
4. **No editar ni revertir archivos locales del usuario.** Hoy: `FluentWinForms/Form1.Designer.cs`.
5. No declarar un bug arreglado porque compila. Se exige reproducir → corregir → volver a verificar.
6. No ocultar errores con `catch` vacíos ni silenciar excepciones para "que compile".
7. No añadir dependencias innecesarias ni tocar funcionalidad ajena al problema.
8. **Proteger `ModernGlassPanel`** y los controles existentes (`FluentElement`, `ModernThemeToggle`).
9. **No borrar código muerto durante la estabilización** (`GlassBackdropCache`, `GlassPainter`, `GlassTypes`). Se limpia recién en Fase 6.
10. No implementar WinUI en esta fase. Primero estabilizar WinForms.
11. En modo Plan: leer, compilar y diagnosticar. No editar hasta que el usuario cambie explícitamente de agente/modo.
12. Separar siempre **hecho verificado / hipótesis / limitación** en cada informe.
13. Auditar **ownership, disposal, concurrencia y lifecycle** en cualquier cambio que toque recursos, bitmaps, pools o workers.
14. **Política de FPS vigente:** objetivo 120 FPS en AC cuando el hardware lo sostenga; 60 FPS en batería; adaptativo con histéresis. No forzar una tasa que el sistema no pueda sostener y no hacer busy-wait en idle.

### API pública que debe conservarse

La identidad de la librería es esta convención encadenada. Se puede **añadir** funcionalidad, pero no renombrar, reordenar ni sustituir por otra convención (`F.Button(...)`, XAML, etc.):

```csharp
var btn5 = FluentElement.Design("btn-gradient")
    .Layout(40, 312, 180, 48)
    .Gradient("#7c3aed", "#db2777", angle: 135)
    .BorderRadius(24)
    .Text("Notificaciones").Bold().FontSize(13).TextColor("#fff")
    .Shadow(3).Glow("#7c3aed", 16)
    .Hover(gradient: ("#6d28d9", "#be185d", 135), scale: 1.03, translateX: 22)
    .Press(scale: 0.95)
    .Badge("5").BadgeOffset(-4, -6)
    .Tooltip("5 notificaciones nuevas")
    .AnimateSpring(180)
    .Apply(this);

var btnEstatico = FluentElement.Design("btnEstatico")
    .Layout(50, 412, 180, 48)
    .Content("Estático", "#fff", 12)
    .Background("#333")
    .Shadow(2)
    .Apply(this);
```

El usuario final de la librería **no** debe gestionar GDI+, SkiaSharp, DPI, HWND, bitmaps ni composición.

---

## 3. Estado verificado del repositorio

### 3.1 Git

```
Rama:          feature/estabilizacion
HEAD:          ae1f469 ADD: RULES TO OPENCODE (+ commit local del fix A+B)
Working tree:  solo FluentWinForms/Form1.Designer.cs (trabajo del usuario)
```

Configuración actual del diseñador (trabajo del usuario, **no tocar ni commitear**):

```csharp
modernFormManager1.AutoSetBlackBackground = false;
modernFormManager1.BackdropType = Core.FormBackdropType.None;
modernFormManager1.BlurAmount = 1;     // HEAD tenía 20
modernFormManager1.UseSkia = false;    // HEAD tenía true
modernFormManager1.BorderThickness = 0F;
```

Para reproducir el Bug A hay que activar `BackdropType = Blur` (+ opcionalmente `UseSkia = true`
y `BorderThickness > 0` para ver el overlay). Se puede forzar la intensidad con
`FWF_BLUR_AMOUNT=<n>` sin tocar el diseñador.

Comandos permitidos para verificar: `git status --short --branch`, `git diff`, `git log --oneline`.
Comandos prohibidos: `git reset --hard`, `git clean`, `git checkout -- <archivo>`, `git restore`, cualquier operación destructiva.

### 3.2 Proyecto

| Elemento | Valor |
|---|---|
| Solución | `FluentWinForms.slnx` (un solo proyecto) |
| Proyecto | `FluentWinForms/FluentWinForms.csproj` (`WinExe`) |
| Target | `net8.0-windows10.0.19041.0` |
| Lenguaje | C# 12, `Nullable enable`, `ImplicitUsings enable` |
| SkiaSharp | 4.148.0 (+ `NativeAssets.Win32`, `Views.WindowsForms`) |
| Svg.Skia | 5.1.1 |
| Vortice | `Direct2D1` 3.8.3, `Direct3D11` 3.8.3 |
| SDK presente en la máquina | .NET 10.0.404 (compila el target net8.0) |

### 3.3 Build

```powershell
dotnet build FluentWinForms.slnx
```

Resultado esperado en `ae1f469`: **0 errores, 1 warning**.
El warning es un campo asignado y nunca leído: `_isResizing`, declarado en `Core/ModernFormManager.cs:69` (se asigna en `:544` y `:736`, nunca se consulta).

### 3.4 Tests

- **No existe proyecto de tests.**
- `GalleryForm.cs` es el único harness visual (golden images, F5 guardar / F6 comparar) y **no se lanza desde `Program.cs`** (`Program.cs` abre `Form1`).
- No hay CI configurado.

### 3.5 Punto de entrada real

`Program.cs` → `ApplicationConfiguration.Initialize()` → `Application.Run(new Form1())`.
`Form1` construye controles con `FluentElement.Design(...)` y configura `modernFormManager1` desde el designer.

---

## 4. Arquitectura real

Todo vive en un único proyecto. Carpetas: `Core/`, `Custom_Buttons/`, `Panel_Controls/`, `Resources/`.

| Subsistema | Archivos clave | Responsabilidad |
|---|---|---|
| Motor de dibujo | `ModernControlBase.Core.cs`, `.Rendering.cs` (~1000 líneas), `.Animation.cs`, `.Effects.cs`, `.States.cs`, `.Media.cs` | `SKBitmap` + `SKCanvas` por control, wrapper GDI zero-copy `Format32bppPArgb`, `DrawImageUnscaled` en `OnPaint`. |
| Árbol visual / estilos | `RenderNode.cs` | Estilos tipo CSS (gradientes, bordes, sombras, badges, layout). |
| Layout | `LayoutEngine.cs` | `Absolute`, `VerticalStack`, `HorizontalStack`, `AutoFitGrid`, min/max/stretch. |
| Fluent API | `ControlBuilder.cs`, `FluentElement.cs`, `F.cs` | Builder encadenado. **API pública intocable.** |
| Animación | `AnimationManager.cs` (~546 líneas), `SceneInvalidator` | Hilo dedicado, `timeBeginPeriod(1)`, suscriptores por `WeakReference`, modo FPS/adaptativo. |
| Form / composición | `ModernFormManager.cs` (~1168 líneas) | DWM, regiones redondeadas, borde Skia/GDI, hook de mensajes, overlay layered. |
| Overlay | `ModernFormOverlay` (dentro de `ModernFormManager.cs`, `:949`) | Ventana layered vía `UpdateLayeredWindow`. |
| Hook de mensajes | `FormMessageHook` (`ModernFormManager.cs:859`) | Intercepta `WM_WINDOWPOSCHANGED` (`0x0047`) y `WM_SIZING` (`0x0214`). |
| Blur GPU | `Direct2DBlurHelper.cs` (~329 líneas) | `BitBlt` de pantalla + `HwndRenderTarget` + `WDA_EXCLUDEFROMCAPTURE`. Hilo propio. |
| Blur CPU / fallback | `AcrylicHelper.cs`, `StackBlurUltra.cs` | `PrintWindow` + pool de bitmaps + blur por software. |
| Sombras | `GdiRenderer.cs` (~709 líneas) | Caché LRU de sombras con workers. |
| Tema | `ThemePalette.cs`, `AppTheme` | Paletas Light/Dark y sincronización con el tema de Windows. |
| Arranque | `FluentEngine.cs` | `[ModuleInitializer]` que fuerza DPI awareness y sincroniza tema. |
| Controles | `FluentElement`, `ModernGlassPanel`, `ModernThemeToggle` | Controles públicos. |
| **Código muerto** | `GlassBackdropCache.cs`, `GlassPainter.cs`, `GlassTypes.cs` | Namespace `FluentWinForms.Rendering`, sin referencias. **No borrar aún.** |

### Flujo de un frame (resumen)

1. `AnimationManager` (hilo dedicado) recorre suscriptores y llama al tick de animación.
2. El control invalida (`Invalidate`) y WinForms agenda `OnPaint` en el hilo UI.
3. `ModernControlBase.OnPaint` repinta el fondo del **padre** en su propio buffer, limpia el canvas Skia a transparente, dibuja los nodos y compone el `SKBitmap` sobre el `Graphics` del control.
4. En paralelo, si hay Blur activo, `Direct2DBlurHelper` captura la pantalla con `BitBlt`, difumina y **escribe en el HWND del formulario**.
5. `ModernFormManager`/`ModernFormOverlay` dibujan el borde redondeado en una ventana layered encima.

**Ese paso 3 y el paso 4 son dos escritores del mismo surface.** Es la raíz conceptual del Bug A.

---

## 5. Hallazgos

### 5.1 Bug A — Regiones negras en Blur/Acrylic

**Estado: `VERIFIED` (prueba visual del usuario, 2026-11-18) + CONFIRMADO (código).**
**ID:** `FWF-COMP-001` · **Severidad:** alta · **Riesgo de fix:** alto

> Fix aplicado y aprobado por el usuario: *"funciona ahora sí… el blur se ajusta al del backdrop
> y el label lo soportó"*. Detalle de la implementación y evidencia en `PROGRESS.md`.

**Síntoma:** las zonas vacías/transparentes alrededor de los elementos Fluent aparecen **negras**.

**Datos de reproducción aportados por el usuario:**

- Ocurre **solo con `FormBackdropType.Blur`**. Con `Acrylic` no se observó.
- El negro aparece **alrededor de la zona de los botones**, en áreas que deberían ser transparentes.
- La forma del área negra coincide con el **rectángulo completo del control** (contenido + margen reservado para sombra/glow), no con el formulario entero.

Esto descarta que el blur "no se dibuje": el backdrop sí se está dibujando en el formulario. Lo que falla es **qué pinta cada control hijo encima de él**.

**Cadena de causa (verificada línea por línea):**

| # | Evidencia | Efecto |
|---|---|---|
| 1 | `ModernFormManager.cs:516-520` — si `BackdropType == Blur`: `_targetForm.BackColor = Color.Black` | El formulario entero tiene fondo negro. |
| 2 | `ModernFormManager.cs:523-530` — con `Acrylic` en Win11+ y `AutoSetBlackBackground` también fuerza negro | Mismo resultado por otra rama. |
| 3 | `ModernControlBase.Rendering.cs:37-44` — cada control hace `InvokePaintBackground(Parent, ...)` con `TranslateTransform(-Left, -Top)` | Cada control pinta el fondo del padre (negro) **dentro de su propio buffer**. |
| 4 | `ModernControlBase.Rendering.cs:70` — `_skCanvas.Clear(SKColors.Transparent)` | El canvas Skia arranca transparente. |
| 5 | `ModernControlBase.Rendering.cs:141-149` — `CompositingMode.SourceOver` + `DrawImageUnscaled(_gdiWrapper, 0, 0)` | Todo lo que Skia no cubrió queda transparente y se ve el negro pintado en el paso 3. |
| 6 | `ModernControlBase.Rendering.cs:949` — `if (m.Msg == 0x0014) return;` (`WM_ERASEBKGND`) | WinForms no borra su propio fondo, pero el blur escribe el mismo HWND. |
| 7 | `Direct2DBlurHelper.cs:111` — `SetWindowDisplayAffinity(_form.Handle, WDA_EXCLUDEFROMCAPTURE)` + `BitBlt` de pantalla (`:271`) y render sobre el HWND del form | Un segundo escritor del mismo surface, fuera del ciclo de pintura de WinForms. |

**Mecanismo confirmado (código + síntoma reportado):**

El problema no es que falte el blur, sino **el modelo de transparencia de WinForms**:

1. El formulario recibe el blur de `Direct2DBlurHelper`, que dibuja directamente sobre el **HWND del form** (`Direct2DBlurHelper.cs:226-233`, `HwndRenderTarget`).
2. Cada `FluentElement` es un **control hijo con su propio HWND**, apilado encima del form. Un HWND hijo es opaco: no puede "ver" lo que D2D dibujó en el HWND padre.
3. WinForms emula la transparencia haciendo que el hijo **repinte el fondo del padre dentro de su propio rectángulo** (`ModernControlBase.Rendering.cs:37-44` → `InvokePaintBackground(Parent, ...)`).
4. Ese repintado usa `Control.OnPaintBackground` del form, es decir, **`BackColor`**, que en Blur es negro (`ModernFormManager.cs:519`). No usa la superficie D2D.
5. Por eso el rectángulo completo de cada control (incluido el margen de sombra/glow, que forma parte de sus `Bounds`) queda negro, mientras el resto del form muestra el blur. Es exactamente el "marco negro alrededor de los botones".

**Conclusión:** el blur y la transparencia de los hijos viven en dos canales distintos que nunca se conectan. Quitar el `BackColor = Black` no lo arregla: solo cambiaría el negro por otro color plano (o por basura sin pintar). El hijo **necesita una fuente de backdrop real** para dibujar en sus zonas transparentes.

**Por qué con Acrylic no se ve:** en Acrylic el backdrop lo compone DWM a nivel de ventana y el resultado visible es más oscuro/uniforme; el desajuste se disimula. No es que el mecanismo sea distinto: es que el contraste entre el halo y el fondo es menor. Confirmar visualmente antes de darlo por cerrado.

**Limitación de verificación:** `SetWindowDisplayAffinity(..., WDA_EXCLUDEFROMCAPTURE)` (`Direct2DBlurHelper.cs:111`) hace que **cualquier captura de pantalla de la ventana salga negra**. Por eso A y C no se pueden validar con screenshots ni con el harness de golden images: la validación visual la tiene que hacer el usuario, o hay que desactivar WDA temporalmente en un build de diagnóstico.

### 5.1.1 Estrategias de fix (elegir antes de tocar código)

| Opción | Cómo | Ventaja | Coste / riesgo |
|---|---|---|---|
| **1. El form pinta el blur como su fondo** | Producir un `Bitmap` difuminado y asignarlo a `form.BackgroundImage`. `InvokePaintBackground` ya propaga `BackgroundImage` a los hijos, así que los halos muestran blur **sin tocar `ModernControlBase`** | Cambio más pequeño, un solo punto de verdad, API intacta | Hay que bajar el frame a CPU (readback GPU o blur CPU). Repinta hijos en cada frame. Riesgo de rendimiento a 120 FPS. |
| **2. Los controles muestrean el backdrop** | `ModernFormManager` expone `TryGetBackdrop(Rectangle, out Bitmap)`; `OnPaint` dibuja esa región en vez de `InvokePaintBackground` | Repinta solo controles, mejor rendimiento; el resto del pipeline no cambia | Toca el `OnPaint` de **todos** los controles (riesgo alto de regresión). Necesita fallback obligatorio. |
| **3. Superficie única** | Eliminar los HWND hijos y componer todo en un solo surface Skia/D2D | Resuelve A de raíz y elimina los dos escritores | Reescritura mayor. **Solo Fase 6**, no ahora. |

**Recomendación:** empezar por la **Opción 1** con blur en CPU a resolución reducida (1/2 o 1/4) usando `StackBlurUltra`, que ya existe y es O(n) independiente del radio. Es un blur: la pérdida de detalle a 1/2 resolución es imperceptible y mantiene el frame barato. Si el rendimiento no alcanza, migrar a la Opción 2.

**Lo que NO se debe hacer:** seguir pintando `BackColor` negro y "tapar" el halo con un color aproximado. Eso convierte un bug de composición en un parche cosmético que se rompe con cualquier fondo claro.

**Criterio de aceptación:** sin zonas negras inesperadas; sin flicker; mover/redimensionar/DPI siguen bien; `ModernGlassPanel` sin regresión; sin caída sostenida de FPS.

---

### 5.2 Bug B — DPI en runtime y artefactos del overlay

**Estado: CONFIRMADO (código) en su defecto mecánico / falta confirmar unidades lógicas vs físicas.**
**ID:** `FWF-DPI-001` · **Severidad:** alta · **Riesgo de fix:** medio

**Síntoma:** al cambiar la escala con la app abierta (125% → 100% → 150%), el overlay/borde queda desalineado, con filas de basura, líneas de color o píxeles corruptos.

**Hechos verificados:**

1. **No existe manejo de `WM_DPICHANGED`** en `ModernFormManager`, `ModernFormOverlay` ni `Direct2DBlurHelper`. (Sí existe `OnDpiChangedAfterParent` en los controles y un log en `Form1.OnDpiChanged`.)

2. **El bitmap del borde se crea con `Form.Width/Height`** — `ModernFormManager.cs:392`:
   ```csharp
   _skBorderBitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
   ```
   donde `w = _targetForm.Width`, `h = _targetForm.Height` (`:378-379`).

3. **`RefreshLayer` vuelve a multiplicar por escala** — `ModernFormManager.cs:1043-1044`:
   ```csharp
   int physW = (int)Math.Round((double)(w * scale));
   int physH = (int)Math.Round((double)(h * scale));
   ```

4. **La copia no escala y deja el resto sin escribir** — `:1081-1086`:
   ```csharp
   for (int y = 0; y < h; y++)
   {
       IntPtr rowSrc = IntPtr.Add(skPtr, y * skStride);
       int copyLen = Math.Min(skStride, physW * 4);
       Marshal.Copy(rowSrc, tmp, y * physW * 4, copyLen);
   }
   ```

5. **El buffer viene del `ArrayPool` y no se limpia** — `:1068` (`Rent(bytes)`) y `:1089` (`Marshal.Copy(tmp, 0, dibBits, bytes)`), devuelto con `Return(tmp, false)` en `:1093`.

**Consecuencia mecánica (independiente de si `w,h` son lógicos o físicos):**

- El bucle recorre `h` filas de origen pero el destino mide `physH` filas. Cuando `scale > 1`, las filas `h .. physH-1` **nunca se escriben** en `tmp`, y luego se copian igual al DIB porque `Marshal.Copy` copia `bytes` completos → **basura del pool**.
- Dentro de cada fila, si `skStride < physW * 4`, quedan bytes sin escribir → **columnas de basura**.
- No hay estiramiento geométrico: las filas se escriben 1:1. Aunque el buffer estuviera limpio, el borde quedaría **recortado/desalineado**, no escalado.

**Si `Form.Width/Height` ya son píxeles físicos** (comportamiento plausible con PerMonitorV2 en .NET 8), entonces además hay **doble escalado**. Eso debe confirmarse en runtime comparando `Form.Width` contra el rectángulo real de la ventana — no asumirlo.

**CONFIRMADO EN RUNTIME (2026-11-18).** Con el proceso de medición en DPI-aware:

```
overlay: bitmap=1116x512 scale=1.25 phys=1395x640 owner=(96,96,1116,512)
form real: 1116x512
overlay real: 1395x640  -> 279 px a la derecha y 128 px abajo fuera del form
```

`Form.Width/Height` son **físicos**; el bitmap del borde también. El factor `scale` en
`RefreshLayer` era un doble escalado puro. Al mover la ventana, ese sobrante se ve como un
borde/rectángulo que se sale del form (síntoma reportado por el usuario: *"se sale del Windows
… aparece cuando la muevo de posición"*).

**Fix aplicado:** `physW/physH = w/h` (sin `* scale`) y copia 1:1 por stride. Tras el fix el
overlay mide exactamente lo mismo que el form y lo acompaña al mover:

```
FORM  rect=(490,156)-(1606,668) size=1116x512
WIN   rect=(490,156)-(1606,668) size=1116x512
```

_Nota:_ el resto de defectos de esta sección (basura del `ArrayPool` en la copia, churn de
`CreateGraphics`, `WM_DPICHANGED` sin manejar) quedan como deuda pendiente para la fase de DPI
en caliente; el caso 125% fijo ya no produce artefactos.

**Iteración 2 — manejo de DPI en caliente.** Ya no queda sin manejar:

- `Form.DpiChanged` → `HandleDpiChange`: invalida el caché del borde
  (`_lastWidth/_lastHeight/_lastRadius/_lastScale`), marca el overlay para reenviar bitmap,
  rehace la calibración de cadencia y re-aplica los efectos.
- `PaintSkiaSmoothing` incluye la **escala** en la clave del caché (`_lastScale`).
- `Direct2DBlurHelper.NotifyDpiChanged()` libera en el próximo frame los DIB, el backdrop
  reducido y los recursos D2D (dependen de píxeles físicos).
- `ModernFormOverlay` resincroniza bounds en `DpiChanged` (no se autoescala).

Verificación con `FWF_FAKE_DPI=144` (simula el mensaje sin cambiar la escala de Windows):

```
dpi: 120 -> 144 form=1116x512
calibracion: paint=17ms -> intervalo backdrop=50ms
overlay 1116x512 at (160,160) == form 1116x512 at (160,160)
```

Pendiente: prueba real moviendo la ventana entre monitores/escalas.

**Adicional (confirmado por lectura):**
- `GetScaleFactor()` (`:366-373`) crea un `Graphics` con `CreateGraphics()` **en cada llamada** → churn de handles GDI. Se llama en `:381`, `:488`, `:594` y dentro de `RefreshLayer` (`:1040` usa su propio `CreateGraphics`).
- `Direct2DBlurHelper` cachea `_dibW/_dibH/_rtW/_rtH` (`:63`, `:212-234`) y no los invalida por cambio de DPI.

**Investigación requerida:**
1. Loguear `DeviceDpi`, `scale`, `Form.Width/Height`, `ClientSize`, tamaño del rect de ventana, `sk.Width/Height/RowBytes`, `physW/physH`, `bytes`.
2. Confirmar con datos si `Form.Width` es lógico o físico en este target.
3. Verificar que todo byte presentado esté inicializado (llenar el buffer con un patrón reconocible antes de copiar).
4. Probar cambio de DPI en caliente, cambio de monitor y recreación de handle.

**Criterio de aceptación:** 100%, 125%, 150% (y 175/200% si existe); cambio de DPI con la app abierta; mover entre monitores; sin filas residuales, sin desalineación, sin fugas de bitmaps/handles/targets.

---

### 5.3 Bug C — Retraso del blur al mover la ventana

**Estado: `FIXED-UNVERIFIED` — causa medida y corregida; falta la prueba visual del usuario.**
**ID:** `FWF-BLUR-001` · **Severidad:** media-alta · **Riesgo de fix:** alto

**Síntoma:** al arrastrar/redimensionar, el backdrop "va detrás" del movimiento.

**Medición (2026-11-18, arrastre programático 40 pasos × 16 ms):**

| Dato | Antes | Después |
|---|---|---|
| Backdrop fps (live, ventana moviéndose) | 4.8–6.9 | **29–31** |
| Coste de repintar la ventana | 27–43 ms | **19–22 ms** |
| Paints del form por frame | **8** | **2** |
| Memoria (WorkingSet) con Blur | **218 MB** | **88 MB** |
| Intervalo | 54–86 ms | 8 ms (normal) / calibrado (rendimiento) |

**Causa raíz (medida, no hipótesis).** El árbol Skia **no** era el culpable: pintar un
`FluentElement` cuesta **0.70 ms** (0.42 ms fondo del padre + 0.28 ms Skia). El problema era
que **el form se pintaba 8 veces por frame**, una por cada hijo con `BackColor = Transparent`:
WinForms repinta el padre debajo de cada hijo transparente, y cada repintado vuelve a procesar
el `BackgroundImage` completo (1116×512 ≈ 2.5 ms). 8 × 2.5 ≈ 20 ms duplicados.

El repintado del padre es **redundante**: `ModernControlBase.OnPaint` ya compone el fondo del
padre en el buffer del propio hijo (`InvokePaintBackground`).

**Fix aplicado:** `MakeControlsOpaque` pone `BackColor` opaco a los `FluentElement` cuando hay
Blur (excluye `ModernGlassPanel` y `ModernThemeToggle`, que sí dependen de
`BackColor.Transparent`). Verificación: halos con luminancia 46–61 frente a 61 del backdrop
adyacente (sin negro) e interior del botón sólido pixel-idéntico (MAD = 0).

**Descartado tras medir:** `ImageLayout.None` (24 vs 25 ms), `Tile` (34 ms),
`DoubleBuffered = false` (41 ms) y cachear el `SKBitmap` por control (~2 ms de ahorro).
Detalle en `PROGRESS.md`.

**Iteración 2 (feedback visual del usuario).** Dos ajustes más:

1. **Congelar el backdrop durante el arrastre.** `FormMessageHook` llamaba a
   `ForceImmediateRender()` sincrónicamente en cada `WM_WINDOWPOSCHANGED`, y cada frame cuesta
   ~15 ms de hilo UI → el arrastre se veía como "una sucesión de fotos". Ahora, mientras
   `_isResizing` (entre `WM_ENTERSIZEMOVE` y `WM_EXITSIZEMOVE`), no se produce ni se aplica
   backdrop; al soltar se fuerza un refresco. Medido: 0 frames aplicados durante el arrastre y
   overlay alineado con el form en 80/80 muestras (0 px de desfase).
2. **Blur fino a resolución nativa.** El pipeline bajaba siempre a 1/2 (`d = 2`), lo que ya
   suavizaba ~4 px aunque el radio fuese 1. Ahora `BlurAmount <= 3` con ventanas ≤1600 px usa
   `d = 1` (radio 2 px reales). Energía de borde: 3.425 (antes 1.283 con amount 5).

**Iteración 3 (feedback: "no es live, solo una screen; el blur se ve feo").** El congelado se
**revirtió**: una imagen quieta se percibe peor que 15 fps reales. Y el blur se reescribió con
la receta de `ModernGlassPanel`, que es la referencia visual pedida:

- Resolución **nativa** para blur fino (sin downscale/upscale) y 1/2–1/4 para blur fuerte.
- Dos pasadas de caja (`StackBlurUltra`) ≈ gaussiana de la misma sigma + `ceil(sigma)+2` de
  margen implícito (la pasada de caja usa clamp en los bordes, sin viñeta).
- Tinte blanco `A=40` (`GlassTint` del panel).
- `BlurAmount` pasa a ser **sigma en DIPs**, idéntico al panel.

**Iteración 4 (feedback: "parece un monitor de captura"; RAM a ~200 MB).**

- `BackdropPerformanceMode` (normal = refresco del monitor, rendimiento = cadencia calibrada).
- Se quitó Skia del camino del backdrop: retenía ~140 MB. RAM 218 → 88 MB.
- Se quitaron `ArrayPool`/`SKImage.FromBitmap` del frame (no reutilizaban/copiaban).
- El worker ya no hace `Invoke` al hilo UI (bounds cacheados por eventos) y hay doble buffer:
  el pipeline dejó de estar serializado.
- Medido: `dump 1116x512`, fps **29–31**, RAM **88 MB**.

**Evidencia verificada:**

| Evidencia | Detalle |
|---|---|
| `ModernFormManager.cs:875-877` | `FormMessageHook.WndProc` intercepta `WM_WINDOWPOSCHANGED`/`WM_SIZING` y llama **sincrónicamente** `_manager._d2dBlurHelper?.ForceImmediateRender()`. |
| `ModernFormManager.cs:573` | Segunda llamada a `ForceImmediateRender()` en el flujo del manager. |
| `Direct2DBlurHelper.cs:113` | Hilo de render propio (`new Thread(...)`). |
| `Direct2DBlurHelper.cs:171-177` | Si `InvokeRequired`, hace `_form.Invoke(_updateBoundsAction)` — **espera al hilo UI**. |
| `Direct2DBlurHelper.cs:271` | `BitBlt(_dibDc, 0, 0, w, h, screenDc, x, y, SRCCOPY)` — captura la pantalla en la posición actual. |
| `Direct2DBlurHelper.cs:111` | `WDA_EXCLUDEFROMCAPTURE` evita capturarse a sí mismo. |

**Hipótesis:** el hook del hilo UI y el hilo de render se pelean por el hilo UI (`Invoke` + `ForceImmediateRender` sincrónico). Además, la captura y la presentación corresponden a instantes/posiciones distintos: cuando la ventana se mueve, se captura el escritorio **detrás** del nuevo rect y el frame se presenta después. Es latencia estructural del pipeline, no solo FPS.

**Investigación requerida:**
1. Medir por separado: captura, procesamiento, espera de `Invoke` y presentación.
2. Registrar timestamp + posición de ventana por frame.
3. Medir cuánto bloquea `ForceImmediateRender()` en el hilo UI.
4. Probar movimiento lento/rápido, resize y ventana parcialmente fuera de pantalla.
5. Verificar que no se acumulen frames obsoletos.

**Criterio de aceptación:** el backdrop acompaña el movimiento sin bloquear la UI, sin deadlocks, sin colas crecientes y con shutdown seguro.

---

### 5.4 Hallazgos secundarios

| ID | Hallazgo | Evidencia | Estado |
|---|---|---|---|
| `FWF-ANIM-001` | `GC.Collect()` **por control** cada 600 frames dentro del tick de animación | `ModernControlBase.Animation.cs:322-326` | CONFIRMADO (código) |
| `FWF-FPS-001` | `AnimationManager.Mode` nunca se asigna; `_targetInterval = TimeSpan.Zero` → `Unlocked` por defecto (~1000 ticks/s). El "120 FPS" del README no está conectado. `AdaptiveEnabled = true` y `PauseWhenAllHidden = true` por defecto, pero `SetSystemMetricsProvider` nunca se llama (queda `DefaultMetricsProvider`, CPU=0, batería=false) | `AnimationManager.cs:42`, `:116-128`, `:110-112` | CONFIRMADO (código) |
| `FWF-LIFE-001` | `Unregister` toma `_lock` y llama `Stop()` → `Join(500)`; el hilo que llama puede bloquearse hasta 500 ms | `AnimationManager.cs:228-245`, `:176-191` | CONFIRMADO (código) |
| `FWF-THREAD-001` | El tick corre en hilo no-UI y toca estado de WinForms (`Visible`, `IsHandleCreated`, `Invalidate()`, `BeginInvoke`) | `ModernControlBase.Animation.cs` (tick) | CONFIRMADO (código) / impacto a medir |
| `FWF-GLASS-001` | `GenerateStaticCache` hace `_blurredCache.Dispose()` **dentro** del `using (var canvas = new SKCanvas(_blurredCache))` y lo reasigna en medio | `ModernGlassPanel.cs:161-168` | CONFIRMADO (código) |
| `FWF-ACRYLIC-001` | `PrintWindow` se llama sin comprobar el retorno; una captura fallida se trata como válida | `AcrylicHelper.cs:97`, `:241` | CONFIRMADO (código) |
| `FWF-POOL-001` | `CaptureBackdropAsync` puede devolver el mismo `Bitmap` cacheado a varios consumidores que luego lo devuelven al pool → doble devolución / uso tras devolución | `AcrylicHelper.cs:314` + `BitmapPool` | CONFIRMADO (código) / impacto a medir |
| `FWF-FORM-001` | `HandleCreated += lambda` sin desuscripción; overlays/handlers sin cleanup; `Start()` podría crear un segundo hilo de blur | `ModernFormManager.cs:128`, `:142-147`, `:868` | CONFIRMADO (código) / idempotencia a verificar |
| `FWF-DIAG-001` | `DpiTrace` escribe `dpi-trace.txt` en el Escritorio; `Form1` escribe `dpi-log.txt` | `Core/DpiTrace.cs:11-12`, `Form1.cs` | CONFIRMADO (código) — es andamiaje temporal |
| `FWF-ARCH-001` | Responsabilidades concentradas: `Core` mezcla render, Win32, animación, layout y API pública | estructura de carpetas | LIMITACIÓN de diseño |

**Nota sobre `GC.Collect()`:** no reemplazarlo a ciegas por otra recolección manual. Primero medir allocations y frame time. Puede que ni siquiera sea necesario (gen0 en .NET 8 es barato); lo grave es hacerlo **N veces por frame**, una por control.

**Nota sobre FPS:** contar iteraciones del loop **no** es FPS presentados. Medir frames realmente pintados.

---

## 6. Riesgos transversales (checklist por cambio)

Antes de tocar cualquier subsistema, responder:

- [ ] ¿Quién **crea** cada `SKBitmap`, `SKCanvas`, `Bitmap`, HBITMAP, HDC, `HwndRenderTarget`?
- [ ] ¿Quién lo **posee** y quién puede accederlo?
- [ ] ¿Cuándo se **invalida** (DPI, tamaño, color, radio, tema)?
- [ ] ¿Quién lo **libera** y exactamente una vez?
- [ ] ¿Hay acceso concurrente a cachés/pools? ¿Doble devolución? ¿Uso tras devolución?
- [ ] ¿Hay buffers parcialmente inicializados que se presentan?
- [ ] ¿Puede llegar un callback después de `Dispose`?
- [ ] ¿Quedan workers vivos al cerrar el form?
- [ ] ¿Las colas tienen límite?
- [ ] ¿Hay reentrancia de `Invoke`/`BeginInvoke`/mensajes?
- [ ] ¿La clave de caché incluye DPI/tamaño/color/radio?
- [ ] ¿Los targets Direct2D tienen dimensiones actuales?
- [ ] ¿Se mezclan coordenadas lógicas y físicas?
- [ ] ¿Hay repintado recursivo o múltiples escritores del mismo surface?

---

## 7. Plan por fases

### Fase 0 — Verificar checkout y preservar cambios

- Confirmar rama, commit y working tree.
- Revisar el diff local y **preservar** `Form1.Designer.cs`.
- Confirmar target, paquetes y build.
- Identificar archivos protegidos (API pública, `ModernGlassPanel`, controles).

**Salida:** confirmación escrita del estado, sin cambios.

### Fase 1 — Harness y baseline (SIGUIENTE PASO)

- Reutilizar `GalleryForm` si sirve; **no alterar el arranque normal** de `Program.cs` sin permiso.
- Crear escenarios mínimos para A, B y C.
- Capturar baseline visual (screenshots con nombre y fecha).
- Loguear: DPI, tamaños lógicos/físicos, stride, tiempos, threads, handles.
- Registrar pasos exactos de reproducción.
- Si un bug no se reproduce → marcarlo `NOT REPRODUCED` con entorno y limitaciones.

**Salida:** evidencia reproducible. Cero cambios de comportamiento.

### Fase 2 — DPI / overlay (B)

- Trazar unidades lógicas vs físicas y stride de punta a punta.
- Confirmar doble escalado y buffer incompleto.
- Corregir de forma localizada.
- Centralizar invalidación DPI (overlay + borde + blur + región), sin duplicar cálculos.
- Pasar matriz de DPI y pruebas de lifecycle.

### Fase 3 — Composición Blur/Acrylic (A)

- Determinar el dueño de cada surface.
- Confirmar la causa exacta del negro con evidencia.
- Definir una composición coherente (un solo escritor, o capas explícitas).
- Probar Blur y Acrylic por separado.
- Regresión completa de controles y `ModernGlassPanel`.

### Fase 4 — Latencia de movimiento (C)

- Medir cada etapa del pipeline.
- Reducir contención del hilo UI.
- Evitar presentar capturas obsoletas.
- Verificar resize, movimiento rápido y límites de recursos.

### Fase 5 — Threads, lifecycle y recursos

- Revisar `GC.Collect()`.
- Conectar scheduler/política FPS con mediciones reales.
- Corregir la frontera UI/worker.
- Auditar locks, `Join`, `Stop`, `Unregister`.
- Corregir ownership de bitmaps/pools.
- Limpiar eventos y overlays.

### Fase 6 — Refactor arquitectónico y limpieza

Solo tras estabilizar:

- Separar responsabilidades y definir contratos/ownership.
- Desacoplar plataforma.
- Mantener API compatible.
- Eliminar código muerto y andamiaje de diagnóstico.
- Añadir tests y samples.

**Dirección sugerida (no ejecutar todavía):** `Core/`, `Rendering/`, `Animation/`, `Effects/`, `Layout/`, `Dpi/`, `Resources/`, `Controls/`, `FluentApi/`, `Platform.Windows/`, `Tests/`, `Samples/`, `Documentation/`.

Evitar abstracciones prematuras. No implementar WinUI.

---

## 8. Matriz mínima de regresión

| Escenario | Resultado esperado |
|---|---|
| Blur apagado | Pintura normal |
| Blur encendido | Sin zonas negras inesperadas |
| Acrylic encendido | Backdrop correcto y fallback válido |
| DPI 100 / 125 / 150% | Geometría y borde correctos |
| DPI 175 / 200% (si disponible) | Sin artefactos |
| Cambio de DPI con la app abierta | Overlay y targets actualizados |
| Mover entre monitores | Escala y geometría coherentes |
| Mover la ventana rápido | Backdrop sin retraso grave |
| Resize | Sin tearing, líneas ni surfaces viejos |
| Minimizar / restaurar | Lifecycle y captura correctos |
| Cerrar durante una animación | Sin callbacks sobre objetos dispuestos |
| `ModernGlassPanel` | Sin regresión visual ni de recursos |
| Abrir/cerrar repetidamente | Sin crecimiento sostenido de memoria/handles |

**Validación visual:** se puede usar OpenCV para screenshots, image diff, ROI y detección de zonas negras, **solo como herramienta de test**. No es dependencia runtime.

No tratar cualquier diferencia de píxel como fallo: varían fuentes, antialiasing, drivers, versión de Windows, escalado fraccional y perfiles de color. Combinar diff visual con asserts de dimensiones/stride, logs de DPI, tiempos, handles y revisión humana.

---

## 9. Métricas a registrar

- FPS deseado, efectivo y **realmente presentado**.
- Frame time promedio, p95 y p99.
- Deadlines fallidos, frames descartados/coalescidos.
- Tiempo de captura, procesamiento y presentación del backdrop.
- CPU/GPU cuando la medición sea fiable.
- Memoria administrada y nativa.
- Handles GDI/USER.
- Refresh rate; batería/AC y datos térmicos solo si son fiables.

**Regla:** no afirmar "120 FPS" por contar iteraciones del loop.

---

## 10. Issue ledger

Estados: `OPEN`, `INVESTIGATING`, `REPRODUCED`, `FIXED-UNVERIFIED`, `VERIFIED`, `BLOCKED`, `PLANNED`.

| ID | Hallazgo | Estado |
|---|---|---|
| `FWF-COMP-001` | Regiones negras Blur/Acrylic | `VERIFIED` |
| `FWF-DPI-001` | Doble escala / artefactos del overlay | `FIXED-UNVERIFIED` |
| `FWF-BLUR-001` | Latencia del blur al mover | `FIXED-UNVERIFIED` (paint 43→15 ms, backdrop 5→16 fps, arrastre congelado) |
| `FWF-ANIM-001` | `GC.Collect()` en `AnimationTick` | `OPEN` |
| `FWF-FPS-001` | Scheduler / política FPS sin conectar | `OPEN` |
| `FWF-THREAD-001` | Acceso a WinForms desde worker | `INVESTIGATING` |
| `FWF-LIFE-001` | Locks, `Join` y lifecycle de `AnimationManager` | `INVESTIGATING` |
| `FWF-GLASS-001` | Disposal dentro de `using` en `ModernGlassPanel` | `INVESTIGATING` |
| `FWF-ACRYLIC-001` | Validación de `PrintWindow` | `OPEN` |
| `FWF-POOL-001` | Ownership compartido `Bitmap`/cache/pool | `INVESTIGATING` |
| `FWF-FORM-001` | Eventos y posible doble `Start` | `INVESTIGATING` |
| `FWF-DIAG-001` | Logs de diagnóstico en el Escritorio | `OPEN` |
| `FWF-ARCH-001` | Responsabilidades concentradas en `Core` | `PLANNED` |
| `FWF-MEM-001` | RAM del backdrop a ~200 MB (`SKImage.FromBitmap` copiaba por frame + `ArrayPool` no reutiliza 2.3 MB) | `FIXED-UNVERIFIED` (88 MB) |

Cada issue debe registrar: ID, título, tipo, severidad, entorno, pasos de reproducción, evidencia, archivos/métodos, causa raíz (hechos vs hipótesis), cambio, riesgos, pruebas y estado.

**No usar `VERIFIED` sin reproducción y re-verificación.** Un fix con código cambiado pero sin prueba real es `FIXED-UNVERIFIED`.

---

## 11. Formato obligatorio de cada informe de cambio

1. Problema y síntoma.
2. Evidencia y reproducción previa.
3. Causa raíz demostrada (separando hipótesis).
4. Archivos modificados.
5. Cambio realizado.
6. Confirmación de compatibilidad de API.
7. Build: comando, entorno y resultado.
8. Tests: `PASS` / `FAIL` / `BLOCKED`, procedimiento y evidencia.
9. Regresiones de `ModernGlassPanel` y escenarios relacionados.
10. Métricas antes/después, si aplican.
11. Revisión del diff para excluir cambios ajenos.
12. Limitaciones e incertidumbres.
13. Confirmación explícita de **no haber hecho commit ni push**.

---

## 12. Artefactos de diagnóstico actuales

| Artefacto | Qué hace | Acción |
|---|---|---|
| `Core/DpiTrace.cs` | Escribe `dpi-trace.txt` en el Escritorio (máx. 3000 líneas) | Andamiaje temporal; retirar en Fase 6. |
| `Form1.cs` (`OnDpiChanged`, `Dump(string tag)`) | Escribe `dpi-log.txt` en el Escritorio y vuelca el árbol de controles | Útil para repro de B; retirar en Fase 6. |
| `GalleryForm.cs` | Golden images (F5 guardar / F6 comparar) contra `%LocalAppData%\FluentWinForms\golden` | Base del harness de Fase 1. |
| `AnimationManager.DumpDiagnostics()` (`:425-430`) | Devuelve `Running`, `Mode`, `Subs`, `Frames`, `AvgDt`, `P95`, `P99`, `MaxDt`, `Drops`, `Forced`, `Uptime` | Base para medir FPS/frame time sin inventar métricas. |
| `FWF_DUMP_BACKDROP=1` | Vuelca el backdrop CPU a `%TEMP%\fwf-backdrop.png` y registra asignación, calibración y fps en `%TEMP%\fwf-backdrop-log.txt` | Diagnóstico del fix de A/B. Solo con la variable activa. |
| `FWF_DUMP_FORM=1` | Vuelca la composición completa a `%TEMP%\fwf-form.png` con `DrawToBitmap` | Verificar halos/transparencia sin capturar el HWND. |
| `FWF_BLUR_AMOUNT=<n>` | Fuerza la intensidad del blur (1–40) en runtime | Probar sensibilidad sin recompilar. |
| `FWF_BACKDROP=Blur\|Acrylic\|None` | Fuerza el tipo de backdrop | Probar A/B/C sin editar el diseñador. |
| `FWF_BORDER_THICKNESS=<n>` | Fuerza el grosor del borde | Necesario para hacer visible el overlay al validar B. |
| `FWF_USE_SKIA=1\|0` | Fuerza el motor Skia del borde | Validar la ruta del overlay. |
| `FWF_FAKE_DPI=<n>` | Simula un cambio de DPI a los 2.5 s | Verificar invalidación de cachés sin cambiar la escala de Windows. |

Ninguno debe romper el arranque normal ni escribir en el Escritorio en una versión release.

---

## 13. Datos del entorno que faltan

Sin estos datos, C queda en hipótesis y B no se puede validar en matriz:

1. Versión/build exactos de Windows (`winver`).
2. Resolución y escala de cada monitor.
3. ¿Uno o varios monitores?
4. GPU y driver.
5. ~~¿El Bug A aparece con Blur, Acrylic o ambos?~~ **Respondido:** solo con `Blur`, alrededor de los botones. Confirmar además si Acrylic se probó a fondo o solo no se notó.
6. Pasos exactos de reproducción de B y C (para A ya hay descripción suficiente).

No bloquear la preparación del harness por datos faltantes: registrar las limitaciones del entorno usado.

---

## 14. Mensaje para el siguiente agente

> Continúa la auditoría y estabilización de FluentWinForms usando este documento como contexto.
>
> Primero confirma rama, commit, working tree y cambios locales. **Preserva `FluentWinForms/Form1.Designer.cs`** y cualquier modificación del usuario. Coteja que los hallazgos siguen correspondiendo al checkout actual: los números de línea pueden haberse movido.
>
> El siguiente paso es **preparar el harness de reproducción y diagnóstico** para A (regiones negras), B (DPI/overlay) y C (latencia del blur), **sin cambiar todavía el comportamiento del framework**.
>
> No hagas commits ni pushes. Mantén intacta la API `FluentElement.Design(...)`. Separa evidencia, hipótesis y conclusiones. No declares un problema solucionado sin reproducirlo y volver a probarlo. Protege `ModernGlassPanel`. No borres código muerto durante la estabilización.
>
> Conserva el objetivo adaptativo de **120 FPS en AC y 60 FPS en batería**, sin forzar una tasa que el sistema no pueda sostener.

---

## 15. Registro de decisiones

| Fecha | Decisión | Motivo |
|---|---|---|
| 2026-11-18 | El documento vive en `AUDIT.md` (raíz) | Visibilidad inmediata; no compite con el `README.md` público. |
| 2026-11-18 | B (DPI/overlay) se ataca antes que A (negro) | Defecto mecánico concreto y verificable (buffer parcial + copia sin escalado). |
| 2026-11-18 | A es el cambio de mayor riesgo; va después de B | Toca composición y puede afectar todos los controles. |
| 2026-11-18 | No eliminar `GlassBackdropCache`/`GlassPainter`/`GlassTypes` | Podrían servir para el rediseño de composición en Fase 6. |
| 2026-11-18 | Preservar `Form1.Designer.cs` con `Blur` + `AutoSetBlackBackground = true` | Es el escenario que reproduce el Bug A. |
| 2026-11-18 | Mantener política 120 FPS AC / 60 FPS batería | Requisito del proyecto. |
| 2026-11-18 | Todo el trabajo nuevo va en `feature/estabilizacion`; **`main` no se toca** | Pedido explícito del usuario. |
| 2026-11-18 | Bug A acotado: **solo Blur**, halos rectangulares alrededor de los controles | Reporte de reproducción del usuario. |
| 2026-11-18 | Bug A implementado con la **Opción 1** (backdrop CPU como `BackgroundImage`) | Es el cambio que no toca `ModernControlBase` ni la API pública. |
| 2026-11-18 | El backdrop se produce a 1/4 de resolución con `StretchBlt` + `StackBlurUltra` | Costo de CPU mínimo y calidad indistinguible en un blur. |
| 2026-11-18 | **Un solo escritor**: con backdrop activo, D2D no dibuja en el HWND | Evita dos superficies de vidrio desalineadas (causa real del Bug A). |
| 2026-11-18 | El backdrop se emite a **tamaño real** (upscale con `StretchBlt` + HALFTONE) | 2.3 ms por repintado vs 6.6 ms del reescalado HQ de WinForms. |
| 2026-11-18 | Cadencia del backdrop **calibrada** al coste medido: `clamp(paint*2, 50, 250)` ms | No pedir al hilo UI más frames de los que puede sostener (32–43 ms medidos). |
| 2026-11-18 | Overlay **sin** multiplicar por DPI (`Form.Width` ya es físico) | El factor 1.25 generaba un overlay 1395×640 sobre un form 1116×512. |
| 2026-11-18 | Controles GDI nativos pasan a `Color.Transparent` una sola vez | El `Label` nativo también muestra el backdrop en vez del `BackColor`. |
| 2026-11-18 | `FluentElement.BackColor` opaco cuando hay Blur (C) | Elimina 6 de los 8 repintados del form por frame; OnPaint ya compone el fondo. Medido: 43→15 ms. |
| 2026-11-18 | No cachear el `SKBitmap` de los controles (C) | Medido: el render Skia cuesta 0.28 ms/control; el caché no aporta y añade riesgo visual. |
| 2026-11-18 | Congelar el backdrop durante el arrastre (C) | Cada frame son ~15 ms de hilo UI; sin repintados el movimiento es fluido (como el acrylic nativo). |
| 2026-11-18 | Congelar el backdrop durante el arrastre (C, revertido) | Se probó para ganar fluidez, pero el usuario lo percibió como "una screen quieta". |
| 2026-11-18 | Blur del backdrop con la receta de `ModernGlassPanel` (C) | Resolución nativa + Skia gaussiano + padding + tinte 40: es la referencia visual que pidió el usuario. |
| 2026-11-18 | Blur fino (`<=3`) a resolución nativa (C) | El downscale 2x ya suavizaba ~4 px; a `BlurAmount=1` se veía "foto movida". |
| 2026-11-18 | Manejar `Form.DpiChanged` e invalidar cachés (D) | Borde, overlay y recursos D2D viven en píxeles físicos; sin esto quedaban con la escala vieja. |
| 2026-11-18 | `BackdropPerformanceMode`: normal = refresco del monitor; rendimiento = cadencia calibrada | El usuario pidió un modo fluido tipo vsync y conservar el económico. |
| 2026-11-18 | Backdrop sin Skia y con 2 buffers fijos (C) | Skia retenía ~140 MB y `ArrayPool` no reutiliza 2.3 MB: RAM 218 → 88 MB. |
| 2026-11-18 | No `Invoke` al hilo UI desde el worker (C) | Serializaba el pipeline detrás de cada repintado (~20 ms). |

---

## 16. Recomendación final

**Verificar checkout → reproducir y medir → DPI/overlay (B) → composición (A) → latencia (C) → threads/lifecycle/recursos → refactor.**

La meta es que FluentWinForms sea estable, predecible, eficiente y utilizable por otros desarrolladores, **sin perder la identidad de su API pública**.
