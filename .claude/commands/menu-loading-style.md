---
description: Extiende el estilo y la geometria de la barra de carga (marco biselado, gemas, franjas, particulas por evento) a todos los menus previos al juego.
argument-hint: "[opcional: elemento concreto, p.ej. 'sliders' o 'LoadPanel']"
---

Objetivo: llevar el estilo y la geometría de la nueva BARRA DE CARGA a todos los menús previos al juego de Valkur, para que el menú principal, sus submenús y la pantalla de carga parezcan una sola pieza.

Si se pasó un argumento ($ARGUMENTS), limita el trabajo a ese elemento; si no, cubre todos.

## 1. Lee primero la referencia (es la fuente de verdad del estilo)
La barra está en el árbol de trabajo (puede no estar commiteada; léela del disco):
- `unity/Valkur/Assets/_Project/Scripts/UI/Loading/LoadingBarFX.cs`: capas, partículas, eventos, destellos
- `UI/Loading/LoadingBarFrameGraphic.cs`: marco (sombra, contorno, bisel dorado, canal hundido, escuadras, gemas)
- `UI/Loading/LoadingBarSegmentsGraphic.cs`: divisores, muescas en rombo, zona objetivo con respiración, franjas de flujo
- `UI/Loading/LoadingBarMesh.cs`: primitivas (Quad, QuadV degradado vertical, Parallelogram, Diamond)
- `CLAUDE.md`, sección "The arranque", los puntos sobre la barra

Y el menú actual:
- `UI/MainMenu/` (`MainMenuUI.*.cs`: Shell, UIBuilder, UIBuilder.Panels, Options.*, LoadPanel.*, ClassSelector, PressToStart)
- `UI/MainMenu/Kit/` (MenuUIKit, MenuPanelView, MenuRow, MenuList, MenuSlider), `MenuArt.cs`, `MenuFxLayer.cs`, `MenuTypography.cs`, `MenuSfx.cs`
- Data: `MenuStyle` (`MenuStyle.Active`: Gold, GoldDim, Panel, TextPrimary…)
- `.github/FRONTEND_MENUS_AUDIT_2026-09-12.md` y `.github/HUD_VISUAL_LANGUAGE.md`

Empieza con `graphify explain "MenuUIKit"` y `graphify explain "MenuPanelView"` para ver quién construye qué.

## 2. El lenguaje visual que hay que extender
- Rectangular y construido con GEOMETRÍA con color por vértice (MaskableGraphic + primitivas de LoadingBarMesh), no con sprites estirados ni 9-slice.
- Marco: sombra suave en dos capas desplazada hacia abajo, contorno casi negro (0.035,0.024,0.016), bisel con degradado vertical de bronce oscuro (0.36,0.22,0.09) abajo a oro claro (0.98,0.82,0.46) arriba, línea de luz de 1 px en el borde superior, canal interior hundido (degradado casi negro), labio inferior con un 12 % de luz cálida.
- Ornamento: escuadras en L fuera de las esquinas; gemas en rombo (contorno, oro, núcleo que se "enciende" hacia el tinte y con brillo); muescas en rombo sobre divisores.
- Luz: capas aditivas (shader `Valkur/UI/HudFx` con `_SrcBlend=SrcAlpha`, `_DstBlend=One`) y una textura radial PROPIA con alfa exactamente 0 en el borde. El TitleMote del menú deja un borde cuadrado si se amplía.
- Movimiento: franjas de luz inclinadas, destello (sheen) que recorre, "respiración" con sinusoides a ritmos distintos.
- Partículas (MenuFxLayer, UseSoftMotes, aditivas): SOLO responden a EVENTOS o MOVIMIENTO, nunca en reposo. Chispas cálidas con gravedad, brasas que suben, estallidos breves, destellos agrupados en pool. No a colores medio blancos: en aditivo saturan a puntos blancos.
- Colores del tinte leídos del estilo (`MenuStyle.Gold`), para que ámbar y rojo de aviso o error cambien todo el elemento.
- Tick propio con dt sub-escalonado (`unscaledDeltaTime`) y material, textura y sprite propios destruidos en Dispose (Destroy en Play, DestroyImmediate en Edit).

Reutiliza: extrae lo común (marco biselado, gema, muesca, textura radial, material aditivo) a un kit compartido en vez de copiarlo. Si lo mueves, la barra de carga debe usar ese kit también, sin cambiar su aspecto.

