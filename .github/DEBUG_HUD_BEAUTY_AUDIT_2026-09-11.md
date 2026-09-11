# Auditoría de belleza, profesionalidad y usabilidad del HUD de depuración (DebugHUD)

**Fecha:** 2026-09-11 · **Nota global: 2.5 / 10** (ponderada 2.47; media aritmética 2.5) ·
Objetivo: **≥ 9** · **Estado:** fases 0-3 construidas el mismo día, **8.3** estimada; la fase 4
(la familia de herramientas) sigue abierta. Ver la sección 10.

Alcance: el panel de arriba a la derecha que construye `DebugHUD`
(`UI/HUD/DebugHUD.cs` + `DebugHUD.Rendering.cs`, 467 líneas) y la estadística que lee de
`Core/PerformanceMonitor.cs`. Se abre desde **Escape → Editor General → Herramientas → Debug HUD**.
Por coherencia se citan el resto de superficies de desarrollo que se dibujan encima del juego
(`SaveTelemetryHUD`, las sondas de rendimiento de Tile y Buildings, `AIDebugOverlay`,
`CombatRangeVisualizer`, la consola) y las piezas del HUD de juego ya reconstruidas hoy (panel
del jugador 8.9, minimapa, barras sobre la cabeza).

El lenguaje visual común está en [`HUD_VISUAL_LANGUAGE.md`](HUD_VISUAL_LANGUAGE.md). Este
documento le añade la sección 6, **el dialecto de herramienta**: qué comparte una herramienta con
el HUD de juego y en qué se tiene que distinguir de él.

---

## Método

Medido en vivo el 2026-09-11 en Play Mode, 1600x800, escena `MainGameplay`, con
`execute_code`, abriendo el panel por reflexión y devolviéndolo oculto al terminar:

- Rect del panel y del texto en píxeles de pantalla (`GetWorldCorners`), escala del canvas,
  cuerpo de letra y alto de línea efectivos, límites de tinta del TMP (`textBounds`).
- Una captura con `ScreenCapture.CaptureScreenshot` y un recorte a 2x de la esquina.
- Contraste WCAG de cada color de la paleta contra tres suelos muestreados de esa captura
  (adoquín de día, tablón de madera, hierba), contra un suelo nocturno y contra el fondo que el
  código declara, compuesto en espacio lineal como lo compone el proyecto.
- Coste de `BuildText` (200 llamadas) y de la malla de TMP (50 `ForceMeshUpdate`), con
  `Stopwatch` y `Profiler.GetMonoUsedSizeLong`.
- Solape de la tinta del texto con cada canvas vivo, y `EventSystem.RaycastAll` en el centro del
  panel.
- Facción real (`EntityFaction.SideOf`) de cada entrada de `EntityRegistry.Monsters`.
- Cobertura de glifos del TMP (`HasCharacter` con y sin fuentes de reserva) y de la fuente
  bitmap del kit (`HudPixelFont`).

---

## 1. Resumen

El panel tiene lo difícil bien hecho y lo fácil mal. Lo difícil: es barato (una malla, 5 Hz,
~0.4 ms por reconstrucción), está registrado como servicio, el Editor General enciende su botón
con el estado vivo y se reinicia bien con el Domain Reload apagado. Lo fácil —que se vea, que diga
la verdad y que no estorbe— falla en las tres cosas.

- **No tiene fondo. Nunca lo ha tenido.** El `Image` del panel mide **0 px de alto**
  (medido `y[788..788]`). `ContentSizeFitter` en `PreferredSize` pregunta la altura preferida a
  los `ILayoutElement` de SU objeto, y el único es un `Image` sin sprite, que responde 0; el TMP es
  un hijo y nadie lee su altura. El rect del texto sale invertido (−20 px) y el texto se dibuja
  por desbordamiento. `COL_BG` no ha pintado un píxel en la vida del proyecto.
- **Por eso tu captura se lee y la mía no.** La tuya es de noche: el mundo está oscuro y hace de
  fondo. De día, sobre adoquín, las etiquetas dan **1.6:1** y el texto atenuado **1.1:1**
  (el mínimo para texto es 4.5:1). Legible o no según la hora del reloj del juego.
- **Tapa el minimapa.** La tinta del texto cubre el **65 %** del minimapa, a orden 200 contra
  105. Las filas de vida y maná pasan por debajo del anillo.
- **Dice cosas falsas.** Seis de las once "entidades" son vendedores neutrales pintados en el
  rojo de monstruo; `[1]`–`[4]` son las casillas internas de `SpellCaster` (la 0 es el clic
  izquierdo, las otras tres están vacías y dicen `RDY`); `Melee` describe un sistema que el
  jugador no usa; `GC 5948` es la cuenta de colecciones del PROCESO del Editor desde que se abrió.
- **No hay gráfica.** Un HUD de rendimiento profesional es, antes que nada, una gráfica de
  tiempo de frame con líneas de presupuesto. Este tiene tres números de ventanas distintas, y un
  tirón que pasó hace tres segundos no deja rastro.

