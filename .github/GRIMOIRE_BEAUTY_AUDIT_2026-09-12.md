# Auditoría de belleza y UI/UX del GRIMORIO

**Fecha:** 2026-09-12 · **Nota global ponderada: 1.1 / 10** (sin ponderar 1.3) · Objetivo: **≥ 8.5**

Alcance: la pestaña GRIMOIRE de la ficha de personaje — `Gameplay/HUD/SpellTreeHUD.cs` (327
líneas, un solo fichero) más la franja de pestañas que la enmarca
(`UI/HUD/CharacterSheetController.cs` + `.UIBuilder.cs`), y los datos (`SpellTree`, `SpellNode`,
`KnownSpells`) allí donde la vista miente sobre ellos o los ignora. Fuera de alcance: la lógica
de `KnownSpells` (que es correcta y está probada), el editor de hechizos, y el árbol de talentos
`SkillTreeHUD` — hermano del grimorio, con exactamente los mismos defectos, que debe
reconstruirse en la misma pasada y por eso se cita en todas las secciones.

Método: lectura completa del código que lo dibuja, medición en vivo en Play Mode (1600x800),
aritmética sobre el contrato del canvas, composición en espacio LINEAL de cada par de colores
(el proyecto es lineal) y la captura adjunta por el usuario. Todo número de este documento está
medido o derivado, nunca estimado, salvo donde se dice.

Documentos hermanos: **`HUD_VISUAL_LANGUAGE.md`** (el contrato: R1-R16 y H1-H9),
**`INVENTORY_HUD_BEAUTY_AUDIT_2026-09-11.md`** (la primera ventana de juego reconstruida contra
él) y **`SPELL_BAR_BEAUTY_AUDIT_2026-09-11.md`** (que dejó abierta la brecha del final: un
hechizo aprendido sin acción de teclado no cabe en la barra).

---

## 0. El diagnóstico en una frase

**El grimorio del jugador es la vista de DEPURACIÓN del grimorio; la vista hermosa ya existe y
solo la ve el autor.**

`Gameplay/Editors/Spells/Graph/` contiene 1727 líneas que dibujan exactamente esto como una
CONSTELACIÓN: `SpellGraphSprites` genera el socket biselado, la placa, el halo, el canal del
conector y las marcas de capstone; `SpellGraphLayout.Resolve` coloca los nodos y rutea las
aristas de prerrequisito; `SpellGraphView` lo pana, lo hace zoom y resuelve el icono de cada
nodo por la cadena `iconOverride` → `spell.iconSprite` → glifo de rol. No tiene una sola línea
bajo `#if UNITY_EDITOR`, no toca `AssetDatabase`, y vive en **`Valkur.Gameplay`** — la misma
asamblea que `SpellTreeHUD`. Es decir: es alcanzable desde el grimorio del jugador hoy, sin
mover un fichero.

Lo que el jugador ve en su lugar es una lista de `UnityEngine.UI.Text` con Arial sobre un
rectángulo negro translúcido:

> `Slash (1 AP) — Available · Unlocks Slash (Damage)`

Los 71 nodos del grimorio **resuelven los 71 un icono** (medido en vivo), 62 de 71 declaran
prerrequisitos y 62 declaran requisito de nivel. Nada de eso llega a la pantalla: ni un icono,
ni una arista, ni una fila de árbol. La estructura existe, el arte existe, el renderizador
existe, y el jugador recibe un volcado de texto.

---

## 1. Medidas

Medido en Play Mode a 1600x800 salvo donde se indica.

| Qué | Medido | Por qué importa |
|---|---|---|
| Nodos del grimorio | **71** en 9 escuelas (arcane 10, radiance 10, martial 9, pyromancy 9, shadow 9, verdant 9, cryomancy 8, ki 8, storm 8) | Es un árbol real, no un prototipo |
| Nodos que resuelven icono | **71 de 71** (`SpellNode.ResolveIcon()`) | Cero se dibujan |
| Hechizos del catálogo con `iconSprite` | **82 de 104**, en `Art/UI/spells/` (73 PNG, 320² y 1024²) | El arte está pagado y sin usar |
| Nodos con prerrequisito | **62 de 71** | El grafo existe; la vista es plana |
| Nodos con requisito de nivel | **62 de 71** | Ver D1 |
| Nodos con `description` escrita | **0 de 71** | No hay prosa que enseñar: ver D6 |
| Escuelas con `flavour` escrito | **9 de 9** | Escrito por el sembrador, **leído por nadie** |
| `SpellRole` escrito por nodo | **71 de 71** | Su propio tooltip dice que existe para "el filtro de rol del grimorio". No hay filtro |
| Referencia del canvas | **800x600, match 0** → factor **2.0** a 1600x800 | `HudLayout` declara 1600x800 / match 0.5. Es el defecto que ya reventó `MusicHUDCanvas` y `ToastCanvas` |
| Órdenes de dibujo | Grimorio 60, talentos 60, personaje 60, récords 70, franja 120 | Minimapa (105), música (140) y toasts (200) se dibujan **encima** del grimorio abierto |
| Desborde de la franja de pestañas | Necesita **548 u** en una franja de **480 u** → **136 px** fuera del panel, hasta x=1416 con el panel acabando en 1280 | Invade la columna del minimapa |
| Solape del botón cerrar | La X ocupa x[442..472] u; la pestaña RECORDS x[416..548] u → **30 u de solape** | Es el `RX` de la captura: la X dibujada encima de RECORDS |
| Nombres de escuela truncados | **8 de 9** ("Pyroma", "Cryoman", "Stormcal", "Radianc", "Umbram", "Verdant", "Inner", "Martial") | 9 pestañas en 752 u de ancho forzado, Arial 12 |
| Fila aprendida vs no aprendida | L=0.0205 vs 0.0092 → **ratio 1.19 : 1** | "Ya sé este hechizo" se dice con un 19 % de luminancia |
| Misma fila, distinto suelo | L=0.0092 sobre piedra oscura vs 0.0177 sobre arena clara → **93 % más clara** | El panel es negro al 85 %: su superficie no es una constante |
| Botón Aprender, etiqueta NEGRA | Umbramancy **3.17 : 1**, Arcana 5.93, Pyromancy 6.72 | WCAG AA pide 4.5. El negro está escrito a fuego para las nueve escuelas |
| Pestaña activa, etiqueta BLANCA | Radiance **1.68 : 1**, Stormcalling 1.93, Inner Fire 2.33, Martial 2.37, Cryomancy 2.71 | **5 de 9** por debajo de 3 : 1. La pestaña activa es la menos legible |
| Activa vs inactiva (luminancia) | Umbramancy 2.23 : 1 … Radiance 6.49 : 1 | "Qué escuela está abierta" es tres veces más obvio en una escuela que en otra |
| Texto de la fila sobre su fondo | 17.7 : 1 | Lo único que pasa contraste |
| `ScrollRect` | **0** | 10 nodos caben justos; la hoja de ruta apunta a 100 hechizos |
| Fuente | `LegacyRuntime.ttf` vía `UnityEngine.UI.Text` | **Tercera** familia tipográfica del juego, y la franja de pestañas 60 px más arriba es TMP |
| Suscriptores de `OnSpellLearned` / `OnNodeLearned` / `OnPointsChanged` | **0** en todo el proyecto | Aprender un hechizo no produce un píxel en ningún sitio |
| Partículas | **0** | |
| Sonidos | **0** llamadas | |
| Ficheros de test que nombran `SpellTreeHUD` o `CharacterSheetController` | **0** | El grafo del EDITOR sí tiene los suyos (`SpellGraphFramingTests`) |
| `new Color(` literales | 6 en `SpellTreeHUD`, 5 en `CharacterSheetController.UIBuilder` | Cero tokens de `HudTheme` |
| Idioma | 100 % inglés ("Grimoire", "Learn", "Available", "Locked", "arcane point(s)") | El juego está en español |

---

## 2. Lo que ya está bien

Para que la nota sea justa, y porque estas piezas se conservan enteras:

- **El modelo.** `KnownSpells` es correcto: reemplaza el libro en vez de sumar (para que un
  respec pueda quitar), cobra el recargo de afinidad redondeando hacia arriba, y emite tres
  eventos en el momento exacto. `PlayerProgression.OnGrimoireChanged` reconstruye la capa de
  estadísticas y sincroniza el libro. Aprender FUNCIONA; lo que no hace es verse.
- **La decisión de separar talentos y grimorio**, con dos monedas. Está argumentada en el
  propio fichero y es la razón de que el panel exista.
- **La afinidad se dice en voz alta.** La cabecera escribe `(affinity)` o
  `(off-affinity ×2)`, y el motivo de rechazo por puntos nombra la escuela y la clase. Es la
  única parte del panel que enseña una regla del juego.
- **Los nueve acentos.** Nueve colores distintos y bien elegidos, escritos en los assets, uno
  por escuela. Es exactamente el dato que hace falta; lo que falla es cómo se usa (tintes
  planos a alfa 0.5 / 0.65 / 0.85).
- **Los nueve `flavour`.** "El cielo no lleva libros y lo salda todo de golpe" para
  Stormcalling. Está escrito, es bueno, y no se enseña.

---

## 3. Puntuación por eje

