# Auditoría de belleza de la barra de hechizos (SpellBarHUD)

**Fecha:** 2026-09-11 · **Nota global: 1.9 / 10** · Objetivo: **≥ 8.5** ·
**Tras la reconstrucción del mismo día: 8.2 / 10** (sección 9)

Alcance: la barra inferior-central de 2x12 casillas que construye `SpellBarHUD`
(`Gameplay/HUD/SpellBarHUD.cs`, `.UIBuilder.cs`, `.DragDrop.cs`) y su drop zone
`DropZoneSpellSlot`. Se citan por coherencia el panel del jugador (`PlayerHUD`, reconstruido hoy
a 8.9), el minimapa, las barras del mundo y la pila de cooldowns de arriba a la izquierda
(`SpellCooldownHUD`).

Método: lectura completa del código, la captura adjunta, una captura propia a 1600x800 con
`ScreenCapture.CaptureScreenshot` y medidas en vivo en Play Mode por `execute_code` (rects en
píxeles de pantalla, contenido de cada casilla, bindings reales de las 24 acciones de hechizo).
Cada nota va con su evidencia, no con gusto.

El lenguaje visual común que la sección 5 da por supuesto está en
[`HUD_VISUAL_LANGUAGE.md`](HUD_VISUAL_LANGUAGE.md).

---

## 1. Resumen

La barra no tiene un problema de belleza. Tiene un problema de **verdad**, y la belleza viene
después. Las 24 teclas que imprime son falsas, la casilla de la bola de fuego muestra una copia
sintética sin icono, 8 de sus 12 casillas de arriba no pueden contener nada, las 4 que sí pueden
no las castea ninguna tecla, y un clic en cualquier casilla dispara hechizos de daño en la
postura de Paz.

Por encima de eso está vestida como un editor. Hereda `TileEditorTheme.Border` y el `ACCENT` de
`TileEditorUIHelpers`, así que a 150 px del panel de piedra y oro del jugador aparece un
rectángulo gris translúcido con texto TMP de 9 pt. En la captura se leen como dos juegos
distintos.

---

## 2. Puntuación por eje

