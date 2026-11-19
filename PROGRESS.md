# FluentWinForms — Registro de avances

> Documento vivo del ciclo **implementar → probar → verificar**.
> `AUDIT.md` describe el problema y el plan. Este archivo describe **qué se hizo, qué evidencia hay y qué falta probar**.
> Si retomás en una sesión nueva: leé `AUDIT.md` primero y después este archivo desde el final.

| Metadato | Valor |
|---|---|
| Rama de trabajo | `feature/estabilizacion` |
| `main` | **intacta** (`ae1f469`, no se toca) |
| Commits | 1 commit local autorizado por el usuario (fix A + B). Sin push. |
| Último build | 0 errores, 1 warning preexistente (`_isResizing`) |
| Entorno detectado | `DeviceDpi=120` (125%), `PerMonitorV2`, pantalla 1536×864 |

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

**Estado: `OPEN` (medido; causa identificada; fix pendiente).**
**ID:** `FWF-BLUR-001`

### Medición real (no estimación)

Arrastre programático a 16 ms/paso con `SetWindowPos`:

```
backdrop fps=6.9
backdrop fps=4.8
calibracion: paint=32ms -> intervalo backdrop=64ms
```

El backdrop se produce a ~5–7 fps durante el arrastre porque **cada actualización invalida
y repinta toda la ventana** (32–43 ms de coste). El blur en sí cuesta ~2.3 ms; el 90% del
frame es el repintado Skia de los controles.

### Próximo paso propuesto (Fase 4 de AUDIT)

- Cachear el `SKBitmap` de cada control y repintarlo solo cuando cambie su estado
  (hoy `ModernControlBase.OnPaint` re-renderiza todo el árbol Skia en cada paint).
- Mientras el form se está moviendo, no invalidar hijos en cada frame de backdrop:
  bastará con repintar el form si los hijos se mueven con la ventana.
- Es un cambio de rendimiento con riesgo visual: se hace **después** de validar B.

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
| 2 | B | Mover la ventana con `BorderThickness > 0`: nada debe salir del form | ⏳ pendiente |
| 3 | A/C | Mover la ventana: ¿sigue el lag? ¿aceptable? | ⏳ pendiente |
| 4 | — | `BlurAmount` 5 vs 20 sobre un fondo con detalle | ⏳ pendiente |

---

## Registro de pruebas del usuario

| Fecha | Bug | Resultado | Notas |
|---|---|---|---|
| 2026-11-18 | A | ✅ PASS | "el blur se ajusta al backdrop y el label lo soportó" |
| 2026-11-18 | B | reportado | "se sale del form cuando la muevo" → causa medida y corregida |
| 2026-11-18 | C | reportado | "se ve lag" → medido en 5–7 fps de backdrop |

---

## Diagnósticos disponibles

| Variable de entorno | Efecto |
|---|---|
| `FWF_DUMP_BACKDROP=1` | Vuelca el primer backdrop a `%TEMP%\fwf-backdrop.png`, registra asignación/calibración/fps en `%TEMP%\fwf-backdrop-log.txt`. |
| `FWF_DUMP_FORM=1` | Vuelca la composición completa del form a `%TEMP%\fwf-form.png` vía `DrawToBitmap`. |
| `FWF_BLUR_AMOUNT=<n>` | Fuerza la intensidad del blur en runtime (1–40) sin recompilar. |

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