| # | Eje | Nota | Evidencia |
|---|---|---|---|
| 1 | Composición / layout | 3 | Cabecera → pestañas → lista es un orden correcto. Pero no hay panel de detalle, ni pie, ni sitio para nada más, y hay DOS franjas de pestañas apiladas (la de la ficha y la de escuelas) que no se parecen en nada |
| 2 | Jerarquía visual | 2 | Todas las filas miden 30 px. El nodo raíz de una escuela y su capstone de 3 AP se dibujan idénticos |
| 3 | Estructura de árbol | **0** | 62 de 71 nodos tienen prerrequisito y `row`/`column` escritos. La vista es una lista plana: no hay una sola arista en pantalla |
| 4 | Materialidad del marco | 1 | `Image` sin sprite, negro al 85 %. Ni chaflán, ni bisel, ni hueco. El mundo se ve a través |
| 5 | Materialidad de las filas | 1 | Rectángulo de color sin textura; el suelo del mundo cambia su luminancia un 93 % |
| 6 | Iconos | **0** | 71 de 71 nodos resuelven icono. Se dibujan cero |
| 7 | Estado del nodo | 2 | Conocido / disponible / bloqueado se distinguen por un prefijo de texto y **1.19 : 1** de fondo |
| 8 | Paleta | 4 | Nueve acentos buenos, usados como tintes planos. Cero tokens compartidos |
| 9 | Tipografía | 1 | `Text` + Arial a 12/13/17 px, sin contorno. Tercera familia del juego, y TMP en la franja de encima |
| 10 | Idioma / textos | **0** | Todo en inglés, incluido `"arcane point(s)"` con el plural entre paréntesis |
| 11 | Nombres de escuela | 1 | 8 de 9 truncados sin elipsis. La escuela es la unidad de navegación del panel |
| 12 | Contraste | 3 | El texto de fila pasa (17.7). El botón Aprender en Umbramancy da 3.17 y la pestaña activa de Radiance 1.68 |
| 13 | Redundancia de forma (daltonismo) | 1 | La pestaña abierta es solo brillo; el botón Aprender es solo color; el estado del nodo es solo texto |
| 14 | Coste y afinidad | 5 | La cabecera y el motivo lo explican bien. No se dice cuántos puntos quedarían, ni se marca qué es asequible AHORA |
| 15 | Motivo del bloqueo | 2 | Orden invertido (coste antes que nivel y prerrequisito), **un solo** motivo, y truncado (D1, D2) |
| 16 | Texto recortado | 1 | `Text` envuelve a una segunda línea dentro de una fila de 30 px con `verticalOverflow: Truncate`: la mitad de la frase se pierde en silencio |
| 17 | Desplazamiento / escala | 1 | Sin `ScrollRect`. 10 filas caben; 11 se dibujan fuera del contenedor, que no tiene máscara |
| 18 | Filtro por rol | **0** | `SpellRole` escrito en los 71 nodos, y su tooltip dice literalmente que existe para el filtro del grimorio |
| 19 | Búsqueda | **0** | Con 73 hechizos y objetivo de 100 |
| 20 | Detalle / tarjeta | **0** | No hay panel de detalle. El único texto generado, `DescribeEffects()`, produce "Unlocks Slash (Damage)": repite el nombre de la fila |
| 21 | Prosa / ambientación | **0** | 9 `flavour` escritos y 0 leídos; 0 de 71 `description` escritas |
| 22 | Previsualización del hechizo | **0** | El editor de hechizos tiene preview en vivo. El jugador compra a ciegas |
| 23 | Motion | 1 | Abrir y cerrar es `SetActive`. Cada refresco **destruye y reconstruye** las 9 pestañas y las 10 filas |
| 24 | Feedback al aprender | **0** | El único momento que el panel existe para producir. Tres eventos, cero suscriptores: ni toast, ni sonido, ni mota, ni reacción del HUD |
| 25 | Partículas / VFX | **0** | Ninguna. Sección 5 |
| 26 | Sonido | **0** | Ninguna llamada |
| 27 | Rejilla de píxel | 1 | Canvas a 800x600 match 0 → factor 2.0 contra el 1.0 del resto del HUD |
| 28 | Posición y convivencia | 2 | El panel centrado libra los instrumentos por casualidad, no por derivación de `HudLayout`; y su franja de pestañas se sale 136 px hacia el minimapa |
| 29 | Coherencia con el resto del HUD | 1 | Quinto dialecto visual: ni piedra, ni oro, ni fuente de píxel, ni marco, ni motas |
| 30 | Tema / tokens | 1 | 11 literales de color entre los dos ficheros; `HudTheme` existe y no se lee |
| 31 | Accesibilidad | 1 | Sin navegación por teclado ni mando, sin foco visible, Arial 13 px, sin redundancia de forma |
| 32 | Estado espiritual (R12) | **0** | En forma de espíritu el mundo es gris y el grimorio sigue en color |
| 33 | Cobertura de test | 1 | Cero fixtures sobre la vista. El grafo del editor sí tiene los suyos |
| 34 | Rendimiento | 6 | Correcto a 10 filas. La reconstrucción total por refresco es la forma del hitch de 3.5 s del editor de Items, a escala pequeña |

**Media ponderada: 1.1.** Pesan doble los ejes 3, 4, 5, 6, 20, 24, 25 y 29 — los que separan
"funciona" de "hermoso", mismo criterio que la auditoría del panel del jugador y la del
inventario.

---

## 3.1 Estado: F0 cerrado (2026-09-12)

Los ocho defectos de la sección 4 están arreglados y la fase está verde: **27/27 en 1.59 s**
(`SpellLockReasonTests`, `GrimoireChromeTests`, `ShippedGrimoireDataTests`), consola de Unity
limpia, DLL verificada más nueva que el `.cs` más nuevo. El panel sigue siendo una lista —
el tablero de constelación es F1 — pero ya no miente.

| Defecto | Qué se hizo |
|---|---|
| D1 | `KnownSpells.CollectLockReasons` devuelve **todos** los impedimentos en orden **nivel → prerrequisito → puntos**. `SpellLock` los lleva como DATOS (números, nodo, recargo, escuela) para que la vista los diga en su idioma y la consola en el suyo. `CanLearn(out string)` delega y conserva su API |
| D2 | Fila de dos líneas, `verticalOverflow: Overflow` en todas las etiquetas, y motivos cortos por construcción ("Nivel 15 · Requiere Grito de guerra · Faltan 2 PA") |
| D3 | La franja **reparte** el ancho que tiene: `HorizontalLayoutGroup` con relleno derecho que RESERVA la X, `LayoutElement` por pestaña (preferido 132, mínimo 72), etiquetas TMP con autoescalado. Tres estáticos públicos (`StripWidthAtReference`, `TabsPreferredWidth`, `TabsMinimumWidth`) hacen el encaje comprobable, porque uGUI no hace layout en EditMode |
| D4 | Los cinco canvas de la ficha escalan contra `HudLayout.ReferenceWidth/Height/Match` |
| D5 | Bandas declaradas: `HudLayout.CharacterSheetSortingOrder` (210) y `...ChromeSortingOrder` (211), por encima del minimapa (105), la música (140) y las herramientas (190), por debajo de la tienda (220) |
| D6 | El `flavour` de la escuela se dibuja bajo la cabecera. Nueve cadenas escritas que llevaban toda la vida del proyecto sin lector |
| D7 | El rol se dibuja como etiqueta propia y traducida en cada fila. El FILTRO sigue siendo F2 |
| D8 | `GrimoireText` con `GameLanguage.Pick(en, es)`, plurales resueltos en ambos idiomas, y los nueve assets de escuela traducidos al español (nombre y `flavour`), verificado memoria contra el texto del fichero, 0 caracteres de reemplazo |

Dos cosas más que entraron con F0 y no estaban en la lista de defectos:

- **El panel es OPACO.** Era negro al 85 %, y la misma fila medía un 93 % más de luminancia
  sobre arena clara que sobre piedra oscura. R3.
- **La lista vive en un `ScrollRect` con máscara.** Sin él, la décima fila de una escuela se
  dibujaba fuera del contenedor —que no tenía máscara— y la undécima en cualquier sitio.

Lo que F0 deliberadamente NO toca, y por qué: los **nombres de los 104 hechizos** siguen en
inglés ("Slash", "War Cry"). Son contenido del catálogo de hechizos, compartido con la barra de
acciones, el editor y el HUD; traducirlos es su propio trabajo con su propio alcance, y hacerlo
a medias desde aquí deja el juego con dos mitades en idiomas distintos en más sitios de los que
arregla. Queda anotado como el resto de D8.

---

## 3.2 Estado: F1-F3 construidos (2026-09-12)

El panel ya no es una lista. Escrito, compilando limpio en `Valkur.Gameplay`; **pendiente de
verse en un frame renderizado**, que es la única prueba de un layout y que quedó bloqueada por
tres sesiones compartiendo este Unity.

### Lo que hay

| Fase | Qué entró |
|---|---|
| F1 | `GrimoireGeometry` (layout puro y testeable), `GrimoireNodeView` (halo, socket, placa, icono horneado, marca de rol, dos líneas de leyenda), `GrimoireLinkView` (la cadena de prerrequisito), `GrimoireNodeState`/`GrimoireNodeStatus` (los seis estados, puros) |
| F2 | Ventana en espacio de texel: raíl vertical de escuelas, tablero al centro, tarjeta de detalle a la derecha; `GrimoireStyle` en `Resources/UI/`; piedra, hueco y texto de `HudTheme`; fuente de píxel para números y etiquetas, `Text` para nombres y prosa |
| F3 | `HudMoteLayer` con los siete eventos, halo que respira solo en el nodo comprable, fundido de apertura, `GrimoireAudio` (id de catálogo con `HasSfx`, si no síntesis) |

### Decisiones que merecen quedar escritas

- **El andamio se COMPARTE, no se copia.** `SpellGraphLayout.Resolve` coloca los nodos y
  `SpellGraphSprites` dibuja socket, placa, halo, canal y marcas de rol. Los dos ya existían
  para el editor de hechizos, son `internal` en `Valkur.Gameplay` y el grimorio está en esa
  misma asamblea. Lo que es nuestro es la LECTURA del jugador: seis estados, la frontera de lo
  alcanzable, y una tarjeta que dice todos los motivos.
- **Lo que NO se comparte son las distancias.** `SpellGraphGeometry` fija 76 px por nodo y 168
  de profundidad, que es correcto para una losa a pantalla completa que un autor panea. Esta
  ventana está en rejilla de texel y su tamaño de nodo está autorado en `GrimoireStyle`, así
  que el espaciado se DERIVA de él — si no, los dos discrepan la primera vez que alguien
  retoca el estilo. Un test lo fija: doblar `nodeTexels` dobla el espaciado.
- **Raíl VERTICAL, y esa es la solución de raíz al truncado.** Nueve pestañas horizontales
  cortaban ocho de los nueve nombres. Un raíl tiene el ancho para deletrearlos y escala a una
  décima escuela sin rediseño. La escuela abierta se marca con **filete de acento**, no solo
  con brillo: medido sobre los acentos enviados, la razón de luminancia activa/inactiva iba de
  2.2 : 1 (Umbramancia) a 6.5 : 1 (Fulgor), o sea el color decía "abierta" tres veces más alto
  en una escuela que en otra.
- **El panel abre en la escuela del personaje**, no en el índice 0. La afinidad es el único
  dato que dice "esta es tuya"; abrir en la de otro es un primer frame que no dice nada de
  este personaje.
- **El botón Aprender está en la TARJETA, no en cada nodo.** Un botón por fila invita a
  comprar sin leer, y la tarjeta es el único sitio con espacio para la lista completa de
  motivos — que es de lo que iban D1 y D2.
- **La redundancia de forma es la LEYENDA, no un glifo de candado.** Un candado dice
  "bloqueado" y el jugador aún tiene que hacer clic para saber por qué; "NIVEL 15" lo dice
  desde el otro lado del tablero. La leyenda ya estaba ahí para el nombre, así que la segunda
  línea no cuesta nada y lleva el motivo entero.
- **El icono se HORNEA al tamaño que se dibuja.** Los 73 iconos son de 320 y 1024 px,
  bilineales, sin mipmaps y dentro de un atlas; minificados por el sampler a un socket de
  ~40 px, titilan. `HudTextureBaker.Icon` los reduce por mitades en la GPU (R10). Si no hay
  dispositivo gráfico —lo que tiene una tanda en batch— cae al sprite crudo.
- **Un enlace para en el BORDE del socket, no en su centro.** El anillo no es opaco (su bisel
  baja a 0.42 de alfa y la placa a 0.72), así que un cable "escondido detrás" de un nodo se
  dibuja hasta a un 58 % cruzándole la cara.
- **Un prerrequisito de OTRA escuela no dibuja cadena.** No hay nada en este tablero a lo que
  apuntar, e inventar un anclaje es trazar una línea a un sitio que no significa nada.
- **Las motas se emiten por DIFERENCIA, no desde el manejador del clic.** El panel compara lo
  que dibujó la última vez con lo que dibuja ahora, así que la consola, un nivel nuevo o una
  partida restaurada producen el mismo feedback que el botón. Un panel que emitiera desde su
  propio clic estaría mudo en todos los demás caminos menos uno.
- **Un rechazo no emite NADA**: un temblor de un texel y el motivo en la tarjeta. Una mota en
  un rechazo enseña al jugador a ignorar las motas.

### Medido en vivo (1600x800, escala 2)

Play Mode, escena `MainGameplay`, enano de nivel 0 con 1 punto arcano:

| Qué | Medido |
|---|---|
| Panel | x[320..1280] y[120..680] — **960x560 px** |
| Raíl | **124 px** = 62 texels x 2 |
| Tablero (viewport) | **592 px** |
| Tarjeta | **208 px** = 104 texels x 2 |
| Escuela abierta | índice 0 = `martial`, **"Formas Marciales"**, afinidad del enano |
| Nodos y cadenas dibujados | **8 nodos, 7 enlaces** |
| Aprender por el camino nuevo | 1 nodo comprado, puntos 4 → 3 |

