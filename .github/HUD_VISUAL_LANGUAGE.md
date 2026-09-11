# El lenguaje visual del HUD de Valkur

**Fecha:** 2026-09-11 · **Estado:** propuesta. Primera pieza construida contra el contrato: la
barra de hechizos (mismo día), que cumple R1, R3, R4, R5, R7, R8, R10 y R11 y deja R2 (el tema
común) para cuando exista `HudTheme`.

Este documento es el contrato que cualquier pieza del HUD de juego debe cumplir para parecer
parte del mismo juego: el panel del jugador, la barra de hechizos, el minimapa, las barras sobre
la cabeza, el chip de postura, el reloj, el registro de misiones, la bandeja de iconos, los
toasts y lo que venga. No cubre los diecisiete editores, que tienen su propio tema (`UITheme`) y
su propia auditoría.

Nace de la auditoría de la barra de hechizos
([`SPELL_BAR_BEAUTY_AUDIT_2026-09-11.md`](SPELL_BAR_BEAUTY_AUDIT_2026-09-11.md)): la barra
llegó a vestirse con el tema del editor de tiles porque no había un sitio donde estuviera escrito
cómo se viste un HUD en este juego.

---

## 1. Por qué hace falta: el estado medido

Hoy hay tres piezas bien hechas y cada una inventó su gramática por separado.

- **Tres assets de estilo con los mismos tokens copiados a mano.** `PlayerHudStyle.gold` es
  `(0.90, 0.76, 0.38)` y `MinimapStyle.ringGold` es `(0.90, 0.74, 0.38)`: el mismo oro con dos
  centésimas de deriva, que es exactamente cómo empieza a separarse un tema. `PlayerHudStyle.outline`
  es `(0.03, 0.03, 0.05)` y `WorldBarStyle.outline` es `(0.04, 0.05, 0.07)`.
- **207 `new Color(` literales** en las carpetas de HUD (`UI/HUD`, `Gameplay/HUD`,
  `Combat/WorldUI`, `HUDIconBar`), más los 36 + 26 + 28 declarados en los tres assets.
- **Dos familias tipográficas.** El panel del jugador usa una fuente bitmap 3x5 / 5x7 con
  contorno horneado. La barra de hechizos, el chip de postura, la pila de cooldowns y otros 15
  ficheros de HUD usan TMP LiberationSans.
- **Dos canvas fuera del contrato de `HudLayout`.** Medido en vivo a 1600x800: `MusicHUDCanvas`
  y `ToastCanvas` escalan contra 800x600 con `match 0`, factor **2.0** frente al 1.0 del resto.
  Es el mismo defecto que ya hizo explotar la fuente del registro de misiones.
- **Órdenes de dibujo que chocan.** La barra de hechizos y el reproductor de música están los
  dos en 150. El minimapa, el chip de postura y el reloj en 105. Inventario, toasts, chat y el
  HUD de depuración en 200.
- **Tres lecturas del mismo cooldown**, en tres estilos: la barra, el panel y la lista de texto
  de arriba a la izquierda.

Lo que funciona y se convierte en norma: el espacio de texel del panel del jugador
(`HudPixelScaleFor`, `HudRect`), su atlas generado (`HudArt`), su casilla de cinco estados
(`HudAbilitySlot`), sus motas por evento (`HudMoteLayer`), la paleta de rango y las rampas de
`WorldBarPalette`, el anillo biselado del minimapa y las bandas declaradas de `HudLayout`.

---

## 2. Las reglas

Cada regla dice qué, por qué y cómo se comprueba. Una regla sin comprobación es una convención,
y las convenciones de este proyecto se han roto todas alguna vez.

### R1 — Un espacio de píxel

