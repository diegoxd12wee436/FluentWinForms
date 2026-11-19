# NOTES.md — Bitácora de trabajo autónomo

> Documento de continuidad. Se actualiza después de cada commit.
> Si se perdió el contexto: leer "ESTADO ACTUAL", `git log -10 --oneline`, `git status` y las REGLAS.

---

## ESTADO ACTUAL

- **Rama:** `feature/estabilizacion` (nunca se cambia; nunca se hace push).
- **Motor del backdrop:** `@revisor` **APROBADO** (3 rondas). `@rompedor`: *"No por el motor
  (pasó todo lo sustancial)"* — sin fuga, `rPx` 1/4/6, DPI sin crash. **Commit 1.**
- **`Form1.cs` (handlers + logging):** verificado por `@revisor`. **Commit 2.**
- **`StressTests/`:** funcional y estable (500 ciclos, 0 fallos), pero **sin los dos APROBADO
  formales** (los revisores tropezaron con su propio sandbox y con métricas ruidosas).
  Queda **sin commitear**, documentado abajo con los TODOs de cobertura. **Decisión del usuario.**
- **`Form1.Designer.cs`, `opencode.json`, `.opencode/`:** del usuario; **no se commitean**.

### Resultados finales (500 ciclos, build actual)

```
[assert] OK blur live (mover): hash 86F5B61B... -> 26B633FC...
[assert] OK blur live (quieto, fondo cambia): hash 26B633FC... -> B5C3E337...
[assert] OK WorkingSet: 159 -> 94 (0.59x)
[assert] OK Private:    117 -> 37 (0.32x)
[assert] OK GDI: 24 -> 24   USER: 13 -> 13
[assert] pendiente Private = negativa
[stress] fallos=0  (110 s)
```

Ambos rompedores coincidieron: Private/GDI/USER/Threads/Handles **estables o a la baja**;
WorkingSet oscila por recorte del SO (por eso el gate de tendencia ahora usa Private, no WS).

### TODOs del harness (anotados por los revisores, no bloquean)

1. Click real con `WM_LBUTTONDOWN/UP` y hit-testing (hoy via `OnMouseDown` por reflexión).
2. Ripple: `ControlBuilder.Ripple()` no activa `UseRipple` (bug de producto detectado); probar
   `btn-danger` y afirmar `_isRippling`.
3. Alternar Blur→None→Blur ×50 y afirmar `BackColor` restaurado por control.
4. `BlurAmount` 1→10→20 con pantalla quieta (hash + `_bdLastRadius`).
5. Bloquear el hilo UI >1.5 s para ejercitar el watchdog de slots.
6. Medir retención de `_bdWork`/handoffs tras un resize 4K (hoy se cierra el form).

### Nota de procedimiento

Los revisores corren con sandbox restringido (solo `dotnet`/`git`): no pueden lanzar la app ni
matar procesos. Sus veredictos sobre RAM de la app real se basan en logs de esta sesión.

---

## Tarea C — 15 fps: qué se probó y por qué no subió

Medido con arrastre programático (150 pasos) y `FWF_DUMP_BACKDROP`:

| Intento | Coste medido | Resultado |
|---|---|---|
| Cache de pantalla difuminada a resolución nativa | refresco 200 ms (blur de 1.3 Mpx) | **6 fps** (descartado) |
| Cache de pantalla a 1/3 + recorte con upscale | crop 8–10 ms + refresco ~100 ms | **15 fps** (descartado) |
| Invalidar solo hijos en 3 de 4 frames (Label cada 4) | `Update()` no pinta HWND hijos → se encolan frames | **1.3 fps** (revertido) |
| Camino actual (captura + downscale + 2×blur + upscale + `Invalidate(true)`) | produce ~20 ms, paint ~20 ms | **15–20 fps** |

**Conclusión:** el techo lo pone el repintado de WinForms (~20 ms por frame ⇒ ~50 fps teóricos),
no el blur. Para igualar al acrylic (que va al ritmo del compositor) haría falta blur a nivel
DWM/compositor, no CPU. Se dejó el camino actual, que es el más rápido medido y estable.
Los tres experimentos se revirtieron; no quedó código muerto.

## Tarea 7 — Sombra/glow: APROBADO y commiteado

`@revisor` verificó que `reach = S(Radius) * 2` es espejo exacto del padding del bake, cubre
offsets negativos/positivos y que 3σ < 2·radio. Observaciones no bloqueantes anotadas:
solape de HWND con hermanos cercanos (el margen pasó de ~15 a ~36-41 px), hit-test del margen,
y `Anchor/Dock` (no se usa en el repo).

