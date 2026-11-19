# FluentWinForms — Registro de avances

> Documento vivo del ciclo **implementar → probar → verificar**.
> `AUDIT.md` describe el problema y el plan. Este archivo describe **qué se hizo, qué evidencia hay y qué falta probar**.
> Si retomás en una sesión nueva: leé `AUDIT.md` primero y después este archivo desde el final.

| Metadato | Valor |
|---|---|
| Rama de trabajo | `feature/estabilizacion` |
| `main` | **intacta** (`ae1f469`, no se toca) |
| Última verificación | **2026-11-19** |
| Commits | A+B `42fc8be`, handler `d67f15c`, ripple `7854f7f`; docs `5dc14cb`. **2/4/5 sin commitear**; glow CSS revertido. Sin push. |
| Último build | **0 errores, 0 warnings** |
| Entorno detectado | `DeviceDpi=120` (125%), `PerMonitorV2`, pantalla 1536×864 |

> **Estado detallado y decisiones nuevas:** ver `NOTES.md` → "ESTADO ACTUAL (2026-11-19)".
> Resumen: tareas 2/4/5 aprobadas y listas para commit; glow CSS en la capa del padre
> **revertido** por artefactos GDI+/Skia; decisión **Skia-only**; backdrop CPU aceptado.

---

## Protocolo de trabajo

1. Implemento un bug en `feature/estabilizacion`.
2. Compilo y genero evidencia verificable (build, volcados, logs, métricas).
3. Te digo **exactamente cómo probarlo**.
4. Vos lo probás y me contás qué ves.
5. Marco el bug como `VERIFIED` o corrijo. Nunca lo doy por arreglado sin tu prueba.
6. Actualizo este documento.

Estados: `OPEN` · `IMPLEMENTED` (código listo, sin probar) · `FIXED-UNVERIFIED` (probado por mí a nivel técnico, falta tu prueba visual) · `VERIFIED` · `REOPENED`.

---

## Bug A — Halos negros alrededor de los botones con `Blur`

**Estado: `VERIFIED` (prueba visual del usuario, 2026-11-18).**
**ID:** `FWF-COMP-001`

> Reporte del usuario: *"funciona ahora sí, lo lograste… el blur se ajusta al del backdrop y el label lo soportó (ojo, ese label es de GDI)"*.

### Causa raíz (confirmada)

El blur se dibuja en el HWND del formulario. Cada `FluentElement` es un HWND hijo **opaco**: no puede ver esa superficie. WinForms emula la transparencia haciendo que el hijo repinte el fondo del padre, y ese fondo era el `BackColor` negro. Resultado: el rectángulo completo del control (incluido el margen de sombra/glow) quedaba negro.

No era un problema del blur ni de `WM_ERASEBKGND`: era el **modelo de transparencia de WinForms**.

### Cambio aplicado (Opción 1 — un solo escritor)

El formulario expone el blur como su `BackgroundImage`. Como `InvokePaintBackground(Parent, ...)` ya propaga el `BackgroundImage` del padre a **todos** los hijos (no solo botones), los halos de cualquier control muestran el backdrop real sin tocar la API pública ni `ModernElement.Design(...)`.

| Archivo | Qué se agregó |
|---|---|
| `Core/Direct2DBlurHelper.cs` | Productor de backdrop en CPU: `StretchBlt` a resolución reducida con `HALFTONE`, `StackBlurUltra.BlurInPlace`, tinte blanco ~5%, `EmitFullSizeBackdrop` (sube a tamaño real con `StretchBlt` HALFTONE), firma de frame para saltar frames idénticos, cadencia adaptativa (`BackdropIntervalMs`). API: `SetBackdropSink`, `ReleaseBackdropBuffer`. |
| `Core/Direct2DBlurHelper.cs` | **Un solo escritor:** si `_backdropEnabled`, `RenderFrame` **no dibuja en el HWND** (rama `return`). Se eliminó la doble superficie de vidrio. |
| `Core/ModernFormManager.cs` | `OnBlurBackdrop` / `ApplyBlurBackdrop` / `ClearBlurBackdrop`, `CalibrateBackdropInterval`, `AdaptNativeControlsToBackdrop` (controles GDI heredan `Color.Transparent`), `InvalidateChildren`. |
| `Core/ModernFormManager.cs` | Limpieza del backdrop al cambiar `BackdropType` y al desuscribir/disponer el form. |