La conclusión es la misma que en las otras piezas del HUD: **primero verdad, después belleza**. Y
una decisión previa: **el HUD de depuración es un instrumento de medida, no un panel de juego.**
Comparte el tema del HUD para que una captura de un informe de bug parezca de este juego, y se
distingue de él en todo lo demás (sección 6).

---

## 2. Puntuación por eje

| # | Eje | Peso | Nota | Evidencia |
| --- | --- | --- | --- | --- |
| 1 | Legibilidad y contraste | 15 | 1.5 | Sin fondo (D1). De día: etiqueta 1.6:1 en adoquín, 1.4:1 en hierba; atenuado 1.0-1.2:1; valor 3.5-4.4:1. De noche todo pasa (valor 14.7:1). Incluso sobre el fondo que el código declara, el atenuado da 3.1:1 y el separador 1.8:1 |
| 2 | Materialidad del panel | 8 | 1.0 | El panel no existe en pantalla. El que se pretendía era un rectángulo recto translúcido (`0.88`) sin contorno ni hueco; en espacio lineal un 12 % de hueco deja pasar bastante más de un 12 % |
| 3 | Verdad de los datos | 14 | 3.0 | Vendedores como monstruos (D4), casillas vacías "listas" (D5), fila de cuerpo a cuerpo sin lector de jugador (D6), GC acumulado del proceso (D7), ventanas mezcladas (D8), CERCA sin ordenar (D14), tiempo en segundos crudos (D15). Posición, velocidad, vida y maná sí son ciertos |
| 4 | Jerarquía | 7 | 3.5 | Cabeceras de sección y el FPS en negrita de color: bien. Todo lo demás pesa igual; el número que importa (ms por frame) va entre paréntesis y atenuado; las cabeceras azul pálido compiten con los valores |
| 5 | Tipografía | 8 | 2.5 | LiberationSans 13 px sin contorno, `lineSpacing -8` (línea de 14.95 px, apretada). Columnas alineadas con espacios en una fuente proporcional (D9): el `RDY` de "Bola de Fuego" sale desplazado. `█ ░ ─` no están en la fuente principal: vienen de la de reserva |
| 6 | Composición y espacio | 8 | 2.0 | Columna de 380 px anclada donde vive el minimapa y el registro de misiones; no hay banda en `HudLayout`; comparte orden 200 con inventario, toasts y chat. Separadores de 32 guiones de ancho fijo |
| 7 | Semántica del color | 6 | 3.0 | 15 literales. El mismo verde `(0.30,0.90,0.40)` significa vida, "listo" y "FPS bueno"; el rojo significa vida baja, FPS malo y (en rosa) "monstruo" aplicado a una gata vendedora. Azul de cabecera parecido al del maná |
| 8 | Visualización de datos | 10 | 1.5 | Barras de texto de 12 caracteres que duplican el panel del jugador. Ninguna gráfica de tiempo de frame, ningún presupuesto dibujado, ningún registro de tirones |
| 9 | Usabilidad e interacción | 10 | 2.5 | Tres pasos para abrirlo (Escape, Editor General, botón); la acción `ToggleDebugHUD` va sin tecla y los comentarios siguen diciendo F9 y F1 (D12). No se pliega, no tiene niveles, no copia nada al portapapeles. En inglés. Bloqueo de clics latente (D3) |
| 10 | Rendimiento y efecto observador | 6 | 8.0 | `BuildText` 0.069 ms + malla TMP 0.31 ms por reconstrucción, a 5 Hz: ~0.08 ms/frame amortizado, una sola malla, `StringBuilder` reutilizado, componentes cacheados, `Update` sale al instante si está oculto. Resta: `ColorUtility` en cada etiqueta, `GetComponent` por monstruo por reconstrucción, `new float[]` cada 2 s en `PerformanceMonitor`, comentario de "30-50 ms" obsoleto |
| 11 | Coherencia con el resto del HUD | 5 | 1.0 | No usa ni una pieza del kit (`HudArt`, `HudPixelFont`, `HudRect`, `HudMoteLayer`) ni un token de `PlayerHudStyle`. La familia de herramientas usa tres tecnologías de dibujo y 53 literales en 8 ficheros (sección 5) |
| 12 | Movimiento y partículas | 3 | 1.0 | Cero partículas, que en reposo es lo correcto. Lo que falta es que los EVENTOS dejen rastro: un tirón, una colección de basura o un error en consola pasan sin que el panel lo marque. Los números saltan cada 200 ms y cambian de ancho |
| | **Ponderada** | 100 | **2.47** | |

---

## 3. Defectos, en orden de gravedad

