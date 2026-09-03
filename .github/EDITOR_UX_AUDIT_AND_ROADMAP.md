# Editor UI/UX — auditoría y hoja de ruta

> Auditoría medida el 2026-09-02 sobre `Scripts/Gameplay/Editors/`.
>
> Objetivo: que los 16 editores en juego compartan una UI/UX profesional,
> funcional, robusta, escalable **y persistente entre sesiones**.

## 1. Inventario medido

16 carpetas de editores · **319 ficheros** · **~77.6k LOC** · 15 registrados en
el launcher ESC (`GeneralEditorRegistry`); `General` es el launcher mismo.

| Editor | LOC | Drag | MenuChrome | PanelChrome | Tutorial | CamPan | Zoom | Resize | Search |
|---|---:|---|---|---|---|---|---|---|---|
| Tile | 15245 | si | si | si | si | si | si | si | **NO** |
| Buildings | 10684 | si | si | si | **NO** | si | si | **NO** | si |
| Map | 9637 | si | si | si | **NO** | si | si | **NO** | **NO** |
| Particles | 8899 | si | si | si | **NO** | si | **NO** | si | si |
| Spells | 7104 | si | si | si | **NO** | si | **NO** | si | si |
| FSM | 4758 | si | si | si | si | **NO** | **NO** | **NO** | si |
| Items | 4372 | si | si | si | si | si | **NO** | si | si |
| Entities | 4054 | si | si | si | si | si | **NO** | **NO** | si |
| Boss | 2904 | si | si | **NO** | **NO** | si | **NO** | **NO** | **NO** |
| Lighting | 2141 | si | si | **NO** | si | si | **NO** | **NO** | si |
| Spawners | 1972 | si | si | **NO** | si | si | **NO** | **NO** | si |
| TimeWeather | 1886 | si | si | si | si | **NO** | **NO** | **NO** | **NO** |
| Inventory | 1390 | si | si | si | si | si | **NO** | **NO** | si |
| Camera | 1201 | **NO** | **NO** | **NO** | **NO** | **NO** | **NO** | **NO** | **NO** |
| DungeonNodeGraph | 835 | **NO** | **NO** | **NO** | **NO** | **NO** | **NO** | **NO** | **NO** |
| General | 510 | **NO** | **NO** | **NO** | **NO** | **NO** | **NO** | **NO** | **NO** |

## 2. Hallazgos

### 2.1 La persistencia prácticamente no existe

El único estado que sobrevive a un Stop/Play son las **columnas ocultas de
tabla**, en 3 editores, vía `PlayerPrefs`, con **tres implementaciones
copiadas**:

- `Items/ItemsRuntimeEditor.TableColumnsConfig.cs:77`
- `Particles/ParticlesRuntimeEditor.Table.cs:85`
- `Spells/SpellsRuntimeEditor.TableColumnsConfig.cs:77`

Nada más persiste. Posición de panel, tamaño, minimizado, qué paneles estaban
abiertos, modo activo, pestaña, scroll, zoom, texto de búsqueda, tamaño de
pincel, capa seleccionada y objeto seleccionado: **todo muere en cada Play**.

### 2.2 `Deactivate()` oculta, no destruye — y eso disimula el agujero

Los diez editores con `Deactivate()` hacen `_root.SetActive(false)`. El estado
sobrevive *dentro* de una sesión, así que el problema solo aparece al arrancar
— y con Domain Reload OFF, aparece en el momento peor.

### 2.3 El repo tiene un anticuerpo contra la persistencia, y tiene razón

`TileEditorTheme.cs:48` resetea los 8 campos del tema en cada entrada a Play.
Su comentario lo dice: lo que un autor arrastraba en el panel UX de F8
sobrevivía a Stop/Play y sangraba en los paneles de todos los editores.

El reset es correcto — pero existe **porque no hay store**. Sin una capa que
distinga *fuga de estado estático* de *preferencia autorada*, la única defensa
posible es tirarlo todo. Ese es exactamente el hueco que abre esta hoja de ruta.