## Tarea 3 — Ripple sin allocations por frame

**Estado:** APROBADO por `@revisor`; commiteado solo la optimización.

- **Allocations:** el bloque del ripple creaba `SKPathBuilder`/`SKPath`/`SKPaint` por frame.
  Ahora hay caché por nodo (`_cachedRippleClip`, `_cachedRipplePaint`) con dirty-check de
  rect/esquina; el paint solo actualiza color/alpha. Se liberan en `ReleaseNativeResources`.
- **Verificación:** build 0/0; el revisor confirmó que no quedan allocations por frame en el
  bloque, sin doble dispose y solo UI thread.

### Bug de producto NO tocado (decisión pendiente del usuario)

`ControlBuilder.Ripple(...)` nunca activa `UseRipple`, así que **el ripple no se reproduce**.
No se activó porque destapa dos bugs preexistentes:

1. El alpha del color se **sustituye** por `opacity` (`Rendering.cs:568`). Con `.Ripple()`
   (default `#40000000`) el círculo sale **100 % opaco** en vez de translúcido; `opacity > 1`
   castea a byte fuera de rango.
2. `_currentPressedNode = null` en `OnMouseUp` (`Animation.cs:481`): el fade nunca se dibuja y
   el ripple desaparece de golpe al soltar.

**No se cambió el comportamiento visual sin decisión del usuario** (regla: no tocar si hay duda).

## Tarea 7c — Expansión de sombra segura (RECHAZADA y revertida)

**Estado: revertida.** Se implementó con acumuladores separados, clamp solo de la sombra,
recálculo de hermanos y aserción del harness (gaps 0/1/20 → OK). El `@revisor` y el `@rompedor`
la rechazaron con dos pruebas:

1. **Diagonal**: el test perpendicular usa el contenido propio (0 en reposo), no el alcance de la
   sombra. Dos vecinos en diagonal con `Glow(14)` se solapan **38×38 px** (57×57 con gap 1/1).
2. **Badge**: en la app real, `btn5` ("Notificaciones") tiene `.Badge("5").BadgeOffset(-4,-6)`,
   que aporta 15 px de contenido hacia arriba. El clamp resta solo el contenido de **este**
   control, así que `btn4` conserva 10 px de sombra: **5 px de solape estático** (reproducido en
   dos dumps, no es animación como decía la nota anterior).

**Conclusión:** la expansión de sombra es un problema de empaquetado 2D: hay que calcular el
contenido y la sombra de **ambos** lados de cada par (no solo del control que se recalcula) y
limitar en los dos ejes para diagonales. Requiere un helper compartido de "expansión deseada por
control" y un pase de layout global. No se hace con un clamp local; se deja el comportamiento
original (`radio + offset`, sin solape) para no romper nada.

**Lo que sí queda demostrado y sirve para la futura implementación:** harness con aserción de
intersección de `Bounds` (gaps 0/1/20), recálculo de hermanos con guard anti-recursión, y la
prueba de que el badge es el caso que rompe los cálculos locales.