**D1 — El fondo mide 0 px.** `DebugHUD.Rendering.cs:244` pone un `ContentSizeFitter` vertical en
el panel, cuyo único `ILayoutElement` es un `Image` sin sprite (altura preferida 0). No hay
`VerticalLayoutGroup` que lea la altura del TMP hijo. Medido: panel `y[788..788]`, texto
`y[798..778]`, `preferredHeight` del TMP 278.8. Se arregla midiendo el texto
(`GetPreferredValues`) o, en la reconstrucción, con la maquetación en texels que ya calcula su
propia altura.

**D2 — Tapa el minimapa.** Tinta del texto `x[1220..1515] y[499..778]` contra el minimapa
`x[1377..1587] y[571..781]`: **65 %** del minimapa cubierto, a orden 200 contra 105. Es el
defecto que `HudLayout` existe para impedir, y el panel no está declarado en ninguna banda.

**D3 — Bloqueo de clics, latente y enmascarado por D1.** El `Image` de fondo y el TMP tienen
`raycastTarget = true` y el canvas lleva `GraphicRaycaster`. `PlayerController.Movement.cs:433,
897, 960` descarta el clic de combate si `IsPointerOverGameObject()`. Hoy no bloquea nada porque
el fondo tiene área 0 (medido: 0 aciertos en el centro). **Arreglar D1 sin arreglar D3 crea una
zona de 380 x 290 px encima del minimapa donde el clic izquierdo no lanza.** Los dos van en el
mismo cambio; el `GraphicRaycaster` sobra salvo para las cabeceras plegables de la propuesta.

**D4 — Los vendedores son "monstruos".** `EntitySetup.cs:227` registra en `Monsters` a todo lo
que pasa por `ConfigureMonster`. Medido: **6 de 11** entradas son neutrales (Pavel, Valeria,
Roberto, Abigail, Smith, Gatita). "Entities 12" las cuenta y CERCA las pinta en `COL_MONSTER`.
El minimapa ya clasifica por `EntityFaction.SideOf`; el panel debe preguntar lo mismo.

**D5 — `[1]`–`[4]` no son lo que el jugador pulsa.** Son las casillas de `SpellCaster`: la 0 es
el clic izquierdo y las 1-3 están vacías para el jugador, así que salen tres `- RDY` (una
casilla vacía declarada lista). Es el mismo defecto que la auditoría del panel del jugador ya
arregló allí: los botones se leen de `PlayerController.PrimarySpellKeyNow`, `SecondarySpellKey`
y `MiddleSpellKey`, con el cooldown del LIBRO.

**D6 — `Melee dmg=1 RDY`.** El jugador no ataca a través de `MeleeCombat` (lo recoge
`CLAUDE.md`: `TryAttack` solo lo llama el `AttackState` de los monstruos). La fila describe un
sistema sin lector para el jugador. El dash sale como `RDY` sin sus cargas.

**D7 — `GC 5948`.** Es `GC.CollectionCount(0)`, acumulado desde que arrancó el PROCESO; en el
Editor incluye todo lo que el Editor ha hecho desde que se abrió. Un número que solo crece no dice
nada. Lo útil es colecciones por minuto de esta sesión y bytes asignados por frame. Además está
condicionado a `perf != null` sin motivo.

**D8 — Tres ventanas de tiempo en una fila.** El FPS es una media local de 0.5 s; p95 y p99
salen de los últimos 300 frames (2.6 s a 114 FPS, 10 s a 30) recalculados cada 2 s. Se leen uno
al lado del otro como si describieran lo mismo. Medido: media 10.3 ms, p95 17.8 ms.

**D9 — Columnas alineadas con espacios.** `{name,-14}` y `{hpStr,-7}` rellenan con espacios en una
fuente proporcional. En la captura, el `RDY` de "Bola de Fuego" está a otra x que los de las
casillas vacías. Alinear columnas exige dígitos de ancho fijo o posiciones absolutas.

**D10 — La barra de texto miente en los extremos.** `ProgressBar` escribe `█` más `filled - 1`
bloques: a 0 % dibuja 1 lleno + 12 vacíos = 13 caracteres, así que la barra crece un carácter y
enseña un bloque de vida con 0 de vida. Los tres glifos (`█ ░ ─`) no están en LiberationSans:
vienen de la fuente de reserva, con otras métricas.

**D11 — Letra apretada y sin contorno.** 13 px con `lineSpacing -8` da una línea de 14.95 px; sin
contorno, el texto depende enteramente de lo que haya detrás.

**D12 — Difícil de abrir, y los comentarios lo niegan.** Solo se llega por Escape → Editor
General → Debug HUD. `ToggleDebugHUD` va sin tecla desde el 2026-09-05, pero `DebugHUD.cs:18` y
`:111` dicen "F9" y `PerformanceMonitor.cs:8` dice "F1".

**D13 — En inglés.** `PERFORMANCE`, `NEARBY`, `Waiting for player...`, `No enemies nearby`, en un
juego cuya interfaz está en español.

**D14 — CERCA no es "lo más cerca".** Recorre el registro en su orden y se queda con los cinco
primeros a menos de 15 u, no con los cinco más cercanos.