| # | Eje | Nota | Evidencia |
| --- | --- | --- | --- |
| 1 | Verdad de las teclas impresas | 0 | 24 de 24 son falsas. Arriba pone `1`..`=`: la tecla `1` castea `darkball` (medido: `<Keyboard>/1 -> darkball`), no la casilla 0. Abajo pone `S+1`..`S+=`: ninguna de las 24 acciones de hechizo está en Shift (bindings reales: `1`-`0`, `q z r t f g c v x o l u m b`). `HotkeyForIndex` es una tabla fija de 12 columnas que no lee ningún binding |
| 2 | Verdad del contenido | 1 | La casilla 0 contiene `ProjectilePrefabFactory.GetFireballSpell()`, una `SpellDefinition` creada con `CreateInstance` (id -136301632, sin nombre, sin icono). El asset `fireball` que castea el clic izquierdo SÍ tiene icono, por eso el panel del jugador lo dibuja y la barra no |
| 3 | Casillas que pueden contener algo | 1 | `SpellCaster.SlotCount` = 4 y `CanAcceptSpellDrop` rechaza el índice ≥ 4, así que las casillas `5`..`=` de arriba no aceptan nada nunca. Y las 4 que sí aceptan no las castea ninguna tecla: `grep "\.TryCast("` da dos llamadores, `NPCAutoCast` y la propia barra. Arrastrar un hechizo a la fila de arriba no cambia ninguna tecla |
| 4 | Lectura de cooldown | 2 | La fila de arriba lee `GetCooldownRemaining(slot)`, el reloj de SLOT, y el clic izquierdo castea por `TryCastByKey`, que pone en marcha el reloj del LIBRO. La cuña de la bola de fuego no se mueve nunca. Es el mismo defecto que el panel del jugador corrigió hoy. Además refresca a 20 Hz (escalones visibles), escribe decimales bajo 1 s y la cuña no tiene filo |
| 5 | Coherencia con el HUD | 1 | Colores de `TileEditorTheme` y `TileEditorUIHelpers.ACCENT`, `using Valkur.Gameplay.TileEditor`. Fondo gris translúcido, outline de 1 px y TMP, al lado de un panel de piedra con bisel, oro y fuente bitmap |
| 6 | Materialidad del marco | 1.5 | `Image` sin sprite a `(0.05,0.05,0.07,0.55)` y un componente `Outline` de 1 px. Se ve el adoquinado a través. En espacio lineal un 45 % de hueco deja pasar bastante más que un 45 % percibido (misma medida que el tooltip del panel) |
| 7 | Rejilla de píxel | 1.5 | Pivote central sobre un ancho impar de 553 px: cada borde cae en medio píxel (medido: raíz en x = 523.5, casilla 11 en x = 1024.5..1064.5). A 1920x1080 el escalado del canvas es 1.27, no entero. El panel del jugador resolvió las dos cosas con su espacio de texel |
| 8 | Iconografía | 4 | El arte pintado es bueno. Pero sale de atlas de 2048 px bilineales sin mipmaps, con rects de 384 y de 1024 px, minificados a 36 px por el sampler: el `slash` y el `laser_beam` pierden líneas y titilan al moverse. El icono del láser es una línea fina perdida en la casilla. El panel del jugador los hornea al tamaño exacto con `HudTextureBaker.Icon` |
| 9 | Tipografía | 2 | TMP LiberationSans en negrita a 9 pt con outline 0.2. `S+1` ocupa ~40 % del ancho de la casilla, encima del icono. Mezcla de fuentes: el panel de al lado usa la fuente bitmap 3x5 / 5x7 |
| 10 | Jerarquía | 1.5 | 24 casillas idénticas. Nada dice "esto es lo que más usas". Los tres hechizos de ratón aparecen dos veces, aquí y en el panel del jugador |
| 11 | Densidad / vacío | 1.5 | El personaje conoce 5 hechizos y la barra tiene 24 casillas: 19 vacías (79 %). Con la progresión los hechizos se ganan, así que la barra está más vacía justo al principio, cuando más se mira |
| 12 | Composición | 3 | Abajo al centro es el sitio correcto. Pero las flechas del paginador caen FUERA del panel (medido: la de arriba en y = 121..139 con el panel acabando en 109, la de abajo en y = 2..20 con el panel empezando en 14) y su comentario dice "purely cosmetic for now". El botón de minimizar (18x18) tapa la casilla `=` 8 px en X y 14 px en Y. Otra altura (95 px) y otra línea base que el panel del jugador (~125 px) |
| 13 | Estados de casilla | 1.5 | Solo vacía y enfriando. Sin listo, sin bloqueado, sin falta de maná, sin pulsado, sin rechazado. `HudAbilitySlot` ya tiene los cinco |
| 14 | Feedback de evento | 0.5 | Nada al castear, nada al volver el cooldown, nada al rechazar por maná. El único evento visual es el resaltado amarillo al soltar un arrastre |
| 15 | Partículas | 0 | Ninguna. El panel del jugador tiene `HudMoteLayer` (un solo Graphic, pool, aditivo, en rejilla de texel) y es reutilizable tal cual |
| 16 | Movimiento | 1 | Mostrar y ocultar es un salto de alfa 0 a 1. La cuña avanza a saltos de 50 ms. Nada tiene easing |
| 17 | Interacción | 3.5 | Clic para castear, arrastrar para reordenar, soltar desde el editor de hechizos y arrastrar la ventana funcionan. Sin tooltip, sin clic derecho, posición no persistida, empieza OCULTA en cada sesión (`SetVisible(false)` en `Start`) |
| 18 | Seguridad del gate de combate | 0 | `OnSlotClicked` llama a `TryCast` / `TryCastByKey` directamente, sin `PlayerStance` ni `InputContexts`. Un clic en una casilla en Paz dispara el hechizo de daño: la garantía de Paz, que CLAUDE.md llama "una propiedad, no una convención", la rompe un botón del HUD. Además apunta desde los pies (`transform.position`) y no desde las manos, y lee `Input.mousePosition` directamente (regla cardinal 5), algo que `InputCentralizationGuardTests` no puede ver porque solo busca patrones de `Mouse.current` |
| 19 | Accesibilidad | 3 | Teclas en blanco al 85 % legibles. El coste de maná en azul a 9 pt apenas se lee. Los estados solo se distinguen por color. Sin opción de escala |
| 20 | Descubribilidad | 2 | Oculta al arrancar. El icono de la bandeja se carga con `AssetDatabase.LoadAssetAtPath`, que en un build devuelve null: botón sin icono |
| 21 | Tokens / arquitectura | 2.5 | 10 `new Color(` literales, dependencia del namespace del editor de tiles, `_whitePixel` estático en la lista `unreset-statics.txt`, sin asset de estilo |
| 22 | Redundancia entre paneles | 1.5 | El mismo cooldown se dibuja tres veces con tres estilos: la cuña de la barra, la cuña del panel del jugador y la lista de texto de arriba a la izquierda (`SpellCooldownHUD`) |
| 23 | Rendimiento | 6 | 24 componentes `Outline` (cada uno replica los vértices del gráfico) y 72 TMP. Sin canvas anidado: cada cambio de `fillAmount` ensucia todo el `SpellBarCanvas`. Barato en absoluto, sin necesidad |