### 2.4 Tres fuentes de tema y 459 colores crudos

> **Actualizado 2026-09-03 (F4):** de los 459, **79 se migraron a tokens** y quedan **387**.
> Pero el número grande engañaba: medidos contra los tokens existentes, solo **37 de 421**
> eran duplicados exactos. El resto son colores semánticos de un solo uso (contorno cian,
> tintes de arrastre, franjas de categoría, chips de leyenda del FSM) donde un reemplazo
> masivo cambiaría píxeles que nadie pidió cambiar. Lo que sí apareció al mirar los valores
> repetidos fue **una barra de scroll copiada literalmente en cinco editores** (pista ×12 +
> asa ×10) y **el rojo en reposo de un botón destructivo copiado en 19 sitios** — tres
> tokens nuevos (`SCROLL_TRACK`, `SCROLL_HANDLE`, `DANGER_IDLE`) que cubren 41 de esos 79.
> Los 387 restantes los sujeta un trinquete, no una promesa: ver §6.2.

`UITheme` (tokens, 60 líneas) → `EditorUIHelpers` (fachada que reexporta
`UITheme`) → `TileEditorTheme` (chrome mutable en runtime, lo que de verdad
pintan los paneles). Encima, **459 `new Color(` literales** dentro de los
editores: Map 90, Tile 85, Spells 68, Buildings 40, Particles 39, FSM 34,
Items 32, TimeWeather 24, resto menos de 11.

Un editor no puede ser visualmente consistente mientras la mayoría de sus
píxeles no pasen por el tema.

### 2.5 Huecos duros

- **Zoom**: `EditorCameraZoomController` solo en 3 de los 11 editores que panean. Ocho
  dejan al autor mover la cámara del mundo sin poder acercarse. **Cerrado en F4.**
- **Tutorial ausente en 8**, incluidos los tres más grandes tras Tile.
- **`PanelResizeHandle` solo en 4**.

### 2.6 Corrección: la columna de chrome de §1 estaba mal medida

La tabla de §1 dice que Camera, DungeonNodeGraph y General no tienen chrome. **Eso es un
error de medición**, corregido el 2026-09-03. El grep contaba si el editor NOMBRA
`DraggablePanel` / `PanelChrome` en sus propios ficheros; Camera, General, Boss, Lighting,
Spawners y Tile no lo nombran porque construyen a través de
`EditorUIHelpers.MakeDropPanel`, que añade **ambos** componentes. Es decir: los que la
tabla marcaba peor son los que lo estaban haciendo bien.

Re-medido con la regla transitiva (nombra el tipo **o** llama a la fábrica):

- **15 de 16 tienen chrome real.** El único hueco es **DungeonNodeGraph**.
- Los nueve con builder propio emparejan `DraggablePanel` + `PanelChrome` +
  `MenuBarChrome` sin excepción. Ahí no había deriva ninguna.

DungeonNodeGraph es una **losa a pantalla completa con regiones ancladas**, no paneles
flotantes. Convertirlo a `MakeDropPanel` sería reescribir su layout, que es una decisión
de diseño y no una corrección de paridad — queda **abierto y explícito**. Lo que sí se
arregló es su paleta: tenía cinco constantes privadas que habían derivado del kit (su fondo
de fila 0.18 contra 0.17, su panel 0.10/0.95 contra 0.09/0.94), así que un autor retocando
el aspecto de los editores habría visto obedecer a todos menos a ese.

## 3. Diagnóstico

No falta disciplina: el patrón canónico ya está escrito
(`.claude/agents/editor-ux-parity.md`, 10 secciones) y aun así deriva, porque
nada lo hace fallar.

Lo que sí aguanta en este repo son los contratos con test —
`FSMBuiltInTransitionRegistryTests`, `AssetConventionsTests`,
`DomainReloadStaticResetTests`. Todos comparten forma: **el test escanea el
código y falla cuando alguien añade algo sin declararlo.**