Todo panel de HUD se diseña en TEXELS y se dibuja a un número entero de píxeles de pantalla por
texel, el que da `PlayerHudStyle.HudPixelScaleFor` (2 a 1600x800, 3 a 1080p, 5 a 4K). Todo rect
es entero, anclado y pivotado abajo a la izquierda con `HudRect`. Motivo: el HUD dibuja pixel
art al lado de un mundo pixel art, y un factor de escala libre (1.27 a 1080p) remuestrea cada
borde. Comprobación: el test que ya recorre el panel del jugador
(`EveryRectInThePixelSpace_SitsOnWholeTexels`) se generaliza a cada panel de HUD.

### R2 — Un tema, muchos estilos

Un asset `HudTheme` (en `Data/UI/`, cargado desde `Resources/UI/`) guarda los tokens que
comparten todos: piedra clara, piedra oscura, contorno, hueco (recess), bisel, oro, sombra del
oro, texto, texto atenuado, peligro, y los colores semánticos de la R6. `PlayerHudStyle`,
`MinimapStyle`, `WorldBarStyle` y el estilo de la barra de hechizos dejan de declarar esos tokens
y los leen del tema. Cada estilo se queda con lo que es SUYO: tamaños, tiempos, capacidades.
Comprobación: un test que falla si dos estilos declaran un campo con el mismo papel, y un
trinquete de `new Color(` por fichero en las carpetas de HUD, igual que el de los editores
(`EditorRawColorRatchetTests`): puede bajar libremente y no puede subir.

### R3 — Una gramática de marco

- Contorno casi negro de 1 texel alrededor de todo lo que es un objeto.
- Bisel claro de 1 texel arriba y a la izquierda, piedra en dos tonos debajo.
- Hueco (recess) oscuro y OPACO para todo lo que contiene un valor: barras, casillas, cuadros de
  texto. Opaco porque en espacio lineal un 4 % de transparencia deja pasar un 18 % de lo que hay
  detrás (medido en el tooltip del panel).
- **El oro es importancia, no decoración.** Marco del panel del jugador, anillo del minimapa,
  borde de la casilla principal. Un HUD donde todo tiene oro es un HUD donde el oro no dice nada.

Comprobación: las piezas salen del atlas generado (`HudArt`), no se dibujan a mano en cada
widget; un widget que crea su propio `Image` sin sprite para hacer un fondo es la señal.

### R4 — Dos tamaños de letra y una sola fuente de juego

La fuente bitmap pequeña (3x5) para teclas, contadores y segundos; la grande (5x7) para valores.
TMP solo para prosa: tooltips de más de una línea, texto de misión, chat. Motivo: TMP a 9 pt en
un canvas escalado es la parte más borrosa de la barra de hechizos, y dos familias tipográficas
en el mismo borde de pantalla se leen como dos juegos. Comprobación: lista blanca de ficheros de
HUD que pueden crear `TextMeshProUGUI`.

### R5 — Una sola casilla

Todo sitio del HUD donde aparece un hechizo o un objeto usa el mismo componente de casilla,
generalizado desde `HudAbilitySlot`: los botones del ratón, la barra de hechizos, y mañana una
barra de consumibles. Mismos cinco estados (vacía, bloqueada, lista, enfriando, sin maná) con el
mismo aspecto, misma tecla sacada del binding vivo, mismo icono horneado. Motivo: dos casillas
se separan en la siguiente iteración, y el jugador aprende el estado "sin maná" una vez.

### R6 — Un color significa una cosa

| Color | Significa | Nunca se usa para |
| --- | --- | --- |
| Verde que pasa a rojo | Vida propia | Nada más |
| Azul | Maná, y "te falta maná" | Agua, hielo o decoración |
| Oro | Experiencia, importancia, nivel | Relleno de fondos |
| Color propio del hechizo | "Este hechizo está listo / ocurrió" | Estados de sistema |
| Sombra oscura | Tiempo que falta (cooldown) | Deshabilitado |
| Gris con candado | No aprendido / no disponible | Cooldown |
| Rojo oscuro de borde | Peligro sobre el jugador | Errores de interfaz |