**D15 — `Time 2607s`.** `Time.time` en segundos crudos desde que empezó Play Mode: ni la
partida, ni la sesión, ni la hora del juego. Ilegible por encima del minuto.

**D16 — Deuda menor.** `Label`/`Val`/`Dim` llaman a `ColorUtility.ToHtmlStringRGB` en cada
llamada (docenas por reconstrucción); `GetComponent<Health>` y `<FSMMonsterBrain>` por monstruo
por reconstrucción; `PerformanceMonitor.ComputeStats` hace `new float[_frameCount]` cada 2 s; el
comentario de "~30-50 ms/frame" describe un coste que ya no existe (medido 0.4 ms).

**D17 — Vida y maná repetidos.** Ya los dibuja el panel del jugador (R11 del lenguaje). Lo que un
panel de depuración debe enseñar es lo que el panel de juego NO enseña: valores exactos,
regeneración por segundo, quién tiene activa la invencibilidad (`SetInvincible` tiene tres
dueños y ha dado dos bugs), estados con su tiempo restante, fase de lanzamiento.

---

## 4. Qué debería ser (la decisión de fondo)

Referencias que se usan como listón, por lo que cada una hace bien y no por su aspecto:

| Referencia | Lo que se toma |
| --- | --- |
| `stat unit` / `stat fps` (Unreal) | Tiempos por hilo (juego, render, GPU) en columnas fijas, con el cuello de botella resaltado |
| Gráfica de rendimiento de Path of Exile / `net_graph` (Source) | Una columna por frame, líneas de presupuesto, y los picos siguen visibles mientras la gráfica avanza |
| F3 de Minecraft | Niveles con UNA tecla (aquí F1), texto con sombra que se lee sobre cualquier suelo |
| Rendering Debugger de Unity | Secciones plegables y un estado que sobrevive a la sesión |
| Factorio (F4/F5) | Densidad sin ruido: todo alineado en columnas, nada se mueve si no cambia |

Y de este mismo proyecto: el `boot` de `BootTimeline` (medir primero, que los números decidan), las
tejas de estado de las barras del mundo, el atlas de iconos del minimapa y el kit en texels del
panel del jugador.

### Tres niveles, una tecla

| Nivel | Qué dibuja | Para quién |
| --- | --- | --- |
| 0 | Nada | El juego normal |
| 1 · Chip | Una fila: FPS, ms, una gráfica de 64 frames con la línea de 16.7 ms | Jugar sabiendo si algo va mal |
| 2 · Panel | Rendimiento, jugador, combate, cerca, mundo; secciones plegables | Depurar |
| 3 · Inspector | El panel más anotaciones en el mundo: las de `ai on`, los alcances de combate, etiquetas sobre las entidades en el mismo dialecto | Depurar una entidad concreta |

La tecla cicla 0 → 1 → 2 → 3 → 0 y el nivel se guarda en PlayerPrefs
(`valkur.debughud.level`: es estado de la MÁQUINA, igual que los pesos de `BootTimeline`). Propuesta:
**F1** (la primera propuesta fue F3, y chocaba: F2-F8 son de las sondas de rendimiento de Tile y
Buildings mientras su panel está abierto; F1 no responde a nada más en el asset). Va en el asset
(`Editors/ToggleDebugHUD`, que ya existe sin tecla) y no en C#, por las razones que da
`BindingConstructionGuardTests`. Sigue teniendo su entrada en el Editor General.

### El panel (nivel 2), en texels

Escala 2 a 1600x800 (1 texel = 2 px), 150 texels de ancho. Fuente pequeña 3x5 para etiquetas,
grande 5x7 para los tres números que se leen de un vistazo.

```text
+------------------------------------------------+
| RENDIMIENTO                        HUD 0.4 MS  |  cabecera pulsable = pliega
| 97 FPS    10.3 MS                              |  5x7, color del semáforo
| ..:.:..::..#..:...:.......:.......:...........  |  gráfica 140x32, 1 columna = 1 frame
| - - - - - - - - - - - - - - - - - - -  16.7    |  presupuestos 60 y 30 FPS
| P50  9.8   P95 17.8   P99 20.6   MAX 41.2      |  dígitos de ancho fijo
| CPU  7.1   REND  3.0  LOTES 212  SETPASS 38    |  ProfilerRecorder (Editor y dev)
| GC 0.4/MIN   ASIG 1.2 KB/FR   ERR 0            |
| TIRONES  41.2 MS 00:12   29.8 MS 01:03 GC      |  los últimos 4, con su causa
+------------------------------------------------+
| JUGADOR                                        |
| LOBBY  173.2 59.5  VEL 0.0   GUERRA  IDLE      |
| VIDA 200/200 +1.2/S   MANA 35/35 +0.5/S        |
| INVENCIBLE: DIOS    [ARDE 2.1] [LENTO 0.8]     |  solo si hay algo que decir
+------------------------------------------------+
| COMBATE                                        |
| IZQ  BOLA DE FUEGO   LISTO                     |  la misma lectura que el panel
| DER  TAJO            0.8  ###..                |  del jugador, en texto
| CEN  LASER           SIN MANA                  |
| ESP  DASH            2/3                       |
+------------------------------------------------+
| CERCA   3 HOSTILES  2 NEUTRALES  0 ALIADOS     |
| <> BARBOL      ####. 40/50  CHASE   4.2        |  glifo de clase = el del minimapa
| () GATITA      ##### 50/50  STROLL  7.0        |  ordenado por distancia
+------------------------------------------------+
| [COPIAR]                                       |  informe de bug al portapapeles
+------------------------------------------------+
```

