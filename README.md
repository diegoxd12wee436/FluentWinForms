<div align="center">
  

# 🚀 FluentWinForms 
**Diseñá como Windows 11. Ligero en RAM como Windows 7.**   

**Design like Windows 11. Light on RAM like Windows 7.**


*Real Blur · Glassmorphism · Smooth Animations · Pure C# · No XAML · No MVVM*

![.NET Version](https://img.shields.io/badge/.NET-4.8%20%7C%206.0%20%7C%208.0%20%7C%209.0%20%7C%2010.0-blue)
![Status](https://img.shields.io/badge/Status-En%20Desarrollo%20(WIP)-orange)
![License](https://img.shields.io/badge/License-MIT-green)

*[Read the English version below](#-english-version)*

*[The engine running (Demo)](#-Demo-test)*
</div>

## 📖 La historia detrás de este motor

¡Hola! Soy un estudiante de 19 años. Desde donde yo lo veo, casi todos mis compañeros de clase ya miran WinForms como algo "viejo". Creen que para hacer interfaces bonitas y modernas (como las de WinUI) si o si tienen que aprender XAML y enredarse con MVVM. Al final, se frustran tanto que tiran la toalla y dejan C# botado.

**Por eso me hice este motor:** para que podamos seguir codificando en C# puro, usando una API súper directa, fácil de leer (como si fuera CSS), y al mismo tiempo tener efectos bestiales (glass, blur de verdad, sombras dinámicas) controlando el renderizado al máximo con **SkiaSharp** y **GDI+**.

> **Nota:** Necesito ayuda de la comunidad para avanzar, yo solo no puedo y no tengo tanta experiencia. ¡Se agradece cualquier ayuda de corazón! :')

---

## ✨ Lo más salvaje del motor (Características)

* 🪟 **Glassmorphism Real:** Nada de colores transparentes; captura el fondo real y le aplica blur dinámico con SkiaSharp.
* 🏎️ **Motor de Animación (120 FPS):** Corre en un hilo aparte (`RenderThread`) con precisión de 1ms. Tu app nunca se va a trabar.
* 🧠 **Rendimiento Inteligente (Zero-Alloc):** Reusa memoria a lo bestia para mantener el consumo de RAM por el suelo y evitar que el Garbage Collector te congele la app.
* 🎨 **100% Personalizable:** ¡Cualquiera puede crear sus propios diseños, controles y estilos desde cero fácilmente!
* 🖱️ **¡Soporte Drag & Drop!** ¿Te cuadra más el diseñador visual? No hay falla. El motor está preparado para que arrastrés los controles directamente desde la caja de herramientas de Visual Studio.
* 🎯 **DPI-Aware: Nitidez perfecta en cualquier escala de pantalla (monitores 4K o escalado Windows).
* 📦 **Layout Moderno:** Adiós al `FlowLayoutPanel`. Usá `AutoFitGrid`, `VerticalStack` y `HorizontalStack` bien responsivos.
* 🔄 **Para Todos:** Corre al cien desde **.NET Framework 4.8** hasta **.NET 10** *(en desarrollo aún, ya que no todas las librerías de Skia son estables en las versiones más nuevas)*.

---

## 🛠️ El Concepto: Fluent API (aun en progreso pero ya casi finalizado)

La idea es cambiar por completo cómo hacemos UI en WinForms. En vez de pelear con propiedades sueltas, encadenás métodos y armás controles complejos rapidísimo. 

**Mirá lo macanudo y ordenadito que queda el código para armar un Botón de Login moderno y animado:**

```csharp
// 🎨 Ejemplo de la Fluent API: Un botón de Login bien clean y animado
var btnLogin = new FluentElement("LoginButton")
    .Layout(x: 50, y: 50, width: 200, height: 45)
    .CornerRadius(10)
    .Background("#0078D7")
    .Content("Iniciar Sesión", hexColor: "#FFFFFF", fontSize: 14)
    .Shadow(elevation: 3)
    .StateHover(h => h
        .Background("#005A9E")
        .Scale(1.05f) // Crece un 5% suavecito al pasar el mouse
    )
    .StatePress(p => p
        .Background("#004275")
        .Scale(0.95f) // Se encoge un toque al darle clic
    )
    .OnClick(node => MessageBox.Show("¡Sesión iniciada al cien!"))
    .Apply(this); // Lo zampa directo en tu Formulario de un solo
```

---

## 🌌 FluentWinUiVerse: Comunidad Abierta
Inspirado en páginas brutales como *uiverse.io*, este proyecto va a tener **FluentWinUiVerse**: un catálogo de componentes en GitHub Pages donde cualquier maje de la comunidad va a poder subir sus controles personalizados y estilos creados con este motor. ¡La idea es aprender y armar cosas tuani entre todos!

---

## 🗺️ Roadmap (Lo que viene)

- [x] `ModernGlassPanel` — Glassmorphism con blur de verdad.
- [x] `ModernThemeToggle` — 10 estilos de toggle con animación.
- [x] `AnimationManager vMax` — Motor de animación súper preciso.
- [x] `BitmapPool` — Gestión de memoria gráfica que no gasta de más.
- [x] `LayoutEngine` — AutoFitGrid, VerticalStack, HorizontalStack.
- [ ] `ModernButton` — Botones Fluent con efecto ripple.
- [ ] `ModernCard` — Cards que se elevan y tienen sombra.
- [ ] `ModernSlider` — Slider con estilo WinUI.
- [ ] `ModernNotification` — Toast notifications nativas.
- [ ] `ModernDialog` — Diálogos modales como los de Windows 11.
- [ ] Lanzamiento de **FluentWinUiVerse** (La galería de la comunidad).
- [ ] Tirar el paquete a NuGet público.
      
## Cosas hechas aun faltan pullir

### **🪟 ModernFormManager — Bordes redondeados + Acrílico**

✅ Bordes suaves (sin dientes) con ModernFormOverlay + UpdateLayeredWindow.

✅ Acrílico nativo en Windows 11 (DWMWA_SYSTEMBACKDROP_TYPE).

✅ Fallback de desenfoque manual en Windows 10, 8 y 7 (AcrylicHelper + StackBlurUltra).

✅ Radio de borde configurable (0–50px).

✅ Arrastre del formulario sin bordes (EnableDrag).
> Los 10 toggles puesto con sus animacioon en modo easi-in-out, ModernFormManager haciendo el acrylic

>**Nota:** en acrylic mode los controles winforms nativos no renderizan bien porque es el limite del GDI+

### **🌫️ Glassmorphism — Cristal ahumado real**

✅ ModernGlassPanel funcional con blur dinámico sobre el fondo real.

✅ Tinte y gradiente semitransparente (GlassEffect).

✅ Bordes con brillo de cristal (Border("#40FFFFFF", 1f)).

### **🎛️ ModernThemeToggle — 10 estilos de Toggle**

✅ Animaciones suaves con Spring y Easing.

✅ Modo Weather_Legacy con sol, luna, nubes y estrellas.

✅ Sombras dinámicas con SKImageFilter.CreateDropShadow.

✅ Integración completa con la Fluent API.

>⚠️ Todo esto es funcional, pero aún estoy puliendo el código y optimizando. Si encontrás algún bug o tenés ideas para mejorar, ¡abrime un Issue o mandame un PR!

# Demo test
## 🎥 DEMO el motor corriendo en WinForms 
> **20 controles Skia + formulario Acrílico ejecutándose al mismo tiempo: 17–20 MB de RAM estables.**

>  **20 Skia controls + Acrylic Form running simultaneously: 17–20 MB stable RAM.**



https://github.com/user-attachments/assets/0e66c22d-1ced-4cdd-9716-a40d83583374









---

## 🤝 ¡Sumate al proyecto! (Ocupo ayuda)
Como te dije, soy estudiante y no me las sé todas. Este proyecto pinta para algo grande, pero el núcleo de la **Fluent API todavía está en pañales** y ocupamos pulirlo . 

Si te gusta C#, sos apasionado de la programación y te llega esta visión de que WinForms tenga una capa visual moderna solo con C#, sin XAML ni MVVM, con una API declarativa que cualquiera pueda usar.... Se aceptan Forks, Pull Requests y cualquier consejo. El proyecto es licencia MIT. ¡Hagamos que esto sea grande!

<div align="center">
  <br>
  <b>¿Te gusta la idea? ¡Dejale caer una ⭐ en GitHub!</b>
</div>

<br><br>

---

<div align="center">

#  English Version

</div>

## 📖 The story behind this engine

Hi! I'm a 19-year-old student. From my perspective, most of my classmates already see WinForms as something "old" and outdated. They think that to move to WinUI or create modern interfaces they have to learn XAML and the whole complex world of MVVM. That frustrates them so much that many end up dropping C# entirely.

**That's why I built this engine:** so we can stick to pure C#, using a super concise, easy-to-read CSS-like API, while still getting modern effects (glassmorphism, real blur, dynamic shadows) and full rendering control thanks to **SkiaSharp** and **GDI+**.

> **Note:** I need help from the community to move forward. I can't do it alone and I don't have that much experience yet. Any help is deeply appreciated! :')

---

## ✨ Key Features

* 🪟 **Real Glassmorphism:** True background capturing coupled with real-time dynamic blur powered by SkiaSharp.
* 🏎️ **Animation Engine (120 FPS):** High-precision (1ms via `winmm.dll`) isolated rendering loop (`RenderThread`) that keeps your app stutter-free.
* 🧠 **Smart Performance (Zero-Alloc):** Relies heavily on memory pooling to maintain an ultra-low RAM footprint and completely bypass Garbage Collector spikes.
* 🎨 **Fully Customizable:** Anyone can easily create their own custom designs, controls, and styles from scratch!
* 🖱️ **Drag & Drop Ready:** Prefer the visual designer? No problem! The engine is built so you can seamlessly drag and drop controls straight from the Visual Studio Toolbox.
* 🎯 DPI-Aware: Automatically scales for any Windows display setting (125%, 150%, 200%). Result: Pixel-perfect clarity on high-DPI and 4K monitors.
* 📦 **Modern Layouts:** Native responsive layout primitives including `AutoFitGrid`, `VerticalStack`, and `HorizontalStack`.
* 🔄 **Wide Compatibility:** Runs perfectly from **.NET Framework 4.8** all the way up to **.NET 10** *(currently in development, as not all Skia libraries are stable on the newest versions yet)*.

---

## 🛠️ The Concept: Fluent API (still working on it but almost done)

We are building a revolutionary way to write UI in WinForms. The idea is that you can chain methods together to build complex controls without dealing with boring property configurations.

**Look how clean and structured the code is to create a beautifully animated Login Button:**

```csharp
// 🎨 Fluent API usage example: A clean and animated Login Button
var btnLogin = new FluentElement("LoginButton")
    .Layout(x: 50, y: 50, width: 200, height: 45)
    .CornerRadius(10)
    .Background("#0078D7")
    .Content("Sign In", hexColor: "#FFFFFF", fontSize: 14)
    .Shadow(elevation: 3)
    .StateHover(h => h
        .Background("#005A9E")
        .Scale(1.05f) // Grows smoothly by 5% on hover
    )
    .StatePress(p => p
        .Background("#004275")
        .Scale(0.95f) // Shrinks subtly on click
    )
    .OnClick(node => MessageBox.Show("Logged in successfully!"))
    .Apply(this); // Injects it directly into your Form
```

---

## 🌌 FluentWinUiVerse: Open Community
Inspired by amazing community hubs like *uiverse.io*, this repository will soon feature **FluentWinUiVerse**: an open component catalog hosted on GitHub Pages. Here, any developer can humbly share and explore custom controls and styles crafted with this engine. Let's learn and build together!

---

## 🗺️ Roadmap

- [x] `ModernGlassPanel` — Glassmorphism with real blur.
- [x] `ModernThemeToggle` — 10 animated toggle styles.
- [x] `AnimationManager vMax` — High-precision animation engine.
- [x] `BitmapPool` — Zero-alloc graphical memory management.
- [x] `LayoutEngine` — AutoFitGrid, VerticalStack, HorizontalStack.
- [ ] `ModernButton` — Fluent buttons featuring ripple and visual states.
- [ ] `ModernCard` — Cards with elevation and dynamic shadows.
- [ ] `ModernSlider` — Animated custom slider styling.
- [ ] `ModernNotification` — Native toast notifications.
- [ ] `ModernDialog` — Windows 11 style modal dialogs.
- [ ] **FluentWinUiVerse** Launch (Community showcase via GitHub Pages).
- [ ] Public NuGet package deployment.

## Things done (still need polishing)

### 🪟 ModernFormManager — Rounded corners + Acrylic

✅ Smooth borders (no jagged edges) with ModernFormOverlay + UpdateLayeredWindow.

✅ Native Acrylic on Windows 11 (DWMWA_SYSTEMBACKDROP_TYPE).

✅ Manual blur fallback on Windows 10, 8 and 7 (AcrylicHelper + StackBlurUltra).

✅ Configurable border radius (0–50px).

✅ Borderless form dragging (EnableDrag).

> The 10 toggles placed with their ease-in-out animations, ModernFormManager doing the acrylic.

> **Note:** in acrylic mode, native WinForms controls don't render well because that's the GDI+ limit.

### 🌫️ Glassmorphism — Real frosted glass

✅ Functional ModernGlassPanel with dynamic blur over the real background.

✅ Semi-transparent tint and gradient (GlassEffect).

✅ Crystal shine borders (Border("#40FFFFFF", 1f)).

### 🎛️ ModernThemeToggle — 10 Toggle styles

✅ Smooth animations with Spring and Easing.

✅ Weather_Legacy mode with sun, moon, clouds and stars.

✅ Dynamic shadows with SKImageFilter.CreateDropShadow.

✅ Full Fluent API integration.

---

## 🤝 Join me and help improve it!
As I told you, I'm a student and I don't know everything. This project has huge potential, but the core of the Fluent API is still in its early stages and we need to polish it.

If you love C#, you're passionate about programming, and you share this vision of giving WinForms a modern visual layer using only C# — no XAML, no MVVM, with a declarative API that anyone can use... then I need you! Forks, Pull Requests, and any advice are completely welcome. The project is MIT licensed. Let's make this grow!

<div align="center">
  <br>
  <b>Love the idea? Support us with a ⭐ on GitHub!</b>
</div>