Tres cosas que confirma: el panel abre en la escuela del PERSONAJE y no en el índice 0, el
nombre traducido llega a la pantalla, y el tablero construye nodos y cadenas de verdad. El
panel además cae entero dentro de x[320..1280], o sea a la izquierda de la columna del
minimapa (que empieza en ~1284) y por encima de la placa de música.

### Lo que sigue abierto

### Lo que enseñó el frame (y que ningún test podía ver)

Cuatro defectos, todos de layout, todos invisibles a las 56 aserciones que estaban en verde
mientras existían. Es la lección de este documento aplicada a sí mismo.

- **`horizontalOverflow = Overflow` ignora el ancho del rect.** Estaba puesto para "no truncar
  nunca el motivo". Es PEOR que truncar: tres leyendas dibujaron una línea cada una más ancha
  que su caja y las tres se imprimieron una encima de otra, así que no se leía ninguna en vez
  de leerse una corta. Y Unity ignora `resizeTextForBestFit` mientras el modo sea `Overflow`,
  o sea que el ajuste que yo creía activo no lo estaba.
- **El ancho de la leyenda salía del eje equivocado.** La profundidad corre en X y los hermanos
  en Y; la leyenda se dimensionaba con el paso de HERMANOS. `SpellGraphGeometry.CaptionWidth`
  ya lo dice (`DEPTH_SPACING - CAPTION_INSET`) y no se copió.
- **Una caja de dos líneas de alto 2h solo cabe si cada línea mide h.** La aritmética de las
  cajas era correcta y lo que la rompía era el tamaño de fuente (11 contra una línea de 7): la
  segunda línea del nombre se salía de su propia caja y tocaba el motivo de debajo.
- **Los nombres del raíl envolvían sobre su contador.** "Formas Marciales" rompía a dos líneas y
  la segunda caía sobre "1/8"; "Ritos Verdes" hacía lo mismo sobre "Fuego Interior". Una línea,
  encogida para caber, con el contador a la DERECHA en la misma fila.

Y una decisión de diseño que salió de mirar el frame y no el código: **el tablero lleva UN
motivo corto y la tarjeta los lleva todos.** El compuesto completo ("Nivel 6 · Requiere Dash")
envolvía a tres líneas bajo cada nodo y el bloque caía sobre el socket de abajo. El tablero
responde "qué es y si puedo"; la tarjeta responde "por qué no".

Nota para quien capture textos: **el interruptor de idioma de la máquina de desarrollo está en
`en`** (`GameLanguage.Current`, en PlayerPrefs), así que el cromo sale en inglés mientras los
nombres de escuela salen en español — esos son contenido autorado en los assets, y esa asimetría
es exactamente la regla, no un fallo.

Y la causa de las caídas de Play Mode, que costó tres capturas averiguar: **Unity rechaza o
aborta Play Mode mientras compila**, y con cuatro sesiones escribiendo casi siempre hay una
compilación en curso. Sondear `EditorApplication.isCompiling` antes de llamar a `play` y esperar
a que baje quitó los rebotes.

### Lo que sigue abierto

- ~~**Verlo.**~~ **Hecho**: el panel está capturado y verificado en un frame renderizado. Lo que
  sigue es lo de abajo. *(Nota histórica de la primera redacción: no había captura. Los tests estructurales pueden estar verdes con la
  ventana ilegible — es la lección de la propia auditoría y aquí no se ha comprobado. No es
  por falta de intentos: con cuatro sesiones sobre un mismo Unity, **Play Mode se cayó tres
  veces a mitad de secuencia**, las tres entre dos llamadas consecutivas (`playing=True
  scene=MainGameplay` en una, `playing=False scene=Bootstrap` en la siguiente, sin que nadie
  llamara a stop). Una captura necesita seis idas y vueltas — entrar, cargar escena, esperar
  el arranque, abrir, comprar, capturar — y esa cadena no sobrevivía a la contención.)*
- ~~`Resources/UI/GrimoireStyle.asset` no está creado~~ — **hecho**, verificado por el accesor
  que usa el juego (`Resources.Load("UI/GrimoireStyle")`) y no por la ruta, con `hudFxShader`
  cableado a `Valkur/UI/HudFx` para que un build del jugador no llame nunca a `Shader.Find`.
- **El filtro de rol y la búsqueda** (ejes 18 y 19) siguen sin existir. El dato está y el rol
  ya se dibuja por nodo y en la tarjeta; falta la fila de filtro al pie.
- **El marco sigue siendo un rectángulo de piedra** con un contorno de un texel, no el atlas
  generado con chaflán y bisel que tiene el inventario. `GrimoireArt` no existe.
- **La ventana no se arrastra, ni se cierra por su propio cromo, ni recuerda su sitio**: sigue
  colgada del marco de la ficha de personaje, cuyo cromo lo reconstruye otra sesión.
- **R12 (gris de espíritu)** no está.
- **Los 104 nombres de hechizo siguen en inglés.** Contenido del catálogo, su propio alcance.
- **F4** — la barra de cargables — sin ella un hechizo aprendido sigue sin tener dónde
  pulsarse: 73 hechizos contra 24 acciones fijas de teclado.

---

## 4. Defectos: arreglar antes de embellecer

Un grimorio precioso que miente sigue estando roto. Estos van primero y ninguno necesita arte.

- **D1 — el motivo del bloqueo está en el orden equivocado.** `KnownSpells.CanLearn` prueba
  COSTE, luego NIVEL, luego PRERREQUISITO, y devuelve el primero. Un nodo de nivel 15 que
  además encadena tres prerrequisitos responde `Need 2 arcane point(s), have 1`: al jugador se
  le dice que ahorre para un hechizo que no podrá tocar en quince niveles. En la captura le
  pasa a `Scatter Volley` (nivel 10) y a `War Cry` (nivel 15). El sistema hermano ya tiene la
  regla escrita: el crafteo prueba **NIVEL primero**, "el único rechazo que el jugador no puede
  arreglar andando o recolectando" (CLAUDE.md). Y debe devolver **todos** los incumplimientos,
  como hace `CraftingService`, no el primero.
- **D2 — la frase del motivo se pierde a mitad.** `Text` con `horizontalOverflow: Wrap` y
  `verticalOverflow: Truncate` dentro de una fila de 30 px a 13 px de cuerpo: la segunda línea
  no se dibuja y no hay elipsis. En la captura: `War Cry (2 AP) — Locked: Need 2 arcane
  point(s), have`. El jugador no puede terminar de leer por qué no puede comprar.
- **D3 — la franja de pestañas de la ficha se sale de su propia franja.** Cuatro pestañas de
  132 u + hueco + inserción = 548 u sobre 480 disponibles. La X de cerrar se dibuja **dentro**
  de la pestaña RECORDS (30 u de solape) y el conjunto invade 136 px de la columna del
  minimapa. Es el `RX` de la captura. La causa raíz es el ancho fijo: la franja debe repartir
  a partes iguales el espacio que tiene, con un ancho mínimo, y reservar la X antes de
  repartir.
- **D4 — cinco canvas de la ficha escalan contra 800x600 con match 0.** Factor 2.0 a 1600x800
  contra el 1.0 del resto. `HudLayout.ReferenceWidth` / `ReferenceHeight` / `Match` existen
  para esto y el contrato lo exige (R9). Es el mismo defecto que ya reventó la fuente del
  registro de misiones.
- **D5 — tres paneles comparten `sortingOrder` 60 y el minimapa (105), la música (140) y los
  toasts (200) se dibujan encima del grimorio abierto.** Los tres paneles son mutuamente
  exclusivos, así que hoy no muerde; es la forma exacta de la colisión que sí mordió en la pila
  de barras del mundo. Las bandas salen de `HudLayout`, no de un número escrito en el widget.
- **D6 — `flavour` está escrito nueve veces y leído cero.** Lo escribe `SpellTreeSeeds`, lo
  serializa el asset, y el único lector del proyecto es el propio sembrador. Misma familia que
  `animation_map.json`: escrito, redondeado en disco, inerte.
- **D7 — `SpellRole` no tiene filtro.** 71 de 71 nodos lo declaran y su propio enum dice que
  existe para "estructura por escuela, búsqueda por rol". Ese filtro no existe. *(Corrección
  sobre la primera redacción de esta auditoría, que decía que el rol no se dibujaba en
  absoluto: sí se dibujaba, dentro del texto generado por `DescribeEffects()` — "Unlocks Slash
  (Damage)" — es decir en inglés y como parte de una frase, no como una etiqueta. Lo que faltaba
  y sigue faltando hasta F2 es el filtro.)*
- **D8 — todo en inglés.** Y `"arcane point(s)"` no es un plural, es un marcador de posición.
  R15 lo prohíbe explícitamente para las ventanas de juego.

---

## 5. Partículas: qué debe haber y qué no

El grimorio tiene **cero**. Eso no es neutro: el panel existe para un acto — gastar un punto
arcano — y ese acto hoy no produce ni un píxel. El jugador pulsa Learn, la lista se reconstruye,
y lo único que cambia es que una palabra pasa de "Available" a "Known".

La regla es **R8** y no se negocia: `HudMoteLayer` y nada más. Un `Graphic` por panel, pool de
capacidad fija, material aditivo de `HudFx`, posiciones en texels enteros, vida máxima 0.6 s
(0.9 s solo para subir de nivel), **nunca** un `ParticleSystem` en un canvas overlay, **nunca**
emisión en reposo. `HudMoteLayer` es `public` y vive en `Gameplay/UIKit/Hud/` desde el
2026-09-11: el grimorio puede usarlo hoy sin tocar una asamblea.

### 5.1 Los siete eventos, y qué dice cada uno

| Evento | Qué se emite | Qué dice |
|---|---|---|
| **Aprender un nodo** | Ráfaga desde el socket en el acento de la escuela: 14-18 motas, anillo corto, y el icono pasa de desaturado a color en 0.18 s | Es EL momento. Es el único que puede permitirse ser grande |
| **La cadena se abre** | Las aristas que salen del nodo recién comprado se encienden una vez, del nodo hacia sus hijos, 0.35 s | Enseña la consecuencia: "esto acaba de desbloquear aquello". Es lo que convierte una compra en un camino |
| **Nodo que pasa a ser asequible** | Dos o tres motas sobre el socket, una sola vez, cuando el jugador gana el punto que le faltaba | Convierte "tengo puntos" en "tengo puntos PARA ESTO" sin abrir el panel |
| **Puntos ganados con el panel abierto** | Motas subiendo hacia el monedero de la cabecera + el número cuenta | Hoy el número salta |
| **Cambiar de escuela** | Barrido corto de motas en el acento nuevo, desde el raíl hacia el tablero | Cubre el corte: nueve nodos y todas sus aristas se reemplazan en un frame, que es la misma costura que `WeaponSwapFlashFX` tapa |
| **Compra rechazada** | **Nada.** Solo un temblor de 1 texel del socket y el motivo en la tarjeta | Una mota en un rechazo enseña al jugador a ignorar las motas |
| **Capstone de escuela completado** | La ráfaga de aprender más un pulso del sigilo de la escuela en el raíl, 0.9 s | Único caso que merece la vida larga, igual que subir de nivel |

### 5.2 Lo que NO debe tener, y por qué