- **La gráfica es la pieza central.** Una columna por frame, altura = ms, color del semáforo,
  líneas de 16.7 y 33.3 ms. Se dibuja escribiendo UNA columna por frame en una textura de
  256 x 64 y desplazando la coordenada UV en el material: coste O(1) por frame, sin 140 `Image` y
  sin reconstruir malla. Un tirón (frame > 2x la mediana y > 25 ms) deja una marca que viaja con
  su columna y entra en la lista de TIRONES con su hora y, si coincidió, `GC` o `CARGA`.
- **Solo se dibuja lo que tiene algo que decir.** La fila de invencibilidad y estados no existe
  cuando no hay ninguno.
- **CERCA clasifica con `EntityFaction.SideOf`** y usa el glifo del `MinimapIconAtlas` para cada
  clase, así el jugador aprende la forma una vez. La barrita de vida sale de las rampas de
  `WorldBarPalette`.
- **COPIAR** deja en el portapapeles (`GUIUtility.systemCopyBuffer`) un bloque de texto plano:
  commit, zona, posición, estadísticas de frame, los últimos tirones y los últimos errores. Es lo
  que convierte "va a tirones en el pueblo" en un informe reproducible.
- **Donde vive:** una banda nueva `HudLayout.ToolColumn`, a la izquierda, entre la columna del
  reloj y el panel del jugador. Medido en la captura: ese hueco tiene ~440 px de alto, es decir
  220 texels a escala 2, y ningún instrumento lo ocupa.

### El chip (nivel 1)

```text
+------------------------------+
| 97 FPS  10.3 MS  ..:.#..:..  |
+------------------------------+
```

Arriba de la misma banda. Es lo que alguien deja encendido mientras juega, así que su regla es
no molestar: una fila, opaco, sin partículas salvo el aviso de tirón.

---

## 5. La familia de herramientas, hoy

| Superficie | Tecnología | Orden | Literales de color |
| --- | --- | --- | --- |
| `DebugHUD` | uGUI + TMP | 200 | 15 |
| `SaveTelemetryHUD` | uGUI + TMP | 220 | 12 |
| `BuildingsPerfProbe.GUI` | IMGUI (`OnGUI`, `GUI.skin`) | — | 4 |
| `TileEditorPerfProbe.GUI` | IMGUI (`OnGUI`, `GUI.skin`) | — | 5 |
| `AIDebugOverlay` | `LineRenderer` en el mundo | 500 | 8 |
| `CombatRangeVisualizer` | `LineRenderer` en el mundo | 999 | 6 |
| `DevConsole` | uGUI + TMP | — | 3 |

Tres tecnologías, siete paletas y ninguna pieza compartida con el HUD de juego. Una captura de un
informe de bug con el panel abierto parece de otro juego, y una herramienta nueva no tiene de
dónde copiar salvo de la última que alguien escribió. La sección 6 del lenguaje visual es la
respuesta; `DebugHUD` es la primera en aplicarla y las demás la siguen en la fase 4.

---

## 6. Partículas: dónde sí y dónde no

Un instrumento de medida tiene una restricción que ningún otro panel tiene: **no puede
perturbar lo que mide**. Toda partícula cuesta tiempo de frame, y ese tiempo aparece en la gráfica
que la partícula decora.

### Dónde NO

- Nunca en reposo. Nada respira, nada brilla sin causa (R8).
- Nunca `ParticleSystem`: en un canvas overlay no se ordena con los `Image` de alrededor y su
  coste es visible en la propia gráfica.
- Nunca sobre los números: un valor que se lee no puede tener algo moviéndose encima.
- Nunca para "hacerlo bonito". En este panel la belleza es la gráfica bien dibujada, el texto
  alineado y el marco del tema.

### Dónde SÍ (`HudMoteLayer`, aditivas, en rejilla de texel, vida ≤ 0.6 s)

