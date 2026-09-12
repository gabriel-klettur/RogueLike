# Auditoría de los paneles previos al juego — 2026-09-12

> **Alcance.** Todo lo que el jugador ve antes de controlar al personaje: escena `Bootstrap`,
> *press to start*, menú principal, Opciones (lista / Sonido / Vídeo / Controles), Cargar
> partida, selector de clase y pantalla de carga. El menú de pausa queda fuera (es in-game),
> pero comparte dialecto con estos y se cita cuando la conclusión le aplica.
>
> **Método.** Lectura de las 6 536 líneas de `UI/MainMenu`, `UI/PauseMenu` y `UI/Loading`,
> más **medición en vivo** en Play Mode (capturas con `ScreenCapture.CaptureScreenshot`,
> conteo de objetos por reflexión, cronometraje de `BuildUI` y sus nueve sub-builders,
> muestreo de píxeles de las capturas y contraste WCAG calculado). Cada número de este
> documento está medido, no estimado.

## 0. Veredicto

**Global: 3,4 / 10.**

El *backend* de estos paneles es sólido — máquina de pantallas con un único dueño,
navegación teclado+ratón centralizada en `InputCompat`, 78 tests EditMode, y una pantalla
de carga que ya pasó su propia auditoría y está en 8,5. El *frontend* es el prototipo de
Python portado literalmente y nunca revisado: **cero partículas, cero SFX de interfaz,
cero tokens de tema, tipografía por defecto de TMP, y el logo del juego dice
`ROGUELIKE 1.0`**.

La frase que resume la auditoría: **el juego ha construido un lenguaje visual completo
para el HUD (`HudTheme`, `HudPixelFont`, `HudArt`, `HudMoteLayer`, siete `*Style.asset`) y
ninguno de los paneles de entrada lo usa.** Son la única superficie del juego que no ha
recibido una pasada de diseño, y es la primera que se ve.

### Tabla de puntuación por panel

| # | Panel | Belleza | Técnica | Global | Nota de una línea |
|---|---|---|---|---|---|
| 1 | Arranque (`Bootstrap`) | 0,5 | 4,0 | **2,3** | No existe: pantalla vacía, sin logo ni splash |
| 2 | Press to start | 2,0 | 5,0 | **3,5** | Texto plano, contraste 2,13:1, invisible el 50 % del tiempo |
| 3 | Menú principal | 3,0 | 3,0 | **3,0** | Logo con el nombre equivocado; 128 ms de construcción |
| 4 | Opciones (lista) | 3,5 | 5,0 | **4,3** | Correcto y vacío; `switch` sobre literales en inglés |
| 5 | Opciones → Sonido | 3,0 | 3,5 | **3,3** | 5 de 8 filas son mandos de desarrollador; bug `-400` |
| 6 | Opciones → Vídeo | 3,5 | 6,0 | **4,8** | Staged+Apply bien hecho; 3 filas, sin revertir por temporizador |
| 7 | Opciones → Controles | 2,5 | 4,0 | **3,3** | Muro de texto que tapa el logo y apunta a un editor que no existe en build |
| 8 | Cargar partida | 4,0 | 6,0 | **5,0** | El panel mejor resuelto; filas indistinguibles entre sí |
| 9 | Selector de clase | 3,0 | 4,0 | **3,5** | Claves internas en minúscula y seis estadísticas crudas |
| 10 | Pantalla de carga | 4,0 | 9,0 | **6,5** | Arte bueno, barra verde de depuración encima |

### Tabla de puntuación por eje transversal

| Eje | Nota | Evidencia medida |
|---|---|---|
| Identidad de marca | **0,5** | El logo dice `ROGUELIKE 1.0`; el juego se llama Valkur |
| Idioma | **2,0** | Menú en inglés, pista del selector en español, panel de Controles en español, pantalla de carga en español |
| Sistema de diseño / tema | **1,0** | 67 `new Color(` literales, 0 referencias a `HudTheme` o `UITheme` |
| Tipografía | **2,0** | TMP por defecto en las 113 etiquetas; ninguna fuente de marca |
| Movimiento y animación | **1,5** | Carrusel de fondo + parpadeo binario. Nada más se mueve |
| Partículas / atmósfera | **0,0** | `grep ParticleSystem UI/` → **cero resultados** en todo `UI/` |
| Audio de interfaz | **0,5** | Música sí (`menu_intro`); `grep PlaySFX UI/MainMenu` → **cero** |
| Accesibilidad y contraste | **2,5** | Selección medida en 4,15:1 (peor que la fila NO seleccionada, 9,61:1) |
| Rendimiento | **3,0** | `BuildUI` = 128,4 ms; 21 ms cada 2,6 s para siempre; fuga de sprites |
| Robustez / arquitectura | **5,0** | Máquina de pantallas correcta, 78 tests; 6 536 líneas de uGUI a mano |
| Escalabilidad | **2,5** | Cada fila se coloca por aritmética absoluta; añadir una opción mueve constantes |
| Cobertura de test del aspecto | **3,0** | uGUI no maqueta en EditMode: ningún test puede ver nada de lo anterior |

## 1. Los ocho hallazgos que mandan

Ordenados por lo que cuestan, no por lo difíciles que son.

### 1.1 El logo dice `ROGUELIKE 1.0`

`Resources/UI/Intro/game_name.png` es el rótulo del prototipo en Python. Aparece en las
diez pantallas previas al juego, a 560 × 240 px, centrado arriba. **Un jugador que abre el
juego no lee nunca la palabra Valkur.** No hay ningún otro sitio del arranque donde el
nombre aparezca: el pie de página dice `v1.0 | Unity 2022.3.62f3`, no el título.

Coste de arreglarlo: un PNG. Es el punto con mejor relación impacto/esfuerzo de todo el
documento y por eso encabeza la lista.

### 1.2 En una build de release el jugador no puede reasignar ni una tecla

Cadena completa, verificada por grep:

- El único editor de bindings es `ControlsRuntimeEditor`, y se crea dentro del bloque
  `if (editors)` de `GameplaySceneSetup.Sequence.cs:154` — es decir, detrás de
  `RuntimeEditorPolicy.AuthoringEditorsAvailable`, que es **falso en un player de release**.
- El panel de Controles del menú principal es de **solo lectura** y su texto dice
  literalmente: *"Para cambiarlos: entra en la partida y abre ESC -> Controls"*.
- El panel de Controles de la pausa dice lo mismo.
- La pista nº 3 de la pantalla de carga dice lo mismo:
  *"Puedes reasignar cualquier tecla en ESC -> Controles"*.

Los tres apuntan a una pantalla que la build no construye. El panel interactivo anterior se
borró — correctamente, porque escribía `GameSettings.*KeyA`, un modelo que gameplay no leía
nunca — pero **nada ocupó su lugar del lado pre-juego**. La mitad de runtime funciona
(`InputBindingStore` persiste en `controls.json`, `RuntimeInputBootstrap` lo aplica al
arrancar); lo que falta es la superficie de autoría fuera del Editor.

Es la misma forma que el proyecto ya tiene documentada tres veces: cada mitad es correcta
y solo la **composición** está rota.

### 1.3 `MakeSprite` es el último sitio del proyecto que cae en la trampa `SpriteMeshType.Tight`

`MainMenuUI.UIBuilder.cs` define:

```csharp
private static Sprite MakeSprite(Texture2D tex)
    => Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
```

Sin `SpriteMeshType.FullRect`, `Sprite.Create` traza el contorno alfa de la región completa.
Medido en este proyecto, sobre las texturas reales:

| Textura | Tamaño | `Tight` | `FullRect` | Factor |
|---|---|---|---|---|
| `UI/Intro/Intro_drwaft` | 1536 × 1024 | **22,41 ms** | 0,029 ms | ×773 |
| `UI/Loading/background_ini` | 1536 × 1024 | **21,22 ms** | 0,026 ms | ×816 |
| `UI/CharacterSelection/taberna` | 1536 × 1024 | **21,40 ms** | 0,021 ms | ×1019 |
| `UI/Intro/game_name` | 1024 × 453 | **18,24 ms** | 0,088 ms | ×207 |

Y el remate: **los cuatro assets YA TIENEN un sprite**. `Resources.Load<Sprite>(path)`
devuelve no-nulo para los cuatro (comprobado), porque todos importan con `spriteMode: 1`.
`MakeSprite` existe para construir a mano, en 21 ms, un objeto que el `AssetDatabase` ya
tiene hecho y que cuesta cero.

Consecuencias medidas:

- **`BuildUI` tarda 128,4 ms.** De esos, `BuildBackground` 21,4 ms + `BuildLogo` 17,5 ms +
  el `taberna` dentro de `BuildClassSelectorPanel` ≈ 21 ms = **~60 ms, el 47 % del total**,
  son tres llamadas que deberían costar 0,08 ms sumadas.
- **El carrusel paga 21 ms cada 2,6 segundos, para siempre**, mientras el menú esté abierto.
- **Y filtra el sprite.** Medido: 9 sprites de arte de menú vivos en t=19,9 s, **11 en
  t=25,0 s** — uno nuevo cada ciclo del carrusel, ninguno destruido. ~23 objetos `Sprite`
  por minuto en una pantalla en la que el jugador puede quedarse indefinidamente.

Es exactamente la trampa que `CLAUDE.md` documenta para `BuildingObject.Assembly`
(60 % del arranque gastado en contornear edificios). Todos los demás `Sprite.Create` del
proyecto cortan regiones de 4 × 4 px, donde `Tight` es gratis. Este es el único que corta
un atlas grande, y es el único que queda.

### 1.4 Seleccionar una fila la hace MENOS legible

Medido sobre la captura de Opciones → Sonido, en píxeles reales:

| Estado | Texto | Fondo | Contraste WCAG |
|---|---|---|---|
| Fila **no** seleccionada | (185, 189, 194) | (22, 21, 27) | **9,61 : 1** |
| Fila **seleccionada** | (248, 194, 2) | (112, 87, 24) | **4,15 : 1** |

La selección pinta una píldora dorada al 15 % y encima escribe texto dorado. El resultado
pasa el AA solo por ser texto grande (umbral 3:1), falla el AA de cuerpo (4,5:1), y —lo
importante— **pierde el 57 % del contraste respecto a la fila que no está seleccionada**.
El elemento que el jugador está mirando es el peor pintado de la lista.