Y ningún estado depende SOLO del color: forma, icono o texto lo confirman (daltonismo).

### R7 — Movimiento

- Rellenos con el mismo lerp (`fillLerpSpeed`), golpe de 1 texel (`knockTexels`), chip que se
  retrasa y se drena con los tiempos del panel.
- Mostrar y ocultar es un fundido de ~0.12 s, nunca un salto de alfa.
- En reposo solo se mueven los relojes (cooldowns, estados). Nada respira sin motivo.

### R8 — Partículas solo por evento

`HudMoteLayer` y nada más: un Graphic por panel, pool con capacidad fija, material aditivo de
`HudFx`, posiciones en texels enteros, vida de 0.6 s como máximo (0.9 s solo para subir de
nivel). Nunca `ParticleSystem` en un canvas overlay. Nunca emisión en reposo. Motivo: si el HUD
brilla sin causa, el brillo deja de avisar cuando hay causa.

### R9 — El espacio se declara

Cada banda de pantalla se declara en `HudLayout`, como ya lo está la columna de arriba a la
derecha: panel del jugador abajo a la izquierda, barra de hechizos abajo al centro, bandeja abajo
a la derecha, columna de arriba a la izquierda (reloj, postura). Los widgets derivan su posición
de ahí. Cada canvas de HUD usa `HudLayout.ReferenceWidth` / `ReferenceHeight` / `Match`. Los
órdenes de dibujo salen de una tabla de bandas en el mismo sitio, no de un número escrito en cada
widget. Comprobación: un test que recorre los canvas de HUD y exige el contrato (habría cazado
`MusicHUDCanvas` y `ToastCanvas`), y otro que falla si dos widgets siempre visibles solapan sus
bandas.

### R10 — Iconos

Todo icono se hornea al tamaño exacto en píxeles de su casilla con `HudTextureBaker.Icon`: el
arte es de 1024 px, bilineal y sin mipmaps, y minificado por el sampler titila. Todo icono va
dentro del mismo marco. Un hechizo sin icono muestra un sigilo generado de su escuela, nunca una
casilla en blanco.

### R11 — Un dato, una lectura

Cada hecho se dibuja en un solo sitio del HUD. Si dos paneles necesitan el mismo cooldown, uno
es el dueño y el otro no lo dibuja. La pila de texto de `SpellCooldownHUD` es la primera en
retirarse cuando la barra nueva exista.

### R12 — El HUD también muere

En forma de espíritu, todo panel sigue a `PlayerHudStyle.spiritStone`: la piedra deriva al gris
azulado y los colores se apagan, igual que el mundo. Un panel que sigue en color mientras el
mundo es gris es un panel que no sabe lo que ha pasado.

---

## 3. Cómo se aplica, en orden

1. **`HudTheme` y el trinquete de colores.** Crear el asset con los tokens de la R2 tomando los
   valores del panel del jugador (es el que más se ha medido). Hacer que `PlayerHudStyle`,
   `MinimapStyle` y `WorldBarStyle` lean de él. Congelar la cuenta de literales por fichero.
2. **El test de canvas.** Referencia, `match` y banda de orden para cada canvas de HUD. Arreglar
   `MusicHUDCanvas` y `ToastCanvas` en el mismo cambio.
3. **La barra de hechizos nueva**, primera pieza construida contra el contrato desde cero (fases
   0 a 3 de su auditoría).
4. **Los widgets pequeños de la columna izquierda** (reloj, chip de postura) pasan al espacio de
   texel y a la fuente bitmap.
5. **La bandeja y los toasts**, que ya tienen arte pintado y solo necesitan el marco y la banda.

## 4. Qué NO hace este documento

- No toca los editores. Un editor es una herramienta y puede ser plano y denso; el HUD es parte
  del mundo del juego.