| Evento | Qué pasa | Por qué |
| --- | --- | --- |
| Tirón | 3-5 motas suben desde la columna del pico, en el color "mal"; la marca que queda en la columna es la parte duradera | Un tirón dura 40 ms: sin algo que tire del ojo, se ve la marca después y nunca el momento |
| Colección de basura | Un soplo gris de 2-3 motas junto al contador GC, en el frame en que `CollectionCount` sube | Si coincide con un tirón, la correlación se ve sin leer números |
| Error en consola | La placa `ERR` late en rojo con 4 motas; se engancha a `Application.logMessageReceived` | Un error en consola es invisible desde el juego; hoy solo se ve con el Editor delante |
| Cambio de semáforo | Un anillo de un texel alrededor del FPS, solo en la transición y con 1 s de histéresis | Avisa del paso, no del estado |

Presupuesto: **≤ 0.05 ms por frame** entre todas, y la fila `HUD x.x MS` de la cabecera muestra el
coste del propio panel, partículas incluidas. Un instrumento que esconde su coste no es de fiar.

---

## 7. Persistencia visual con el resto del HUD

**Sí hay que escribirlo, y el sitio ya existe:** [`HUD_VISUAL_LANGUAGE.md`](HUD_VISUAL_LANGUAGE.md)
tiene doce reglas para los instrumentos (R1-R12) y cuatro para las ventanas (sección 5). Pero su
sección 4 excluye "los editores" y no dice nada de las herramientas que se dibujan encima del
juego, que no son ni una cosa ni la otra. Esta auditoría le añade la **sección 6, el dialecto de
herramienta**, con reglas H1-H9.

La idea en una frase: **mismo tema y misma rejilla que el HUD, distinto acento.** Una herramienta
toma de `HudTheme` la piedra oscura, el contorno, el hueco y el texto, se dibuja en el mismo
espacio de texel y con la misma fuente de píxel; y renuncia al oro, al bisel ornamental y a los
colores de juego (verde de vida, azul de maná), con un semáforo propio. Así una captura parece de
Valkur y, a la vez, se distingue de un vistazo qué es juego y qué es instrumento.

### Cómo sale el panel contra las reglas hoy

| Regla | ¿Cumple? | Por qué |
| --- | --- | --- |
| R1 Espacio de píxel | No | Canvas escalado libre, TMP |
| R2 Un tema | No | 15 literales propios |
| R3 Gramática de marco | No | Sin marco: el fondo no existe (D1) |
| R4 Fuente | No | TMP para todo, incluidas columnas de números |
| R6 Un color = una cosa | No | Un verde para tres cosas; rojo de monstruo en neutrales |
| R8 Partículas por evento | Sí, por omisión | No tiene ninguna |
| R9 Bandas | No | Tapa el 65 % del minimapa |
| R11 Un dato, una lectura | No | Vida y maná duplicados |

### Lo que se toma de cada pieza ya hecha

| De | Se toma |
| --- | --- |
| Panel del jugador | Espacio de texel (`HudPixelScaleFor`, `HudRect`), piezas de marco y hueco de `HudArt`, `HudPixelFont`, `HudMoteLayer`, la regla de alfa 1 en espacio lineal |
| Minimapa | Los glifos de clase de entidad (`MinimapIconAtlas`) para CERCA |
| Barras del mundo | Las rampas de `WorldBarPalette` para las barritas de vida, y el patrón de `WorldBarContrastTests` para exigir contraste |
| `boot` | La costumbre: el panel se valida midiendo, y el número que importa se pinta grande |
| Registro de misiones | La banda declarada en `HudLayout`, derivada de sus vecinos |

### La fuente de píxel necesita crecer

`HudPixelFont` (ya en `Gameplay/UIKit/Hud/`) tiene en la cara pequeña A-Z, 0-9 y `/ + - . : %`, y
en la grande solo dígitos y `/ + -`. Sus dígitos son de ancho fijo (3 y 5 texels), que es justo lo
que una columna de números necesita y lo que arregla D9. Para este panel faltan `( ) = , < > # !`
y las letras con tilde y la Ñ, que en mayúscula piden una fila más por encima. Mientras no
existan, los NOMBRES (hechizos, entidades) pueden seguir en TMP; los números y las etiquetas no.

### Por qué un documento no basta

Todo lo que este proyecto sostiene de verdad tiene un test. Para las herramientas:

- `ToolDialectGuardTests`: ningún `OnGUI` nuevo fuera de las dos sondas actuales (trinquete), y
  ningún literal de color nuevo en los ficheros de herramienta (el trinquete de R2).
- `DebugHudContrastTests`: todo color de texto ≥ 4.5:1 contra el hueco, incluido el atenuado.
  Hoy fallaría: el atenuado da 3.1:1 incluso sobre el fondo que el código declara.
- `DebugHudRaycastTests`: solo cabeceras y botones son `raycastTarget`.
- El test de rejilla de R1 recorriendo también este panel.

---

## 8. Roadmap por fases (nota estimada tras cada una)

### Fase 0 — Verdad antes que belleza (2.5 → 4.5)