**Media: 1.9.** Los ejes 1-4 y 18 no son estética, son la razón por la que embellecer esta
barra tal cual sería pintar algo que miente.

---

## 3. Defectos, en orden de gravedad

- **D1 — La casilla rompe la Paz.** Un clic en cualquier casilla castea sin pasar por el gate de
  postura. Arreglo: la barra no castea por su cuenta. O el clic desaparece o pasa por la misma
  puerta que `PollCombatActions` (postura, máscara por acción, `InputContexts`).
- **D2 — 24 teclas falsas.** Arreglo: cada casilla muestra el binding VIVO de la acción que la
  castea, como hace `HudAbilitySlot.UpdateBinding` con `InputBindingResolver.Primary`. Si se
  reasigna una tecla, la etiqueta se mueve.
- **D3 — La fila de arriba no la castea nada.** Los 4 slots del `SpellCaster` solo los usan los
  monstruos. Arrastrar un hechizo ahí no hace nada para el jugador. Es la forma "authored and
  inert" que CLAUDE.md recoge una docena de veces.
- **D4 — La bola de fuego sintética.** `EntitySetup` pone en el slot 0 la definición de fallback
  de `ProjectilePrefabFactory` aunque el catálogo haya cargado el asset real. Debería usar el
  asset `fireball` del libro y dejar el fallback para cuando el catálogo está vacío.
- **D5 — Reloj de cooldown equivocado** en la fila de arriba (lee el slot, el clic usa el libro).
- **D6 — Paginador fuera del panel** y sin función, y **minimizar encima de la casilla `=`**.
- **D7 — `Input.mousePosition` directo** y un guard que no lo ve. Se arregla usando
  `SpellTargeting.ResolveAimDirection` y añadiendo el patrón legacy al guard.
- **D8 — Icono de la bandeja nulo en build** (`AssetDatabase` en código de runtime).

---

## 4. Qué debería ser la barra (la decisión de fondo)

Hoy conviven dos modelos que no se hablan:

1. **El modelo de input real.** 24 acciones fijas en `ValkurInputActions`, cada una con su
   hechizo (`PayloadKey`). La tecla `2` es siempre `iceball`.
2. **El modelo que la barra dibuja.** Casillas donde el jugador arrastra hechizos, estilo WoW.

Un panel profesional necesita UNO. Recomendación: **el segundo, hecho de verdad**, una barra de
carga (loadout) como la de Diablo, Path of Exile o WoW:

- La barra tiene N casillas (10 por defecto, ampliable a 2x10). Cada casilla es una acción
  `ActionBar/Slot1..N` en el asset, reasignable en el editor de Controles, con `1`..`0` por
  defecto.
- El CONTENIDO de cada casilla es un dato de partida (qué hechizo hay en la casilla 3), guardado
  en el save y editable arrastrando desde el grimorio.
- El catálogo ya piensa así: "a spell SLOT is the unit of trust, not the spell". Las 24 acciones
  están marcadas `ReachesDamage` por casilla, no por hechizo, así que el gate de Paz se conserva
  sin cambios.
- Gana tres cosas que el modelo fijo no puede dar: el jugador elige su kit, la barra nunca está
  llena de casillas que no puede usar, y el arrastrar deja de ser decorativo.

Mientras eso llega, la **Fase 0** hace honesto el modelo actual: la barra enseña los hechizos
que el personaje CONOCE con la tecla que de verdad los castea.

---

## 5. Dirección visual

Principio: **la barra es el hermano pequeño del panel del jugador, no un widget distinto.** Se
construye con las mismas piezas (`HudArt`, `HudAbilitySlot`, `HudPixelText`, `HudMoteLayer`,
`HudTextureBaker`) en el mismo espacio de texel y con el mismo estilo. Todo eso vive en
`Valkur.UI`, que puede referenciar `Valkur.Gameplay` (comprobado en su asmdef), así que la barra
nueva se muda a `UI/HUD/SpellBar/`.