- No pide rehacer lo que ya cumple. El panel del jugador, las barras del mundo y el minimapa
  solo cambian de dónde leen sus tokens.
- No es un sistema de temas intercambiables. Hay un tema. La razón de existir de `HudTheme` es
  que el oro esté escrito una vez, no que el jugador pueda elegir otro.

---

## 5. Ampliación: las ventanas de juego

Añadido desde la auditoría del inventario
([`INVENTORY_HUD_BEAUTY_AUDIT_2026-09-11.md`](INVENTORY_HUD_BEAUTY_AUDIT_2026-09-11.md)). Las
secciones 1-4 cubren los INSTRUMENTOS (lo que está siempre en pantalla); esta cubre las
VENTANAS que el jugador abre para decidir: inventario, tienda, crafteo, chat, misiones, árboles,
estadísticas, pausa, mapa del mundo.

### 5.1 El censo de Gameplay

Medido 2026-09-11 con grep sobre `Scripts/`:

| Superficie | Asamblea | Viste | `new Color(` |
| --- | --- | --- | --- |
| Inventario | Gameplay | `TileEditorTheme` + `TileEditorUIHelpers` (tema de EDITOR, estático y mutable) | 7 + 4 serializados inertes |
| Barra de hechizos | Gameplay | `TileEditorTheme` | 10 |
| Crafteo | Gameplay | `UITheme` (el de los editores) | 1 |
| Chat | Gameplay | literales | 51 |
| Tienda | Gameplay | literales | 26 |
| Misiones | Gameplay | literales | 13 |
| Árboles / estadísticas | Gameplay | literales | 9 / 4 |
| Pausa | UI | literales | 14 |

La regla que falta en la sección 4 y lo resume: **nada que el jugador vea en un build lleva
cromo de editor** — ni `TileEditorTheme`, ni `TileEditorUIHelpers`, ni `EditorUIHelpers`, ni
`PanelChrome`. Los editores pueden seguir siendo planos; una ventana de juego es parte del juego.

### 5.2 La causa estructural: el kit no se puede usar desde Gameplay

`HudArt`, `HudPixelFont`, `HudPixelText`, `HudRect`, `HudMoteLayer`, `HudTooltip`,
`HudTextureBaker` y `HudAbilitySlot` viven en `Valkur.UI`. El inventario, la tienda, el chat, el
crafteo y las misiones viven en `Valkur.Gameplay`, y **`Valkur.Gameplay → Valkur.UI` está
prohibido**. Ninguna de esas ventanas puede cumplir R1, R3, R4, R5 ni R8 aunque quiera: por eso
cada una se inventó su estilo o se lo pidió prestado al editor. R5 ("una sola casilla") en
concreto es imposible para la casilla de objeto del inventario y de la tienda sin esto.

Propuesta (no ejecutar mientras otra sesión esté editando alguno de estos ficheros):

1. `Valkur.UIKit` gana la referencia a `Valkur.Data` (Data solo ve Core: no hay ciclo).
2. `git mv` del kit a `Gameplay/UIKit/Hud/` (namespace `Valkur.UIKit.Hud`), conservando GUIDs.
   Los estilos siguen en `Data/UI/`.
3. `HudArt` se parte en atlas base (piezas del lenguaje) y piezas de superficie (retrato del
   panel, paper doll del inventario) para que el atlas común no crezca con cada ventana.

### 5.3 Componentes que añaden las ventanas

- **`WindowChrome`** — barra de título en fuente bitmap, arrastre, cerrar, minimizar que
  COLAPSA a la barra de título (en el inventario hoy minimizar = cerrar), geometría en PlayerPrefs
  `valkur.<ventana>.*` escrita al final del gesto, `Escape` vía `EscapeOwnership`. Hoy está
  repartido entre `QuestLogHUD.Window`, `MusicPlayerHUD` y `WindowDragHandler`.