Faltan tres piezas, y las tres:

1. Un modelo de estado de UI serializable por editor.
2. Un único propietario del ciclo guardar/restaurar, enganchado donde ya pasa todo.
3. Un test de contrato que impida a un editor nuevo saltarse la capa.

Sin (3), las otras dos se degradan igual que se degradó la paridad.

## 4. Arquitectura

Capa nueva en `Gameplay/Editors/_Shared/Workspace/`.

### 4.1 Piezas

- **`EditorLayoutSnapshot`** — por panel: id, posición, tamaño, minimizado,
  abierto, orden Z. Se captura **genéricamente desde `DraggablePanel`**, sin
  código por editor. 13 de 16 editores ya lo usan, así que esto entrega el
  grueso del layout sin tocarlos.
- **`DraggablePanel.PanelId` + `CaptureState()` / `ApplyState()`** — el único
  cambio en UIKit.
- **`IEditorWorkspaceStore`** → `JsonEditorWorkspaceStore` en
  `Application.persistentDataPath/EditorWorkspace/<editor>.json`.
- **`EditorWorkspaceService`** (ServiceLocator), enganchado en **un solo sitio**:
  `GameEditorManager.OpenExclusive` / `NotifyDeactivated`. Un hook, no dieciséis.
- **`IProvidesWorkspaceState { Capture(w); Restore(w); }`** — opcional por
  editor, para lo específico: modo activo, categoría, búsqueda, zoom, capa,
  selección. Los 3 `TableColumnsConfig` duplicados migran aquí y desaparecen.

### 4.2 Por qué `persistentDataPath` y no `PlayerPrefs`

`PlayerPrefs` en Windows es el registro: sin versionado, sin backup, sin
escritura atómica, y con tope práctico por entrada. El repo ya tiene el patrón
bueno (`IRepository` + escritura atómica + checksum + backups rotatorios) y es
donde ya viven `Saves/` y `profile.json`.

Un layout es preferencia personal de máquina, no dato del proyecto: fuera de
git, sin ensuciar diffs ni provocar conflictos.

### 4.3 Alcance: layout + sesión + selección viva

Decidido el 2026-09-02. Tres niveles, cada uno con su propia fragilidad:

1. **Layout** — geometría de paneles. Genérico, sin código por editor.
2. **Sesión** — modo activo, pestaña, búsqueda, columnas, zoom, capa. Valores
   propios, se validan contra su propio dominio al restaurar.
3. **Selección viva** — qué objeto estaba seleccionado (edificio, emisor,
   spell, estado FSM). **Es la parte frágil** y necesita política explícita.

### 4.4 Política de resolución de la selección restaurada

Un id guardado puede haber desaparecido entre sesiones: el edificio se borró, el
slot de mapa cambió, la zona es otra, el catálogo se reautoró. Reglas:

- La selección se guarda como **par `(tipo, id estable)`**, nunca como índice de
  lista ni referencia de escena. Un índice apunta a otro objeto en cuanto la
  lista cambia de orden, y falla en silencio.
- Al restaurar, se resuelve **contra el mundo vivo**. Si no resuelve, la
  selección queda **vacía** y el editor abre en su estado neutro. Nunca se
  selecciona "el más parecido" ni "el primero": seleccionar el objeto
  equivocado es peor que no seleccionar nada, porque la siguiente acción del
  autor edita algo que no eligió.
- Una selección que no resuelve **no es un warning**. Es el caso esperado al
  cambiar de slot de mapa o de zona. Se informa por `SetStatus`, no por consola
  — la consola debe seguir limpia (regla cardinal).
- La selección se guarda **junto al slot de mapa / zona en que se tomó**, y se
  descarta de entrada si el contexto al abrir es otro. Es más barato que
  intentar resolver y fallar, y evita el falso positivo de un id reutilizado
  entre slots.

### 4.5 Rescate de layout

Un layout guardado a 2560 px deja paneles fuera de alcance a 1366 px. Al
restaurar, todo panel que caiga fuera del canvas vivo vuelve a su dock por
defecto. Sin esto, la persistencia es una trampa de un solo sentido.