Detalles de diseño:

- **Resolución adaptativa** a ~640 px de ancho (`d = max(2, ceil(w/640))`): para un blur la pérdida de detalle es imperceptible y el coste de CPU queda acotado.
- **Mapeo de intensidad:** `r ≈ sigma·√3·(dpi/96)/d`. Así `BlurAmount=20` → `sigma=20, boxRadius=22` en el buffer reducido, y el resultado en pantalla coincide con el del motor D2D.
- **`StretchBlt` + `HALFTONE`** para bajar y subir: promedia bloques (exactamente lo que un blur necesita) sin bucles en C#.
- **`ArrayPool`**: sin allocations por frame.
- **Emisión a tamaño real:** el `BackgroundImage` final mide exactamente lo que el form. WinForms lo pinta con blit 1:1.

### Evidencia generada

**Build:**
```
Build succeeded.
0 Error(s)
1 Warning(s)  -> CS0414 _isResizing (preexistente)
```

**Cadena completa verificada** (`FWF_DUMP_BACKDROP=1`):
```
dump 558x256 sigma=20 boxRadius=22
BackgroundImage asignado 1116x512 form=1116x512 dpi=120
  calibracion: paint=32ms -> intervalo backdrop=64ms
```

**Composición volcada** (`FWF_DUMP_FORM=1` → `%TEMP%\fwf-form.png`): fondo continuo difuminado alrededor de los botones, sin halos negros; el `Label` nativo de GDI aparece con `back=Color [Transparent](0)`.

### Por qué la cadencia no es 120 fps

Medición del coste real de repintado con la ventana completa:

| Escenario | Coste |
|---|---|
| Repintar form + hijos (`Invalidate(true); Update()`) | **32–43 ms** |
| Emitir backdrop a tamaño real | ~2.3 ms |
| Emitir backdrop escalando con HQ bicubic (descartado) | ~6.6 ms |

El límite no es el blur: es el repintado Skia completo de cada control en cada `OnPaint`
(`ModernControlBase.Rendering.cs`). Por eso la cadencia se **calibra**:
`intervalo = clamp(paint*2, 50, 250) ms` → 64 ms con este hardware. Es la política
"no pedir más frames de los que el hilo UI puede sostener".

### Limitaciones conocidas

- El `BackgroundImage` del form pasa a estar gestionado por el manager. Si tu form usa uno propio, se pisaría.
- El halo se actualiza a la cadencia calibrada (hoy ~64 ms). Es intencional.
- El coste de repintado es el próximo cuello de botella (ver Bug C).

---

## Bug B — Overlay del borde más grande que el form (artefacto al mover)

**Estado: `FIXED-UNVERIFIED` — falta tu prueba visual.**
**ID:** `FWF-DPI-001`

### Síntoma reportado

*"Algo raro que apareció de la nada… se sale del Windows, al menos del form; aparece cuando la muevo de posición."*

### Causa raíz (medida, no hipótesis)

`ModernFormOverlay.RefreshLayer` volvía a multiplicar por el DPI un bitmap que **ya estaba en píxeles físicos**:

```
overlay: bitmap=1116x512 scale=1.25 phys=1395x640 owner=(96,96,1116,512)
```

`_skBorderBitmap` se construye con `_targetForm.Width/Height` (físicos). Al multiplicarlo por 1.25 se generaba una ventana layered de **1395×640** anclada en la esquina del form: **279 px por la derecha y 128 px por abajo fuera del form**. Al mover la ventana, ese sobrante se ve como un borde/rectángulo que se sale.

### Cambio aplicado

- `physW/physH = w/h` (sin `* scale`): el bitmap ya está en píxeles físicos.
- Copia 1:1 por stride (sin el caso especial de escala, que ya no existe).
- La ventana del overlay pasa a medir exactamente lo mismo que el form en cada refresco.

### Evidencia

**Antes** (arrastrando la ventana 40 pasos):
```
FORM  rect=(160,160)-(1276,672) size=1116x512
WIN   rect=(160,160)-(1491,736) size=1395x640   <- overlay, 279x128 px fuera
```