- **Nada que brille en reposo.** Un grimorio con partículas flotando permanentemente es un
  salvapantallas: R7 dice que en reposo solo se mueven los relojes. Es la tentación grande aquí
  — "motas de magia sobre el libro" — y hay que resistirla, porque es exactamente lo que hace
  que la ráfaga de aprender deje de significar algo.
- **Aristas con flujo continuo.** Un conector que fluye siempre es movimiento en reposo. El
  flujo se dispara al abrir la escuela y al comprar, y se apaga.
- **Un `ParticleSystem`.** No ordena con los `Graphic` de un canvas overlay, que es la razón
  documentada de que `HudMoteLayer` exista.
- **Partículas en el rechazo.** Ver la tabla.
- **Partículas como decoración del marco.** El oro y la filigrana ya dicen "esto es importante"
  sin moverse.

### 5.3 Lo que sí puede moverse sin ser una partícula

El halo del socket del nodo que el jugador **puede comprar ahora** puede respirar muy despacio
— es el único estado que invita a una acción y el equivalente exacto del anillo "activo" de la
barra de acciones. Uno solo a la vez por escuela (el más barato disponible), no todos: nueve
sockets latiendo es ruido.

---

## 6. De dónde copiar: coherencia con el resto del HUD

El usuario pide persistencia con el resto de la UI. Casi todo está construido; esto es el mapa
de qué pieza se toma de dónde, y ninguna es nueva.

| Qué necesita el grimorio | De dónde sale | Ya lo hace |
|---|---|---|
| Espacio de texel, rects enteros | `PlayerHudStyle.HudPixelScaleFor` + `HudRect` | Panel del jugador, barra de acciones, inventario |
| Marco de piedra, bisel, hueco opaco | Atlas generado propio (`GrimoireArt`), como `InventoryArt` | Inventario, placa de música |
| Tokens de color | `HudTheme` (`Resources/UI/HudTheme.asset`) | Inventario |
| Fuente | `HudPixelFont` 3x5 / 5x7; TMP **solo** para la prosa de la tarjeta | Panel del jugador, placa de música, HUD de depuración |
| Iconos nítidos | `HudTextureBaker.Icon` al tamaño exacto del socket | Panel del jugador, inventario |
| Motas por evento | `HudMoteLayer` | Panel del jugador, barra de acciones, placa de música, inventario |
| Cromo de ventana (arrastrar, cerrar, minimizar, geometría en PlayerPrefs, Escape por `EscapeOwnership`) | `WindowChrome` de la sección 5.3 del contrato, hoy repartido entre `QuestLogHUD.Window` y `MusicPlayerHUD` | Registro de misiones, música, inventario |
| Banda de pantalla y orden de dibujo | `HudLayout` + `GameWindowRightInset` (R13) | Inventario |
| Tarjeta de detalle | La tarjeta de objeto del inventario (`InventoryItemCard`), con rareza sustituida por escuela | Inventario |
| Marco de estado con redundancia de forma | La casilla de 5-6 estados de `HudAbilitySlot` / `InventorySlotView` | Barra de acciones, inventario |
| Sonido | Id del catálogo con `HasSfx`, si no sintetizado (`InventoryAudio`) | Inventario |
| Gris de espíritu (R12) | `PlayerHudStyle.spiritStone` + `SpiritTintExempt` donde no toca | Barras del mundo |
| **Socket, placa, halo, conector, marcas** | **`SpellGraphSprites`**, ya en `Valkur.Gameplay` | Editor de hechizos |
| **Colocación de nodos y ruteo de aristas** | **`SpellGraphLayout.Resolve(nodes)`**, función pura | Editor de hechizos |

Las dos últimas filas son el atajo: el andamio de la constelación no hay que escribirlo, hay
que **compartirlo**. `SpellGraphSprites` genera mapas de luminancia (blanco con alfa variable,
nunca un tono) precisamente para que un mismo socket sirva a un nodo aprendido, a uno
disponible y a uno bloqueado, y para que cada escuela tiña el suyo — es la decisión que hace
falta aquí, escrita hace semanas para otro consumidor.

Y una consistencia que va en la otra dirección: **el grimorio y el árbol de talentos deben ser
la misma ventana con dos contenidos**, no dos ventanas parecidas. Hoy `SkillTreeHUD` y
`SpellTreeHUD` son dos implementaciones independientes del mismo panel, con los mismos
defectos, copiados. Una moneda distinta y un contenido distinto no justifican dos marcos, dos
franjas de pestañas y dos listas.

---

## 7. La propuesta: el grimorio como constelación

### 7.1 La forma

Una ventana de juego (sección 5 del contrato), en el espacio de texel, con tres zonas:

- **Raíl de escuelas, vertical, a la izquierda.** Nueve entradas: sigilo de la escuela, nombre
  COMPLETO en la fuente grande, y `conocidos/total` debajo en la pequeña. Vertical resuelve de
  raíz el truncado (eje 11) y escala a doce escuelas sin rediseño. La escuela abierta se marca
  con **filete de acento + desplazamiento de 2 texels**, no solo con brillo — la redundancia de
  forma que exige R6.
- **Tablero de constelación, al centro.** Los nodos de la escuela colocados por
  `SpellGraphLayout`, unidos por sus prerrequisitos. Pan y zoom con los mismos gestos que el
  resto del proyecto. Un nodo es socket + placa + icono horneado + marca de estado.
- **Tarjeta de detalle, a la derecha.** Icono grande, nombre, sigilo de escuela, etiqueta de
  ROL, coste en puntos con "te quedarían N", requisito de nivel, cadena de prerrequisitos
  (nombrada, no "Requires 'X'"), efectos generados, y el `flavour` de la escuela al pie la
  primera vez que se abre. Botón Aprender aquí, **no en cada fila**: una decisión se toma
  después de leer, y un botón por fila invita a comprar sin mirar.

Pie: monedero de puntos arcanos con el medallón del panel del jugador, filtro de rol (los seis
valores de `SpellRole`, que resucita D7) y búsqueda. La búsqueda **atenúa** en vez de filtrar,
como hace la lista de acciones del editor de Controles: un árbol al que le faltan ramas no se
lee como un árbol.

### 7.2 Los seis estados de un nodo, cada uno con forma además de color

| Estado | Socket | Icono | Marca |
|---|---|---|---|
| Aprendido | Relleno, filete de acento | Color pleno | Ninguna: el relleno ya lo dice |
| Disponible | Hueco, halo latiendo lento | Color pleno | Ninguna |
| Faltan puntos | Hueco, apagado | Desaturado | Glifo de punto arcano con el número que falta |
| Falta nivel | Hueco, apagado | Desaturado | Numeral del nivel requerido |
| Falta prerrequisito | Hueco, apagado; la arista de entrada dibujada como cadena rota | Silueta | Glifo de candado |
| Fuera de afinidad | Cualquiera de los anteriores, más | | Filete doble en el borde del socket, y el coste en la tarjeta dice por qué |

Ningún estado se distingue solo por el color (R6), y el motivo del bloqueo se lee **en el
tablero** sin abrir la tarjeta, que es lo que convierte un árbol en un plan.

### 7.3 Las aristas dicen el camino

Una arista entre un nodo aprendido y su hijo se dibuja **encendida** en el acento; entre dos
bloqueados, apagada; una cuyo destino está a un solo requisito de distancia, en el acento
atenuado. El jugador ve la frontera de lo que puede alcanzar sin leer una palabra. Es lo mismo
que hace la niebla del minimapa con su frontera degradada, y por la misma razón.

### 7.4 Lo que se arregla del modelo

`CanLearn` pasa a devolver **todos** los incumplimientos en el orden nivel → prerrequisito →
puntos (D1). El grimorio necesita la lista, no el primero, y la tarjeta tiene sitio para ella.
El cambio es aditivo: se conserva el `out string reason` de una sola frase para los llamadores
existentes.

---

## 8. Plan por fases

**F0 — verdades (sin arte, sin riesgo).** D1 a D8. El orden y la lista de motivos, el recorte de
texto, la franja de pestañas que se reparte el ancho, `HudLayout` en los cinco canvas, las
bandas de orden, el español, y leer `flavour` y `SpellRole`. Sale un panel feo que no miente.

**F1 — el tablero.** `GrimoireBoard` sobre `SpellGraphLayout` + `SpellGraphSprites`, iconos
horneados con `HudTextureBaker.Icon`, los seis estados con sus marcas, las aristas por estado.
Aquí es donde la nota sube de golpe: es el 80 % del salto visual y no necesita arte nuevo.

**F2 — marco, raíl y tarjeta.** `GrimoireArt` (atlas generado propio, como `InventoryArt`),
`HudTheme`, fuente de píxel, raíl vertical, tarjeta de detalle, monedero, filtro de rol y
búsqueda. `WindowChrome` compartido con el registro de misiones.

**F3 — vida.** Las siete motas de la sección 5, el halo del nodo comprable, el barrido de
cambio de escuela, sonido (catálogo con `HasSfx`, si no sintetizado), fundido de 0.12 s al
abrir y cerrar, y R12 (gris de espíritu).

**F4 — la brecha que dejó abierta la barra de acciones.** Un hechizo aprendido tiene que caber
en algún sitio donde pulsarlo: hay 73 hechizos y 24 acciones fijas de teclado. Es el trabajo de
la barra de cargables (`ActionBar/SlotN` con carga desde el guardado), y es lo que hace que
aprender un hechizo tenga consecuencia. Sin F4 el grimorio es hermoso y sigue siendo un
catálogo.

**F5 — el árbol de talentos.** Misma ventana, mismo tablero, otra moneda y otro contenido.
Debería costar un día porque no se escribe nada nuevo.

---

## 9. Comprobaciones

Una regla sin comprobación es una convención, y en este proyecto se han roto todas.

- `GrimoireCanvasContractTests` — referencia, `match` y banda de orden desde `HudLayout` en los
  cinco canvas de la ficha; y la franja de pestañas cabe en su franja a 1280, 1600 y 3840 de
  ancho. Habría cazado D3 y D4.
- `GrimoirePixelGridTests` — todo rect del espacio de texel cae en texels enteros, la
  generalización del test que ya recorre el panel del jugador.
- `GrimoireContrastTests` — para las **nueve** escuelas, la etiqueta del botón y la del raíl
  contra su propia superficie compuesta en lineal, ≥ 4.5 : 1; y activa vs inactiva ≥ 3 : 1.
  Habría cazado el 1.68 de Radiance y el 3.17 de Umbramancy.
- `GrimoireNodeStateTests` — los seis estados se distinguen con el color anulado (forma o
  glifo), y ningún par comparte marca.
- `KnownSpellsReasonOrderTests` — nivel antes que prerrequisito antes que puntos, y un nodo que
  incumple tres devuelve tres.
- `ShippedGrimoireDataTests` — los 71 nodos resuelven icono, las 9 escuelas tienen `flavour`,
  todo `SpellRole` cae en el filtro, ningún `nodeId` duplicado.
- `HudDialectGuardTests` (extensión) — `Gameplay/HUD` no puede crear `UnityEngine.UI.Text`, y
  la lista blanca de TMP cubre solo la prosa de la tarjeta.
- `HudKeyLabelTests` / `HudOpenPositionTests` (extensión) — español y R13 para esta ventana.
- `GrimoireMoteTests` — el panel no emite en reposo, y un rechazo no emite nada.

---

## 10. Lo que NO hace esta auditoría

- **No propone tocar `KnownSpells` más allá de D1.** El modelo es correcto y está probado; el
  problema es entero de la vista.
- **No propone un segundo renderizador de grafo.** `SpellGraphView` seguirá siendo la vista del
  AUTOR (selecciona, no compra); lo que se comparte es el andamio, no la ventana.