El fichero lleva **versión de esquema**; una versión desconocida se descarta
entera en vez de leerse a medias.

### 4.6 Qué pasa con `TileEditorTheme`

Mantiene su reset estático — sigue siendo correcto con Domain Reload OFF — y el
store rehidrata **después**. Distinguir fuga de preferencia es justo el punto de
toda esta capa.

## 5. Agentes

Dos: uno ampliado, uno nuevo. Ninguno nuevo para la fase de arquitectura.

### 5.1 Ampliar `editor-ux-parity` (existente)

Sigue siendo el auditor/aplicador **por editor**. Se le añaden tres secciones al
patrón canónico:

- **11 · Persistencia de workspace** — cada panel declara `PanelId`; el editor
  implementa `IProvidesWorkspaceState`; no escribe `PlayerPrefs` por su cuenta;
  la selección se guarda como `(tipo, id estable)` con su contexto.
- **12 · Tema** — cero `new Color(` crudo; un único origen; `PanelChrome`
  presente.
- **13 · Feedback** — `[Tooltip]` en cada control, estado vacío legible, error
  visible en `StatusText` y no solo en consola.

### 5.2 `editor-workspace-architect` (nuevo)

Dueño de la **capa**, no de los editores. Ámbito estrecho a propósito:
`_Shared/Workspace/`, `UIKit/`, `Core/GameEditorManager.cs` y los tests de
contrato. **Prohibido editar los 15 editores** — eso lo hace parity.

La separación existe porque los dos trabajos tienen criterios de "hecho"
incompatibles: la capa está hecha cuando el test de contrato pasa con 15
editores registrados; un editor está hecho cuando su auditoría de 13 secciones
sale limpia. Un agente con ambos objetivos negocia consigo mismo y afloja el
contrato para poder cerrar editores. Mismo motivo por el que `unity-tester`
tiene prohibido tocar producción.

### 5.3 Por qué no un tercero

Un agente "editor-theme" partiría el tema del resto de la paridad, y el tema es
precisamente lo que parity ya audita por editor. Fragmentar más multiplica la
coordinación sin cubrir nada nuevo.

El test de contrato lo escribe `unity-tester`, que ya existe.

## 6. Fases

| Fase | Trabajo | Dueño |
|---|---|---|
| F0 | Este documento | — |
| F1 | Capa Workspace + `DraggablePanel.PanelId` + `EditorWorkspaceContractTests`. Sin tocar editores | `editor-workspace-architect` + `unity-tester` |
| F2 | Dos pilotos: **Items** ✅ y **Tile** ✅ — ambos 2026-09-03 | parity |
| F3 | Los 13 restantes ✅ 2026-09-03 — 15/15 editores registrados adoptados | parity |
| F4 | Chrome para Camera / DungeonNodeGraph / General; unificar tema (459 colores); zoom en los 10 que no lo tienen | parity + `unity-architect` |

Si Tile entra limpio en F2, la capa aguanta los otros trece. Si no, el fallo es
de la capa y se arregla en F1 antes de tocar nada más.

## 6.1 Estado por editor

Los 15 editores registrados implementan `IProvidesWorkspaceState`. `General` es el
lanzador: sin `DraggablePanel` y sin estado propio, no tiene nada que recordar.