- **Casilla de OBJETO** — la casilla de R5 con los estados de un objeto en vez de los de un
  hechizo: vacía, hover, seleccionada, destino válido, destino inválido, deshabilitada; cantidad
  en la fuente bitmap; y marco de RAREZA cuya redundancia de forma es la cuenta de esquinas
  (Común 0, Poco común 1, Raro 2, Épico 4, Legendario 4 + filete).
- **Tarjeta de tooltip** — `HudTooltip` generalizada: título en color de rareza o elemento,
  filas de estadísticas en fuente bitmap, siempre dentro de la pantalla.

### 5.4 Reglas que añaden las ventanas

- **R13 — Una ventana no se ABRE encima de un instrumento.** Se puede arrastrar encima de lo que
  quiera, pero su posición por defecto sale de una banda `RightWindowColumn` de `HudLayout`
  (a la izquierda de la columna del minimapa). Medido: el inventario abre en x[1284..1584]
  y[156..784] y tapa el minimapa al 100 %, a orden 200 contra 105.
- **R14 — Colores de rareza en `HudTheme`**, con el significado "rareza" y ningún otro (se
  añaden a la tabla de R6).
- **R15 — Todo en español y ninguna tecla escrita a mano.** El pie del inventario dice
  `Tab/I close | Q drop`: `Tab` es la postura, `Q` el teletransporte y soltar es `Delete`.
- **R16 — Ningún sprite de runtime se carga con `AssetDatabase`.** Los tres botones de la bandeja
  (inventario, hechizos, música) lo hacen bajo `#if UNITY_EDITOR` y son cuadrados grises en un
  build del jugador. Van referenciados desde un asset de estilo bajo `Resources/UI/`.

Comprobaciones: `HudDialectGuardTests` (ningún fichero de `Gameplay/Inventory`, `Gameplay/HUD`,
`Gameplay/Chat`, `Gameplay/Vendors`, `Gameplay/Crafting`, `Gameplay/Quests`, `UI/HUD` ni
`UI/PauseMenu` nombra los tipos de tema de editor), `HudOpenPositionTests` (R13),
`HudKeyLabelTests` (R15) y `HudRuntimeSpriteLoadTests` (R16).

### 5.5 Orden, después del de la sección 3

1. El kit a UIKit (5.2), cuando la barra de hechizos nueva esté en `main`.
2. Inventario (nota 2.1, el peor) — fases de su auditoría.
3. Tienda, que comparte casilla de objeto y tarjeta con el inventario.
4. Chat y misiones; después crafteo, árboles, estadísticas y pausa.

---

## 6. Ampliación: el dialecto de herramienta

Añadido desde la auditoría del HUD de depuración
([`DEBUG_HUD_BEAUTY_AUDIT_2026-09-11.md`](DEBUG_HUD_BEAUTY_AUDIT_2026-09-11.md)). Las secciones
1-4 cubren los INSTRUMENTOS y la 5 las VENTANAS; esta cubre las HERRAMIENTAS que se dibujan
encima del juego: el HUD de depuración, `SaveTelemetryHUD`, las sondas de rendimiento de Tile y
Buildings, las etiquetas de `AIDebugOverlay`, `CombatRangeVisualizer` y la consola. No cubre los
diecisiete editores, que siguen con `UITheme` (sección 4).

### 6.1 Por qué hace falta

Medido 2026-09-11: siete herramientas, **tres tecnologías de dibujo** (uGUI + TMP, IMGUI con
`OnGUI` y `GUI.skin`, `LineRenderer` en el mundo), **53 literales de color en 8 ficheros**, órdenes
200 / 220 / 500 / 999 y ninguna pieza compartida con el HUD de juego. El HUD de depuración tapa el
65 % del minimapa y su fondo mide 0 px. Una captura de un informe de bug parece de otro juego.

### 6.2 La idea