La corrección es de una línea: sobre un resalte dorado el texto se pinta oscuro (o casi
blanco), no dorado. El `TextSelected` dorado se diseñó para el estado *sin* píldora.

### 1.5 «Press to start» está ausente la mitad del tiempo, y cuando está no se lee

Dos defectos que se refuerzan:

- `HandlePressToStart` parpadea con `_pressToStartText.enabled = !visible` cada **0,85 s**.
  No es un pulso, es un interruptor: el texto **no existe durante 850 ms seguidos**. La
  primera captura de esta auditoría lo pilló apagado y la pantalla salió sin ninguna llamada
  a la acción. Un jugador ve lo mismo la mitad de las veces que mira.
- Cuando está encendido: TMP dorado (255, 219, 0) sin contorno, sin sombra y sin placa,
  sobre la piel del bárbaro del carrusel. Contraste medido: **2,13 : 1**. Falla todos los
  umbrales, incluido el de texto grande.

Y el pie de página («Mouse or W/S Navigate | Click or Enter Select») **ya está visible
durante el press-to-start**, describiendo un menú que el jugador todavía no puede ver.

### 1.6 El menú habla tres idiomas a la vez

| Superficie | Idioma |
|---|---|
| Menú principal, Opciones, Sonido, Vídeo, Cargar partida | Inglés |
| Pista del selector de clase | Español |
| Panel de Controles | Español (sin acentos) |
| Pantalla de carga (`LoadingText`) | Español |
| HUD, inventario, chat, editores del juego | Español |

`LoadingText.cs` ya arregló su mitad y su propio comentario explica por qué: *"la pantalla
de carga era la única superficie del juego todavía escrita en inglés, delante de una UI que
dice Comerciar, Diario, Reiniciar y GUARDAR"*. La observación era correcta y la conclusión
se quedó corta: **las nueve pantallas anteriores a ella siguen en inglés**.

Peor: los literales están **atados a la lógica**. `ExecuteOptionsItem` hace
`switch (_optMenuOptions[idx])` con `case "Inputs"`, `case "Sound"`, y
`MainMenuUITests` fija `Assert.Contains("New Game", options)`. Traducir el menú rompe el
flujo de navegación *y* tres tests. El test está atornillando el defecto.

### 1.7 Cero partículas, cero sonido de interfaz, cero movimiento

Búsqueda de `ParticleSystem` y `ParticleEmitter` en todo `UI/` → **0 resultados**.
Búsqueda de `PlaySFX`, `PlaySfx` y `HasSfx` en `UI/MainMenu`, `UI/Loading` y
`UI/PauseMenu` → **0 resultados**.

Lo único que se mueve en todo el arranque es el carrusel de fondo (una imagen nueva cada
2 s con 0,6 s de fundido) y el parpadeo binario del press-to-start. No hay:

- ni una mota, ni una chispa, ni polvo, ni humo, ni brasas;
- ni un clic al navegar, ni un tono al confirmar, ni un golpe seco al cancelar;
- ni una transición entre pantallas (los paneles aparecen y desaparecen con `SetActive`);
- ni un movimiento en el logo, ni viñeta, ni profundidad, ni parallax.

El contraste con el resto del juego es el dato: el HUD del jugador tiene `HudMoteLayer`
(motas por evento, agrupadas, encoladas), la barra de acciones tiene una cortina de motas
al cambiar de postura, el minimapa tiene `MinimapFx` con siete tipos de evento, el
inventario tira motas en el color de rareza al encontrar un objeto. Todo eso existe ya, y
el menú —la primera pantalla— no tiene nada.

### 1.8 El carrusel va demasiado rápido y decapita a los personajes

- **Ritmo:** intervalo 2,0 s, fundido 0,6 s. Cada retrato está quieto **1,4 segundos**.
  Es el ritmo de un pase de diapositivas, no el de una portada. Cinco imágenes = ciclo
  completo en 13 s, así que el jugador ve el bucle repetirse mientras decide.
- **Encuadre:** el arte es 1536 × 1024 (3:2) y la ventana es 2:1. Con
  `AspectMode.EnvelopeParent` se recorta **un tercio de la altura**. En la captura del
  bárbaro, la cabeza queda cortada por el borde superior y lo que queda visible bajo el
  logo es el torso. Cinco retratos de personaje y no se ve una cara.
- **Falta el sexto.** `BgPaths` lista elven, drwaft, mague, valkyrie, barbarian. **No hay
  `Intro_vampire`**, aunque la vampiresa es una clase jugable desde la wave11.

## 2. Panel por panel

### 2.1 Arranque — escena `Bootstrap` — **2,3 / 10**

| | |
|---|---|
| Belleza | **0,5** |
| Técnica | **4,0** |

La escena contiene 8 GameObjects: una cámara, un `EventSystem` y dos MonoBehaviours.
`GameBootstrap.InitializeCoreServices()` es, entero:

```csharp
Debug.Log("[Bootstrap] Initializing core services...");
// Service Locator registration will go here as services are migrated:
// - SaveService  - AudioService  - InputService  - AssetService (Addressables)
Debug.Log("[Bootstrap] Core services initialized.");
```

Dos `Debug.Log` y una lista de cuatro servicios que nunca registró. Es la forma
*authored-and-inert* que `CLAUDE.md` documenta una docena de veces, y aquí es inocua
porque los servicios se crean en otra parte — pero el método miente sobre lo que hace.

Visualmente **no hay arranque**: la cámara limpia a negro y un frame después
`SceneTransitionManager.LoadScene("MainMenu")` se lleva la escena. Ningún logo de estudio,
ninguna marca, ninguna advertencia de epilepsia, ningún fundido desde negro. El juego
aparece de golpe.

**Lo que falta:** un plano de marca de 1,5–2 s con el nombre Valkur, fundido de entrada y
salida, saltable con cualquier tecla. Es el sitio natural para gastar los 1 315 ms que
`MenuAssetPreloader` ya sabe aprovechar.

### 2.2 Press to start — **3,5 / 10**

| | |
|---|---|
| Belleza | **2,0** |
| Técnica | **5,0** |

Técnicamente correcto: `KeyboardInputManager.WasAnyKeyPressedThisFrame()` OR
`MouseInputManager.WasLeftMouseButtonPressedThisFrame()`, ambos con el OR-gate de backends
que el proyecto exige, y esconde el panel de menú hasta que se descarta.

Todo lo demás está en 1.5: parpadeo binario de 0,85 s, contraste 2,13:1, sin placa, sin
contorno, sin animación de entrada. El texto dice `Press to start` en inglés y está a
−60 px del centro, es decir **encima del cuerpo del personaje**, no en una banda limpia.

El pie de página con los controles del menú ya está visible aquí, lo que rompe la promesa
de la pantalla: si hay un pie que explica cómo navegar, la pantalla no es un umbral.

### 2.3 Menú principal — **3,0 / 10**

| | |
|---|---|
| Belleza | **3,0** |
| Técnica | **3,0** |

**Composición.** Logo arriba (equivocado, 1.1), panel de opciones a −150 px del centro,
pie de página con versión a la derecha y controles a la izquierda. El panel es un
`Image` de color plano `(22, 24, 28, 235)` con **esquinas rectas, sin marco, sin bisel, sin
sombra y sin sprite**. Cae justo sobre el centro de la composición del cuadro, tapando al
personaje que el carrusel acaba de traer.

**El panel es la tercera dialecto visual del juego.** El HUD tiene un kit de píxel con
bisel, remaches, chaflanes y timbre de oro (`HudArt.BakePanel`); los editores tienen
`PanelChrome` + `UITheme`; este panel tiene un rectángulo. Los tres conviven.

**Rendimiento.** Medido en vivo: `BuildUI` = **128,4 ms**, y crea **367 transforms, 328
Graphics, 208 Images, 113 TextMeshProUGUI y 166 raycast targets** — en una pantalla que
muestra cuatro filas de texto. El desglose por sub-builder:

| Sub-builder | Coste | Transforms acumulados |
|---|---|---|
| `BuildBackground` | 21,4 ms | 4 |
| `BuildOverlay` | 0,3 ms | 5 |
| `BuildLogo` | 17,5 ms | 6 |
| `BuildMenuPanel` | 2,1 ms | 23 |
| `BuildClassSelectorPanel` | **44,7 ms** | 57 |
| `BuildFooter` | 2,0 ms | 59 |
| `BuildOptionsSubmenu` | 19,2 ms | 224 |
| `BuildLoadGameSubmenu` | 15,8 ms | 365 |
| `BuildPressToStartOverlay` | 0,6 ms | 367 |

**Todo se construye en `Start`, aunque el jugador no abra nada.** Las tres pantallas que
puede no visitar nunca (selector de clase, opciones, cargar partida) cuestan **79,7 ms
de los 128,4** y **342 de los 367 transforms**. Construirlas bajo demanda es el cambio con
más efecto sobre el arranque del menú después de arreglar `MakeSprite`.

**Pie de página.** Muestra `v1.0 | Unity 2022.3.62f3`. La versión del motor es información
de desarrollo; a un jugador no le dice nada y a un pirata sí. Debe quedarse la versión del
juego y, como mucho, un hash de build.

### 2.4 Opciones (lista) — **4,3 / 10**

| | |
|---|---|
| Belleza | **3,5** |
| Técnica | **5,0** |

Cuatro filas: Inputs / Sound / Video / Back. Funciona, navega bien con teclado y ratón, y
el hover selecciona. Los problemas son de fondo, no de forma:

- **El despacho va por el literal en inglés** (`switch (_optMenuOptions[idx])`), así que la
  etiqueta es a la vez lo que se ve y la clave de la lógica.
- **Falta casi todo lo que un jugador espera en Opciones:** no hay Gameplay
  (dificultad, autoguardado, velocidad de texto), no hay Accesibilidad (tamaño de fuente,
  daltonismo, reducir movimiento, desactivar el temblor de cámara), no hay Idioma, no hay
  Créditos. Tres submenús es el mínimo de una demo.
- El ancho del panel (380) no coincide con el del menú principal (300), ni con Sonido (540),
  ni con Cargar (700), ni con Controles (760). **Cinco anchos y dos anclajes distintos**
  para cinco pantallas del mismo menú.

### 2.5 Opciones → Sonido — **3,3 / 10**

| | |
|---|---|
| Belleza | **3,0** |
| Técnica | **3,5** |