D1 con D3 en el mismo cambio. D4 (`EntityFaction.SideOf`), D5 (los botones reales), D6 (quitar
cuerpo a cuerpo, cargas de dash), D7 (GC por minuto y bytes por frame), D8 (una ventana de
tiempo para todo, en segundos y no en frames), D10, D14, D15, los comentarios de D12, y
`PerformanceMonitor` sin `new float[]` y con acceso de lectura a su búfer circular.

### Fase 1 — Un solo lenguaje (4.5 → 6.8)

Espacio de texel y kit del panel del jugador; `DebugHudStyle` bajo `Resources/UI/` que lee los
tokens de `HudTheme`; semáforo propio; `HudLayout.ToolColumn` y banda de orden; cadenas en
español desde una tabla; fuente de píxel ampliada; los cuatro tests de la sección 7.

### Fase 2 — Instrumento (6.8 → 8.3)

Gráfica de tiempo de frame (una columna por frame), registro de tirones, contadores de
`ProfilerRecorder` (hilo principal, render, lotes, SetPass, asignación por frame; en Editor y
builds de desarrollo), niveles 0-3 con F1 en el asset y nivel persistido, secciones plegables,
COPIAR, y la fila de jugador con lo que el panel de juego no enseña (D17).

### Fase 3 — Eventos y movimiento (8.3 → 8.9)

Motas de la sección 6, contador de errores enganchado a la consola, fundido de 0.12 s al cambiar
de nivel, dígitos que no cambian de ancho, y la fila `HUD x.x MS` con el coste propio.

### Fase 4 — La familia (8.9 → 9.3)

`SaveTelemetryHUD`, las dos sondas de rendimiento (de IMGUI al dialecto), las etiquetas de
`AIDebugOverlay` y la consola, todas en el dialecto de herramienta. La sección Herramientas del
Editor General pasa a leerse de un registro de superposiciones en vez de una entrada escrita a
mano por cada una.

---

## 9. Qué NO hacer

- **No arreglar D1 sin D3.** Dibujar el fondo sin quitar `raycastTarget` convierte la esquina del
  minimapa en una zona muerta para el clic de combate.
- No dibujar la gráfica con un `Image` por columna ni reconstruir la malla del TMP cada frame.
- No usar oro, bisel ornamental, verde de vida ni azul de maná en una herramienta.
- No añadir un segundo contador de FPS en otro sitio: `PerformanceMonitor` es el dueño de la
  medida, el panel es solo su lectura.
- No hacer el panel arrastrable ni redimensionable. Una herramienta con geometría persistida es
  una herramienta que un día abre fuera de pantalla; una banda fija no.
- No esconder el coste del propio panel.
- No meter el panel en la columna del minimapa: es la mitad derecha de D2.

---

## 10. Lo construido (mismo día) y la nota tras medirlo

Fases 0 a 3 construidas y verificadas en vivo a 1600x800 el 2026-09-11. La fase 4 (llevar
`SaveTelemetryHUD`, las sondas IMGUI, `AIDebugOverlay` y la consola al dialecto) sigue abierta.

```text
Core/Diagnostics/FrameTimeHistory   anillo de frames, ventana en SEGUNDOS, percentil por rango, sin asignar
Core/Diagnostics/FrameHitchLog      la regla del tirón (x2 la mediana Y >= 25 ms) y los ultimos 8
Core/Diagnostics/ConsoleLogTally    errores y avisos de la consola, thread-safe
Core/PerformanceMonitor             UNICO dueno de la medida: muestrea siempre, aunque nada se vea
Data/UI/DebugHudStyle               el dialecto: superficies del panel del jugador + semaforo propio
UI/HUD/Debug/DebugHUD(.Build/.Readouts/.Events)   niveles, maquetacion en texels, lecturas, motas, informe
UI/HUD/Debug/DebugFrameGraph        una columna por frame en una textura en anillo, desplazada por UV
UI/HUD/Debug/DebugHudCounters       ProfilerRecorder (CPU, lotes, SetPass, asig/frame, memoria) + GPU
Gameplay/Bootstrap/DevConsole.Commands.DebugHud   debughud [0-3|informe|copiar|reiniciar]
```

**Cada defecto de la sección 3, cerrado:** D1 el panel se maqueta solo, sin `ContentSizeFitter`
(163-171 texels medidos); D2 banda `HudLayout.ToolColumn*`, a la izquierda, orden 190; D3 solo las
cabeceras y COPIAR reciben clics (medido: 0 aciertos en el cuerpo, 1 en la cabecera); D4 CERCA
clasifica con `EntityFaction.SideOf` y ordena por distancia; D5 los botones son los del ratón, leídos
de `PlayerController` con el cooldown del LIBRO; D6 la fila de cuerpo a cuerpo desapareció; D7 GC por
minuto de la sesión; D8 una ventana de 3 s para todo; D9 celdas en posiciones fijas con dígitos de
ancho fijo; D10 barra de texto retirada; D11 fuente de píxel con contorno horneado; D12 F1 en el asset;
D13 todo en español desde `DebugHudText`; D14 los N más cercanos; D15 relojes `m:ss`; D16 sin asignar
en el orden; D17 la fila del jugador enseña lo que el panel de juego no (invencible, estados con tiempo).