**Mismo tema y misma rejilla que el HUD, distinto acento.** Una herramienta toma de `HudTheme` la
piedra oscura, el contorno, el hueco y el texto, se dibuja en el espacio de texel y con la fuente
de píxel; y renuncia a todo lo que en el juego significa algo. Así una captura parece de Valkur y a
la vez se distingue de un vistazo qué es juego y qué es instrumento.

### 6.3 Reglas

Numeradas H1-H9 para no chocar con las R: la sección 5.4 usa R13-R16 y la auditoría del
reproductor de música propone OTRAS R13-R16 con otro significado (su sección 7). Esas cuatro
deberían renumerarse a R17-R20.

- **H1 — Mismo tema, sin oro.** Tokens de `HudTheme` para superficies y texto. El oro, el bisel
  ornamental, el verde de la vida y el azul del maná son del juego y no aparecen en una
  herramienta.
- **H2 — Misma rejilla, misma fuente para los números.** Espacio de texel (R1) y `HudPixelFont`,
  cuyos dígitos son de ancho fijo (3 texels en la cara pequeña, 5 en la grande): justo lo que una
  columna de números necesita. Nunca se alinea una columna con espacios en una fuente
  proporcional. Los NOMBRES (hechizos, entidades) pueden ir en TMP mientras la cara pequeña no
  tenga tildes ni Ñ.
- **H3 — El contraste se mide contra el hueco, no contra el mundo.** Fondo opaco (alfa 1, por el
  espacio lineal) y todo color de texto, el atenuado incluido, a ≥ 4.5:1 sobre `recess`. Una
  herramienta que solo se lee de noche no es una herramienta.
- **H4 — Semáforo propio.** Bien / aviso / mal en una tríada que no comparte tono con ningún color
  de R6 (propuesta: verde azulado `(0.30, 0.80, 0.72)`, ámbar `(0.96, 0.72, 0.25)`, rojo magenta
  `(0.95, 0.32, 0.45)`), y siempre con redundancia de forma: altura de barra, marca, texto.
- **H5 — Se puede atravesar.** `raycastTarget = false` en todo salvo cabeceras y botones. Una
  herramienta nunca bloquea el clic de combate por estar abierta.
- **H6 — Banda declarada.** `HudLayout.ToolColumn`, a la izquierda, entre la columna del reloj y
  el panel del jugador, que es el único hueco alto que ningún instrumento ocupa. Orden de la tabla
  de bandas: por encima de los instrumentos, por debajo de las ventanas modales.
- **H7 — Eventos, no ambiente, y el coste a la vista.** Partículas solo según R8 y solo para tirón,
  colección de basura, error en consola y cambio de semáforo; ≤ 0.05 ms por frame entre todas. La
  herramienta enseña su propio coste: un instrumento que lo esconde no es de fiar.
- **H8 — Verdad o nada.** Cada fila se lee del mismo sitio que usa el sistema que describe (los
  botones del ratón de `PlayerController`, la facción de `EntityFaction.SideOf`), nunca de una
  estructura parecida. Una casilla vacía que dice "listo" es peor que una fila menos.
- **H9 — En español y alcanzable con una tecla.** Cadenas desde una tabla; un binding en el asset
  (el HUD de depuración: F3, ciclando niveles), además de la entrada del Editor General.

### 6.4 Comprobaciones

`ToolDialectGuardTests` (ningún `OnGUI` nuevo fuera de las dos sondas actuales, y trinquete de
literales en los ficheros de herramienta), `DebugHudContrastTests` (H3), `DebugHudRaycastTests`
(H5) y el test de rejilla de R1 recorriendo también las herramientas.

### 6.5 Orden

1. El HUD de depuración, fases 0-3 de su auditoría.
2. `SaveTelemetryHUD` y la consola, que ya son uGUI.
3. Las dos sondas de rendimiento, de IMGUI al dialecto.
4. Las etiquetas de `AIDebugOverlay` y los alcances de combate.