### Esqueleto propuesto (en texels, escala 2 a 1600x800)

```text
                 marco de piedra: outline 1 · bisel 1 · piedra en 2 tonos
   gema de la  ◆──────────────────────────────────────────────────────────────◆
   escuela     ╔════╗╔════╗╔════╗╔════╗╔════╗   ╔════╗╔════╗╔════╗╔════╗╔════╗
               ║1   ║║2   ║║3   ║║Q   ║║R   ║   ║F   ║║G   ║║C   ║║V   ║║X   ║
               ║ ◉  ║║ ❄  ║║ ✦  ║║ ≈  ║║ ϟ  ║   ║ ☁  ║║ ☁  ║║ ✧  ║║ ✺  ║║ ✚  ║
               ║  7 ║║    ║║    ║║    ║║    ║   ║    ║║    ║║    ║║    ║║    ║
               ╚════╝╚════╝╚════╝╚════╝╚════╝   ╚════╝╚════╝╚════╝╚════╝╚════╝
               └──── grupo 1: 5 casillas ────┘   └──── grupo 2: 5 casillas ────┘
               ▔▔▔▔▔▔▔▔▔▔▔ línea de cast / canalización (2 texels) ▔▔▔▔▔▔▔▔▔▔▔▔▔
```

- **Casilla de 22 texels** (44 px a escala 2), un texel más que las del ratón del panel (20).
  Separación de 2 texels y un hueco de 5 entre grupos de 5: la mano lee "1-5" y "6-0" como dos
  bloques, igual que los dedos.
- **Una sola fila por defecto.** La segunda fila aparece solo si tiene contenido, o con un botón
  de expandir en el propio marco. Nunca casillas vacías que no se pueden llenar.
- **Anatomía de casilla**, idéntica a `HudAbilitySlot`: marco hundido, icono horneado al tamaño
  exacto, tecla en un tapón (keycap) arriba a la izquierda en fuente bitmap, segundos enteros al
  centro, borde interior teñido del color del hechizo (`SpellColour`, oro si no está autorado).
- **Estados:** listo, enfriando, sin maná (icono lavado en azul), no aprendido (gris con
  candado), pulsado (icono baja 1 texel), rechazado (flash azul y golpe de 1 texel), canalizando
  (brillo que respira mientras se mantiene), cargando (anillo que se llena para los hechizos con
  `ChargeMath`), efecto activo (un borde interior que se vacía mientras dura el escudo o el aura,
  la misma gramática que el temporizador de estados de las barras del mundo).
- **Cuña de cooldown con filo:** la sombra oscura más una línea brillante de 1 texel en el borde
  que avanza, como la aguja de un reloj. Paso de 1/64, por frame, no a 20 Hz.
- **Línea de cast** bajo la barra: 2 texels, del color del hechizo, se llena durante
  `prepareDuration` y se vacía durante `channelDuration`. Es el único sitio del HUD donde se
  leería "estoy casteando", que hoy no se lee en ninguno.
- **Gemas en las esquinas del marco** con el color de la escuela del último hechizo lanzado: el
  único adorno que cambia, y cambia por un evento.
- **Mismo suelo que el panel del jugador:** mismo margen inferior, misma altura de marco o una
  fracción exacta de ella, y el hueco entre los dos declarado en `HudLayout`.

---

## 6. Partículas: dónde sí y dónde no

La regla ya está escrita en `HudMoteLayer` y aquí se aplica igual: **una partícula responde a un
EVENTO**. Una barra que brilla en reposo es una barra cuyo brillo no significa nada cuando
importa.

### Dónde NO

- Nada en reposo: ni destellos ambientales ni brillos en bucle sobre las casillas listas.
- Nada en un rechazo. Un "no" es un golpe seco y un flash azul, no una celebración.
- Nada de `ParticleSystem`: en un canvas overlay no ordena con las `Image`.
- Nada en un cooldown corto. El clic izquierdo tiene 0.4 s y celebrarlo sería una fuente de
  chispas. El umbral de `readyFlashMinCooldown` (1.5 s) vale aquí también.

### Dónde SÍ (vida ≤ 0.6 s, todo aditivo y en rejilla de texel)