**Tres cosas que la medida en vivo corrigió** y que ningún test habría visto:

- F3 chocaba con las sondas de rendimiento (F2-F8): el HUD va a **F1**.
- La primera versión de las motas del GC nacía encima de su contador y convirtió "4.9" en un
  glifo ilegible. Las motas de fila nacen ahora en el borde del marco y el anillo empieza fuera de
  las cifras.
- ZONA decía "-" en el Lobby: `ZoneManager` no está en el `ServiceLocator`. Se resuelve con el
  mismo fallback que usan las misiones, cacheado y reintentado como mucho una vez por segundo.

**Dos defectos ajenos encontrados de paso:** el comando `boot` no imprimía nada y `boot all` nunca
había funcionado (el `Handler` de la consola es un `Action` que recibe el nombre del comando en
`args[0]` y descarta lo que devuelve la lambda). Arreglado y fijado por
`DevConsoleHandlerContractTests`. Y el p99 de 1..100 salía 100 por redondeo de float
(`0.99f * 100` es 99.0000x): lo cazó el primer test.

**Medido:** coste propio 0.05 ms/frame (visible en la cabecera); motas vivas tras un tirón
sintético con GC: 7 (5 del tirón + 2 del GC); nivel persistido entre sesiones de Play; el acordeón
pliega JUGADOR al abrir COMBATE en una banda de 181 texels.

| # | Eje | Antes | Después | Por qué no más |
| --- | --- | --- | --- | --- |
| 1 | Legibilidad y contraste | 1.5 | 8.5 | Nombres sin tildes (la cara 3x5 no las tiene) |
| 2 | Materialidad | 1.0 | 8.0 | Sin bisel a propósito (H1) |
| 3 | Verdad de los datos | 3.0 | 8.5 | CPU/GPU/lotes son "-" en un build de release |
| 4 | Jerarquía | 3.5 | 8.0 | |
| 5 | Tipografía | 2.5 | 8.5 | Ídem 1 |
| 6 | Composición | 2.0 | 8.0 | A 1080p caben dos secciones abiertas; el resto se pliega |
| 7 | Semántica del color | 3.0 | 8.5 | |
| 8 | Visualización | 1.5 | 8.5 | Sin desglose por sistema del tiempo de CPU |
| 9 | Usabilidad | 2.5 | 8.5 | No se puede fijar una entidad concreta en CERCA |
| 10 | Rendimiento propio | 8.0 | 9.0 | |
| 11 | Coherencia | 1.0 | 6.5 | La familia de herramientas (fase 4) sigue sin el dialecto |
| 12 | Movimiento y partículas | 1.0 | 8.0 | |
| | **Ponderada** | **2.47** | **8.3** | |

---

## Medidas de referencia

| Medida | Valor |
| --- | --- |
| Rect del panel (px, origen abajo a la izquierda) | `x[1208..1588] y[788..788]`, alto 0 |
| Rect del texto | `y[798..778]`, invertido |
| Tinta del texto | `x[1220..1515] y[499..778]` |
| Minimapa (su gráfico mayor) | `x[1377..1587] y[571..781]`, cubierto al 65 % |
| Escala del canvas | 1.000 (referencia 1600x800, `match 0.5`: cumple el contrato de `HudLayout`) |
| Cuerpo y línea | 13 px, línea 14.95 px (`lineSpacing -8`) |
| `BuildText` | 0.069 ms, ~4 KB por llamada (granularidad de página de Mono) |
| Malla TMP | 0.31 ms por reconstrucción |
| Coste amortizado | ~0.08 ms/frame a 5 Hz |
| Aciertos de raycast en el centro del panel | 0 (por D1) |
| Entradas de `Monsters` neutrales | 6 de 11 |
| `█ ░ ─` en la fuente principal | no (fuente de reserva) |

Contraste WCAG por color y suelo (4.5:1 es el mínimo para texto):

| Color | Adoquín de día | Madera | Hierba | Noche | Fondo declarado |
| --- | --- | --- | --- | --- | --- |
| Cabecera | 2.83 | 3.07 | 2.47 | 10.24 | 7.97 |
| Etiqueta | 1.60 | 1.74 | 1.39 | 5.78 | 4.50 |
| Valor | 4.05 | 4.40 | 3.53 | 14.67 | 11.41 |
| Atenuado | 1.10 | 1.19 | 1.04 | 3.97 | 3.09 |
| Listo | 2.95 | 3.21 | 2.58 | 10.70 | 8.33 |
| Monstruo | 1.96 | 2.14 | 1.72 | 7.12 | 5.54 |
| Separador | 1.60 | 1.47 | 1.84 | 2.26 | 1.76 |
