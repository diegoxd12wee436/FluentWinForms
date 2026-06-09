<div align="center">
  

# 🚀 FluentWinForms 
<img width="1536" height="427" alt="FluentWinForms" src="https://github.com/user-attachments/assets/b104a692-a03f-4cce-ae65-fa92d87637c9" />

*Real Blur · Glassmorphism · Smooth Animations · Pure C# · No XAML · No MVVM*


![Status](https://img.shields.io/badge/Status-En%20Desarrollo%20(WIP)-orange)
![License](https://img.shields.io/badge/License-MIT-green)

*[Read the English version below](#-english-version)*

*[The engine running (Demo)](#-Demo-test)*
</div>

## 📖 La historia detrás de este motor

¡Hola! Soy un estudiante de 19 años. Desde donde yo lo veo, casi todos mis compañeros de clase ya miran WinForms como algo "viejo". Creen que para hacer interfaces bonitas y modernas (como las de WinUI) si o si tienen que aprender XAML y enredarse con MVVM. Al final, se frustran tanto que tiran la toalla y dejan C# botado, No todos lo ven con buena percepción; lo consideran algo viejo, no moderno

**Por eso me hice este motor:** para que podamos seguir codificando en C# puro, usando una API súper directa, fácil de leer (como si fuera CSS), y al mismo tiempo tener efectos bestiales (glass, blur de verdad, sombras dinámicas) controlando el renderizado al máximo con **SkiaSharp** y **GDI+**.

> **Nota:** Este proyecto no está en contra de WinUI ni WPF. Son frameworks poderosos, pero mi objetivo es ofrecer un camino alternativo para estudiantes y desarrolladores que quieran diseño moderno sin dejar WinForms.

> ¡Este es un proyecto para la comunidad, hecho por la comunidad! Necesito de su ayuda para llevar FluentWinForms al siguiente nivel. ¿Encontraste un bug? ¿Tienes una idea? ¡Tu contribución es bienvenida y profundamente apreciada :')

---

## ✨ Lo más salvaje del motor (Características)

* 🪟 **Glassmorphism Real:** Nada de colores transparentes; captura el fondo real y le aplica blur dinámico con SkiaSharp.
* 🏎️ **Motor de Animación (120 FPS):** Corre en un hilo aparte (`RenderThread`) con precisión de 1ms. Tu app nunca se va a trabar.
* 🧠 **Rendimiento Inteligente (Zero-Alloc):** Reusa memoria a lo bestia para mantener el consumo de RAM por el suelo y evitar que el Garbage Collector te congele la app.
* 🎨 **100% Personalizable:** ¡Cualquiera puede crear sus propios diseños, controles y estilos desde cero fácilmente!
* 🖱️ **¡Soporte Drag & Drop!** ¿Te cuadra más el diseñador visual? No hay falla. El motor está preparado para que arrastrés los controles directamente desde la caja de herramientas de Visual Studio.
* 🎯 **DPI-Aware: Nitidez perfecta en cualquier escala de pantalla (monitores 4K o escalado Windows).
* 📦 **Layout Moderno:** Usá `AutoFitGrid`, `VerticalStack` y `HorizontalStack` bien responsivos.
* 🔄 **Para Todos:** Corre al cien desde **.NET Framework 4.8** hasta **.NET 10** *(en desarrollo aún, ya que no todas las librerías de Skia son estables en las versiones más nuevas)*.

---

## 🛠️ El Concepto: Fluent API (aun en progreso pero ya casi finalizado)

La idea es cambiar por completo cómo hacemos UI en WinForms. En vez de pelear con propiedades sueltas, encadenás métodos y armás controles complejos rapidísimo. 

**M y ordenadito que queda el código para armar un Botón de Login moderno y animado:**

```csharp
// 🎨 Ejemplo de la Fluent API: Un botón de Login bien clean y animado
var btn = new FluentElement { Name = "btnGuardar" };
btn.Design()
   .Layout(20, 20, 160, 44)
   .Content("Guardar", "#FFF", 14)
   .Glass("#40FFFFFF")              
   .BorderRadius(12)
   .Hover(scale: 1.1f)
   
   //demo it works but i want to make it super devfriendly
   .Apply(this);  
```

---

## 🌌 FluentWinUiVerse: Comunidad Abierta
Inspirado en páginas brutales como *uiverse.io*, este proyecto va a tener **FluentWinUiVerse**: un catálogo de componentes en GitHub Pages donde cualquiera de la comunidad va a poder subir sus controles personalizados y estilos creados con este motor. ¡La idea es aprender y armar cosas tuani entre todos!

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

## Demo test

## 🎥 DEMO el motor corriendo en WinForms 
> **20 controles Skia + formulario Acrílico ejecutándose al mismo tiempo: 17–20 MB de RAM estables.**

>  **20 Skia controls + Acrylic Form running simultaneously: 17–20 MB stable RAM.**



https://github.com/user-attachments/assets/0e66c22d-1ced-4cdd-9716-a40d83583374









---

<div align="center">
  <br>
  <b>¿Te gusta la idea? ¡Dejale caer una ⭐ en GitHub!</b>
  <br>
  <b>Le doy gracias a Dios 💫 por permitirme hacer este proyecto.Se que sera de su agrado y ayudara a muchos de la comunidad WinForms  :') </b>
</div>

<br><br>

---

<div align="center">

## English Version

</div>

## 📖 The story behind this engine

Hi! I'm a 19-year-old student. From my perspective, most of my classmates already see WinForms as something "old" and outdated. They think that to move to WinUI or create modern interfaces they have to learn XAML and the whole complex world of MVVM. That frustrates them so much that many end up dropping C# entirely. Not everyone has a positive perception of it; many consider it old rather than modern.

**That's why I built this engine:** so we can stick to pure C#, using a super concise, easy-to-read CSS-like API, while still getting modern effects (glassmorphism, real blur, dynamic shadows) and full rendering control thanks to **SkiaSharp** and **GDI+**.

> **Disclaimer:** This project is not against WinUI or WPF. They are powerful frameworks, but my goal here is to provide an alternative path for students and developers who want modern design while staying in WinForms.

## 🤝 Join me and help improve it!

> **Note:**"This is a project for the community, made by the community! I need your help to take FluentWinForms to the next level. Did you find a bug? Do you have an idea? Your contribution is welcome and deeply appreciated :')

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
// 🎨 Fluent API usage example: A clean and animated Button
var btn = new FluentElement { Name = "btnSave" };

btn.Design()
   .Layout(20, 20, 160, 44)       // Position (x,y) and size (width,height)
   .Content("Save", "#FFF", 14)   // Text, color, and font size
   .Glass("#40FFFFFF")            // Translucent glass effect
   .BorderRadius(12)              // Rounded corners
   .Hover(scale: 1.1f)            // Hover animation (slight zoom)
   .Apply(this);                  // Apply to the current form

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




<div align="center">
  <br>
  <b>Love the idea? Support us with a ⭐ on GitHub!</b>
  
  <b>I thank God 💫 for allowing me to do this project. I hope it pleases Him and helps the comunity of WinForms :') </b>
</div>