**Después:**
```
FORM  rect=(490,156)-(1606,668) size=1116x512
WIN   rect=(490,156)-(1606,668) size=1116x512   <- overlay idéntico al form
  overlay: bitmap=1116x512 scale=1 phys=1116x512 owner=(160,160,1116,512)
```

Nota metodológica: hay que medir con el proceso de PowerShell en **DPI-aware**
(`SetProcessDPIAware`), si no Windows virtualiza las coordenadas y el overlay
parece más chico de lo que es.

### Cómo probarlo

Con `BorderThickness > 0` y `UseSkia = true` (si no, el overlay es transparente y no se ve nada):

1. Abrí la app y mové la ventana rápido por toda la pantalla.
2. Mirá el borde derecho e inferior: **no debe asomar nada fuera del form**.
3. Dejá la ventana quieta y comprobá que el borde coincide con el contorno.

---

## Bug C — Latencia del blur al mover

**Estado: `FIXED-UNVERIFIED` — falta tu prueba visual.**
**ID:** `FWF-BLUR-001`

### Síntoma

Al arrastrar la ventana, el backdrop va visiblemente por detrás.

### Diagnóstico (medido, no estimado)

Instrumenté el repintado por partes. Primera sorpresa: **el caché de Skia no era el problema**:

| Medición | Valor |
|---|---|
| Pintar un `FluentElement` | **0.70 ms** (0.42 ms fondo del padre + 0.28 ms Skia) |
| Form (`TargetForm_Paint_Hybrid`) | 0.9–1.2 ms |
| `form.Invalidate(true); form.Update()` | 27–43 ms |

Con 6–7 controles, nuestro código de pintura suma ~5 ms. Cachear el `SKBitmap` ahorraría ~2 ms: no valía la pena el riesgo.

**La causa real:** al registrar los rectángulos de clip descubrí que **el form se pinta 8 veces por frame**, una por cada hijo con `BackColor = Transparent` (WinForms repinta el padre debajo de cada hijo "transparente"). Cada repintado vuelve a procesar el `BackgroundImage` de 1116×512 completo (~2.5 ms) → 8 × 2.5 ≈ 20 ms tirados.

```
paint clip={X=0,Y=0,Width=1116,Height=512}
paint clip={X=300,Y=10,Width=259,Height=20}   <- Label
paint clip={X=25,Y=25,Width=210,Height=78}    <- btn1
paint clip={X=39,Y=107,Width=182,Height=50}   <- btn2
... (8 en total)
```

Y es trabajo **duplicado**: `ModernControlBase.OnPaint` ya compone el fondo del padre en el buffer del propio hijo. Nada necesita que el padre pinte otra vez debajo.

### Cambio aplicado

`MakeControlsOpaque`: los `FluentElement` pasan a `BackColor` opaco (= color del form) cuando hay Blur. Se **excluyen** `ModernGlassPanel` y `ModernThemeToggle`, que sí dependen de `BackColor.Transparent` para su propia composición (`ModernThemeToggle.cs:535`).

### Evidencia

| Métrica | Antes | Después |
|---|---|---|
| Paints del form por frame | **8** | **2** (form + Label nativo) |
| Coste del repintado | 27–43 ms | **15 ms** |
| Backdrop durante arrastre | 4.8–6.9 fps | **14.8–16.5 fps** |
| Intervalo calibrado | 54–86 ms | **50 ms** (suelo) |

Log real:
```
calibracion: paint=15ms -> intervalo backdrop=50ms
19:37:11.774 backdrop fps=16.5
19:37:12.814 backdrop fps=16.2
19:37:13.829 backdrop fps=14.8
19:37:14.826 backdrop fps=16.0
```

**Comprobación visual** (dump de composición con hijos opacos):

| Zona | Luminancia media |
|---|---|
| backdrop sobre btn1 | 42.4 |
| halo superior btn1 | 46.6 |
| backdrop entre botones | 52.0 |
| halo superior btn2 | 61.1 |
| backdrop lateral | 61.4 |

Los halos siguen mostrando el backdrop (nada de negro) y el interior del botón sólido es **pixel-idéntico** (MAD = 0) entre ambas configuraciones.

### Experimentos descartados (medidos, para no repetirlos)