- **No propone esconder las escuelas sin afinidad.** Esa decisión ya está tomada y argumentada
  en el propio fichero: cobrar de más convierte una clase en una tendencia, esconderla la
  convierte en un muro.
- **No propone arte nuevo.** Los 71 iconos existen y el andamio se genera. Si más adelante se
  pinta un sigilo por escuela, entra en el mismo hueco donde hoy va el generado.

---

# Segunda auditoría — 2026-09-12, después de F0-F3

**Nota global ponderada: 6.8 / 10** (era 1.1) · Objetivo sigue siendo **≥ 8.5**

Método: lectura del código enviado, aritmética del layout ejecutada sin Play Mode sobre las
nueve escuelas reales, y la captura del panel abierto sobre el mundo. Todo medido.

## A. El diagnóstico, otra vez en una frase

**Ya es un grimorio; todavía no responde.** El tablero dice qué hay, qué tienes y qué te falta,
y lo dice bien. Lo que no hace es reaccionar a quien lo mira: no se puede filtrar, ni buscar, ni
navegar con teclado, el ratón deja de previsualizar en cuanto eliges algo, y el icono de la
tarjeta se dibuja con una textura horneada para un socket de 27 px dentro de una caja de 64.

## B. Medidas nuevas

Aritmética pura, sin canvas y sin Play Mode, sobre las nueve escuelas enviadas:

| Escuela | Nodos | Tablero (texels) | Zoom | Nodo en pantalla |
|---|---|---|---|---|
| arcane | 9 | 249x159 | 1.00 | 44 px |
| radiance | 9 | 249x159 | 1.00 | 44 px |
| pyromancy / shadow / verdant | 8 | 249x159 | 1.00 | 44 px |
| martial | 8 | 249x107 | 1.00 | 44 px |
| cryomancy | 7 | 249x159 | 1.00 | 44 px |
| ki / storm | 7 | 249x107 | 1.00 | 44 px |

Ventana 480x280 texels; columna del tablero **296x246**.

| Qué | Medido | Lectura |
|---|---|---|
| Uniformidad entre escuelas | **9 de 9 a zoom 1.00** | El objetivo de diseño se cumple: un nodo no cambia de tamaño al cambiar de escuela |
| Ocupación del tablero | **84 % del ancho, 43-65 % del alto** | Hay sitio libre, y es donde caben el filtro y la búsqueda |
| Icono de la tarjeta | horneado a **27 px**, dibujado en **64 px** | **2.4x de aumento**: la caché de iconos está keyada por nodo y no por tamaño, así que la tarjeta reusa el horneado del tablero |
| `HudLayout.ApplyScaler` | **0 usos** en el grimorio | El panel ignora el tamaño de interfaz del jugador (`GameSettings.uiScale` x `TextScale()`), que es una preferencia viva que otras cuatro superficies ya respetan |
| `new Color(` en el grimorio | **15** (7 en NodeView, 4 en LinkView, 3 en Card, 1 en Board) | Ninguno sale de `GrimoireStyle` |
| Referencias a `_theme` | 24 | La migración a `HudTheme` está hecha a medias: la ventana sí, las piezas del tablero no |
| Estado espiritual (R12) | **0 hits** | El mundo se vuelve gris y el grimorio sigue en color |
| Arrastrar / cerrar / recordar sitio | **0 hits** | Sigue colgado del marco de la ficha |
| `SpellRole` leído por la vista | 0 | El filtro sigue sin existir |
| Navegación por teclado | 0 | Ni flechas, ni Intro, ni mando |

## C. Puntuación por eje (comparada con la primera)

| # | Eje | Antes | Ahora | Por qué |
|---|---|---|---|---|
| 1 | Composición | 3 | **8** | Raíl, tablero y tarjeta, con bandas derivadas y comprobadas |
| 2 | Jerarquía visual | 2 | **7** | Socket lleno/hueco/apagado, halo solo en lo comprable, filete en la escuela abierta |
| 3 | Estructura de árbol | 0 | **8** | Cadenas dibujadas, con tres lecturas (abierta / a un paso / lejos) |
| 4 | Materialidad del marco | 1 | **4** | Piedra opaca con contorno de un texel. Sin chaflán, sin bisel, sin atlas generado |
| 5 | Materialidad del nodo | 1 | **8** | Socket biselado, placa, halo, todo de `SpellGraphSprites` |
| 6 | Iconos | 0 | **7** | Los 71 se dibujan y se hornean. Menos puntos por el aumento de 2.4x en la tarjeta |
| 7 | Estado del nodo | 2 | **8** | Seis estados, con leyenda que los distingue sin color |
| 8 | Paleta | 4 | **7** | Acento por escuela usado como tinte de socket, cadena y halo |
| 9 | Tipografía | 1 | **6** | Fuente de píxel para números y etiquetas, `Text` para nombres y prosa. Sin TMP |
| 10 | Idioma | 0 | **8** | Todo por `GameLanguage.Pick`, plurales resueltos, assets traducidos |
| 11 | Nombres de escuela | 1 | **9** | Raíl vertical, nueve nombres completos, contador en la misma fila |
| 12 | Contraste | 3 | **7** | Etiqueta del botón elegida por luminancia del fondo. Falta medirlo sobre el socket |
| 13 | Redundancia de forma | 1 | **8** | Filete en el raíl, leyenda por nodo, relleno del socket |
| 14 | Coste y afinidad | 5 | **8** | "1 PA · TE QUEDAN 2" y el recargo dicho en la tarjeta |
| 15 | Motivo del bloqueo | 2 | **9** | Todos, en orden nivel → prerrequisito → puntos |
| 16 | Texto recortado | 1 | **8** | Encogido para caber, nunca cortado |
| 17 | Escala / desplazamiento | 1 | **8** | Ajuste automático, 9 de 9 escuelas a zoom 1.00 |
| 18 | Filtro por rol | 0 | **0** | Sin cambios |
| 19 | Búsqueda | 0 | **0** | Sin cambios |
| 20 | Tarjeta de detalle | 0 | **7** | Icono, nombre, rol, coste, restante, efectos, motivos, botón |
| 21 | Prosa | 0 | **5** | El `flavour` de la escuela se lee. Los 71 nodos siguen sin `description` |
| 22 | Previsualización del hechizo | 0 | **0** | Sin cambios |
| 23 | Motion | 1 | **7** | Fundido de apertura, halo que respira, flujo de cadena, temblor de rechazo |
| 24 | Feedback al aprender | 0 | **8** | Ráfaga, cadena que se abre, sonido, y todo por DIFERENCIA |
| 25 | Partículas | 0 | **8** | Siete eventos, nada en reposo, nada en un rechazo |
| 26 | Sonido | 0 | **7** | Seis sonidos, catálogo primero, síntesis de respaldo |
| 27 | Rejilla de píxel | 1 | **7** | Todo el interior en texels enteros. **Menos: ignora el tamaño de interfaz del jugador** |
| 28 | Posición y convivencia | 2 | **7** | x[320..1280]: libra minimapa y placa de música. Derivado a medias |
| 29 | Coherencia con el HUD | 1 | **7** | `HudTheme`, fuente de píxel, `HudMoteLayer`, `HudTextureBaker`. Quedan 15 literales |
| 30 | Tema / tokens | 1 | **6** | `GrimoireStyle` existe y está enviado; las piezas del tablero aún no leen de él |
| 31 | Accesibilidad | 1 | **3** | Contraste mejor, forma redundante. Sigue sin teclado ni mando ni foco |
| 32 | Estado espiritual (R12) | 0 | **0** | Sin cambios |
| 33 | Cobertura de test | 1 | **8** | 56 aserciones, layout como aritmética pura |
| 34 | Rendimiento | 6 | **7** | 8-9 nodos por escuela; reconstrucción total por refresco, aceptable a esta escala |

**Media ponderada: 6.8.** Pesan doble 3, 4, 5, 6, 20, 24, 25 y 29.

## D. Los seis defectos que quedan, en orden de coste-beneficio

- **N1 — El panel ignora el tamaño de interfaz del jugador.** `HudLayout.ApplyScaler` llegó
  después de que se construyera esta ventana y cuatro superficies ya lo usan. Una preferencia
  que el jugador ve funcionar en una pantalla y no en la siguiente es exactamente la forma que
  este proyecto lleva toda la noche quitando.
- **N2 — El icono de la tarjeta se aumenta 2.4x.** `ResolveIcon` cachea por NODO, no por
  TAMAÑO, así que la tarjeta reusa el horneado del socket: 27 px dentro de una caja de 64. Es
  el mismo defecto de aliasing que R10 existe para evitar, con el signo cambiado.
- **N3 — El ratón deja de previsualizar en cuanto eliges.** El manejador de hover está guardado
  por `if (_selected == null)`, y el de salida no hace nada. O sea: la tarjeta sigue a lo que
  señalas hasta el primer clic y después se queda quieta, y al salir del nodo no vuelve a lo
  seleccionado. Comparar dos nodos deja de ser posible justo cuando empieza a hacer falta.
- **N4 — No hay filtro ni búsqueda**, con 71 nodos y 104 hechizos en la hoja de ruta. El dato
  (`SpellRole`) está escrito en los 71 y su propio enum dice para qué es. Y hay sitio: el
  tablero ocupa el 43-65 % del alto de su columna.
- **N5 — El grimorio no sabe que el jugador ha muerto** (R12). El mundo se vuelve gris y este
  panel sigue en color.
- **N6 — Quince literales de color** en las piezas del tablero, ninguno en `GrimoireStyle`. La
  ventana ya lee el tema; el nodo y la cadena no.

## E. Lo que NO se arregla en esta pasada, y por qué

- **El marco generado con chaflán y bisel** (eje 4). Es un atlas nuevo (`GrimoireArt`) y su
  propio trabajo; la piedra opaca con contorno ya no miente, solo es sobria.
- **Los 104 nombres de hechizo en inglés.** Contenido del catálogo, compartido con la barra de
  acciones, el editor y el HUD.
- **El cromo de ventana propio** (arrastrar, cerrar, recordar). La ficha de personaje lo está
  reconstruyendo otra sesión; dos marcos compitiendo es peor que uno sobrio.
- **La barra de cargables.** Sigue siendo el trabajo que le da consecuencia a aprender un
  hechizo, y sigue siendo un cambio del modelo de input, no del grimorio.

## F. Estado: N1-N6 cerrados (2026-09-12, misma sesión)

| Defecto | Qué se hizo |
|---|---|
| N1 | El canvas pasa por **`HudLayout.ApplyScaler`**, que lleva la referencia y el `match` compartidos Y el tamaño de interfaz del jugador (`GameSettings.uiScale` x `TextScale()`). Era la única superficie donde esa preferencia no hacía nada |
| N2 | La caché de iconos está keyada por **nodo + tamaño**. La tarjeta pide el suyo (`cardIconTexels x pixelScale`) en vez de reusar el horneado del socket: se acabó el aumento de 2.4x |
| N3 | El hover previsualiza **siempre**, no solo hasta el primer clic, y `Unhovered` devuelve la tarjeta a lo seleccionado. Comparar dos nodos vuelve a ser posible justo cuando hace falta |
| N4 | **Fila de filtro por rol**: ocho chips bajo el tablero (uno por `SpellRole` más uno que lo apaga). El mismo chip enciende y apaga |
| N5 | **Gris de espíritu (R12)**: la piedra deriva a `PlayerHudStyle.spiritStone` con `Health.IsDead`, la misma fuente que el panel del jugador y la placa de música |
| N6 | Los quince literales son ahora tokens de `GrimoireStyle`: factores de atenuación del socket y la placa, tinte del icono bloqueado, alfas del halo y de los tres estados de cadena, y el atenuado del botón deshabilitado |