| Evento | Qué se ve | Motas |
| --- | --- | --- |
| Hechizo lanzado | 3-4 motas del color del hechizo suben desde la casilla, y la gema de la escuela se enciende | 3-4 |
| Cooldown largo vuelve | Flash blanco sobre el icono, anillo del color del hechizo y un barrido de brillo diagonal sobre el icono (shader, 0.25 s) | 6 |
| Carga completa | Chispas en las cuatro esquinas de la casilla en el instante en que la carga llega al máximo | 8 |
| Hechizo aprendido | La casilla nueva cae 2 texels con rebote y suelta una estrella dorada: el momento grande de la barra | 16-24 |
| Soltar un arrastre | Un soplo de polvo dorado donde cae el icono | 5 |
| Fin de canalización | Las motas que quedaban en la línea de cast se dispersan hacia arriba | 6 |

Presupuesto: pool de 48 motas en su propio `HudMoteLayer`, dentro del canvas anidado de la barra,
un draw call.

---

## 7. Roadmap por fases

### Fase 0 — Verdad antes que belleza (1.9 → 3.5)

D1 a D8. La barra enseña los hechizos que el personaje conoce, cada uno con la tecla que de
verdad lo castea, lee el reloj del libro, no castea saltándose la postura y no depende del tema
del editor de tiles.

### Fase 1 — Un solo lenguaje (3.5 → 6.5)

Mudanza a `UI/HUD/SpellBar/`. Construida con `HudArt`, `HudAbilitySlot` y `HudPixelText` en el
espacio de texel del panel, con el estilo del tema común. Una fila, grupos de 5, marco de piedra,
iconos horneados. Se retira la lista de texto de `SpellCooldownHUD`: un cooldown se dibuja una
vez.

### Fase 2 — Feel (6.5 → 7.8)

Cuña con filo por frame, pulsado, rechazado, canalizando, cargando, efecto activo, línea de cast,
fundido al mostrar y ocultar, y tooltip con el mismo `HudTooltip` del panel.

### Fase 3 — Partículas (7.8 → 8.5)

La tabla de la sección 6, con `HudMoteLayer` y el material aditivo de `HudFx`.

### Fase 4 — Barra de carga de verdad (8.5 → 9+)

`ActionBar/Slot1..N` en el asset y en `InputActionCatalog`, contenido guardado en el save,
arrastrar desde el grimorio, y retirada de las 24 acciones de hechizo fijas (o su conversión en
los valores por defecto de la barra).

---

## 8. Qué NO hacer

- **No pintar encima de lo que hay.** Un marco bonito sobre 24 teclas falsas es peor que lo
  actual, porque la mentira se vuelve más creíble.
- **No escribir un segundo componente de casilla.** `HudAbilitySlot` ya tiene los cinco estados,
  el horneado de iconos, el binding vivo y el flash de listo. Generalizarlo es más barato que
  mantener dos casillas que se desvían.
- **No dar a la barra su propio tema de color.** Es el camino por el que llegó al tema del editor
  de tiles.
- **No añadir partículas antes de la Fase 2.** Sin estados claros, una partícula no tiene evento
  al que responder.
- **No hacer la barra más grande para que quepan 24 hechizos.** El problema no es el tamaño, es
  que 19 casillas estén vacías.

---

## 9. Resultado de la reconstrucción (mismo día)

La barra se reconstruyó con **dos caras, una por postura**, a petición expresa: en Guerra enseña
lo que se puede lanzar; en Paz, lo que se puede hacer.

### Qué se construyó

```text
SpellBarHUD (+.Faces, .Verbs, .Motes)   UI/HUD/SpellBar/   la barra: caras, giro, motas, clics
SpellBarModel                            UI/HUD/SpellBar/   qué casillas lleva cada cara (puro)
SpellBarArt                              UI/HUD/SpellBar/   9 glifos de verbo + marco con gema de postura
SpellBarSlotClick                        UI/HUD/SpellBar/   reenvía el clic a la barra
HudSlotVerb + modo verbo de HudAbilitySlot  UI/HUD/PlayerPanel/  una casilla para hechizos Y acciones
SpellBarStyle                            Data/UI/ + Resources/UI/SpellBarStyle.asset
PlayerController.TryCastFromHud          el único camino por el que la barra castea
HUDManager.SpellBar                      la crea junto al panel del jugador, dentro del canvas del HUD
```