| Prueba | Resultado |
|---|---|
| `BackgroundImageLayout = None` en vez de `Stretch` | 24 ms vs 25 ms → sin ganancia real |
| `BackgroundImageLayout = Tile` | 34 ms → peor |
| `DoubleBuffered = false` | 41 ms → mucho peor |
| Caché de `SKBitmap` por control | ahorraría ~2 ms → no justifica el riesgo |

### Iteración 2 (feedback del usuario: "parece que va a 12 fps", "se ve como tomando fotos", "muy borroso incluso con 1")

**Problema 1 — no se siente fluido.** No era (solo) la frecuencia: durante un arrastre seguíamos
produciendo y aplicando backdrop en cada mensaje de movimiento. `FormMessageHook` llamaba a
`ForceImmediateRender()` **sincrónicamente** en cada `WM_WINDOWPOSCHANGED`, y cada frame costaba
~15 ms de hilo UI. El resultado es exactamente lo que describiste: una sucesión de fotos.

**Fix:** mientras `_isResizing` (entre `WM_ENTERSIZEMOVE` y `WM_EXITSIZEMOVE`):

- `FormMessageHook` ya no fuerza renders.
- `OnBlurBackdrop` / `ApplyBlurBackdrop` descartan los frames pendientes.
- La ventana se mueve como bitmap (sin repintados) → movimiento fluido a la tasa del compositor.
- Al soltar (`ResizeEnd`) se fuerza `ForceImmediateRender()` + `Invalidate(true)` y el blur se
  recoloca en la posición final.

**Evidencia:**

```
arrastre simulado (WM_ENTERSIZEMOVE + 60 pasos):  SIN líneas de fps  -> 0 frames aplicados
tras soltar:                                      1 frame aplicado y luego 0.3 fps (estático)
```

**Problema 2 — se veía borroso incluso con `BlurAmount = 1`.** El pipeline bajaba **siempre** a
media resolución (`d = 2`): 1116×512 → 558×256 → blur → volver a 1116×512 con HALFTONE. Ese
reescalado ya suaviza unos 4 px, aunque el radio fuese 1.

**Fix:** con intensidades bajas y ventanas moderadas se trabaja a **resolución nativa**
(`fineBlur = amount <= 3 && w <= 1600 → d = 1`). Para intensidades altas se mantiene el camino
rápido a media resolución.

**Evidencia:**

```
dump 1116x512 sigma=1 boxRadius=2      <- antes: 558x256 sigma=1 boxRadius=1
calibracion: paint=15ms -> intervalo backdrop=50ms
backdrop fps=16.0 / 15.5 / 15.3        <- el blur nativo sostiene la cadencia
```

Energía de borde del backdrop (más = más nítido):

| Backdrop | Resolución | Energía de borde |
|---|---|---|
| `BlurAmount=1` (nuevo, nativo) | 1116×512 | **3.425** |
| `BlurAmount=5` (media res) | 558×256 | 1.283 |
| `BlurAmount=20` (media res) | 558×256 | 0.415 |

Ahora `1` es realmente sutil y el control tiene rango visible.

### Iteración 3 (feedback: "no es live, solo una screen; el blur se ve feo, mirá el ModernGlassPanel")

**Se revirtió el congelado.** Durar el arrastre sin repintar da fluidez, pero el resultado
es una imagen quieta y eso se siente peor que 15 fps reales. Ahora el backdrop vuelve a
actualizarse en vivo durante el movimiento.

**El blur se reescribió copiando la receta de `ModernGlassPanel`** (`GenerateStaticCache`),
que es la referencia visual que pidió el usuario:

| Paso | Implementación |
|---|---|
| Resolución | **Nativa** (1116×512), sin downscale/upscale |
| Algoritmo | `SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp)` |
| Padding | `ceil(sigma) + 2` px por lado en un bitmap aparte |
| Recorte | Del bitmap difuminado al tamaño real |
| Tinte | Blanco `A=40` (`GlassTint` por defecto del panel) |

Se eliminó el camino de `StackBlur` a media resolución y el `StretchBlt` de subida (ya no
hacen falta: el blur sale a tamaño real). `BlurAmount` ahora es **sigma en DIPs**, igual que
en el panel.

**Evidencia:**