| Editor | Estado propio que recuerda | Selección viva | Columnas migradas |
|---|---|---|---|
| Items | modo, pestaña, búsqueda, ítem del catálogo | drop del mundo (`DropId`) | ✅ |
| Tile | herramienta, capa, pincel, AUTO, tile, terreno, sub-modo, tag de colisión, capa de salto, 5 toggles de vista | celda | n/a |
| Particles | modo, preset, búsqueda, pestaña, panel de spells | emisor (`StableGuid`) | ✅ |
| Spells | spell, búsqueda, filtro de audiencia | — (edita catálogo) | ✅ |
| Buildings | modo, plantilla, búsqueda, pestaña, pincel de colisión | edificio (`InstanceId`) | n/a |
| Map | restricción de edición por zona | zona (por nombre) | n/a |
| Entities | modo, categoría, búsqueda, entidad | — | n/a |
| Lighting | modo, búsqueda, preset | — | n/a |
| Spawners | modo, búsqueda | — (ver nota) | n/a |
| Inventory | modo, categoría, dos búsquedas, entidad | — | n/a |
| FSM | búsqueda, zoom del grafo | — (ver nota) | n/a |
| Camera | cue en edición | — | n/a |
| Boss | jefe, fase | — | n/a |
| TimeWeather | — (solo layout, ver nota) | — | n/a |
| DungeonNodeGraph | grafo abierto | — | n/a |

**Ningún editor reabre en un modo destructivo.** Buildings no restaura Delete ni Erase,
Entities no restaura Delete, Inventory no restaura DeleteItem, Tile no restaura sus modos
de pintado de colisión ni de saltos. Abrir un editor directamente en un modo que borra es
como se destruye algo que solo se iba a mirar.

**Lo que se decide NO persistir, y por qué** — cada caso es una decisión, no un hueco:

| Editor | No persiste | Razón |
|---|---|---|
| Tile | modos de colisión y saltos, portapapeles, zoom de cámara | reset de seguridad autorado; semántica de portapapeles del sistema; escalera de `SnapOrthoSize` |
| Lighting | `_ambientEnabled` / `_pointLightsEnabled` | override de diagnóstico sobre el ciclo vivo: restaurarlo dejaría el mundo a oscuras sin explicación |
| Spawners | instancia seleccionada | sus coordenadas son el objeto de `SPAWNER_COORDINATE_SPACE_DRIFT`; resolverla por esa vía apuntaría la siguiente edición a otro spawner |
| FSM | set y estado seleccionados | un estado solo existe dentro de un set (clave compuesta que un renombrado invalida), y elegir mal el set es editar los jefes en vez de los melee |
| Boss | chart y cue | un índice de cue es una posición en una línea de tiempo que el autor está editando |
| Map | `NextZoneIndex` | contador derivado del disco; restaurarlo acuñaría un nombre que colisiona |
| TimeWeather | todo | la hora y el clima son estado del MUNDO, con dueños propios (`DayNightCycle`, `WeatherManager`) — restaurarlos sobrescribiría el mundo que el autor cargó |
| DungeonNodeGraph | los nodos | son el documento, que ya se guarda en disco; una segunda copia discreparía |

Lo que el piloto de Items dejó fijado para los demás:

- **La clave de la bolsa de sesión es un literal, nunca `nameof()`.** Es una clave en
  disco: renombrar el campo C# no puede huerfanizar en silencio el valor guardado.
- **Se guarda la CLAVE de la pestaña, no el int al que mapea.** La clave (`"equipment"`)
  es el vocabulario que comparten la UI y el autor; el int es un id interno que un
  refactor del catálogo puede renumerar, y renumerado restauraría otra pestaña sin fallar.
- **Restaurar la pestaña llama a `SetActive(key)` y NO toca `_categoryFilter`.** El
  `TabChanged` que dispara es quien lo actualiza — escribir ambos crea dos dueños de un
  mismo hecho.
- **`SetTextWithoutNotify` al restaurar la búsqueda**, o el restore reentra en el callback
  y refresca dos veces por un cambio que no hubo.
- **Solo se recuerda un drop VIVO y PERSISTENTE** (con `DropId`). Guardar el id de un drop
  de runtime garantizaría un registro irresoluble, y la política existe para que lo
  irresoluble sea la excepción.
- **Un encabezado de columna que el esquema ya no tiene se descarta al restaurar**, o la
  etiqueta de conteo miente y el popup no ofrece casilla para volver a mostrarla.
- **Una entrada ausente significa "no hay nada guardado", no "todo visible".**

### Lo que el piloto de Tile añadió