- **Cara de Guerra:** los hechizos que el personaje conoce y que una tecla suya castea, en el
  orden del teclado, en grupos de 5, con la tecla leída del binding vivo, el cooldown del reloj del
  libro y los cinco estados del panel. Al final, el interruptor de postura.
- **Cara de Paz:** Interactuar (se enciende cuando hay algo al alcance y el tooltip dice qué:
  "Talar", "Conversar"…), Inventario, Mapa del mundo, Oficios, Misiones, Talentos y Grimorio.
  Cada verbo abre lo mismo que su tecla o su botón de siempre, y su casilla muestra un anillo
  mientras su panel está abierto. Al final, el interruptor.
- **La regla de pertenencia es la misma que decide si la tecla funciona**:
  `InputContextPolicy.IsLive(acción, postura)`. Un verbo que el jugador silencia para Paz en el
  editor de Controles desaparece de la cara, y nada que llegue al camino de daño puede estar en la
  de Paz.
- **El interruptor enseña a dónde LLEVA**: una hoja verde en la cara de Guerra, espadas rojas en
  la de Paz. El chip de arriba a la izquierda dice dónde estás; el interruptor dice a dónde vas.
- **El giro:** al cambiar de postura cada casilla se estrecha hasta desaparecer, de izquierda a
  derecha, la cara se reconstruye detrás, y vuelven a abrirse con la otra postura. La gema del
  marco se recorta en el color nuevo, la palabra "PAZ" o "GUERRA" sube desde el borde y una
  cortina de motas del color de la postura sale del canto superior. Es el único momento en que la
  barra grita, porque es el único en que cambia el significado de todas las teclas.
- **Partículas, solo por evento:** motas del color del hechizo al lanzarlo y la gema brilla en ese
  color; anillo de chispas cuando vuelve un cooldown largo; estrellas doradas cuando se aprende un
  hechizo nuevo; chispa al usar un verbo; la cortina del giro. Nada en reposo, nada en un rechazo
  (un rechazo es un destello azul de maná).

### Defectos de la sección 3, estado

| Defecto | Estado |
| --- | --- |
| D1 — la casilla rompía la Paz | **Cerrado.** La barra no castea: pide `PlayerController.TryCastFromHud`, que aplica postura, máscara por acción, editor abierto, entrada bloqueada, aturdimiento, espíritu, dash y carga. Medido en vivo: en Paz devuelve `false`, en Guerra castea. `SpellBarHudTests` fija los siete filtros y que la postura se comprueba antes de castear |
| D2 — 24 teclas falsas | **Cerrado.** Cada tecla sale del binding vivo de la acción que castea ese hechizo (medido: Z, B, TAB, E, I, N) |
| D3 — slots que no castea nadie | **Cerrado.** Ninguna casilla depende de los 4 slots del `SpellCaster`. El arrastre desde el editor de hechizos, cuyo único destino eran esos slots, se retiró entero (`DraggableSpellItem`, `SpellDragContext`, `DropZoneSpellSlot`) |
| D4 — bola de fuego sintética | **Cerrado.** El slot 0 recibe el asset del catálogo; el de código queda para un catálogo que no cargó |
| D5 — reloj de cooldown equivocado | **Cerrado.** Solo se lee el reloj del libro |
| D6 — paginador fuera, minimizar encima | **Cerrado.** No hay paginador ni botón propio: se oculta desde la bandeja, con fundido |
| D7 — `Input.mousePosition` directo | **Cerrado.** Ya no se lee; el guard `InputCentralizationGuardTests` busca ahora ese patrón y el único otro uso (la rejilla del editor de tiles) pasa por `MouseInputManager` |
| D8 — icono de bandeja nulo en build | **Cerrado.** El icono es una referencia en `SpellBarStyle.asset` |

Hallazgos que salieron al construir:

- **`CraftingPanelUI` no lo instanciaba nadie.** Tenía estaciones que lo abrían, botón de bandeja
  y tests, y en juego `CraftingStation` llamaba a `Instance?.OpenAt` sobre un null: el oficio era
  inalcanzable. Ahora lo crea `EntitySetup` junto al inventario, y se abre desde la casilla Oficios.
  Su botón de bandeja (sin arte, un cuadrado plano con una "C" entre tres botones pintados) se
  retiró.
- **`SpellCooldownHUD` (la pila de texto de arriba a la izquierda) se retiró**: dibujaba cada
  cooldown por tercera vez en un tercer estilo.