```
dump 1116x512 sigma=10 sigmaPx=12
BackgroundImage asignado 1116x512 form=1116x512 dpi=120
calibracion: paint=30ms -> intervalo backdrop=60ms
backdrop fps=14.3 / 15.5 / 15.3 / 15.8 / 16.0     <- live otra vez
```

Esquinas y bordes del backdrop: `A255` en las cuatro esquinas (el padding + Clamp evitó el
borde oscuro/transparente):

```
(0,0)     = A255 R45 G46 B50
(1115,0)  = A255 R45 G45 B50
(0,511)   = A255 R45 G45 B50
(1115,511)= A255 R56 G56 B60
```

### Cómo probarlo

Con `BackdropType = Blur` (tu diseñador ya está así), arrastrá la ventana y compará la textura
del fondo con la del `ModernGlassPanel` del proyecto.

**Qué mirar:**

1. ¿El fondo se ve como el vidrio del panel (lechoso, suave, con tinte)?
2. ¿Vuelve a estar "live" al mover (no una foto quieta)?
3. ¿Los halos de los botones siguen mostrando el blur (no negro)?
4. `BlurAmount=1` vs `20`: ¿se nota el rango?

### Iteración 4 — modo de refresco + fix de memoria

**1. Propiedad nueva `BackdropPerformanceMode`** (`Modern Form - Blur`, por defecto `false`):

| Modo | Comportamiento | Uso |
|---|---|---|
| `false` (normal) | Sigue el refresco del monitor (`GetDeviceCaps VREFRESH`, acá 120 Hz) | Movimiento fluido |
| `true` (rendimiento) | Cadencia calibrada con el coste real de repintado (~15-20 fps) | Menos CPU/batería |

**2. Bug de memoria (encontrado por el usuario).** La RAM subía a ~200 MB. Causas medidas:

- `SKImage.FromBitmap` **copia** los píxeles y se llamaba 3 veces por frame (Skia retenía ~140 MB).
- `ArrayPool<byte>.Shared` **no** reutiliza buffers de 2.3 MB (supera el máximo del pool) → LOH nuevo por frame.

**Fix:** el blur del backdrop dejó de usar Skia (2 pasadas de `StackBlurUltra` = gaussiana
equivalente + tinte blanco 40) y se usan dos buffers fijos en vez de `ArrayPool`.

```
antes:  WorkingSet 218 MB / Private 172 MB
ahora:  WorkingSet  88 MB / Private  39 MB
```

**3. Pipeline menos serializado:**

- El worker ya no hace `_form.Invoke` al hilo UI en cada iteración (los bounds se cachean por
  `LocationChanged`/`SizeChanged`), que lo bloqueaba detrás de cada repintado.
- Doble buffer (`_bdHandoffs[2]`) + liberación del slot **después de copiar y antes de pintar**:
  permite preparar el siguiente frame mientras la UI pinta el actual.
- `form.Update()` como backpressure: el frame no se libera sin haberse pintado.

**4. Rendimiento medido (Debug, 120 Hz, `BlurAmount=10`):**

| Métrica | Antes (Skia) | Ahora |
|---|---|---|
| RAM | 218 MB | **88 MB** |
| Blur por frame | 3.5 ms | 5.6 ms |
| Repintado (apply+paint) | 19–26 ms | 19–22 ms |
| Backdrop fps (live) | 14–17 | **29–31** |

**5. Blur fino sin coste:** si `sigma < 2.5 px` no se difumina: se entrega la captura nativa
con el tinte. Así `BlurAmount=1` es nítido y gratis.

**Compromiso:** el techo real lo pone el repintado de WinForms (~20 ms): 40–50 fps como máximo
con esta arquitectura. Para 60 fps estables haría falta composición en una sola superficie
(Fase 6).

### Limitaciones

- El blur gaussiano nativo cuesta más que el de media resolución, pero corre en su propio hilo: el backdrop sostiene ~15 fps y no bloquea la UI.
- Sigue habiendo ~15 ms por frame de repintado: la mayor parte es pintar el `BackgroundImage` y componer los HWND hijos. Bajar de ahí requiere composición en una sola superficie (Fase 6).
- El `Label` nativo sigue transparente (no puede pintar el fondo por sí mismo): 1 repintado extra del form, barato.
- `MakeControlsOpaque` corre al aplicar los efectos: controles creados después no reciben la optimización.