**Encontró un fallo real en la capa, que es para lo que estaba.**
`TileEditorManager.Deactivate()` limpia `SelectedCellPos` y **después** llama a
`NotifyDeactivated`. Un cierre llega al manager por dos caminos y en un editor que se
autocierra **disparan los dos**: el llamante captura antes de `Deactivate`, y el editor
notifica después. La segunda captura escribía ese null encima de la selección que la
primera había grabado bien. `GameEditorManager` ahora deduplica por cierre
(`_capturedOnClose`), y `EditorWorkspaceCaptureDedupTests` lo fija con un editor falso que
reproduce exactamente esa forma.

**Tres cosas NO se persisten en Tile, cada una por decisión:**

- **`CurrentColliderMode` / `CurrentLayerJumpMode`** — `HandleToggle` los resetea a `None`
  en cada activación. Es una decisión de seguridad: abrir F8 directamente en un modo de
  pintado destructivo es como se pinta colisión sobre un mapa que solo se iba a mirar.
  Restaurarlos anularía ese reset deliberado.
- **El portapapeles** — `TileEditorState` lo documenta con semántica de portapapeles del
  sistema, perdido al cerrar. Persistirlo además exigiría serializar referencias a tiles,
  que son assets, no datos.
- **El zoom de cámara** — `EditorCameraZoomController` no guarda nivel que leer, y escribir
  `orthographicSize` de vuelta choca con la escalera de `SnapOrthoSize`: mantiene el ortho
  en peldaños donde un texel de arte son píxeles enteros de pantalla, y un valor restaurado
  entre peldaños hace que todos los tiles de la pantalla reptén.

**Y dos reglas nuevas para los trece restantes:**

- **`WorkspaceRoot` es el CANVAS, no lo que `SetVisible` conmuta.** Tile no tiene raíz
  visible única: `SetVisible` conmuta la barra de menú y el indicador de capa por separado,
  y los ocho paneles son desplegables por debajo. Recorrer algo más estrecho se salta
  paneles en silencio — un fallo que parece "la persistencia va a ratos", no un bug.
- **Un índice de lista nunca es un id.** Tile guarda `(categoría, nombre)` y **no**
  `SelectedCatalogIndex`: el índice es una posición que un re-import reordena, y fallaría
  seleccionando otro tile sin avisar. Misma familia que la regla del `PanelId`.

## 6.2 F4 — el trinquete del tema

Los 387 colores crudos que quedan están fijados por
`EditorRawColorRatchetTests` contra
`Tests/EditMode/Baselines/editor-raw-colors.txt`: un recuento por fichero que **puede bajar
libremente y no puede subir nunca**. Un fichero nuevo con colores crudos y no listado
también falla, porque si no, la vía de escape sería crear un fichero.

Es la misma forma que las dos únicas convenciones que han aguantado en este repo
(`DomainReloadStaticResetTests`, `FSMBuiltInTransitionRegistryTests`): el test escanea el
código y falla cuando alguien añade algo sin declararlo. La alternativa —reescribir los 387
de golpe— se descartó con datos: solo 37 de 421 coincidían exactamente con un token, así
que el resto habría sido adivinar la intención de un diseñador.

`Tile/TileEditorTheme.cs` es la única exclusión, y un tercer test falla si ese fichero
desaparece — de otro modo la exclusión dejaría de excluir nada en silencio y quien lo
sustituyera quedaría sin guardia.

## 7. Criterios de aceptación

- `EditorWorkspaceContractTests` falla si un `IGameEditor` registrado no declara
  `PanelId` en todos sus `DraggablePanel`.
- Un round trip guardar/cargar devuelve el layout dentro de tolerancia de un
  píxel de canvas.
- Un layout guardado a una resolución mayor no deja ningún panel fuera de
  alcance a 1366x768.
- Una selección que no resuelve deja el editor en estado neutro y **no** escribe
  en consola.
- Consola Unity limpia (0 errores, 0 warnings accionables) tras cada lote.