### Decisiones nuevas que merecen quedar escritas

- **El filtro ATENÚA, no esconde.** Un árbol al que le quitan ramas no se lee como un árbol:
  las cadenas acabarían en el aire y la forma que el tablero existe para enseñar sería otra
  forma. Es la misma decisión que toma el editor de Controles con su búsqueda, y es lo que
  hace que un filtro sea fiable en vez de sospechoso.
- **Una cadena con UN extremo vivo sigue encendida.** Es el camino HACIA lo que el jugador
  está mirando, que es casi toda la razón de que un filtro valga la pena sobre un árbol y no
  sobre una lista. Para poder decirlo, la lista de cadenas guarda ahora **los dos extremos**
  (`ChainEnds`) en vez de solo el padre; resolver el hijo buscándolo en la lista de nodos es
  exactamente cómo se escribe un fallo de arrays paralelos, y el primer intento lo tenía.
- **El chip encendido se pinta con el acento de la ESCUELA, no con un color fijo.** El filtro
  pertenece al tablero que hay debajo, y un chip dorado estaría reclamando una importancia que
  el oro tiene reservada.
- **No hay caja de búsqueda libre, y es deliberado.** Un `TMP_InputField` aquí necesita
  arbitrar el foco con `InputBlocker` — un campo enfocado es justo lo que hace que
  `KeyboardInputManager` rechace toda tecla menos Escape — y responde la misma pregunta que ya
  responde esta fila para un grimorio de 71 nodos. Se gana el sitio en las 100, no antes.
- **El tablero CEDE sitio a la fila en vez de dibujarse encima.** `BoardViewportRect()` es una
  función y no dos rects escritos por separado, para que la aritmética del ajuste y el viewport
  no puedan discrepar sobre cuánto sitio hay de verdad.

### Lo que sigue abierto después de esto

- El marco generado con chaflán y bisel (eje 4, sigue en 4).
- La previsualización del hechizo (eje 22, sigue en 0): el editor tiene preview en vivo y el
  jugador compra a ciegas.
- Navegación por teclado y mando (eje 31): sigue sin flechas, sin Intro y sin foco visible.
- Los 71 nodos sin `description` (eje 21).
- Los 104 nombres de hechizo en inglés.
- El cromo de ventana propio.
- La barra de cargables, que es lo que le da consecuencia a aprender un hechizo.

## G. Segunda iteración cerrada y VISTA (2026-09-12)

**71/71 en 1.45 s**, consola a 0, y el panel capturado en un frame renderizado con el filtro
puesto y la navegación usada.

Lo que enseñó ese frame, además de confirmar N1-N6:

- **Los chips del filtro se pisaban.** Repartía el ancho de la fila a partes iguales y las
  palabras no lo son: un octavo de la fila son 35 texels y "PROTECCIÓN" pide 40, así que se
  leía `PROTECCIÓNCURACIÓN` como una sola palabra. Cada chip mide ahora **su propia tinta**
  (`HudArt.Measure`) y el sobrante se reparte a partes iguales — medido, los ocho suman unos
  254 texels en una fila de 296, así que caben todos a su tamaño y sobran cuarenta.
- **La línea de efectos seguía en inglés.** `SpellNode.DescribeEffects` la construye en
  `Valkur.Data`, que no tiene tabla de idiomas y no debe tenerla: esa cadena es la que quieren
  el editor de hechizos y la consola. `GrimoireText.Effects` construye la del jugador
  ("Desbloquea Dash (Movilidad)"); los modificadores conservan su propio `Describe()`, que ya
  es español porque `StatCatalog` lo es.

### Navegación por teclado (eje 31: 3 -> 7)

- **Las flechas deciden por GEOMETRÍA, no por orden de lista.** El vecino más cercano EN ESA
  DIRECCIÓN, con la desviación lateral penalizada 2.2x y lo que queda detrás rechazado de
  plano. Recorrer la lista de colocaciones es correcto por construcción y salta por el tablero
  en cuanto el empaquetador reordena hermanos, que a la vista es la selección vagando.
- **La semilla es el nodo comprable, no el índice 0.** Esa es la respuesta del tablero; el
  índice 0 es la de la lista. Y el único nodo donde aterrizar vale algo es el que está a un
  Intro de comprarse.
- **Intro compra por el MISMO camino que el botón de la tarjeta**, así que una compra desde el
  teclado produce las mismas motas, la misma cadena encendida y el mismo sonido, y un rechazo
  produce el mismo temblor y nada más.
- **Escape NO se lee aquí.** La ficha de personaje lo posee vía `EscapeOwnership`, y dos
  lectores de una tecla en un orden de `Update` indefinido es cómo una pulsación cierra a la
  vez el panel y la ventana de detrás — o ninguno, según el frame.
- Todo por `InputCompat`, con una guardia de fuente que falla si aparece `Keyboard.current`,
  `UnityEngine.Input.` o `CancelPressed`.

### Dos lecciones de método de esta tanda

- **Un `'\\n'` escrito por heredoc de bash se convirtió en un salto de línea REAL dentro del
  literal de carácter**, y rompió la compilación para las tres sesiones que comparten este
  Unity. CLAUDE.md ya documenta la regla de las barras invertidas. La solución que queda no es
  reparar el literal: es **no meter texto con escapes en un heredoc** — el script de parcheo va
  en un `.py` escrito con la herramienta de edición, que lo lee y lo aplica.
- **Un monitor puede ser una sonda sobre otra pregunta.** Vigilé el mtime del DLL de tests como
  proxy de "el run del compañero terminó"; ese fichero no cambia si nadie COMPILA, así que su
  silencio no distinguía "sigue corriendo" de "acabó hace cinco minutos". Lo que responde la
  pregunta es el runner: `TestJobDataHolder.TestRuns` **y** `TestRunStatus._isRunning`, que se
  separaron de verdad esta noche (1 contra False) y solo leyendo las dos se podía decidir que
  la entrada era residuo y no un huérfano.

### Nota de puntuación

Con N1-N6, el filtro y la navegación cerrados, los ejes movidos son 6 (7 -> 9), 18 (0 -> 8),
19 (0 -> 3: hay filtro, no hay búsqueda de texto), 27 (7 -> 9), 29 (7 -> 8), 30 (6 -> 9),
31 (3 -> 7) y 32 (0 -> 8). **Media ponderada: 7.9.** Lo que queda por debajo de 8 y no se ha
tocado sigue siendo lo de la sección E: el marco generado, la previsualización del hechizo, las
descripciones de los 71 nodos, los nombres de hechizo en inglés, el cromo de ventana propio y
la barra de cargables.

## H. La fuente de píxel, y un test que bloqueaba una función correcta

Salió de esta tanda aunque el fichero no es del grimorio, porque el grimorio es quien lo
consume: los chips del filtro dicen DAÑO, PROTECCIÓN, CURACIÓN e INVOCACIÓN en la cara pequeña.

`PlayerHudTests.EveryGlyph_IsARectangleOfTheFacesHeight(Small)` estaba rojo porque la `'Á'`
declara siete filas en una cara de cinco. **El código estaba bien y el test sobre-especificado.**

Lo que lo decide son dos líneas del renderizador:

```csharp
// HudPixelText.cs
float qx = pen - 1f, qy = y0 - 1f;                          // misma base para todos
AddQuad(vh, qx, qy, g.CellWidth, g.CellHeight, g.Uv, c);    // altura POR GLIFO
```

El quad toma su altura del GLIFO, no de la cara, y todos se asientan sobre la misma `y0`: un
glifo de siete filas crece hacia ARRIBA, que es donde va un acento. Una cara de cinco filas no
tiene sitio para un acento dentro de la altura de una mayúscula — ponerlo ahí se come el
travesaño o toca la letra, y a dos píxeles de pantalla por texel eso se lee como un borrón.

Contado por la API y no por expresión regular (el primer intento con regex partió el fichero y
dio nueve falsos positivos de la cara grande):

```
Small: altura=5  glifos=64  fuera-de-altura=9  filas-dentadas=0
       Á É Í Ó Ú Ü Ñ ¿ ¡   todos exactamente 7
Large: altura=7  glifos=15  fuera-de-altura=0  filas-dentadas=0
```

El arreglo separa las dos mitades que el test confundía en una:

- **Rectangular es el invariante y vale para todos sin excepción.** Filas de distinto ancho sí
  rompen el empaquetado del atlas.
- **La altura no lo es.** Pasa a `h` o `h + HudPixelFont.AccentRows`, y solo para un conjunto
  CERRADO (`ClaimsAccentRows`). Sin el conjunto cerrado, "un glifo puede ser más alto" es
  permiso para que cualquiera mida cualquier cosa.
- **`AccentRows` vive en la FUENTE, no en el fixture.** Un `2` literal en el test es un número
  que caduca el día que la cara cambie y que nadie relaciona con la causa.
- El mensaje del fallo termina diciendo que no se arregle recortando filas, porque el recorte
  es lo que cualquiera haría y quita el acento o el travesaño.

Dos tests nuevos cierran el conjunto por los dos lados: que la cara pequeña pueda deletrear las
palabras que el grimorio dibuja HOY, y que todo carácter declarado acentuado EXISTA en la cara
y mida lo declarado — el lado que falla en silencio y que este proyecto tiene documentado una
docena de veces como "autorizado e inerte".

**110/110 en 2.96 s**, con `PlayerHudTests` entera dentro.

---

# Tercera auditoría — SOLO LO VISUAL, 2026-09-12

**Nota visual ponderada: 3.6 / 10** · Objetivo: **≥ 8.5**

Las dos auditorías anteriores midieron si el panel DICE la verdad y si RESPONDE. Ésta mide si es
bello, que es la pregunta que quedaba y la que la captura del usuario contesta que no.

Método: la captura del usuario (el panel a tamaño real, 1600x800, escala 2, `interfaceScale` 1),
más aritmética sobre el estilo enviado y composición en espacio lineal de cada par de
superficies.

## A. El diagnóstico en una frase

**La ventana no tiene material.** No es que esté mal compuesta — la composición es correcta y
eso ya se midió. Es que todas sus superficies son el mismo negro: medido, el panel contra el
hueco de las columnas da **1.06 : 1**, y el panel contra su propio borde **1.10 : 1**. Un uno
por ciento de canal. No hay marco, no hay columnas, no hay relieve, no hay hueco: hay UNA losa
negra con texto encima, y el ojo no tiene nada que agarrar. Es el mismo defecto que este
proyecto ya registró en el editor de Controles (`INPUT_FREE` 0.14 sobre `BG_SURFACE` 0.13) y en
el inventario, y aquí afecta a todo a la vez.

El segundo, y es el que más se ve en la captura: **el raíl usa nueve tamaños de letra
distintos.**

## B. Medidas

### Superficies: no se distinguen

| Par | Ratio | Lectura |
|---|---|---|
| Panel (`stoneDark`) vs hueco (`recess`) | **1.06 : 1** | Las tres columnas no existen visualmente |
| Panel vs borde (`outline`) | **1.10 : 1** | El marco de un texel no se ve |
| Luminancias absolutas | panel 0.0075 · hueco 0.0042 · borde 0.0024 | Todo por debajo de 0.01: la ventana entera es negro |

### El raíl: un tamaño por escuela

La caja del nombre mide **37 texels** (62 de raíl menos 7 de margen menos 18 del contador).
Cuánto ocupa cada nombre, en texels:

| Nombre | f=10 | f=8 | f=6 | ¿Cabe? |
|---|---|---|---|---|
| Formas Marciales | 83.2 | 66.6 | 49.9 | **En ninguno** — envuelve a dos líneas |
| Fuego Interior | 72.8 | 58.2 | 43.7 | **En ninguno** |
| Ritos Verdes | 62.4 | 49.9 | 37.4 | **En ninguno** (por 0.4) |
| Stormcalling / Tormentas | 62.4 | 49.9 | 37.4 | **En ninguno** |
| Umbramancia | 57.2 | 45.8 | 34.3 | Solo a 6 |
| Fulgor | 31.2 | 25.0 | 18.7 | A 10 |

**Uno de los nueve cabe a tamaño legible.** `resizeTextForBestFit` hace lo que se le pide —
encoger hasta caber— y como se lo pide a cada etiqueta por separado, el resultado son nueve
tamaños: "Fulgor" grande, "Umbramancia" diminuto, y tres envolviendo sobre su propio contador.
Eso es exactamente lo que se ve en la captura, y es un pecado tipográfico, no un bug de encaje.

### Texto: lo único que sí pasa

| Color | Sobre el hueco | Veredicto |
|---|---|---|
| `text` | 18.59 : 1 | Bien |
| `gold` | 11.28 : 1 | Bien |
| `textDim` | 10.34 : 1 | Bien |
| `textDisabled` | **4.44 : 1** | Justo por debajo del 4.5 de WCAG AA, y es el color de los nodos bloqueados |

## C. Puntuación visual, eje por eje

| # | Eje visual | Nota | Qué se ve |
|---|---|---|---|
| 1 | Materialidad del marco | **1** | Un rectángulo plano con un contorno de un texel a 1.10 : 1. No hay chaflán, ni bisel, ni sombra, ni filete |
| 2 | Separación de superficies | **1** | Panel, raíl, tablero y tarjeta a 1.06 : 1 entre sí. Las columnas no se ven; se deducen por dónde cae el texto |
| 3 | Profundidad y relieve | **0** | Nada tiene arriba ni abajo. Ni un texel de luz, ni una sombra interior |
| 4 | Luz y foco | **1** | Iluminación uniforme y nula. Lo único que emite es el halo del nodo disponible, y por eso es lo único que el ojo encuentra |
| 5 | Jerarquía tipográfica | **3** | Hay tres niveles (título, nombre, motivo) y se leen, pero ningún salto es decidido: todo vive entre 6 y 11 texels |
| 6 | Consistencia tipográfica | **1** | **Nueve tamaños en el raíl.** Dos familias conviviendo (fuente de píxel y Arial) sin una regla visible de cuál va dónde |
| 7 | Legibilidad del raíl | **2** | Tres nombres envuelven sobre su contador; uno es ilegible de pequeño |
| 8 | Uso del espacio | **3** | El tablero llena el 84 % del ancho y el 43-65 % del alto de su columna, y la tarjeta está vacía la mayor parte del tiempo. Mucho negro sin trabajo |
| 9 | La tarjeta en reposo | **1** | 104 texels de ancho con dos palabras centradas. Es el segundo objeto más grande de la ventana y no dice nada |
| 10 | Escala de los nodos | **4** | 44 px de diámetro en un tablero de 592. Correctos y pequeños; el arte del icono no llega a leerse |
| 11 | Leyendas de nodo | **5** | Ya no se pisan entre columnas, pero "Slash (Cleave)" envuelve y roza su propio "Nivel 12" |
| 12 | Cadenas | **6** | La forma es correcta y la única encendida se lee bien. Son líneas planas de dos texels, sin grosor ni brillo |
| 13 | Lectura del estado | **6** | Lleno / hueco / apagado funciona. Todo lo apagado es el mismo apagado: tres motivos distintos, un solo gris |
| 14 | Paleta y acento | **5** | Nueve acentos buenos, usados solo en el socket y el filete. El resto de la ventana no sabe de qué escuela es |
| 15 | Contraste de texto | **7** | Tres de cuatro bien; `textDisabled` a 4.44 |
| 16 | Contraste de superficie | **0** | 1.06 : 1 |
| 17 | Ornamento / identidad | **0** | Nada dice "grimorio". Ni sigilo, ni filete, ni textura, ni una sola forma que no sea un rectángulo o un círculo |
| 18 | Fila de filtro | **5** | Legible y bien repartida. Ocho rectángulos planos idénticos |
| 19 | Cabecera y monedero | **5** | Jerarquía correcta (oro a la izquierda, cuenta a la derecha). Flota sobre nada: no hay banda, ni regla, ni separación |
| 20 | Pie | **4** | La frase de la escuela está bien escrita y bien puesta. En cursiva sintética sobre el borde inferior |
| 21 | Ritmo vertical | **4** | Todo cae en texels enteros, que es correcto y no es lo mismo que tener ritmo: los huecos entre bandas no guardan proporción |
| 22 | Bordes y esquinas | **1** | Esquinas a 90 grados en todo. Un `Outline` de uGUI, que son cuatro copias del mismo quad |
| 23 | Movimiento | **6** | El fundido de apertura y el halo que respira están bien medidos. No hay nada más en reposo, que es correcto, pero tampoco hay nada al entrar salvo la opacidad |
| 24 | Partículas | **7** | Siete eventos, bien acotados. Solo se ven al comprar, que es lo pedido |
| 25 | Coherencia con el HUD | **5** | Lee del tema y de la fuente compartidos. Y al lado del panel del jugador —piedra, filigrana dorada, bisel— parece de otro juego, porque lo que comparte son los colores y no el ACABADO |
| 26 | ¿Parece de este juego? | **2** | No. Parece una herramienta de autor bien hecha |

**Media ponderada: 3.6.** Pesan doble 1, 2, 3, 4, 16, 17 y 26 — los ejes que separan "correcto"
de "bello", que es lo que esta auditoría mide.

## D. Por qué pasa: una causa, no veintiséis

Los ejes 1, 2, 3, 16, 17, 22 y 26 son **el mismo defecto contado siete veces**: no existe
`GrimoireArt`. Cada superficie de esta ventana es un `Image` sin sprite, o sea un rectángulo de
color plano, y un rectángulo de color plano no puede tener chaflán, ni bisel, ni hueco, ni
esquina, ni filete. El inventario, la placa de música y el panel del jugador tienen los suyos
(`InventoryArt`, `MusicHudArt`, `HudArt`); el grimorio se quedó sin el suyo dos iteraciones
seguidas, y todo lo que se aplazó con él se aplazó junto.

El eje 6 y el 7 son el segundo, y es de dimensionado: **la caja del nombre del raíl es
demasiado estrecha para ocho de los nueve nombres**, así que el mejor ajuste automático tiene
que resolverlo etiqueta a etiqueta y produce nueve respuestas distintas.

El eje 9 es el tercero: **la tarjeta no tiene estado de reposo**, solo un mensaje.

---

# Cuarta auditoría visual — 2026-09-12, con el atlas puesto

**Nota visual ponderada: 5.8 / 10** (era 3.6) · Objetivo: **≥ 8.5**

Puntuada contra un frame REAL del panel con el atlas, los sigilos y el raíl nuevo. Tres
correcciones están escritas y compiladas pero **todavía no vistas**, y se marcan como tales:
puntuar lo que no se ha mirado es el error que esta serie de auditorías existe para no cometer.

## A. Qué cambió de verdad

La causa única de la tercera auditoría está cerrada. El panel tiene marco, las tres columnas se
distinguen, los nodos tienen relieve y el raíl tiene nueve caras. Medido antes: panel contra
hueco **1.06 : 1**. El bisel no cambia ese número y no hace falta que lo cambie — un texel de
luz arriba e izquierda y uno de sombra abajo y derecha se lee como objeto a cualquier tono, y es
lo que ahora separa las columnas.

## B. Puntuación, tercera contra cuarta

| # | Eje visual | 3ª | 4ª | Qué se ve ahora |
|---|---|---|---|---|
| 1 | Materialidad del marco | 1 | **5** | Hay marco, chaflán y bisel. A este tono el borde exterior sigue siendo tenue |
| 2 | Separación de superficies | 1 | **7** | Raíl, tablero y tarjeta se distinguen sin leer nada |
| 3 | Profundidad y relieve | 0 | **6** | Sockets biselados, huecos hundidos, filas con placa |
| 4 | Luz y foco | 1 | **4** | La luz del tablero está, y es demasiado suave: el ojo sigue yendo al nodo con halo |
| 5 | Jerarquía tipográfica | 3 | **4** | Tres niveles reales, pero las filas de la tarjeta se solapan |
| 6 | Consistencia tipográfica | 1 | **6** | **Un solo tamaño en el raíl**, por construcción. Siguen dos familias |
| 7 | Legibilidad del raíl | 2 | **7** | Nueve nombres completos con su marca. El más largo toca su contador |
| 8 | Uso del espacio | 3 | **4** | El tablero sigue con la mitad inferior vacía |
| 9 | Tarjeta en reposo | 1 | **2** | Enseña sigilo, nombre, afinidad y progreso — **impresos unos encima de otros** |
| 10 | Escala de los nodos | 4 | **7** | 52 px. El arte del icono por fin se lee |
| 11 | Leyendas de nodo | 5 | **7** | Sin colisiones en el frame |
| 12 | Cadenas | 6 | **7** | Tres texels; la abierta se distingue de las apagadas |
| 13 | Lectura del estado | 6 | **7** | Nivel, cadena y bolsa como tres formas. Pequeñas |
| 14 | Paleta y acento | 5 | **7** | El acento llega al raíl por el sigilo y a la tarjeta por el nombre |
| 15 | Contraste de texto | 7 | **7** | Sin cambios |
| 16 | Contraste de superficie | 0 | **7** | Resuelto por el bisel, no por el tono |
| 17 | Ornamento / identidad | 0 | **7** | Nueve sigilos. Es lo que hace que parezca un grimorio y no un grafo |
| 18 | Fila de filtro | 5 | **4** | **Regresión**: el octavo chip sale cortado por el borde |
| 19 | Cabecera y monedero | 5 | **5** | La regla dorada no se distingue |
| 20 | Pie | 4 | **3** | **Regresión**: la frase de la escuela aparece DOS veces, en la tarjeta y en el pie |
| 21 | Ritmo vertical | 4 | **6** | Las filas del raíl marcan un pulso; las bandas siguen sin proporción |
| 22 | Bordes y esquinas | 1 | **6** | Chaflán en marco, hueco y chip |
| 23 | Movimiento | 6 | **6** | Sin cambios |
| 24 | Partículas | 7 | **7** | Sin cambios |
| 25 | Coherencia con el HUD | 5 | **7** | Atlas propio en la gramática de la casa, como el inventario y la música |
| 26 | ¿Parece de este juego? | 2 | **6** | Sí, ya. Todavía no "hermoso" |

**Media ponderada: 5.8** (misma ponderación: doble en 1, 2, 3, 4, 16, 17 y 26).

## C. Lo que el frame enseñó, medido en vez de mirado

Tres colisiones, las tres de dimensionado, las tres con número exacto:

| Defecto | Medido | Causa |
|---|---|---|
| El nombre más largo toca su contador | "FORMAS MARCIALES" inka **62** texels en una caja de **56** | El raíl a 92 con sigilo 9 y contador 18 deja 56 |
| El octavo chip sale cortado | Los ocho piden **294** texels en una columna de **266** | Cada chip se mide por su tinta y NADIE comprobaba el total; apareció solo cuando el raíl le quitó ancho al tablero |
| Las filas de la tarjeta se pisan | Nombre en caja de 11 texels, dos líneas | Cada fila se colocaba con un desplazamiento constante en vez de por la altura de la anterior |