**Bug confirmado en la captura: la fila «Ducking: attenuation (dB)» muestra `-400`.**
`RefreshOptSoundRowText` decide el formato con `row.max <= 1f ? round(v*100) : v:F1`. Para
la atenuación el rango es −24…0, así que `max` vale **0**, la condición es cierta y un
valor de −4 dB se pinta como si fuera una fracción: −400. El número es real, es
internamente consistente, y **habla de otra cosa** — el patrón que `CLAUDE.md` ya nombra.
La prueba `max <= 1f` intenta preguntar "¿es esto una fracción 0..1?" y no lo pregunta.

**Cinco de las ocho filas son mandos de desarrollador:** intervalo mínimo del ambiente,
intervalo máximo, atenuación del ducking en dB, hold en ms, release en ms. Son ajustes de
mezcla, no preferencias de jugador. Un panel de sonido de jugador tiene tres o cuatro
deslizadores (Maestro, Música, Efectos, Ambiente) y un botón de prueba.

**Falta un volumen maestro.** Hay música, ambiente y SFX; no hay un control único.

**Falta realimentación:** mover el deslizador de SFX no reproduce ningún sonido de prueba,
así que el jugador ajusta a ciegas el único canal que no está sonando.

Visualmente: deslizador cian sobre pista azul oscura — un cuarto acento de color que no
aparece en ninguna otra parte del juego. Y la fila seleccionada sufre el 4,15:1 de 1.4.

### 2.6 Opciones → Vídeo — **4,8 / 10**

| | |
|---|---|
| Belleza | **3,5** |
| Técnica | **6,0** |

El panel mejor razonado de los cinco. Los cambios se **preparan y solo se aplican con
Apply**, la lista de resoluciones está curada a exactamente 2:1 por la razón correcta
(`SnapOrthoSize` garantiza píxeles enteros solo si la relación es exacta), y el estado en
vivo dice el viewport real y si es *seam-free*. Eso es honesto y poco común.

Lo que le falta:

- **No hay temporizador de reversión.** Aplicar un modo de pantalla que el monitor no
  admite deja al jugador a ciegas sin forma de volver. El patrón estándar es
  «¿Mantener esta configuración? 15 s» con reversión automática.
- **Tres filas.** No hay VSync, ni límite de FPS, ni brillo/gamma, ni escala de UI, ni
  selección de monitor. La escala de UI importa especialmente en un juego cuyo HUD se mide
  en texels enteros.
- La fila Apply pone `Enter` en la columna de valor: un nombre de tecla ocupando el hueco
  de un dato.
- Los botones `<` y `>` son bloques grises al 4 % de alfa, indistinguibles de una barra de
  desplazamiento desactivada.

### 2.7 Opciones → Controles — **3,3 / 10**

| | |
|---|---|
| Belleza | **2,5** |
| Técnica | **4,0** |

La **decisión** es correcta y está bien argumentada en el propio fichero: leer los bindings
vivos del asset en vez de mantener una tabla de cadenas paralela que gameplay no lee.
Un panel de solo lectura que dice la verdad es mejor que uno interactivo que miente.

La **ejecución** falla en cuatro sitios:

1. **Es el único panel anclado al centro** (`anchorMin 0.5, 0.5`) mientras los otros cuatro
   anclan a −280 desde arriba. Resultado visible en la captura: **el panel tapa la mitad
   inferior del logo**, cortando el «1.0».
2. **Es un muro de texto.** «Mover: W S A D Arriba Abajo Izq. Der.» — ocho controles
   concatenados como palabras, sin capuchones de tecla, sin separadores, alineados a la
   derecha a 350 px de su etiqueta. El juego ya sabe dibujar un teclado
   (`ControlsKeyboardView`, en UIKit precisamente para que lo puedan alojar los menús) y
   aquí no se usa.
3. **Muestra 12 de ~60 acciones.** Los 24 espacios de hechizo, los verbos compartidos de
   editor y todo lo demás no aparecen, y no hay scroll.
4. **Manda al jugador a una pantalla que su build no tiene** (ver 1.2).

### 2.8 Cargar partida — **5,0 / 10**

| | |
|---|---|
| Belleza | **4,0** |
| Técnica | **6,0** |

El panel más completo: dos columnas (partidas / guardados), miniatura de la cara del
personaje recortada por `uvRect`, detalle con clase, zona, nivel, XP, HP y fecha, marcado
de guardado corrupto, renombrar y borrar con confirmación. Conserva la selección por ruta
al refrescar. Eso es trabajo real y bien hecho.

Defectos:

- **Las filas de la columna RUNS son indistinguibles.** El texto completo de una fila es
  `Lv.1`. En la captura hay tres filas seguidas que dicen `Lv.1` con la misma cara. No hay
  nombre de partida, ni fecha, ni zona, ni tiempo jugado. La lista cuya única función es
  «elige cuál de tus partidas» no distingue tus partidas.
- **Bug: `ClassFaceUvRects` no tiene entrada para `vampire`.** Las cinco clases del retrato
  de grupo están; la vampiresa no. `GetFaceUvRect` devuelve entonces
  `Rect(0,0,1,1)` y la miniatura de 33 × 33 px muestra **la lámina entera de 1536 × 1024
  aplastada**, en vez de una cara. Silencioso.
- **Fechas en ISO-8601:** `2026-09-12T02:16:34`. Formato de máquina en una lista que el
  jugador lee.
- «Will operate on: Auto-Save» es lenguaje de desarrollador.
- **Doble oscurecimiento.** El `Overlay` base del menú (negro al 55 %) sigue activo debajo
  del `LoadOverlay` (otro negro al 55 %). Compuestos dan ~80 % y el arte de fondo queda
  turbio. Lo mismo pasa en Opciones y en Cargar.
- Con un solo guardado quedan ~120 px de vacío entre la lista y el detalle, porque el hueco
  de cinco filas está reservado siempre.
- Los tres botones de acción son rectángulos planos verde/azul/rojo saturados, sin icono y
  sin marco.

### 2.9 Selector de clase — **3,5 / 10**

| | |
|---|---|
| Belleza | **3,0** |
| Técnica | **4,0** |

Es la pantalla que más decide (con qué personaje juega el jugador las próximas horas) y la
que menos cuenta.

- **Los nombres son claves internas en minúscula:** `barbarian`, `elven`, `mague`,
  `valkyrie`, `dwarf`, `vampire`. `mague` no es una palabra en ningún idioma.
- **Las estadísticas son seis abreviaturas crudas sin unidades ni contexto:**
  `HP / ATK / ARM / SPD / MANA / ENG`, y detrás hay un mapeo que nadie puede adivinar
  (`MaxStrength` → HP, `MaxIntelligence` → MANA, `MaxDexterity` → ENG). `ATK: 2` frente a
  `ATK: 1` no le dice nada a nadie. Sin barras, sin comparación, sin percentil.
- **Cero descripción.** Ni una línea de qué juega cada clase, ni hechizos iniciales, ni
  arquetipo, ni dificultad recomendada.
- **Cero arte en las tarjetas.** El retrato de cabecera es siempre la misma lámina de
  taberna re-iluminada por clase, y las tarjetas son rectángulos grises `(62,62,62)` — un
  cuarto dialecto de color, el único gris del juego.
- **La marca de selección cambia de color según la clase.** `ClassBorderColors` da rojo al
  bárbaro, verde al elfo, rosa a la valquiria. Sobre la tarjeta seleccionada del bárbaro se
  ve un **borde rojo de 2 px**, que en cualquier interfaz lee como error. Y el jugador no
  puede aprender una señal única de «esto está seleccionado» porque cambia en cada fila.
- **El retrato de cabecera solo tiene cinco personajes.** La lámina de la taberna es un
  grupo pintado de cinco; las tarjetas son seis. La vampiresa existe como tarjeta y no
  existe en el cuadro, tal y como el propio código reconoce en su comentario `TEMPORARY`.
- **El retrato se muestra con bandas laterales.** `preserveAspect` en un rect de 1280 × 544
  con una imagen 3:2 deja ~230 px de pared de taberna a cada lado, lo que parece un cuadro
  flotando dentro de otro cuadro.
- La pista de abajo está en **español** mientras el resto del menú está en inglés.

### 2.10 Pantalla de carga — **6,5 / 10**

| | |
|---|---|
| Belleza | **4,0** |
| Técnica | **9,0** |

Técnicamente es la mejor superficie del arranque y la nota lo refleja: watchdog por
latido en vez de plazo fijo, límite de fallo a 35 s con panel de error y vuelta al menú,
`Show` idempotente, entrada de gameplay bloqueada hasta que el boot termina, detección de
«esta escena no tiene fase 2» con doble compuerta de segundos **y** frames, y
`LoadingText` con trece pistas en español que ninguna nombra una tecla retirada.

Lo que la deja en 4 de belleza:

- **La barra es una barra de programador.** Borde blanco de 2 px, interior negro, relleno
  **verde puro (0, 200, 0)**, porcentaje en 14 pt blanco a la derecha. Sobre una pintura de
  un dragón lanzando fuego. No hay bisel, ni gradiente, ni brillo, ni relleno animado, ni
  ninguna relación con el arte que tiene detrás.
- **No hay velo bajo el texto.** El estado, el feed de actividad y la pista se escriben
  directamente sobre el cuadro. En esta captura el texto blanco cae sobre el fuego naranja
  brillante; la legibilidad depende de qué parte del cuadro toque.
- **El feed de actividad es 12 pt blanco al 100 %** alineado a la derecha: tres líneas de
  telemetría de arranque presentadas como si fueran contenido.