---

## Bug D — Cambio de DPI en caliente

**Estado: `FIXED-UNVERIFIED` — falta probar en un monitor/escala distinta.**
**ID:** `FWF-DPI-001`

### Qué faltaba

El borde, el overlay y el backdrop están todos en **píxeles físicos**, pero nadie escuchaba
`WM_DPICHANGED`. Al cambiar la escala podían quedar cachés con la escala vieja (grosor del
borde, radio escalado, bitmaps del overlay, recursos Direct2D) y la calibración de cadencia.

### Cambio aplicado

- `ModernFormManager` se suscribe a `Form.DpiChanged` y lo trata en `HandleDpiChange`:
  invalida el caché del borde (`_lastWidth/_lastHeight/_lastRadius/_lastScale`), marca el
  overlay para reenviar bitmap, rehace la calibración de cadencia y re-aplica los efectos.
- `PaintSkiaSmoothing` incluye la **escala** en la clave del caché (`_lastScale`), así que un
  cambio de DPI reconstruye el borde aunque el tamaño en píxeles coincida.
- `Direct2DBlurHelper.NotifyDpiChanged()` marca los recursos nativos (DIB, backdrop reducido,
  render target y bitmaps D2D) para liberarlos y recrearlos en el próximo frame.
- `ModernFormOverlay` resincroniza bounds con el owner en `DpiChanged` (no se autoescala:
  `AutoScaleMode.None`).

### Evidencia (cambio simulado con `FWF_FAKE_DPI`)

```
pre  : overlay 1116x512 at (160,160) | form 1116x512 at (160,160)
post : overlay 1116x512 at (160,160) | form 1116x512 at (160,160)
dpi: 120 -> 144 form=1116x512
calibracion: paint=17ms -> intervalo backdrop=50ms   (recalibrado tras el cambio)
vivo: True                                            (sin crash)
```

### Cómo probarlo de verdad

1. Con la app abierta, mové la ventana a un monitor con otra escala (100% ↔ 125% ↔ 150%).
2. O cambiá la escala en Windows (`Configuración → Pantalla → Escala`) con la app abierta.
3. Mirá: borde alineado, sin franjas ni píxeles basura, blur recolocado, sin crash.

---

## Sensibilidad al `BlurAmount` (5 vs 20) — medido

**ID:** `FWF-BLUR-002`

Medición sobre los dos backdumps reales (`%TEMP%\fwf-amt5.png` / `fwf-amt20.png`, 558×256):

| Métrica | Valor |
|---|---|
| Diferencia media absoluta | 6.93 / 255 |
| RMS | 12.79 |
| Píxeles con diferencia > 8 niveles | 32.4 % |
| Energía de borde con `r=5` | 1.283 |
| Energía de borde con `r=20` | 0.415 |
| Reducción de detalle (20 vs 5) | **−67.6 %** |

**Conclusión:** el parámetro **sí funciona** (4.4× más radio, 67.6% menos detalle). Lo que
pasa es que el escritorio de fondo es casi plano: la energía de borde del backdrop es
**1.28 sobre 255** (0.5%). Con tan poco detalle, a partir de `r=5` ya no queda nada que
borrar, así que 5 y 20 se ven casi iguales.

**Para verlo de verdad:** poné una foto/ventana con texto o imágenes detrás del form.
Sobre un fondo liso, cualquier blur se ve igual.

Si aun así querés más rango percibido, la opción es un mapeo perceptual
(`r ∝ amount^0.7`), pero no añade información: solo hace que los valores bajos sean
más nítidos.

---

## Pruebas pendientes del usuario

| # | Bug | Qué probar | Estado |
|---|---|---|---|
| 1 | A | Halos de botones + label GDI con Blur | ✅ `VERIFIED` |
| 2 | B | Mover la ventana con `BorderThickness > 0`: nada debe salir del form | ✅ `VERIFIED` ("ya no se asoma nada") |
| 3 | C | Arrastrar: ¿live, fluido y con el aspecto del `ModernGlassPanel`? | ⏳ pendiente (iteración 4) |
| 4 | C | `BackdropPerformanceMode`: normal (fluido) vs rendimiento (menos CPU) | ⏳ pendiente |
| 5 | C | RAM estable (<100 MB) tras mover un rato | ✅ medido (88 MB) |
| 5 | D | Cambiar escala/monitor con la app abierta: borde y blur alineados | ⏳ pendiente |