**La segunda es la interesante**: no es un error de cálculo, es un layout cuyas piezas se
dimensionan de forma independiente. Los chips cabían perfectamente hasta que otra columna
creció. Un reparto que no comprueba su total es correcto hasta el día que deja de serlo.

## D. Correcciones escritas y compiladas, PENDIENTES DE VERSE

- Raíl a 96, tarjeta a 92, contador a 15 → la caja del nombre pasa de 56 a **63** contra los 62
  que mide el más largo.
- Los chips se **aprietan** si no caben, en vez de salirse: el reparto proporcional conserva que
  la palabra más larga sea el chip más ancho, que es para lo que existe el dimensionado por
  tinta.
- Las filas de la tarjeta se colocan por la altura de la fila anterior más un hueco, y el
  nombre declara que mide dos líneas.
- La frase de la escuela se dice **una vez**, en el pie.
- **Los sigilos se dibujan a su 9 nativo**, no a 7. Con filtrado de punto, 9 en 7 no es "un poco
  más pequeño": el muestreador descarta dos filas y dos columnas, así que la X pierde brazos y
  el copo pierde puntas. Y en la tarjeta van a 9x3=27 en vez de a 32, porque **9 en 32 es 3,55**
  y un escalado de punto a ratio fraccionario repite unas filas y no otras, lo que tuerce una
  marca simétrica.

Ese último es de otra sesión y merece quedar como regla: **cuando algo se ve escaso o sucio,
mide la COBERTURA — tamaño del elemento contra el paso de la rejilla en que se dibuja — antes
que el conteo o el detalle.** Son dos números distintos y solo uno está en el código; el otro
vive en el atlas. Yo había puesto el sigilo a 7 porque me faltaban dos texels para el nombre, o
sea tratando el tamaño del elemento como el hueco que sobra.

## E. Lo que queda para acercarse al 10

- **La luz del tablero es demasiado tenue** (eje 4). Es el cambio de un alfa.
- **La mitad inferior del tablero está vacía** (eje 8): las escuelas ocupan 107-159 texels de
  los 231 de su columna. O el tablero se centra en su contenido, o la columna se acorta.
- **La regla dorada no se ve** (eje 19).
- **Dos familias tipográficas** (eje 6): los nombres de hechizo siguen en Arial porque la cara
  de píxel es solo mayúsculas y un nombre propio gritado se lee peor.
- **Las marcas de bloqueo son pequeñas** (eje 13).
- **El marco exterior es tenue** (eje 1): el bisel existe y el tono del panel casi no lo deja
  hablar.

---

# Quinta auditoría visual — 2026-09-12, con el panel recortado y el catálogo en español

**Nota visual ponderada: 7.6 / 10** (era 5.8) · Objetivo: **≥ 8.5**

Puntuada contra `grimoire_v6.png`, un frame real a 1600x800 con `_pixelScale = 2` y
`_panelTexels = (480, 224)`. Las cinco correcciones que la cuarta auditoría dejó
"pendientes de verse" están vistas y funcionan; lo que sigue es lo que el frame nuevo
enseña.

## A. La causa única de esta ronda no era de dibujo, era de RESOLUCIÓN

El defecto más grave encontrado hoy no se veía en ningún frame que hayamos capturado
nunca, porque **todos capturamos a 1600x800 y el defecto solo existe por encima**.

`GrimoireGeometry.PanelTexels` divide un tamaño de lienzo FIJO entre la escala de píxel, así
que una pantalla de más DPI recibe MENOS texels para esta ventana. Todo lo dimensionado en
texels lo aguanta —el tablero es una constelación con zoom—, pero las dos cosas dibujadas con
una cara de mapa de bits no pueden: una cara de mapa de bits tiene **un solo tamaño por
construcción**. Medido antes del tope:

| Pantalla | Escala pedida | Panel | Tablero | Fila de chips | Raíl |
|---|---|---|---|---|---|
| 1600x800 | 2 | 480x264 | 270 | 256 en 256 | 100, entero |
| **1920x1080** | **3** | **320x176** | **110** | **256 en 96** | **100, entero** |
| **3840x2160** | **5** | **192x105** | — | — | **recortado a 76 contra un nombre de 62** |

O sea: **la ventana estaba en su mejor momento en la pantalla más pequeña**, y a 1080p —la
resolución más común que hay— cada etiqueta del filtro se apretaba hasta su suelo de diez
texels. `GrimoireGeometry.PixelScaleFor` deriva ahora el tope de la tinta de la propia fila y
de la altura de las nueve filas del raíl, en los DOS ejes, y el layout sale idéntico de 720p a
4K. El precio, dicho claro: en una pantalla grande el panel se dibuja más basto de lo que sus
vecinos querrían. Es el intercambio honesto — ensanchar el panel no está disponible, porque
`PanelLeft`/`PanelRight` están clavados a la pestaña de la hoja de personaje, que vive en otro
ensamblado.

## B. Puntuación, cuarta contra quinta

| # | Eje visual | 4ª | 5ª | Qué se ve ahora |
|---|---|---|---|---|
| 1 | Materialidad del marco | 5 | **7** | Bisel a 1.7:1, borde exterior legible |
| 2 | Separación de superficies | 7 | **8** | Tres columnas sin leer nada |
| 3 | Profundidad y relieve | 6 | **7** | Sin cambios estructurales |
| 4 | Luz y foco | 4 | **7** | La luz del tablero a 0.55 por fin centra el ojo |
| 5 | Jerarquía tipográfica | 4 | **7** | Tres niveles; las filas de la tarjeta ya no se pisan |
| 6 | Consistencia tipográfica | 6 | **8** | **El tablero entero es una sola cara.** Queda Arial en la prosa |
| 7 | Legibilidad del raíl | 7 | **9** | Caja de 67 contra un nombre de 62, con 5 de holgura |
| 8 | Uso del espacio | 4 | **7** | Banda de 230 a 190; sobra 4 sobre la escuela más alta |
| 9 | Tarjeta en reposo | 2 | **6** | Filas apiladas por su altura; ya no repite el nombre |
| 10 | Escala de los nodos | 7 | **7** | Sin cambios |
| 11 | Leyendas de nodo | 7 | **9** | Un tamaño para todas, en la rejilla, cortadas por palabra |
| 12 | Cadenas | 7 | **7** | Sin cambios |
| 13 | Lectura del estado | 7 | **8** | Marcas de bloqueo al doble de su 5 nativo |
| 14 | Paleta y acento | 7 | **7** | Sin cambios |
| 15 | Contraste de texto | 7 | **8** | La cara de píxel lleva contorno horneado |
| 16 | Contraste de superficie | 7 | **7** | Sin cambios |
| 17 | Ornamento / identidad | 7 | **8** | La regla dorada y su diamante por fin se ven |
| 18 | Fila de filtro | 4 | **8** | Ocho chips enteros; el apretón es proporcional |
| 19 | Cabecera y monedero | 5 | **8** | Regla visible, título y bolsa sobre ella |
| 20 | Pie | 3 | **7** | La frase de la escuela se dice una vez |
| 21 | Ritmo vertical | 6 | **7** | Filas del raíl marcando pulso, banda ajustada |
| 22 | Bordes y esquinas | 6 | **7** | Sin cambios |
| 23 | Movimiento | 6 | **6** | Sin cambios |
| 24 | Partículas | 7 | **7** | Sin cambios |
| 25 | Coherencia con el HUD | 7 | **8** | Misma cara que la pestaña de talentos de al lado |
| 26 | ¿Parece de este juego? | 6 | **8** | Sí. Y ahora además está en su idioma |
| **27** | **Idioma** | — | **9** | **Los 71 nodos en español.** Nuevo eje, ver abajo |
| **28** | **Independencia de resolución** | — | **9** | **Nuevo eje.** Mismo layout de 720p a 4K |

**Media ponderada: 7.6** (doble en 1, 2, 3, 4, 16, 17, 26, 27 y 28).

## C. Dos ejes nuevos, y por qué no estaban antes

**Eje 27, idioma.** No lo puntué en las cuatro primeras rondas porque miraba el panel y no el
contenido. El frame lo enseñaba desde el principio: un raíl que dice "FORMAS MARCIALES" con
nodos debajo que dicen "Scatter Volley" y "War Cry". Y el catálogo no estaba en inglés — estaba
**a medias**: cinco de los 71 hechizos ya venían en español ("Bola de Fuego", "Bola de Hielo",
"Bola de Luz", "Bola de Oscuridad", "Aura de Curación") y 66 en inglés. Alguien había empezado
la traducción y la había dejado. Terminarla es completar un trabajo empezado, no abrir uno
nuevo, y `displayName` es solo presentación en todo el proyecto — verificado con grep: ninguna
comparación de igualdad, ninguna búsqueda por nombre, ninguna referencia serializada.

**Eje 28, independencia de resolución.** Tampoco estaba, y esa es la lección: *la muestra en la
que trabajamos era la que ocultaba el defecto*. Un eje que solo se puede puntuar midiendo en
varias resoluciones no aparece solo por mirar frames.

## D. Lo que el frame nuevo enseña, medido

| Cantidad | Antes | Ahora |
|---|---|---|
| Banda de las columnas | 230 texels | **190** |
| Sobra sobre la escuela más alta | 44 | **4** |
| Caja del nombre en el raíl | 63 (holgura 1) | **67 (holgura 5)** |
| Tamaños tipográficos en el tablero | uno por leyenda | **uno** |
| Nodos con nombre en inglés | 66 de 71 | **0** |
| Layout a 1080p | tablero 110, chips al suelo | **idéntico al de 800p** |

## E. Lo que queda para el 10

- **El tablero sigue con aire en las escuelas cortas** (eje 8, 7/10). Tres de las nueve miden
  117 texels y seis miden 173, y la banda tiene que aguantar la más alta. No se arregla con
  zoom: el ajuste lo ata el ANCHO en las nueve (1.006 medido), porque todas tienen exactamente
  cuatro pasos de profundidad. Una ventana cuya altura cambiara al hacer clic en el raíl sería
  una ventana que salta bajo el cursor, que es peor que un margen callado. **Se arregla dando
  más hijos a las escuelas cortas, es decir con contenido, no con layout.**
- **Arial sobrevive en la prosa** (eje 6, 8/10): el nombre de la tarjeta, sus dos párrafos y la
  frase del pie. Y hay una razón dura: **la cara grande del kit no tiene letras**, solo dígitos
  y signos, así que el único nivel tipográfico mayor disponible es Arial. Dibujar el nombre de
  la tarjeta con la cara pequeña lo igualaría a todo lo demás y le quitaría a la tarjeta su
  única jerarquía. El arreglo de verdad es un alfabeto grande en `HudPixelFont`, que es trabajo
  de kit compartido y no de esta ventana.
- **La tira de pestañas sigue en inglés y en una tercera fuente** — CHARACTER / SKILLS /
  GRIMOIRE / RECORDS. Es lo más discordante que queda en el frame y **no es del grimorio**:
  pertenece a `CharacterSheetController`, en `Valkur.UI`. Es también la razón por la que
  `PanelTop` y los dos bordes laterales no se pueden mover.
- **Las cadenas apagadas casi no se ven** (eje 12, 7/10).
- **Movimiento** (eje 23, 6/10): solo respira el nodo comprable, que es correcto por R7, pero la
  ventana no tiene ningún acento de apertura más allá del deslizamiento.