- **Los remaches de la piedra del panel asomaban bajo las casillas** con 4 texels de relleno; la
  barra usa 6.

### Puntuación por eje, después

| # | Eje | Antes | Después | Por qué no es 10 |
| --- | --- | --- | --- | --- |
| 1 | Verdad de las teclas | 0 | 9 | Una tecla de más de 3 letras ("Espacio") no cabe en la casilla y solo sale en el tooltip |
| 2 | Verdad del contenido | 1 | 9 | — |
| 3 | Casillas utilizables | 1 | 9 | — |
| 4 | Lectura de cooldown | 2 | 7 | La cuña aún no tiene filo luminoso |
| 5 | Coherencia con el HUD | 1 | 9 | Mismo kit, misma rejilla, misma línea base que el panel |
| 6 | Materialidad | 1.5 | 8.5 | — |
| 7 | Rejilla de píxel | 1.5 | 9 | Test de rects enteros; el giro avanza de dos en dos texels |
| 8 | Iconografía | 4 | 7.5 | Los hechizos son arte pintado HD y los verbos glifos de píxel: conviven, no son una familia |
| 9 | Tipografía | 2 | 8 | Fuente bitmap del panel; TMP solo en el tooltip |
| 10 | Jerarquía | 1.5 | 7 | Grupos de 5 e interruptor anclado, pero ningún hechizo "principal" destaca |
| 11 | Densidad | 1.5 | 9 | Ninguna casilla vacía |
| 12 | Composición | 3 | 8.5 | Centrada, sin pisar el panel, misma base |
| 13 | Estados | 1.5 | 8 | Faltan carga y efecto activo |
| 14 | Feedback de evento | 0.5 | 8 | Sin sonido |
| 15 | Partículas | 0 | 8 | — |
| 16 | Movimiento | 1 | 8 | — |
| 17 | Interacción | 3.5 | 8 | Sin barra de carga: no se elige qué hechizo va en cada tecla |
| 18 | Seguridad del gate | 0 | 10 | — |
| 19 | Accesibilidad | 3 | 6.5 | Sin opción de escala |
| 20 | Descubribilidad | 2 | 8 | Visible por defecto |
| 21 | Tokens | 2.5 | 8 | Colores de `PlayerHudStyle` y `SpellBarStyle`; `HudTheme` aún por migrar |
| 22 | Redundancia | 1.5 | 8 | La pila de texto se fue; `slash` sigue en clic derecho Y en Z porque son dos botones |
| 23 | Rendimiento | 6 | 8 | Canvas anidado, sin `Outline`, cuña cuantizada |

**Media: 8.2.**

### Método de verificación

- Suite EditMode completa: 8.417 tests. Los grupos tocados (HUD, Input, Core, que incluyen los
  18 tests nuevos de `SpellBarModelTests` y `SpellBarHudTests`) pasan 653 de 653. Los 10 fallos de
  la suite son de `InventoryEquipmentTests` / `InventoryFixedArrayTests` /
  `InventoryUnifiedIndexTests` y pertenecen al refactor de inventario que otra sesión tenía a medias
  en disco (su autora los confirmó como esperados).
- Capturas en vivo a 1600x800 de la cara de Guerra, del giro congelado a mitad (`Time.timeScale = 0`
  y la barra avanzada a mano) y de la cara de Paz.
- Pruebas en vivo del clic: castear en Paz se rechaza, en Guerra castea; el clic en Inventario abre
  la mochila, la casilla pasa a Activa y el segundo clic la cierra.

### Lo que queda abierto

- **Fase 4, la barra de carga.** Hoy la cara de Guerra solo puede enseñar hechizos que tienen una
  de las 24 acciones fijas del asset. **Los hechizos aprendidos que no tienen acción no aparecen,
  porque ninguna tecla los castea** — y el grimorio tiene 73 hechizos frente a 24 teclas. Es la
  razón de más peso para hacer la barra de carga: `ActionBar/Slot1..N` como acciones, contenido
  guardado en el save y arrastrar desde el grimorio.
- Filo luminoso en la cuña de cooldown, anillo de carga, borde de efecto activo y línea de casteo.
- Sonido en el giro y al usar verbos (con `HasSfx`, el catálogo no tiene ids para ello).
- Migrar los colores a `HudTheme` cuando exista (ver `HUD_VISUAL_LANGUAGE.md`).