### Tanda única de pruebas (C iteración 2 + D)

```powershell
$env:FWF_BACKDROP="Blur"; $env:FWF_BLUR_AMOUNT="1"; $env:FWF_BORDER_THICKNESS="2"; $env:FWF_USE_SKIA="1"
dotnet run --project FluentWinForms\FluentWinForms.csproj
# 1) arrastrar: fluidez + halos  2) subir BlurAmount por código/variable y comparar
# 3) mover a otro monitor o cambiar la escala de Windows
Remove-Item Env:\FWF_BACKDROP, Env:\FWF_BLUR_AMOUNT, Env:\FWF_BORDER_THICKNESS, Env:\FWF_USE_SKIA
```

---

## Registro de pruebas del usuario

| Fecha | Bug | Resultado | Notas |
|---|---|---|---|
| 2026-11-18 | A | ✅ PASS | "el blur se ajusta al backdrop y el label lo soportó" |
| 2026-11-18 | B | ✅ PASS | "al mover ya no se asoma nada, está correcto" |
| 2026-11-18 | C | ⚠️ parcial | sin parpadeo y halos OK, pero "parece que va a 12 fps", "se ve como tomando fotos", "muy borroso incluso con 1" → iteración 2 |
| 2026-11-18 | C | ⚠️ parcial | "ni siquiera es live, solo una screen y empeoró"; "el blur se ve feo, mirá el ModernGlassPanel" → iteración 3 (live + receta del panel) |
| 2026-11-18 | C | ⚠️ parcial | "parece un monitor de captura"; pide modo rendimiento + modo normal vsync; reporta RAM a ~200 MB → iteración 4 (modos + fix de memoria) |

---

## Diagnósticos disponibles

| Variable de entorno | Efecto |
|---|---|
| `FWF_DUMP_BACKDROP=1` | Vuelca el primer backdrop a `%TEMP%\fwf-backdrop.png`, registra asignación/calibración/fps en `%TEMP%\fwf-backdrop-log.txt`. |
| `FWF_DUMP_FORM=1` | Vuelca la composición completa del form a `%TEMP%\fwf-form.png` vía `DrawToBitmap`. |
| `FWF_BLUR_AMOUNT=<n>` | Fuerza la intensidad del blur en runtime (1–40) sin recompilar. |
| `FWF_BACKDROP=Blur\|Acrylic\|None` | Fuerza el tipo de backdrop sin editar el diseñador. |
| `FWF_BORDER_THICKNESS=<n>` | Fuerza el grosor del borde (necesario para ver el overlay). |
| `FWF_USE_SKIA=1\|0` | Fuerza el motor Skia del borde. |
| `FWF_FAKE_DPI=<n>` | Simula un cambio de DPI a los 2.5 s (prueba la invalidación de cachés). |

**Receta para probar B y C sin tocar `Form1.Designer.cs`:**

```powershell
$env:FWF_BACKDROP="Blur"; $env:FWF_BORDER_THICKNESS="2"; $env:FWF_USE_SKIA="1"
$env:FWF_DUMP_BACKDROP="1"; $env:FWF_BLUR_AMOUNT="20"
dotnet run --project FluentWinForms\FluentWinForms.csproj
# al terminar:
Remove-Item Env:\FWF_BACKDROP, Env:\FWF_BORDER_THICKNESS, Env:\FWF_USE_SKIA, Env:\FWF_DUMP_BACKDROP, Env:\FWF_BLUR_AMOUNT
```

Todos son solo diagnóstico y no cambian el comportamiento si la variable no está activa.

---

## Rollback

```powershell
# Bug A + B (todo lo commiteado)
git checkout -- FluentWinForms/Core/Direct2DBlurHelper.cs FluentWinForms/Core/ModernFormManager.cs

# Documentos
git checkout -- AUDIT.md PROGRESS.md
```

`FluentWinForms/Form1.Designer.cs` **nunca** se incluye: es tu archivo de trabajo.