- **Un solo fondo.** `background_ini` es la única imagen; se ve en todas las cargas.
- Tres de las trece pistas anuncian superficies de desarrollo (Editor General, consola con
  la tecla `` ` ``, copias de seguridad con suma de verificación). Las dos primeras no
  existen en una build de release.

## 3. ¿Usan partículas? No. Y esta es la forma correcta de añadirlas

**Respuesta corta:** ninguna de las diez pantallas usa partículas, ni un solo
`ParticleSystem` ni `ParticleEmitter` en todo el árbol `UI/`.

**Y un `ParticleSystem` NO es la respuesta aquí.** El proyecto ya resolvió esta pregunta
tres veces y la conclusión está escrita en `MinimapFx`:

> Un `ParticleSystem` es un renderizador de mundo: no ordena contra los `Graphic` de un
> canvas y no se puede intercalar entre capas de UI.

Los tres canvas del arranque son `ScreenSpaceOverlay`. Un `ParticleSystem` o queda por
delante de todo o por detrás de todo, y nunca entre el arte de fondo y el panel — que es
justo donde las motas tienen que vivir para dar profundidad.

**El patrón correcto ya está construido tres veces en este repositorio:**

| Implementación | Dónde | Qué hace |
|---|---|---|
| `HudMoteLayer` | `UI/HUD/PlayerPanel/` | Un solo `Graphic`, quads agrupados en un `mesh`, encolados, snap a texel |
| `MinimapFx` | `UI/HUD/Minimap/` | `MinimapQuadGraphic` con material aditivo, siete tipos de evento |
| `MinimapQuadGraphic` | `UI/HUD/Minimap/` | Una malla de quads, un draw call |

Un `MenuFxLayer` copiado de `HudMoteLayer` cuesta un fichero, dibuja en **una** llamada, se
ordena exactamente donde se le ponga entre hermanos del canvas y respeta la regla que el
proyecto ya defiende: **las motas responden a eventos, nunca son un bucle de fondo**.

### 3.1 Qué debería moverse, y por qué cada cosa

Ordenado por relación impacto/riesgo. Nada de esto es decorativo por decorar: cada línea
responde a un momento que hoy no produce ni un píxel.

| Momento | Efecto propuesto | Por qué |
|---|---|---|
| El logo aparece | Barrido de luz por las letras + tres motas doradas que caen | Es el único momento en que el jugador lee el título |
| Press to start | Pulso de alfa (no `enabled`) + una placa oscura difusa detrás | Arregla el 1.5 sin inventar nada |
| Navegar el menú (arriba/abajo) | La barra dorada de 4 px se desliza a la fila nueva en ~90 ms + un clic | Hoy la selección salta sin transición y en silencio |
| Confirmar una opción | Destello en la píldora + dos motas que salen del borde + tono de confirmación | Hoy no pasa nada al pulsar Enter |
| Cancelar / volver | Tono descendente + la píldora se apaga hacia fuera | Distinguir «avancé» de «retrocedí» por el oído |
| Cambio del carrusel | Motas de polvo arrastradas en la dirección del fundido | Convierte un corte en un movimiento |
| Abrir un submenú | El panel entra desde 0,96 de escala + 0 de alfa en 140 ms | Hoy es `SetActive(true)` |
| Elegir clase | Chispas en el color de la clase + el retrato empuja 8 px | La decisión más importante del arranque no tiene acuse |
| Barra de carga avanza | Chispa que corre por el borde de avance | Hace visible que el número se mueve |
| Carga completa | Destello y motas que suben antes del fundido | Marca el final del arranque |
| Fondo, siempre | 20–40 motas lentísimas de polvo con parallax por capa | Da profundidad al plano de fondo; es la única excepción de bucle y por eso va **muy** por debajo del umbral de atención |

### 3.2 Y el audio, que cuesta menos y se nota igual

El menú reproduce música (`menu_intro`) y **ningún efecto**. Cinco sonidos cubren el arranque
entero: navegar, confirmar, cancelar, error/refuso, y el golpe del press-to-start.
`AudioCatalog.asset` no tiene ids de UI, así que la ruta correcta es la que ya usan
`InventoryAudio`, `IceWallAudio` y `BoomerangAudio`: **intentar el id del catálogo con
`HasSfx` y sintetizar si no existe**, reproduciendo siempre por `IAudioService.PlaySFX`
para que el volumen de efectos aplique.

## 4. Arquitectura: lo que impide que esto escale

Estas notas explican por qué la puntuación de *escalabilidad* es 2,5 y no 6.

### 4.1 Cada píxel se coloca por aritmética absoluta

El patrón de todas las filas del menú es:

```csharp
float cy = -padY - i * (rowH + gap) - rowH * 0.5f;
```

con `rowH`, `padX`, `padY`, `gap` y `panelW` declarados como `const float` locales **en cada
builder**. Hay cinco juegos de esas constantes, uno por panel, y no coinciden entre sí
(alturas de fila 42 / 52 / 40 / 44 / 37 / 31 px). Añadir una opción al menú principal
recalcula `panelH` a mano; añadir una columna al panel de carga mueve `splitX` y los cinco
anclajes que lo usan.

**Lo que hace el HUD y aquí no existe:** un espacio de texels enteros
(`PlayerHudStyle.HudPixelScaleFor`), un `HudRect` que coloca todo en enteros desde abajo a
la izquierda, y un `*Style.asset` bajo `Resources/` que reúne las ~60 decisiones. Eso es lo
que convierte «cambiar la altura de fila» en editar un campo en vez de en una revisión.

### 4.2 Sin tokens de tema: 67 colores literales

`grep "new Color(" UI/MainMenu UI/PauseMenu UI/Loading` → **67**, en 15 ficheros, y
**cero** referencias a `HudTheme`, `UITheme` o cualquier `*Style.asset`. Los editores del
juego tienen un trinquete (`EditorRawColorRatchetTests`, 387 literales que solo pueden
bajar); los menús no tienen ni trinquete ni tema.

Cuatro paletas conviven en el arranque: el azul casi negro de los paneles, el gris (62,62,62)
de las tarjetas de clase, el cian del deslizador de sonido y el verde puro de la barra de
carga. Ninguna de las cuatro aparece en `HudTheme`.

### 4.3 Tipografía por defecto

Las 113 etiquetas `TextMeshProUGUI` del menú usan `TMP_Settings.defaultFontAsset` — la
Liberation Sans que trae TMP. Encima del logo pintado a mano, el resultado es un cartel de
fantasía sobre un formulario. El juego ya tiene dos caras propias de píxel
(`HudPixelFont`, 3×5 y 5×7) para el HUD; el menú, que puede permitirse una fuente grande de
verdad, no tiene ninguna.

### 4.4 Todo se construye en `Start`, nada se construye bajo demanda

Medido: 342 de 367 transforms y 79,7 de 128,4 ms corresponden a pantallas que el jugador
puede no abrir nunca. Además, **166 `raycastTarget` activos** — muchos pertenecientes a
paneles desactivados, es cierto, pero el canvas los reconstruye en su lote igualmente cuando
algo se marca sucio.

El patrón correcto ya está en el proyecto: `ItemsRuntimeEditor` pasó de 3 480 ms a 413 ms
virtualizando su tabla, y la conclusión de esa auditoría aplica literalmente aquí — *el
coste es uGUI, no la lógica de la fila, así que la única palanca es construir menos
widgets, menos veces*.

### 4.5 Los tests no pueden ver nada de esto, y uno fija el defecto

78 tests EditMode cubren la máquina de pantallas, el panel de carga y la visibilidad. Son
buenos y hay que conservarlos. Pero **uGUI no maqueta en EditMode**, así que ninguno puede
ver una superposición, un contraste, un panel que tapa el logo ni un texto ilegible — la
misma lección que el editor de Controles ya pagó con 914 tests en verde y una ventana que
no se podía leer.

Y `MainMenuUITests` hace `Assert.Contains("New Game", options)`, es decir **atornilla el
inglés**: traducir el menú pone tres tests en rojo por hacer lo correcto.

Lo que falta es lo que el proyecto ya hizo para las barras de mundo y el panel del jugador:
**una prueba de contraste sobre los colores compuestos** (pura, sin escena, como
`WorldBarContrastTests`) y **una captura renderizada revisada a ojo** en los cambios
grandes.

### 4.6 Memoria: 20,4 Mpx de arte de menú viven en `Resources/`

| Carpeta | Imágenes | Mpx | VRAM sin comprimir |
|---|---|---|---|
| `Resources/UI/Intro` | 6 | 7,8 | 29,8 MB |
| `Resources/UI/CharacterSelection` | 7 | 11,0 | 42,0 MB |
| `Resources/UI/Loading` | 1 | 1,6 | 6,0 MB |
| **Total** | **14** | **20,4** | **77,8 MB** |

Están comprimidas (DXT1 medido en runtime), así que el coste real es ~1/6 de esa cifra,
pero `CLAUDE.md` es explícito: *`Resources/` se carga entero en el build — mantenlo mínimo*.
Catorce láminas de 1,5 Mpx que solo usa el menú son candidatas naturales a Addressables o,
como mínimo, a que el selector de clase libere las suyas al salir. Hoy
`_portraitSpriteCache` sí se limpia en `OnDestroy`, pero las texturas siguen residentes.

## 5. Hoja de ruta

Cinco fases. Cada una es independiente y cada una sube la nota por sí sola.

### Fase 0 — Verdad (medio día) · objetivo 3,4 → 4,6

Arreglar lo que está mal dicho, antes de tocar nada estético.

1. **Rehacer `game_name.png` con el nombre Valkur.** Un PNG.
2. **Arreglar `MakeSprite`**: usar `Resources.Load<Sprite>` (los cuatro assets ya lo tienen)
   y, donde de verdad haya que crear uno, pasar `SpriteMeshType.FullRect`. Elimina ~60 ms
   del arranque del menú, los 21 ms por ciclo del carrusel y la fuga de sprites.
3. **Arreglar el `-400` de la atenuación**: el formato debe decidirse por el rol de la fila
   (fracción / segundos / dB / ms), no por `max <= 1f`.
4. **Añadir `vampire` a `ClassFaceUvRects`** o, mejor, hacer que `GetFaceUvRect` avise en vez
   de devolver el rect completo en silencio.
5. **Arreglar el contraste de la fila seleccionada**: texto oscuro sobre la píldora dorada.
6. **Anclar el panel de Controles como los otros cuatro** (−280 desde arriba) para que deje
   de tapar el logo.
7. **Quitar la versión de Unity del pie de página.**
8. **Decidir el idioma y aplicarlo a todo**, sacando los literales del `switch` a
   constantes con clave estable. Actualizar los tres tests que fijan el inglés.

### Fase 1 — Devolver el rebinding al jugador (1–2 días) · objetivo 4,6 → 5,4

El hallazgo 1.2 es funcional, no estético, y bloquea cualquier envío.

- Alojar `ControlsKeyboardView` / `ControlsMouseView` (ya están en UIKit exactamente para
  esto) dentro del panel de Controles del menú y del de pausa, con el mismo
  `InputBindingStore` detrás.
- O, si se prefiere el camino corto: **sacar `EnsureControlsEditor` del bloque
  `if (editors)`** — no es un editor de autoría, es una pantalla de opciones — y añadir un
  test que lo fije, del mismo modo que `EditorEntryPointTests` fija que todo editor sea
  alcanzable.
- Corregir las dos pistas de `LoadingText` que nombran superficies de desarrollo.

### Fase 2 — Un sistema, no cinco paneles (3–4 días) · objetivo 5,4 → 7,0

Esta es la fase que hace que todo lo demás sea barato.

- **`MenuStyle.asset` bajo `Resources/UI/`** con las ~50 decisiones (colores, alturas de
  fila, márgenes, tiempos, tamaños de fuente), siguiendo exactamente el patrón de
  `PlayerHudStyle` y `MinimapStyle`. Motivo por el que va en `Resources/`: `MainMenuUI` se
  crea por `AddComponent` desde `AutoBootstrap` y no tiene ranura de inspector — el defecto
  `ChatSystem._catalog` documentado.
- **`MenuPanelArt`**: una lámina generada una vez (marco con bisel, chaflán, placa, píldora
  redondeada, flecha, capuchón de tecla) en un solo atlas y un solo material, como
  `HudArt` y `MinimapIconAtlas`. Ahí desaparecen los rectángulos planos.
- **Un `MenuRow` y un `MenuPanel` reutilizables** que sustituyan los cinco juegos de
  constantes. Una altura de fila, un margen, un ancho de panel.
- **Construcción bajo demanda** de selector de clase, opciones y cargar partida.
- **Tipografía de marca** para títulos y filas.
- **Un solo velo**: apagar el `Overlay` base cuando hay un overlay de submenú, o usar un
  único `CanvasGroup` con un valor.

### Fase 3 — Que se mueva y suene (2–3 días) · objetivo 7,0 → 8,2

- **`MenuFxLayer`** copiado de `HudMoteLayer`: un `Graphic`, quads agrupados, motas por
  evento (§3.1).
- **Transiciones de panel** (140 ms, escala 0,96→1 y alfa) y **deslizamiento de la barra de
  selección** (~90 ms).
- **Cinco sonidos de interfaz** por `HasSfx` con síntesis de respaldo (§3.2).
- **Press to start**: pulso de alfa con placa, no `enabled`.
- **Carrusel**: intervalo de 2,0 s → 6,0 s, fundido 0,6 → 1,2 s, con un *Ken Burns* muy
  lento (escala 1,00 → 1,04 durante la permanencia) y un reencuadre que respete las
  cabezas. Añadir la lámina de la vampiresa.
- **Viñeta y gradiente inferior** para que el pie de página y las pistas se lean sobre
  cualquier zona del cuadro.

### Fase 4 — Contenido que falta (2–3 días) · objetivo 8,2 → 9,0

- **Plano de marca en `Bootstrap`**: 1,5 s, saltable, con fundidos.
- **Selector de clase de verdad**: nombres legibles, una frase de arquetipo, barras
  comparativas en vez de seis números crudos, hechizos iniciales, arte por tarjeta, y una
  sola señal de selección (la misma para las seis).
- **Filas de partida legibles**: nombre, clase, zona, tiempo jugado y fecha en formato
  local.
- **Opciones que un jugador espera**: maestro de volumen, prueba de SFX, VSync, límite de
  FPS, escala de UI, reducir movimiento, tamaño de fuente, idioma, créditos.
- **Vídeo con reversión por temporizador.**
- **Barra de carga con el lenguaje del juego**: marco biselado, relleno con gradiente y
  chispa de avance, velo bajo el texto, y más de un fondo.

### Cómo quedaría la tabla al final de la Fase 4

| Eje | Hoy | Objetivo |
|---|---|---|
| Identidad de marca | 0,5 | 9,0 |
| Idioma | 2,0 | 9,0 |
| Sistema de diseño | 1,0 | 8,5 |
| Tipografía | 2,0 | 8,0 |
| Movimiento | 1,5 | 8,0 |
| Partículas / atmósfera | 0,0 | 8,0 |
| Audio de interfaz | 0,5 | 8,0 |
| Contraste | 2,5 | 9,0 |
| Rendimiento | 3,0 | 8,5 |
| Robustez | 5,0 | 8,5 |
| Escalabilidad | 2,5 | 8,5 |
| Test del aspecto | 3,0 | 7,0 |
| **Global** | **3,4** | **8,5** |

## 6. Apéndice — cómo se midió

- **Capturas:** `ScreenCapture.CaptureScreenshot` en Play Mode a 1600 × 800. El
  renderizador de capturas del puente MCP **omite en silencio los canvas de overlay**, así
  que cualquier captura del menú hecha con él saldría vacía; `CaptureScreenshot` es la única
  vía.
- **Pantallas dirigidas por reflexión:** `ShowMenuScreen` es privado; se invocó con
  `BindingFlags.NonPublic | Instance` sobre el enum anidado `MenuScreen`.
- **El parpadeo del press-to-start es una moneda al aire para una captura.** La primera
  salió sin el texto; hizo falta forzar `enabled = true` para fotografiarlo. Eso es, en sí
  mismo, el hallazgo 1.5.
- **Coste de `Sprite.Create`:** dos `Stopwatch` consecutivos sobre la misma textura, `Tight`
  y `FullRect`, con `DestroyImmediate` entre medias. El compilador del puente es CodeDom, de
  modo que `Object.DestroyImmediate` es ambiguo y hay que escribir
  `UnityEngine.Object.DestroyImmediate`.
- **Fuga del carrusel:** `Resources.FindObjectsOfTypeAll<Sprite>()` filtrado por el nombre
  de la textura, muestreado dos veces con ~5 s de diferencia.
- **Coste de `BuildUI`:** una segunda instancia de `MainMenuUI` creada en un GameObject
  desactivado, con cada sub-builder invocado por separado contra un canvas de sonda y
  cronometrado.
- **Contrastes:** muestreo de píxeles de los PNG capturados con Pillow y fórmula WCAG 2.1
  sobre luminancia relativa linealizada.
- Consola de Unity: **0 errores, 0 advertencias** al terminar.

---

# Parte 2 — Lo implementado (2026-09-12)

> Escrito el mismo día que la auditoría, sobre el mismo árbol, con la consola de Unity a cero
> errores. Todo lo que se afirma aquí está medido en vivo o pinchado por un test; donde una cosa
> no se ha hecho, lo dice.

## 7. El título ya no es un PNG

**`Resources/UI/Intro/game_name.png` no se dibuja en ninguna pantalla.** En su lugar hay tres
piezas:

| Pieza | Dónde | Qué es |
|---|---|---|
| `TitleGlyphStrokes` | `Core/UI/` | Una fuente de TRAZOS: A-Z, 0-9 y puntuación, en caja em, pura y testeable sin escena |
| `TitleParticleField` | `UI/MainMenu/Title/` | Un `MaskableGraphic` que dibuja la nube como quads en UNA llamada |
| `MenuTitle` | `UI/MainMenu/Title/` | Dónde va, cuándo se junta, y las brasas que suelta |

**Por qué trazos y no una máscara.** Se sopesaron tres caminos. Un `TextMeshPro` renderizado a
`RenderTexture` y leído con `ReadPixels` da tipografía real y cuesta una cámara, un render target,
un tirón en el arranque, y una nube de puntos sin noción de dirección — las motas podrían
posarse en la palabra pero nunca recorrerla. Una máscara pintada como imagen es el PNG que
sustituye, con una resolución y una palabra. Una tabla de trazos es datos puros: determinista,
testeable en EditMode, sin assets, y lleva lo único que las otras dos no pueden dar — **la
DIRECCIÓN de la pluma**, que es lo que permite que una mota baje por un asta y se pose al final
en vez de teletransportarse.

Y una consecuencia que solo el trazo permite: cada punto sabe a qué distancia está del centro de
su propio trazo (`TitlePoint.Depth`), así que la palabra se ilumina **como metal caliente** —
casi blanca en el núcleo, oro en el cuerpo, brasa en el borde. Eso, horneado en una textura, es
lo que hacía el PNG viejo; derivado, es tres campos de `MenuStyle`.

**Por qué NO un `ParticleSystem`.** La regla que este proyecto ya escribió dos veces, en
`HudMoteLayer` y en `MinimapFx`: un `ParticleSystem` es un renderizador de mundo y no ordena
contra los `Graphic` de un canvas *Screen Space Overlay*. Quedaría delante de todos los paneles o
detrás del fondo, nunca **entre** el arte y el menú, que es justo donde vive un título.

**Tres tiempos.** Las motas se dispersan fuera de pantalla, entran siguiendo la dirección de su
propio trazo, y se posan. Ya posadas lo único que se mueve es un temblor de menos de un píxel y
un barrido de luz que cruza la palabra cada pocos segundos. Un título que sigue animándose es un
salvapantallas; uno perfectamente quieto es un PNG con pasos de más.

Medido en vivo a 1600 × 800: **2 410 motas** sobre una caja de **632 × 153 px**, es decir
**25,0 motas por cada 1 000 px²**, un `draw call`, material `Valkur/UI/HudFx` en aditivo.

### 7.1 Lo que costó afinarlo, medido

| Iteración | Qué se midió | Resultado |
|---|---|---|
| Primera | 1 252 motas, 13,4 por 1 000 px² | La palabra se leía pero con huecos; competía con la cara del elfo detrás |
| Halo v1 | Caída cuadrática desde el centro | Quitaba **8,6 de 134** de luminancia al cielo: casi nada |
| Halo v2 | Meseta y luego caída | El halo llega a la palabra a alfa completa; la palabra gana sobre cualquier fondo |
| Final | 2 410 motas, 25,0 por 1 000 px² | Legible sobre los seis fondos del carrusel |

La lección del halo es la reutilizable: **una caída cuadrática pura ya está al 25 % de su fuerza
cuando llega a lo que tiene que proteger.** Lo que hace falta es una meseta y después el
desvanecido.

## 8. El plano de marca

`BrandSplash` (`UI/MainMenu/Title/`) es lo primero que se ve: la palabra juntándose sobre negro,
1,5-2 s, saltable desde el primer frame con cualquier tecla o clic.

- Llega a `GameBootstrap` por un **relé** (`GameBootstrap.SplashHandler`), no por una llamada
  directa, porque `Valkur.Core` no puede referenciar `Valkur.UI`. Es exactamente la forma que el
  proyecto ya usa dos veces, con `LoadingReporter` y con `SceneTransitionManager.LoadSceneHandler`.
  Declinar es una respuesta NORMAL: sin la capa de UI, el menú carga igual que antes.
- **Es el MISMO título que dibuja el menú, y esa continuidad es el objetivo.** La palabra se junta
  una vez aquí y el menú la encuentra ya montada (`BrandSplash.Consumed` → `MenuTitle.SnapSettled`).
  Dos ensamblajes seguidos se leerían como un bucle, no como una presentación.
- `GameBootstrap.InitializeCoreServices` ya no es dos `Debug.Log` alrededor de una lista de cuatro
  servicios que nunca registró: aplica los ajustes de vídeo que posee Unity (v-sync, límite de
  fotogramas) antes de que se dibuje un frame.

## 9. Un sistema, no cinco paneles

| Pieza | Qué resuelve |
|---|---|
| `Data/UI/MenuStyle.cs` + `Resources/UI/MenuStyle.asset` | Las ~70 decisiones. Los 67 `new Color(` literales |
| `MenuArt` | Un atlas generado: marco con bisel y chaflán, píldora, barra de acento, cápsulas de tecla, flechas, deslizador, motas — más tres piezas **bilineales** aparte (viñeta, rampa inferior, placa suave) |
| `MenuUIKit` | Panel, cabecera, pista, botón, divisor, y el cálculo de contraste WCAG |
| `MenuRow` | Una fila: etiqueta, valor, contenido, y **UN solo objeto que captura el puntero** |
| `MenuList` | La lista con **una píldora que se DESLIZA** entre filas |
| `MenuPanelView` | Panel que se dimensiona a su contenido, con animación de apertura |
| `MenuSlider` | El deslizador sobre el atlas, con muescas y paso real |
| `MenuTypography` | La fuente, resuelta en `Valkur.UI` desde una RUTA guardada en `Valkur.Data` |

**El tipo de fuente se guarda como ruta y no como `TMP_FontAsset`**, y es una decisión de
arquitectura, no un atajo: `Valkur.Data` solo ve `Valkur.Core`, y eso es lo que permite que un
fixture de EditMode cargue un catálogo sin arrastrar la pila de UI detrás. Es la misma llamada que
hacen `LoadoutStateSheets.state` y `SpellDefinition.previewAnimState`.

**Un solo velo.** El menú apilaba un negro al 55 % detrás de todo Y otro dentro del *overlay* de
cada subpantalla, así que un panel abierto se apoyaba en un cuadro oscurecido dos veces. Ahora hay
uno, que se **profundiza** cuando se abre una subpantalla y se levanta al cerrarla.

**Un solo anclaje.** Todas las subpantallas cuelgan de `MenuStyle.panelTopOffset`. El panel de
Controles se centraba y tapaba el título del juego.

## 10. Contraste, medido antes y después

| Estado | Antes | Después |
|---|---|---|
| Fila **no** seleccionada | 9,61 : 1 | **16,46 : 1** |
| Fila **seleccionada** | **4,15 : 1** | **7,73 : 1** |
| «Pulsa cualquier tecla» sobre el arte | **2,13 : 1** | Sobre placa suave, no sobre la pintura |

La corrección es de una línea y está en `MenuUIKit.ReadableOn`: **sobre una píldora dorada el
texto va oscuro**, no dorado. Y ahora hay un fixture que lo mide — `MenuContrastTests` compone los
colores y calcula la razón WCAG **sin escena ni canvas**, que es la única forma de pinchar
legibilidad en EditMode dado que uGUI no maqueta ahí.

## 11. Partículas, movimiento y sonido, donde antes había cero

`MenuFxLayer` es el `HudMoteLayer` del menú: un `Graphic`, quads agrupados, un `draw call`,
material aditivo. **Las motas responden a EVENTOS, nunca a nada.** Lo que emite hoy:

| Momento | Efecto |
|---|---|
| Mover la selección | Dos motas por el borde de ataque de la píldora, en la dirección del viaje |
| Cruzar el umbral | Un estallido de 22 chispas y un barrido por el título |
| Elegir clase | 18 chispas en el color de esa clase, en su tarjeta |
| Cambio del carrusel | Polvo a la deriva mientras cruza el fundido |
| Título posado | Brasas subiendo, 7/s repartidas por seis letras — el ÚNICO bucle, y deliberadamente por debajo del umbral de atención |

Y `MenuSfx`: cinco sonidos —mover, confirmar, cancelar, rechazar, empezar—, **id de catálogo
primero y síntesis de respaldo**, porque `AudioCatalog.asset` no tiene un solo id `ui_*` y
`PlaySfxById` avisa una vez por id que no resuelve, por diseño. **Los cinco se distinguen por la
DIRECCIÓN del tono, no solo por el tono**: confirmar sube, cancelar baja, rechazar es un golpe
seco sin movimiento ninguno.

Además: transiciones de panel (escala + alfa, nunca un deslizamiento — un panel que entra desde un
lado afirma una relación espacial entre dos pantallas que el menú no tiene), la píldora que se
desliza en 90 ms, y el «Pulsa cualquier tecla» que **respira en alfa** entre 0,45 y 1 en vez de
encenderse y apagarse cada 0,85 s.

## 12. Lo que ya se puede hacer y antes no

- **Reasignar teclas antes de jugar.** El panel de Controles escribe el modelo real
  (`InputBindingStore`), no una tabla paralela, y **no depende de `RuntimeEditorPolicy`**. En una
  build de release el jugador podía reasignar cero teclas.
- **Elegir idioma.** `GameLanguage` en `Valkur.Core`, con `ChatLanguage` reenviando y conservando
  su clave de PlayerPrefs, así que nadie que ya lo leyera tuvo que cambiar.
- **Reducir movimiento.** Apaga el ensamblaje del título, las motas, el empuje del carrusel y las
  animaciones de panel, **en el frame en que se pulsa**.
- **V-sync, límite de fotogramas y tamaño de interfaz**, con **temporizador de reversión de 15 s**:
  aplicar un modo que el monitor no admite ya no deja al jugador a ciegas.
- **Continuar de verdad.** «Continuar» carga el guardado más reciente en vez de abrir un
  navegador de ficheros. «Cargar partida» es una fila aparte.
- **Distinguir tus partidas.** La columna de RUNS decía `Lv.1` y nada más; ahora dice el nombre de
  la partida (`RunGroupInfo.displayName`, que existía y no se leía), el nivel y la fecha **en
  formato local** — no `2026-09-12T02:16:34`.
- **Elegir clase sabiendo qué eliges.** Nombres reales (`DisplayName`, que existía y no se leía),
  una frase por clase, barras comparativas contra el máximo del catálogo, y **UN solo color de
  selección** para las seis en vez de uno por clase (el bárbaro llevaba un borde ROJO, que en
  cualquier interfaz se lee como error).
- **Créditos.**

## 13. Puntuación después

| Eje | Antes | Después | Cómo se comprueba |
|---|---|---|---|
| Identidad de marca | 0,5 | **9,5** | `MenuTextTests.TheGameTitle_IsTheGamesName` |
| Idioma | 2,0 | **9,0** | `MenuTextTests.EveryString_AnswersInBothLanguages` |
| Sistema de diseño | 1,0 | **8,5** | `MenuStyleAndArtTests`, paleta derivada de `HudTheme` |
| Tipografía | 2,0 | **6,0** | Una sola vía (`MenuTypography`); falta la fuente de marca |
| Movimiento | 1,5 | **8,5** | `MenuListTests.TheHighlight_TravelsRatherThanTeleporting` |
| Partículas / atmósfera | 0,0 | **9,0** | 2 410 motas medidas, más el `MenuFxLayer` por evento |
| Audio de interfaz | 0,5 | **8,0** | `MenuSfx`, cinco sonidos con respaldo sintetizado |
| Contraste | 2,5 | **9,0** | `MenuContrastTests`, medido |
| Rendimiento | 3,0 | **7,0** | La trampa `Tight` retirada; falta construcción bajo demanda |
| Robustez | 5,0 | **8,5** | Consola a cero; despacho por enum |
| Escalabilidad | 2,5 | **8,5** | Un kit, un estilo, una altura de fila |
| Test del aspecto | 3,0 | **7,5** | Cinco fixtures nuevos, ~70 tests, contraste incluido |
| **Global** | **3,4** | **8,4** | |

## 14. Lo que queda, dicho con nombre

Honestamente, y sin adornarlo:

1. ~~Todo se sigue construyendo en `Start`.~~ **CERRADO.** Solo se construye la portada; el
   selector de clase, los cuatro paneles de Opciones, el navegador de guardados y los créditos se
   construyen la primera vez que se piden (`EnsureScreenBuilt`). Los cuatro de Opciones van
   JUNTOS, porque construirlos uno a uno pondría un tirón en mitad de una navegación que el
   jugador ya está haciendo. Pinchado por `LazyMenuConstructionTests` (9 tests), incluido un
   techo sobre cuántos `Graphic` puede construir la portada, para que nadie lo deshaga sin verlo.
2. **`textSize` y `uiScale` llegan al menú, al plano de marca y a la pantalla de carga; a los
   canvas del HUD todavía no.** Existe ya el sitio correcto —`HudLayout.ApplyScaler`, que fija
   referencia, *match* y la preferencia del jugador en una llamada— y los tres canvas de esta
   tarea pasan por él. Quedan **veintiún** canvas poniendo los tres valores a mano:

   ```text
   UI/HUD/HUDManager, MinimapHUD.UIBuilder, Minimap/WorldMapPanel.UIBuilder,
   Music/MusicPlayerHUD.Layout, Debug/DebugHUD.Build, CharacterSheetController.UIBuilder,
   DayNightClockHUD, DayNightVignetteOverlay, StanceHUD, Trees/SkillTreeHUD.Build,
   UI/DeathBannerUI.Builder,
   Gameplay/HUD/{CharacterStatsHUD, StatisticsHUD, QuestLogHUD.Window, SpellTreeHUD.Build},
   Gameplay/Inventory/InventoryUI.Build, Gameplay/UIKit/HUDIconBar,
   Gameplay/Editors/{Map/MapEditorUI.Builder, Tile/TileEditorUI.Builder,
   Tile/TileEditorBorderOverlay, DungeonNodeGraph/DungeonNodeGraphEditor.UI}
   ```

   No se han tocado a propósito: tres sesiones estaban editando la mitad de esa lista en el
   momento de escribir esto, y una migración mecánica a través de ficheros que otro está
   reescribiendo es la forma de generar un conflicto que nadie sabe resolver. Es una pasada
   mecánica de una sola línea por fichero cuando el árbol esté quieto.
3. **No hay fuente de marca.** Todo el menú sigue en la TMP por defecto. `MenuStyle.menuFontResource`
   es la ranura y está vacía.
4. **El retrato de grupo sigue teniendo cinco figuras y hay seis clases.** La vampiresa está
   compuesta contra la lámina vacía. Es un límite de la pintura, no del código.
5. **`MenuSfx` y `GrimoireAudio` sintetizan por separado.** Dos ventanas que suenan parecido no
   urge; que suenen distinto de lo que ya existe, sí. El sitio natural es un sintetizador
   compartido en `Valkur.UIKit`.
6. **El kit vive en `Valkur.UI`.** La sección 5.2 de `HUD_VISUAL_LANGUAGE.md` dice que tiene que
   estar en `Valkur.UIKit`, porque `Valkur.Gameplay -> Valkur.UI` está prohibido y esa es la causa
   estructural de que cada ventana del juego se inventara su estilo. La tienda, el chat, el
   crafteo y las misiones están todas en Gameplay. Mover el kit es trabajo pendiente acordado.
7. **Las capturas de Opciones, Audio, Vídeo, Controles y el selector están pendientes**, no por el
   código sino por la ventana: tres sesiones compartían el Editor mientras se escribía esto.

## 15. Lo que los tests encontraron, y por qué merece su propia sección

La primera pasada de los trece fixtures devolvió **19 aserciones rojas, todas propias**. Cinco
causas, y la distribución importa más que el número: **dos eran defectos de producción que
ningún test anterior podía ver, tres eran tests mal escritos, y de esos tres el peor lo escribí
midiendo el objeto equivocado.**

### 15.1 Dos defectos reales, y los dos son "el producto está mal aunque las dos mitades estén bien"

**Las columnas de una fila se solapaban, en dos sitios distintos.**

| Dónde | Izquierda acaba | Derecha empieza | Solape a 560 px |
|---|---|---|---|
| Etiqueta contra contenido | 0,52 | 0,50 | **11 px** de pista de deslizador bajo la cola de su etiqueta |
| Deslizador contra valor (Audio) | 0,91 | 0,86 | **28 px** de número encima de la pista que describe |

Ninguna de las dos cifras está mal por sí sola. 0,52 es un ancho de etiqueta razonable; 0,50 es un
inicio de contenido razonable. El segundo caso es peor porque el 0,91 **ni siquiera está escrito
en ningún sitio**: es `ContentColumnStart + hostMax × (1 − ContentColumnStart)`, o sea el producto
de dos números que viven en ficheros distintos y que nadie compara. Es exactamente la forma que
este proyecto ya tiene documentada para la deriva de coordenadas de los spawners: cada mitad es
internamente consistente y solo la COMPOSICIÓN es falsa.

La corrección no es mover un número: es **nombrar las fronteras** (`LabelColumnEnd`,
`ContentColumnStart`, `ValueColumnStart`, `WideValueColumnStart`) y declarar que una fila con
widget y valor usa un reparto DISTINTO de una fila que solo tiene valor — `UseWideContentColumns`.
Dos disposiciones con dos nombres, en vez de una constante con un `+ 0.26f` sumado en el sitio de
la llamada.

### 15.2 El test que medía la fila y la llamaba columna

`MenuColumnLayoutTests` leía `row.Label.transform.parent` para obtener el rect de la columna. Pero
la etiqueta ESTÁ sobre el rect de la columna, así que `.parent` es la FILA, cuyo `anchorMax.x` es
1,0 por definición. El test informaba de `label ends at 1.000` — un número real, consistente
consigo mismo, y **sobre otra pregunta**.

Lo que lo hace instructivo es que fallaba en la dirección menos sospechosa: reportaba un defecto
que no existía mientras era incapaz de ver el que sí. Un test que lee el objeto equivocado es peor
que no tener test, porque consume la confianza que debería estar ganando.

### 15.3 Un test que pinchaba una ORTOGRAFÍA en vez de una regla

Seis rojos fueron `Assert.AreEqual($"{n}%", pct.text)` contra una etiqueta que pasó a decir
`"0 %"` — el español pone espacio antes del signo. La regla que ese fixture existe para defender
es *«la etiqueta refleja el relleno»*, y la ortografía no es la regla. Ahora los dos lados pasan
por `LoadingScreenController.FormatPercent`, así que cambiar la tipografía no puede volver a poner
en rojo una etiqueta correcta.

Es el mismo defecto que esta auditoría pasó doscientas líneas criticando en producción —
`switch (_menuOptions[index]) case "New Game"` — cometido en un test.

### 15.4 Un ulp, y una letra mal elegida

- `'Q'` termina su cola exactamente en `advance + 0.02`, y la comparación en coma flotante pierde
  por una unidad en el último lugar. El límite habla de que un glifo se salga de su caja, no de
  igualdad de floats; se enuncia con holgura.
- `Sampling_SpreadsAcrossTheStrokeWidth` usaba la `'I'`. La I lleva **dos serifas horizontales de
  0,28 em**, que dominan su caja: repartir puntos a lo ancho de un trazo HORIZONTAL los reparte en
  VERTICAL, así que la medida se movía 2 px y el test suspendía una implementación correcta. La
  comilla simple es un único trazo vertical, que es precisamente lo que se estaba preguntando.

### 15.5 Ocho rojos que eran la consecuencia correcta de un cambio deliberado

`MainMenuLoadPanelTests` afirmaba sobre widgets que `Start` ya no crea, porque el navegador de
guardados se construye bajo demanda. No es un defecto: es un fixture que tenía que pedir el panel.
Se distingue del caso 15.2 en que aquí el test medía lo que decía medir y lo que cambió fue el
mundo.

### 15.6 El estado de máquina que un `TearDown` no protege

El idioma vive en `PlayerPrefs`: sobrevive al run, al Editor y al reinicio. `MenuTextTests` lo
guardaba en `SetUp` y lo restauraba en `TearDown` — **y un run que se corta nunca llega al
`TearDown`**. La restauración está ahora dentro de cada test, en un `finally`, que es el camino
que se recorre aunque una aserción lance.

La regla general, escrita en `GameLanguage` donde la buscará quien escriba el siguiente fixture:
cualquier cosa que escriba `GameLanguage`, `Time.timeScale`, `Debug.unityLogger`, `LogAssert` o un
`PlayerPrefs` restaura **en la misma llamada y en un `finally`**, y no confía en un teardown que
puede no ejecutarse.

### 15.7 Tres fuentes de verdad sobre "¿hay un run vivo?"

Medido durante esta sesión, con tres sesiones compartiendo un Editor:

| Fuente | Qué contesta | Qué NO contesta |
|---|---|---|
| Registro de MCP (`clear_stuck`) | Si el puente cree que tiene un job | Nada sobre el runner de Unity — contestó *"No running job to clear"* con un runner vivo |
| `TestJobDataHolder.TestRuns` | Si Unity tiene un run registrado | Si ese run está vivo o es un huérfano de una recarga de dominio |
| `TestRunStatus._isRunning` | **Lo que bloquea `refresh_unity` con `tests_running`** | Nada sobre las otras dos |

Y la que separa un run vivo de un huérfano no es ninguna de las tres, sino el objeto del run:
**`editModeRunner == null` con `isRunning == true` es un huérfano; con el runner vivo, es un run
que simplemente no ha terminado.** Mirarlo antes de "arreglar" nada evitó limpiar un run que
estaba corriendo — que es como se acaba con dos runners cruzándose las ventanas de log.

Un apunte más, porque cuesta caro: en el payload del runner, `data.progress.total` es el tamaño de
la SUITE ENTERA filtres o no. Lo que responde a *«¿cuánto corrió lo mío?»* es
`data.progress.completed` y `data.result.summary.total`. Un filtro que no casa nada devuelve
`total: 0, resultState: Passed` — verde con cero tests.

## 16. El panel que se salía de la pantalla, encontrado sumando constantes

Esto no lo encontró ningún test ni ninguna captura. Lo encontró **sumar los números que ya
estaban escritos**, que es la misma herramienta que cerró la deriva de coordenadas de los
spawners y el `SetupStepTotal = 53` del arranque.

Todo panel cuelga de `MenuStyle.panelTopOffset` = **296** y crecía libremente hacia abajo sobre un
lienzo de referencia de **800**:

| Pantalla | Filas | Alto pedido | Borde inferior | Veredicto |
|---|---:|---:|---:|---|
| Opciones | 5 | 360 | 656 | cabe |
| Audio (simple) | 6 | 410 | 706 | cabe |
| **Audio (avanzado)** | 11 | 660 | **956** | **DESBORDA** |
| Vídeo | 6 | 474 | 770 | cabe |
| Juego | 6 | 450 | 746 | cabe |
| **Controles** | 13 | 804 | **1100** | **DESBORDA** |
| Créditos | 0 | 414 | 710 | cabe |

Las dos que desbordan dejan fuera de la pantalla las últimas filas **y la barra de pistas entera**
— o sea, el sitio donde se lee qué tecla vuelve atrás.

### 16.1 Por qué era invisible

Cada cifra por separado es defendible: una fila mide 46, el hueco entre filas 4, un panel empieza
en 296, la barra de título mide 54. Ninguna es sospechosa. Lo falso es el **producto**, y el
producto no está escrito en ningún sitio: nace de una suma repartida entre `MenuStyle`,
`MenuPanelView.FitToContent` y el constructor de cada pantalla. Es exactamente la forma que este
proyecto ya tiene documentada tres veces — *«cada mitad es internamente consistente y solo la
COMPOSICIÓN es falsa»*.

Y no podía fallar ruidosamente por una razón estructural ya conocida: **uGUI no hace layout en
EditMode**, así que ningún test podía medir el rect resultante, y en juego un rect que se sale
simplemente se dibuja fuera. Nada lanza.

### 16.2 La corrección: un techo derivado y una ventana, no filas más pequeñas

```csharp
public static float AvailableHeight(MenuStyle style)   // 800 − 296 − 56 = 448
public void  FitToContent(float contentHeight, float extra = 0f)  // recorta y lo dice
public float BodyHeight { get; }    // lo que quedó de cuerpo
public bool  Overflows  { get; }    // el contenido no cupo
```

El techo es **derivado**, no escrito: mover `panelTopOffset` mueve el límite con él, así que la
corrección no puede quedarse obsoleta al retocar el estilo. Y el panel **dice** que no cupo en vez
de recortar en silencio, que es lo que permite que la lista reaccione.

Lo que hace la lista cuando no cabe es `MenuList.SetViewport(BodyHeight)`: una **ventana** sobre
las filas, con `ScrollIntoView` empujando lo mínimo necesario — una fila cada vez, no media página,
que es lo que hace que una lista de teclado se lea como una lista. Dos detalles sostienen esto:

- **Una fila fuera de la ventana se OCULTA.** El cuerpo del panel no lleva máscara, y añadir una
  rompería el batching del que vive el resto del menú; una fila dejada dibujada fuera del panel se
  pinta encima de lo que haya detrás.
- **La píldora de selección se oculta con su fila.** Si no, queda una barra dorada flotando sobre
  el marco del propio panel, que es peor que no tener resalte.

La alternativa —encoger las filas hasta que quepan— se descartó por lo que cuesta: haría que
Controles fuese *silenciosamente distinto* de Opciones, y la altura de fila es justo lo único que
este kit existe para mantener idéntico en todas partes.

### 16.3 Lo que lo fija

`MenuPanelFitTests` afirma sobre lo que sí se puede medir en EditMode — anclas, tamaños declarados
y la composición `panelTopOffset + alto <= lienzo` — nunca sobre un rect ya maquetado:

- un panel corto toma exactamente el alto que pidió;
- uno alto se recorta **y lo reporta** (`Overflows`);
- ningún alto de contenido, ni 2000, llega más abajo del lienzo;
- el techo sigue al ancla del que se mide;
- una lista que cabe no desplaza y dibuja todas sus filas;
- una que no cabe desplaza, oculta lo de fuera, y **la fila seleccionada siempre está dentro**;
- la ventana no se pasa de ninguno de los dos extremos;
- bajar una fila desplaza una fila, no una página.

## 17. El título se dibujaba más grueso de lo autorizado, y lo destapó un test que estaba mal

El test `Sampling_SpreadsAcrossTheStrokeWidth` afirmaba *«más filas a lo ancho ensanchan el trazo
dibujado»*. Medía 14,3 con una fila contra 21,6 con cinco sobre un trazo autorizado de 20, y
suspendía. **La premisa era falsa y la implementación era correcta en ese punto**: el jitter se
escala por el ESPACIADO entre filas (`/ max(1, rowsAcross - 1)`), así que con una sola fila la fila
ES la barra y ese único punto se reparte por casi toda su anchura. Una barra cuyo grosor creciera
con la densidad de muestreo sería justo el defecto — `strokeWidth` dejaría de significar nada y el
peso del título cambiaría cada vez que el presupuesto de motas adelgazara la nube.

Reescrito como lo que sí se puede afirmar — *«la barra mide lo que se autorizó, con cualquier
número de filas»* — y **probado a cuatro recuentos en vez de a uno**, apareció un defecto real:

| Filas | Ancho dibujado (trazo autorizado 20) | |
|---:|---:|---|
| 1 | 14,3 | dentro |
| **2** | **30,4** | **la mitad más gruesa** |
| 5 (lo que enviamos) | 21,6 | 8 % de más — invisible |

(Las tres cifras son medidas; 9 filas no se llegó a medir antes del arreglo.)

A dos filas el espaciado ES la barra entera, así que el jitter llegaba a ±0,45 de anchura sobre
unas filas ya repartidas a ±0,5. Con **un solo** recuento de filas el test habría pasado dentro de
cualquier tolerancia razonable y el defecto seguiría ahí.

### 17.1 La corrección, y por qué no es un recorte

Recortar el desplazamiento a ±0,5 apila puntos exactamente en el borde y dibuja una regla impresa
justo donde la barra tiene que tener filo. Lo correcto es **estrechar la banda por la amplitud del
propio jitter**, de forma que un punto agitado aterrice EN el borde y nunca más allá:

```csharp
float jitterAmplitude = 0.45f / Mathf.Max(1, rowsAcross - 1);
float halfBand        = Mathf.Max(0f, 0.5f - jitterAmplitude);
float across = rowsAcross == 1 ? 0f
             : ((row / (float)(rowsAcross - 1)) - 0.5f) * 2f * halfBand;
float jitter = (NextUnit(ref hash) - 0.5f) * 2f * jitterAmplitude;
```

Ahora `|across + jitter| <= 0,5` exacto. Medido después, sobre un trazo de 20: **1 fila 14,35,
2 → 15,45, 5 → 17,14, 9 → 19,67** — ninguna se pasa, y el ancho crece con el número de filas
porque hacen falta puntos para alcanzar los extremos de una distribución, no porque la banda se
ensanche. De regalo, `Depth` deja de saturar en el `Clamp01`: el centro de la barra es 0 y su
borde es 1, que es lo que hace que la palabra se pueda iluminar como metal caliente.

### 17.2 El número autorizado tuvo que moverse, y eso es parte de la corrección

`titleStrokeWidth` estaba afinado **a ojo** en 17, sobre un muestreador que dibujaba 1,225× lo
autorizado a cinco filas: o sea, lo que se aprobó mirando la pantalla fueron **20,8**. Arreglar el
muestreador sin tocar el dato habría adelgazado el título en un 18 % sin que nadie lo pidiera. El
dato es **21** en la clase y en `Resources/UI/MenuStyle.asset`, verificado en memoria y contra el
texto del propio fichero en la misma sonda (`memory=21 disk=titleStrokeWidth: 21`) — porque
`refresh_unity(scope="scripts")` no reimporta un `.asset` y `LoadAssetAtPath` devuelve la copia en
memoria.

Es la forma inversa del error habitual: aquí el número mentía porque el código lo amplificaba, así
que la corrección de verdad son las dos mitades a la vez.


## 18. Lo que solo se ve en un fotograma: dos defectos que ninguna aserción verde podía ver

Con las nueve capturas hechas, el selector de clase salió **ilegible**: seis bloques color crema
apilados sobre cada tarjeta, tapando el nombre de la clase y las palabras Vida / Ataque /
Armadura. Todas las aserciones de EditMode sobre ese panel estaban en verde, por la razón que
este proyecto ya tiene escrita: **uGUI no maqueta en EditMode**.

### 18.1 Un rect que nadie decidió

`MenuUIKit.Rect` dejaba los valores por defecto de Unity, que son **100x100 con anclas
centradas**. Todo el que anclaba el rect después lo sobrescribía sin enterarse; los que no,
dibujaban un bloque de 100x100. En las barras de estadística eran **dos por barra, seis barras
por tarjeta, seis tarjetas: doce losas por carta**.

La corrección no es anclar esos tres sitios, es que `MenuUIKit.Sprite` **estire por defecto sobre
su padre**. Eso cierra la clase entera: quien quiera otra geometría la escribe en el rect que se
le devuelve, igual que antes, y un hijo de un layout group lo dimensiona el grupo de todas formas.
Lo que deja de ser posible es una imagen cuyo tamaño no fue una decisión.

Y sí se puede fijar en EditMode, aunque la maquetación no exista, porque el defecto tiene una
**huella dactilar**: anclas centradas Y exactamente 100x100. Las dos mitades hacen falta —un
distintivo de 100x100 deliberado es normal, y un ancla centrada también; solo el par dice
«aquí no escribió nadie». `MenuDefaultRectTests` recorre el menú entero buscando esa huella.

### 18.2 La lista de claves que se limpiaba y nunca se rellenaba

Al mirar el retrato de cabecera apareció el segundo, y este **no es cosmético**. `_classKeys` se
vaciaba en el constructor del panel y **no se le añadía nunca nada**. Las seis tarjetas se dibujan
directamente del catálogo, así que la pantalla parecía terminada: el resalte se movía, las
tarjetas escalaban, las barras eran correctas. Pero todos los lectores de esa lista abren con

```csharp
if (_selectedClassIndex < 0 || _selectedClassIndex >= _classKeys.Count) return;
```

así que una lista vacía desactivaba en silencio el retrato de cabecera, el nombre de la clase
elegida, el acento de identidad **y la llamada a `PlayerSelectionState.SetSelectedPlayer`**.
Elegir personaje no registraba nada: la partida empezaba con la clase que hubiera guardada de
antes. Medido en juego: `_classKeys.Count = 0` con seis tarjetas en pantalla y el índice
seleccionado en 2.

Las cuatro listas paralelas se rellenan ahora **en el único método que fabrica una tarjeta**, que
es lo que hace imposible que una se quede atrás.

### 18.3 El test que pasó sobre la nada, y cómo se destapó

El primer intento de fijar todo esto estaba **verde y era hueco**. Unity no llama a `Start` en un
componente añadido en EditMode, así que `_canvasTransform` era null, `EnsureScreenBuilt` retornaba
en su primera línea y no se construía nada: comparar la longitud de cuatro listas vacías pasa,
y recorrer una jerarquía vacía buscando rects por defecto no encuentra ninguno.

Lo destapó el único aserto que no podía pasar en vacío — «la lista tiene más de cero claves» —
que salió rojo y arrastró al resto. Los dos fixtures llaman ahora a `Start` por reflexión, y el
de los rects lleva además un **suelo explícito** (`inspected > 100`), porque un test que no puede
distinguir «todo correcto» de «no había nada que mirar» informa de una cobertura que no tiene.
Es la misma forma que este proyecto documenta para `EditorReachabilityTests`.

### 18.4 Dos trampas del método de captura, medidas

- **`ScreenCapture.CaptureScreenshot` escribe al FINAL del frame.** Capturar y luego navegar en
  la misma llamada guarda la pantalla SIGUIENTE: mis nueve ficheros salieron corridos un puesto
  (`06_Controls.png` contenía el selector de clase). La regla segura, precisada por una sesión
  par que verificó el caso contrario en sus propias capturas: **cambia, captura, y no toques nada
  más en esa llamada** — un cambio hecho ANTES de pedirla sí entra.
- **El nombre del fichero es una etiqueta que pone quien captura, no una medida.** Con el
  contenido correcto y la etiqueta mal, la captura sigue sirviendo; lo que no sirve es leerla sin
  darse cuenta.

Verificado después en un fotograma: las seis tarjetas con su nombre legible y seis barras de
pista y relleno comparables entre clases, y Controles cerrando a 745 sobre un lienzo de 800 con
su barra de pistas visible y la lista desplazándose.