**Estado:** implementado, pendiente de revisión (reportado por el usuario: "se superponen los
botones y se ven recortados").

> Histórico: primera versión del clamp (rechazada por casos límite); ver más abajo.

- **Causa:** al expandir el HWND a `2*S(Radio)` (36-41 px), botones separados 20 px se solapan;
  cada hijo pinta el fondo del padre en todo su rect, así que el de arriba borra al de abajo.
- **Fix:** en `UpdatePhysicalBounds` se recorre a los hermanos visibles y cada lado se limita a
  `min(expansión, gap/2 - 1)`, usando su `LogicalBounds`.
- **Evidencia:** bounds medidos sin solapes: **(4,4,252,93)** — top recuperado a 36 px —,
  (39,107,182,50), (19,167,222,66), (29,235,202,66), (0,303,261,82), (29,391,222,95). Chequeo
  geométrico de los 6 rects: **0 pares con solape**. Dump sin botones tapados. Harness 40 ciclos:
  0 fallos, blur live (mover + quieto) OK, Private estable.
- **Alcance del clamp:** solo contra hermanos que solapan en el eje perpendicular (un diagonal no
  limita), `hueco/2 - 1` si el vecino también expande y `hueco - 1` si no (nativos/GlassPanel), y
  nunca por debajo del overflow de hover/scale.
- **Estado de revisión:** 2 rechazos por casos límite, **sin commitear**:
  1. Vecinos en diagonal: el test se hace con los rects inflados por la expansión actual; aun así,
     la expansión del vecino no se conoce y puede quedar solape de esquina en layouts escalonados.
  2. Hover/translate (`.Hover(translateX: 22)`) con un vecino a <9 px: el tope recorta el
     desplazamiento del nodo durante el hover (el suelo actual solo cubre `scale`, no `translate`).
  3. Suelo `+1` puede invadir 1 px con huecos de 0-1 px.
- **Decisión:** se mantiene en el working tree porque **elimina el solape reportado por el usuario**
  en el layout real (una columna, 0 pares con solape). La solución completa exige separar los
  acumuladores (anim/translate/badge/sombra) y recalcular todos los hermanos en `OnLayout`,
  un refactor de layout mayor. Pendiente de decisión del usuario.
- **Trade-off:** con separación de 20 px el margen de glow queda en ~9 px; para glows grandes hay
  que separar más los controles (es geometría, no un bug).

## Tarea C4 — Backend de compositor para Blur: 3.ª iteración (RECHAZADA)

Se implementó la checklist completa de 11 puntos y **midió bien** (System: 0.00 s CPU, 62.9 MB,
0 frames; Cpu: 18.8 %, 82.3 MB, 4 frames), pero el `@revisor` la rechazó con hallazgos nuevos.
**Revertida.** Los bloqueos definitivos para una sesión dedicada:

1. **Windows 10 roto**: `useSystemBlur` exige Win11, pero los filtros de frames comparan con el
   backend *configurado* (default System) → en Win10 el motor CPU trabaja y los frames se
   descartan (form negro). Usar el backend efectivo `Cpu || !isWin11` en los filtros.
2. **`TransparentStyle` desde Blur/Cpu**: la rama None no llama `StopCpuBackdrop()` →
   `BackgroundImage` estático + hilo capturando. El setter de `BackdropType` sí limpia.
3. **Cambio de `TargetForm` en Cpu**: `UnsubscribeTargetForm` no hace `Stop/SetBackdropSink(null)`
   ni incrementa la generación; el worker sigue capturando el form viejo y pinta en el nuevo.
4. **Acrylic→None/Blur(System)**: `DisableSystemBlur` solo actúa si `_systemBlurHwnd != 0`, pero
   el modo Acrylic aplica accent/frame sin registrarlo → tinte/frame residual.
5. **Carrera en `StopCpuBackdrop`**: `SetBackdropSink(null)` antes de `Stop()` deja que el worker
   entre en el camino GPU y pinte un snapshot estático sobre el material. `Stop()` no hace join.
6. **`_systemBlurHwnd` obsoleto**: falta `HandleDestroyed`/validación `IsWindow`.
7. **Memoria**: al pasar Cpu→System no se dispone el helper (DIBs + buffers + 2 handoffs) y
   `Start/Stop` filtra un `CancellationTokenSource` por ciclo.
8. **Harness**: sus checks exigen `BackgroundImage`; hay que forzar `BlurBackend=Cpu` y añadir un
   check de que System produce 0 frames.
9. **`TargetForm`**: el overlay viejo no se dispone ni se desuscriben sus lambdas/hook.
10. **Estado visual del form viejo**: no se restaura `BackColor/TransparencyKey/Region/corners`.
11. **Docs**: el enum `Blur` sigue diciendo "100% GPU Direct2D"; `FWF_BLUR_CPU` no está en
    `PROGRESS.md`/`AUDIT.md`.

**Conclusión:** el material del compositor funciona y es la vía correcta, pero enchufarlo a `Blur`
toca el ciclo de vida completo (motor CPU, overlay, handle, TargetForm). No es seguro hacerlo al
final de una sesión. **Recomendación vigente: usar `BackdropType = Acrylic`** (mismo material,
0 % CPU, ya validado por el usuario; sin tocar).

## Tarea C3 — Backend del compositor para Blur (2.ª iteración, histórica)

**Estado: revertido.** Se implementó el diseño (propiedad `BlurBackend`, `ApplySystemBlur`,
`DisableSystemBlur`, sin mutar `BackdropType`) y **funcionó**: `System` = 0.00 s CPU (0 %), 63 MB,
0 frames de backdrop; `Cpu` = 8.8 %, 82.3 MB. Pero el `@revisor` lo rechazó con 11 puntos y,
por la regla de dos intentos, se revirtió.

### Bloqueos que debe resolver una implementación futura

1. **Cambio en caliente Cpu→System no apaga el motor CPU**: hay que `Stop()` + `SetBackdropSink(null)`
   + `ClearBlurBackdrop()` + restaurar `_opaqueControls` antes del `return`.
2. **Frames CPU encolados** (`BeginInvoke`) pueden reponer el `BackgroundImage` sobre el material:
   filtrar por backend/generación en `ApplyBlurBackdrop` y `OnBlurBackdrop`.
3. **`TransparentStyle`** muta a `None` sin `ApplyModernEffects`: el material y el frame extendido
   quedan aplicados. Debe llamar a `DisableSystemBlur()`.
4. **`Dispose`/cambio de `TargetForm`** dejan el material en el form viejo: limpiar sobre el HWND
   anterior antes de soltarlo y resetear la bandera en `UnsubscribeTargetForm`.
5. `DisableSystemBlur()` debe fijar también `DWMWA_SYSTEMBACKDROP_TYPE = NONE`.
6. Estado por HWND (no global) para saber a qué ventana pertenece el material.
7. `EnableAcrylic()` no es un fallback: se aplica siempre → doble tinte. Usar el HRESULT.
8. `BlurAmount`/`BackdropPerformanceMode` quedan inertes con `System`: documentarlo en **esas**
   propiedades o mapearlas.
9. **Sin evidencia visual**: los hijos son HWND opacos y pintan `BackColor` negro; no está probado
   que el material DWM se vea a través de ellos. Requiere `DrawToBitmap`/foto del usuario.
10. El harness de "blur live" hay que forzarlo a `BlurBackend=Cpu` y añadir un check de que
    `System` no genera frames CPU.

**Atajo que sigue disponible sin tocar código:** `BackdropType = Acrylic` en el diseñador (mismo
material, 0 % CPU, ya validado visualmente por el usuario).

## Tarea C2 — Intento anterior (revertido, histórico)

**Estado:** revertido; se conserva como registro.

- En `ApplyModernEffects`, si `BackdropType == Blur` y es Windows 11, se trata internamente
  como `Acrylic`: usa el material del compositor (`SetWindowCompositionAttribute`
  `ACCENT_ENABLE_ACRYLICBLURBEHIND`), el mismo backend que el usuario validó como "perfecto".
  El camino CPU queda para Windows 10 (o con `FWF_BLUR_CPH=1`… ver nota).
- **Medición (app real, diseñador en Blur, 5 s en reposo):**

| Backend | CPU | RAM | Frames de backdrop CPU |
|---|---|---|---|
| Compositor (Win11) | **0.00 s (0.0 % de un core)** | **55.9 MB** | **0** |
| CPU (`FWF_BLUR_CPU=1`) | 0.25 s (5.0 %) | 82.5 MB | 0 (quieto) |

  Sin frames de backdrop y con CPU 0, el pintado lo hace DWM al refresco del monitor: es
  exactamente el comportamiento del acrylic que el usuario pidió.
- **Trade-off:** el look cambia al material del sistema (grano/tinte propios de DWM). El usuario
  tendrá que validarlo; si prefiere el blur CPU, `FWF_BLUR_CPU=1` lo fuerza.
- **Pendiente:** validación visual del usuario y revisión de los agentes.

## Limitaciones conocidas del backdrop CPU (confirmadas por el usuario)

1. **Ventanas POR ENCIMA del form aparecen en el backdrop.** El backdrop CPU usa BitBlt de la
   pantalla; WDA excluye solo NUESTRA ventana, así que cualquier ventana que esté encima se
   captura y se ve como si estuviera detrás. Es una limitación del diseño (captura de pantalla),
   no un bug puntual. El compositor (Acrylic) sí respeta el z-order.
2. **El rosado/púrpura de las corridas del rompedor** era una **sonda del propio harness**
   (RunLiveBlurCheck): un form TopMost de color Magenta que tapa la ventana 1.2 s para
   comprobar que el backdrop se actualiza. Ya se cambió a un color neutro (24,24,32) y quedó
   comentado en el código.

### Revisión: RECHAZADO -> revertido (bloqueo documentado)

`@revisor` bloqueó el atajo de reescribir `_backdropType`:

1. Rompe el contrato público: `BackdropType` deja de devolver `Blur` tras el primer
   `ApplyModernEffects` (y el harness que exige `BackgroundImage` falla).
2. Al volver a `None` **no se limpia** el accent (`ACCENT_DISABLED` no existe en el repo) ni
   `DwmExtendFrameIntoClientArea`.
3. El branch Acrylic fuerza `DWMWA_USE_IMMERSIVE_DARK_MODE=0` → cambia el title bar a apps que
   usan `Blur`.
5. El flip no hace la limpieza del setter (motor CPU corriendo, `BackgroundImage` viejo).
6. `BlurAmount` y `BackdropPerformanceMode` quedan sin efecto en Blur/Win11.

**Conclusión:** el backend del compositor sí funciona (medido: 0 % CPU, 55.9 MB, 0 frames), pero
usarlo para `Blur` requiere un diseño aparte:

- `BackdropType` efectivo calculado en local (`_effectiveBackdropType`), sin mutar la propiedad.
- `ACCENT_DISABLED` + revertir `DwmExtendFrameIntoClientArea` al salir a `None`/`TransparentStyle`.
- No tocar `DWMWA_USE_IMMERSIVE_DARK_MODE` en el camino Blur.
- Mapear `BlurAmount` al material del sistema (no aplica) o documentar que se ignora.
- Limpieza del motor CPU al cambiar de camino.

**Atajo inmediato para el usuario (sin tocar código):** poner `BackdropType = Acrylic` en el
diseñador. Es el mismo backend que pidió, ya validado visualmente por él ("está perfecto") y con
0 % de CPU. El cambio está revertido; no quedó código a medias.

## Nota sobre el harness (flakiness conocida)

`blur live (mover)` compara el hash del `BackgroundImage` antes/después de mover la ventana. Si
el escritorio detrás es uniforme en ambas posiciones, el hash no cambia y falla sin que haya
regresión. El chequeo "quieto + ventana opaca encima" sí es determinista. Pendiente: mover a una
zona con contenido distinto o combinar ambos chequeos en un solo veredicto.

## Tarea 6 — Hit-test (análisis, sin tocar)

Divergencia detectada: `WM_NCHITTEST` calcula `drawnArea` con `CalculateMaxOverflow` usando
`ScaleX/ScaleY` **actuales**, mientras `UpdatePhysicalBounds` usa `1f` y calcula el máximo de
los estados. Además el hit-test no incluye la sombra (correcto: no debe ser clickeable).
**No se toca sin reproducir**; queda anotado.

### Hallazgo pendiente del rompedor (fuera del harness)

- `FluentWinForms/Form1.cs:126` suscribe `AnimationManager.FrameTicked` y **nunca** se daba de baja;
  cada Form1 dejaba un handler estático. También `Dump()` escribía en el Escritorio en cada
  construcción. **Corregido** (ver "Cambio C" abajo): delegado guardado y `-=` en `FormClosed`;
  `Dump` solo escribe con `FWF_DIAG_LOG=1`.

---

## Cambio C — Form1: fuga de handlers y logging en el constructor

- **Antes:** cada `new Form1()` añadía un handler permanente a `AnimationManager.FrameTicked`
  (evento estático) y escribía `dpi-log.txt` en el Escritorio.
- **Ahora:** el delegado se guarda en `_frameTickedHandler` y se da de baja en `FormClosed`;
  `Dump()` solo escribe si `FWF_DIAG_LOG=1`, dentro de try/catch.
- Motivo: el harness crea 500+ Form1 y el coste por frame/IO contaminaba las mediciones.
- Estado: build 0/0. Pendiente de `@revisor` + `@rompedor`.

## Harness v3 (ronda tras el 2.º rechazo)

- `Set()` ya no traga excepciones: lanza con el mensaje y el stack del setter.
- `AssertGrowth` falla si la medición es inválida (baseline/final ≤ 0) en vez de pasar en silencio.
- Pendiente: solo se evalúa con ≥6 muestras y ≥150 ciclos (evita falsos positivos).
- Blur LIVE: además de mover, se prueba con la ventana **quieta** y una ventana opaca encima
  (distingue "live" de "recapturar solo al mover"). Verificado OK.
- DPI: se verifica que el form siga vivo tras `HandleDpiChange`.
- El catch del blur imprime el stack completo.
- Doc del harness: el criterio es 50 % (no 25 %).

## Cambio D — Correcciones del rompedor (backdrop)

- `BlurAmount=1` volvía a no difuminar (umbral 2.5 px). Umbral bajado a 1 px: el modo Blur
  siempre difumina (`rPx=1` con el diseñador actual).
- `NotifyDpiChanged` ya no resetea `_bdInFlight` (podía permitir 4 frames sobre 2 slots).
- `ReleaseBackdropBuffer` identifica el slot por referencia (`_bdSlotInFlight`): un doble
  release ya no rompe el backpressure.
- Buffers de trabajo y handoffs **se reutilizan** y se encogen si sobran (>4×): el pico de un
  resize a 4000×3000 (139 MB) vuelve a 94 MB al terminar.
- Verificado: 200 ciclos del harness, 0 fallos, blur live (mover + quieto) OK,
  WS 139→94 (0.68x), Private 89→39, GDI/USER planos, pendiente −0.32 MB/ciclo.

### Problemas abiertos (registrados por el usuario)

1. Hit-test (`WM_NCHITTEST`, `ModernControlBase.Rendering.cs` ~895-924) no coincide con lo dibujado.
2. Sombra/glow se recortan en el borde del HWND expandido (`EngineOffset`).

### Decisiones pendientes del usuario (no tocar)

- ¿Se elimina código muerto (`GlassBackdropCache`, `GlassPainter`, `GlassTypes`)? **No** por ahora.
- Cualquier cambio de API pública de `FluentElement.Design(...)`.

---

## REGLAS INQUEBRANTABLES (recordatorio)

1. Prohibido eliminar/simplificar/reemplazar funciones existentes. Lo que sobre → aquí.
2. **El blur debe seguir siendo LIVE.** Nada de congelar el backdrop.
3. Leer el archivo COMPLETO antes de tocarlo.
4. Un cambio pequeño a la vez; nada de reescribir archivos.
5. No bajar calidad visual ni comportamiento para ganar rendimiento.
6. Mantener .NET 5+ y el target actual (`net8.0-windows10.0.19041.0`).
7. Pruebas SOLO en `StressTests/` (proyecto aparte).
8. Commit únicamente con `@revisor` **y** `@rompedor` en APROBADO. Un commit por cambio.
9. Si un cambio falla 2 veces: revertir ese cambio (solo mis archivos), anotar y seguir.

---

## Línea base — cambios pendientes de la sesión anterior (C + D)

### Comportamiento ANTES (HEAD `42fc8be`)

- Backdrop CPU a resolución reducida (1/2), `StackBlurUltra` + tinte +5%, emitido a tamaño real
  con `StretchBlt`, cadencia calibrada `clamp(paint*2, 50, 250)` ms.
- Overlay del borde sin doble escalado (commit anterior).
- Sin manejo de `WM_DPICHANGED`.
- RAM con Blur: ~218 MB. fps al arrastrar: 14–17.

### Comportamiento DESPUÉS (working tree, sin commit)

- `BackdropPerformanceMode` (nuevo, default `false`): normal = refresco del monitor (VREFRESH),
  rendimiento = cadencia calibrada.
- Blur sin Skia en el backdrop: 2 pasadas de `StackBlurUltra` (σ en DIPs) + tinte blanco A=40;
  sin blur si σ < 2.5 px (entrega nativa nítida).
- Doble buffer (`_bdHandoffs[2]`), sin `Invoke` al hilo UI desde el worker, backpressure con `form.Update()`.
- Manejo de DPI en caliente (`Form.DpiChanged` → `HandleDpiChange`, `NotifyDpiChanged`).
- Medido: RAM 88 MB, fps 29–31, `apply+paint` 19–22 ms.

### Verificación ya realizada

- Build: 0 errores, 0 warnings.
- RAM: 218 → 88 MB (medida con `Get-Process`).
- Backdrop vivo: dump `1116x512`; fps 29–31 en arrastre programático.
- `Form1.Designer.cs` intacto (hash del usuario).

### Riesgos conocidos de ese cambio

- El worker ya no hace `Invoke`; depende de `LocationChanged`/`SizeChanged` para los bounds.
- El camino Direct2D original sigue existiendo (solo se usa cuando el backdrop CPU está desactivado).
- Se eliminaron funciones **introducidas en la misma sesión** (no en HEAD): `BlurWithSkia`,
  `EmitFullSizeBackdrop`, `EnsureFullBackdrop`, `CleanupFullBackdrop`. No había API pública afectada.

---

## Tareas del usuario

- [ ] 0. Crear `StressTests/` con el harness completo (500+ ciclos, hover/click, resize, tema,
      valores extremos, doble Dispose; imprimir WorkingSet64/PrivateMemorySize64/GC.GetTotalMemory).
- [ ] 1. Auditoría SOLO LECTURA: leaks, nativos sin Dispose, allocations por frame.
- [ ] 2. Corregir hallazgos (cache con dirty-check, mutar en sitio, `ReleaseNativeResources`).
- [ ] 3. Ripple: `SKPath`/`SKPaint` por frame durante el click.
- [ ] 4. `RenderNode`: evaluar `IDisposable`.
- [ ] 5. Evaluar si el `GC.Collect()` periódico sigue siendo necesario (medir antes/después).
- [ ] 6. Bug hit-test (`WM_NCHITTEST`).
- [ ] 7. Bug sombra/glow recortados en `EngineOffset`.

---

## Auditoría SOLO LECTURA (Tarea 1) — sin cambios de código

Método: lectura de `ModernControlBase.Rendering.cs`, `.Core.cs`, `.Animation.cs`, `RenderNode.cs`
y búsqueda dirigida de allocations por frame y recursos nativos.

### Allocations por frame confirmadas (candidatas a la Tarea 2/3)

| # | Ubicación | Qué se crea por frame | Nota |
|---|---|---|---|
| A1 | `Rendering.cs:836-839` (`DrawBaseEngine`) | `SKPathBuilder` + `Detach()` → `SKPath` del relleno interior | Se puede cachear con dirty-check igual que `_sharedPath` (ya existe `_lastBasePathRect/_lastBasePathRadius`) |
| A2 | `Rendering.cs:296-298` (sweep) | `SKPathBuilder` + `SKPath` del clip redondeado | Solo cuando el nodo tiene sweep visible |
| A3 | `Rendering.cs:550-560` (ripple) | `SKPathBuilder` + `SKPath` + `SKPaint` | **Tarea 3 explícita**. El paint puede ser un campo reutilizado |
| A4 | `Rendering.cs:754-756` (hijos) | `SKPathBuilder` + `SKPath` del clip del nodo con hijos | Solo con `Corners.TopLeft > 0` |
| A5 | `Rendering.cs:320` | `SKRect` (struct) | No cuenta como allocation de heap |

### Riesgos / deuda (no tocar sin repro)

| # | Ubicación | Observación |
|---|---|---|
| R1 | `Rendering.cs:819` | `_sharedPaint.Shader?.Dispose()` podría disponer `_cachedBaseGradientShader` si una rama deja el shader asignado. Hoy las ramas lo anulan (866) pero es frágil. **No se toca sin reproducir.** |
| R2 | `Animation.cs:323-326` | `GC.Collect()` por control cada 600 frames globales (`AnimationManager.Frames`): con N controles animando se ejecutan N recolecciones en el mismo frame. **Tarea 5**: medir RAM antes/después antes de tocar. |
| R3 | `RenderNode` | Ya tiene caches por nodo y `ReleaseNativeResources()`; **no implementa `IDisposable`** formal (Tarea 4: evaluar, no obligatorio). |
| R4 | `ModernControlBase.Dispose` | Correcto: desuscribe tema, `DisposeAnimations`, `ClearCaches`, `ReleaseNativeResources` y dispone paint/font/path/shader/gdiWrapper/canvas/bitmap/clipPath. Sin fugas evidentes. |
| R5 | `RenderNode` caches | Sombra se hornea solo si `filterDirty`; gradientes/acrylic con dirty-check. Correcto. |

### Lo que NO se pudo verificar

- Fugas nativas reales (handle counts de Skia no visibles desde .NET). El harness mide Handles/Threads
  del proceso: estables en 500 ciclos.
- Multi-monitor / DPI distinto al 125% actual.
- El hit-test y el recorte de sombra/glow (Tareas 6 y 7) requieren inspección específica, aún no hecha.

---

## Cambio "atender revisión del backdrop" (working tree, sin commit)

El `@revisor` RECHAZÓ el draft anterior. Se corrigió:

| # | Hallazgo | Corrección |
|---|---|---|
| 1 | Label/nativos congelados (Bug A) | `form.Invalidate(true)` restaurado (todos los hijos) |
| 2 | Leak: lambdas de bounds + handoffs | Se desuscriben en `Dispose`; `CleanupBackdrop` limpia `_bdHandoffs` |
| 3 | `_bdInFlight` podía congelar el blur | Watchdog de 1 s + reset en `NotifyDpiChanged` |
| 4 | UI bloqueada por `ForceImmediateRender` en Resize/DPI | `RequestRender()` no bloqueante en Resize/ResizeEnd/DPI |
| 5 | `MakeControlsOpaque` con color viejo y permanente | Se ejecuta tras `UpdateFormBackground`; `RestoreControlsTransparency` al salir de Blur |
| 6 | Modo normal trabajaba con el escritorio quieto | Firma de la **captura** antes del blur → sin trabajo si no cambió |
| 7 | `ImageLayout.None` rompía forms con borde | Vuelto a `Stretch` |
| 8 | Churn de LOH | buffers fijos por slot (solo reasigna al cambiar tamaño) |
| 9 | Carreras benignas en bounds | `Volatile.Read` |
| 10 | Cambios de comportamiento | documentados en PROGRESS/AUDIT |
| 11 | Frames tras `Dispose` | guard `_disposed` en `ApplyBlurBackdrop` |
| 12-15 | menores | `rPx` en el log, `using` muerto eliminado; multi-monitor y floor anotados |

**Medición tras corregir:** idle 5 s → 0.38 s de CPU (7.5 % de un core), WS 83 MB;
arrastre live 28–29 fps; build Rebuild 0 errores / 0 warnings.

### Segunda ronda de correcciones (tras el 2.º rechazo)

| # | Hallazgo | Corrección |
|---|---|---|
| 1 | `BlurAmount` no se aplicaba con pantalla quieta (early-return de captura) | El setter y `RequestRender` resetean `_bdLastCaptureSignature` |
| 2 | La firma de captura se comprometía antes de emitir | Solo se asigna tras `sink(...)` exitoso |
| 3 | Watchdog no cubría "nunca hubo release" | Usa `_bdLastEnqueueTicks` (timestamp del encolado) |
| 4 | `RestoreControlsTransparency` imponía Transparent | Guarda el `BackColor` original por control y lo restaura |
| 5 | Comentario de backpressure incorrecto | Corregido (el límite es el doble buffer) |
| 6 | `_blurBackdropBitmap` podía quedar dispuesto si `new Bitmap` fallaba | Se crea el nuevo antes de disponer el viejo |

Pendiente: 3.ª revisión.

---

## Registro de cambios (un commit por cambio)

| # | Cambio | Revisor | Rompedor | Commit | RAM antes/después |
|---|---|---|---|---|---|
| 0 | `StressTests/` (proyecto aparte) | pendiente | pendiente | — | 500 ciclos: WS 71–105 MB, threads 16–22 estables, handles ~400 estables |

## Tarea 0 — StressTests (v2, tras rechazo del rompedor)

- Corregido el fallo crítico: ya no se hace clic en el `Label`; se seleccionan los
  `ModernControlBase` reales y se **verifica** `_isHoveringInternal` (si no se activa, falla).
- Añadido: aserciones de memoria (WS/Private/GDI/USER vs máximo estable, 1.5x + pendiente),
  `GC.Collect` antes de cada muestra, `GetGuiResources` (GDI/USER), escenario de masa (200
  `FluentElement`), cambio de DPI por reflexión, resize 1×1 y 4000×3000, Dispose sin `Close`
  cada 10 ciclos, manejadores globales de excepción y verificación de **blur LIVE** (hash del
  `BackgroundImage` antes/después de mover).
- Resultado 100 ciclos: **0 fallos**, blur live OK, WS 99→94, Private 40→35, GDI/USER 24/13
  estables, threads 21, handles 405.
- Pendiente: veredictos de `@revisor` y `@rompedor` sobre esta v2.

### Tarea 0 — StressTests (v1, histórico)

- Creado `StressTests/StressTests.csproj` + `StressTests/Program.cs` (no toca la solución).
- Escenarios: 500 ciclos crear/mostrar/cerrar/dispose (doble Dispose), hover+click x3 con
  `WM_MOUSEMOVE`/`WM_LBUTTONDOWN`/`WM_LBUTTONUP`, click interrumpido, resize/minimize/restore,
  cambio de tema (`AppTheme.ApplyTheme`), valores extremos vía reflexión sobre
  `modernFormManager1` (radio 9999, borde 100, blur 40, modo rendimiento, opacidad 0/1).
- Métricas impresas: WorkingSet64, PrivateMemorySize64, GC.GetTotalMemory, Threads, Handles.
- Resultado 500 ciclos: **0 fallos**, 66.4 s, WS 71–105 MB (final 71), Private 17–49 MB,
  threads 16–22 (sin crecimiento), handles 394–418 (estables).

---

## Dudas anotadas (no bloquean)

- El `GetDeviceCaps(VREFRESH)` devuelve el refresco del monitor principal; en multi-monitor con
  refrescos distintos se usa el primario. Anotado, no se toca.
- El modo "normal" puede saturar el hilo UI en equipos lentos; el modo rendimiento queda como escape.