## 3. Qué elementos llevarlo
Audita y lista todos los elementos visibles antes de tocar nada. Propuesta de correspondencias (ajústala tras mirar):
- Paneles o ventanas (MenuPanelView): marco biselado con escuadras; cabecera con gemas en los extremos.
- Filas o botones de lista (MenuRow/MenuList): canal hundido; la fila seleccionada se rellena como la barra (degradado + sheen + borde brillante) con una ráfaga de chispas al CAMBIAR la selección; confirmar = estallido + destello.
- Sliders (MenuSlider, Opciones > Vídeo/Audio): exactamente la barra; muescas en rombo como pasos; chispas al arrastrar proporcionales a la velocidad.
- Toggles o checks: gema que se enciende.
- Panel de cargar partida (LoadPanel): cada ranura como marco biselado; divisores con muescas.
- Selector de clase (ClassSelector): tarjeta con marco y gemas; la elegida con destello.
- "Pulsa para empezar" (PressToStart): línea tipo barra con sheen y respiración.
- Divisores, pills y key caps del MenuArt: sustituirlos por la geometría equivalente.
- Menú de pausa (`UI/PauseMenu`) solo si comparte kit; pregúntame antes de ampliar el alcance.

No toques el título ni el logo (`UI/MainMenu/Title`), ni el fuego del dragón de la pantalla de carga.

## 4. Reglas del proyecto que no puedes saltarte
- `CLAUDE.md` es obligatorio: lee "Cardinal rules", "Input pipeline" y las trampas de uGUI (sin layout en EditMode, Image+TMP en el mismo GameObject, ColorBlock vs Graphic, orden de hermanos para scrims, cadenas de UI en ASCII para los textos sensibles).
- Entrada solo vía MouseInputManager, KeyboardInputManager, InputCompat e InputService; nunca `Mouse.current`, `Keyboard.current` ni `UnityEngine.Input`.
- Nada de ParticleSystem en canvas overlay (no ordena entre Graphics): usa MenuFxLayer.
- Sin `new Color(` sueltos en editores (ratchet); en el menú, colores del MenuStyle o constantes nombradas.
- Estado estático: reset con `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` (Domain Reload está OFF).
- Una clase por fichero; partials por aspecto; ~250 líneas.
- No escribas assets compartidos desde tests (`MenuStyle.Active` incluido).

## 5. Verificación
- Tras cada lote de C#: `refresh_unity` (compile=request, mode=force, scope=scripts) y `read_console` (error, warning). Consola limpia. Confirma que tu código está cargado (reflexión + fecha de las DLL de `Library/ScriptAssemblies` frente a tu `.cs`), porque otra assembly en rojo congela el dominio.
- Tests: todos los de `Tests/EditMode/UI/MainMenu/` (y `Kit/`, `Title/`) con "Menu", "MainMenu" y "Title" en el nombre (MainMenuUITests, MenuListTests, MenuContrastTests, MenuPanelFitTests, MenuParticleTests, MenuStyleAndArtTests, MenuColumnLayoutTests…) más LoadingScreenControllerTests. Si un test falla, investígalo; no lo etiquetes como preexistente sin demostrarlo.
- Visual: hay OTRAS sesiones de Claude trabajando en este repo y usando Play Mode. Antes de crear objetos de prueba, comprueba que no hay tests corriendo (`TestJobDataHolder.TestRuns` vacío). Para ver el resultado sin Play Mode, construye el elemento en un Canvas ScreenSpaceCamera con una cámara a RenderTexture 1600x800 (capa 5), haz Tick manual varios frames, `cam.Render()`, guarda PNG en tu scratchpad y míralo. Así se validó la barra, y así se detectó el borde cuadrado del resplandor. Captura estados en reposo, selección/hover, confirmación y slider arrastrando.
- Contraste: el texto sobre los nuevos rellenos debe seguir pasando MenuContrastTests.

## 6. Entrega
- Primero un plan corto: inventario de elementos, correspondencias y qué pasa al kit compartido. Espera mi OK.
- Luego implementa por lotes (kit, paneles, filas, sliders, resto), verificando cada uno.
- Al final: capturas antes/después, tests ejecutados con resultado, y un bloque para la sección del menú en `CLAUDE.md` con las decisiones no obvias.
- No hagas commit sin que te lo pida; si lo pido, directamente en main, revisando antes `git status` por cambios de otras sesiones.
