# HERRAMIENTAS — auditoría de UI/UX

> Medida el 2026-09-09 sobre el frame RENDERIZADO en Play Mode, no sobre el código.
> Esa distinción es el motivo de que el hallazgo A exista: los 8192 tests de EditMode
> estaban en verde, la aritmética del panel era correcta, y la pestaña se dibujaba rota.
> `CLAUDE.md` ya lo dice — **uGUI no hace layout en EditMode**, así que leer `sizeDelta`
> devuelve el número que se escribió y nunca lo que una pasada de layout haría con él.
>
> Alcance: la tira de pestañas, las cinco entradas de HERRAMIENTAS
> (Seleccion · Map Backups · Combat Ranges · Debug HUD · Save Log) y el panel completo
> de la herramienta de Seleccion.

## Puntuación por eje

| Eje | Nota | Qué la baja |
|---|---|---|
| Layout renderizado | **2.0** | la tira de pestañas se dibuja a 180 px en vez de 24 (A) |
| Legibilidad de estado | **3.0** | Save Log reporta ON para siempre (B) |
| Reversibilidad y seguridad | **4.5** | Borrar grupo sin confirmación, contra la norma del proyecto (H) |
| Propiedad de Escape | **5.0** | Map Backups no reclama Escape; funciona por casualidad (C) |
| Aprovechamiento del espacio | **5.0** | 180 px de vacío (60 % del panel) en dos de las tres pestañas (E) |
| Consistencia con los otros editores | **6.5** | sin overlay tutorial, sin ayuda al pasar el ratón |
| Persistencia | **6.5** | la pestaña activa no se recuerda (F) |
| Retroalimentación | **7.5** | cada acción escribe en la línea de estado; falta el aviso de deshacer en Duplicar |
| Alcanzabilidad por teclado | **9.0** | medido: el foco sigue a la pestaña abierta en las tres |
| Nomenclatura e idioma | **9.0** | español sin acentos, igual que Muerte / Economía / Skills |
| Cobertura de tests | **1.0** | **0 de 13 ficheros nuevos tienen test** (K) |

**Media 5.5 / 10.** El grueso está en tres defectos concretos, no repartido.

## A — SEVERO · la tira de pestañas se dibuja a 180 px

Medido en vivo, con la pestaña HERRAMIENTAS abierta:

```text
Tabs   rendered=180   LayoutUtility pref=24  flexible=1.00   [LE pref=24 flex=-1]
  Act_EDITORES      85 x 180
  Act_HERRAMIENTAS  85 x 180
  Act_JUEGO         85 x 180
```

Tres barras verticales gigantes con la etiqueta centrada en medio. La captura no deja duda.

**Causa, y es la trampa que `CLAUDE.md` ya documenta palabra por palabra** para la fila de
entrada del chat: `Tabs` lleva un `LayoutElement` **y** un `HorizontalLayoutGroup` en el mismo
GameObject. uGUI resuelve cada propiedad de layout de forma INDEPENDIENTE — el
`LayoutElement` (prioridad 1) gana la altura preferida y deja `flexibleHeight` sin poner
(-1), así que el valor que se usa es el del `HorizontalLayoutGroup`, que reporta **1** porque
tiene `childForceExpandHeight` activo. `Tabs` es el único hijo flexible del contenido, así
que absorbe todo el sobrante: `274 − 12 − 78 − 4 = 180`. Exactamente lo medido.

**Arreglo:** `flexibleHeight = 0` en el `LayoutElement` de `Tabs`. Una línea.

**Lo que esto dice del proceso:** la nota existía, estaba escrita, y aun así se volvió a
escribir el mismo patrón. La única defensa que funciona no es una nota — es medir el frame.

## B — SEVERO · el Save Log es un interruptor de una sola dirección

Medido llamando `SaveTelemetryHUD.Toggle()` cuatro veces:

```text
inicio    : Instance=null              (el lanzador muestra OFF)
toggle #1 : Instance!=null visible=True  (ON)      correcto
toggle #2 : Instance!=null visible=False (ON)      <-- tinte MENTIROSO
toggle #3 : Instance!=null visible=False (ON)      <-- ya no se puede reabrir
toggle #4 : Instance!=null visible=False (ON)      <-- nunca más en la sesión
```

Dos defectos en el mismo sitio:

1. `Close()` sólo desactiva `_root`; el GameObject es `DontDestroyOnLoad`, así que
   `Instance` no se vuelve null nunca. El botón lee `isActive: () => Instance != null` y
   **reporta ON durante el resto de la sesión**, con el panel oculto.
2. `Toggle()` es `if (Instance == null) Open(); else Instance.Close();`. Tras el primer
   cierre `Instance` sigue no siendo null, así que **siempre llama a `Close()`**. El Save Log
   queda inalcanzable hasta reiniciar Play.

Es la forma exacta que este proyecto ya tiene catalogada: un control que informa de un estado
que no tiene. Preexistente — venía de la sección DIAGNOSTICS — pero ahora vive aquí.

Combat Ranges y Debug HUD se midieron igual y **los dos hacen el viaje de ida y vuelta bien**
(`False → True → False`).

## C — MODERADO · Map Backups no reclama Escape

Medido con el navegador abierto: `EscapeOwnership.IsClaimed = False`.

`MapBackupBrowserUI.Update` cierra con Escape, y `GeneralEditorManager.Update` sólo cede ante
una reclamación. Con el navegador arriba hay **dos lectores de una pulsación** en un orden que
nada fija — que es literalmente el fallo por el que `SaveTelemetryHUD` sí reclama.

Hoy no se nota porque los dos caminos quieren lo mismo (volver al lanzador) y
`GeneralEditorManager.Activate` es idempotente. Funciona por casualidad, no por construcción.

## D — MODERADO · la cabecera repite el nombre de la pestaña

`Act_HERRAMIENTAS` (pestaña activa, en oro) y justo debajo `Hdr_Tools` con el texto
`"HERRAMIENTAS"`. La misma palabra dos veces separadas por 4 px, y 18 px de alto gastados.

La cabecera tenía sentido cuando las tres secciones estaban apiladas y había que separarlas.
Con pestañas, la tira YA dice en cuál estás.

## E — MODERADO · 180 px de vacío en dos pestañas de tres

`ComputePanelHeight` devuelve la altura de la pestaña MÁS ALTA (EDITORES, 19 entradas = 7
filas = 228 px). HERRAMIENTAS ocupa 78 px y JUEGO 78 px, así que el panel queda **60 % vacío**
en dos de las tres.

Fue una decisión deliberada (un panel que no salta), pero el precio medido es peor que el
salto: un panel que es sobre todo hueco se lee como algo a medio construir.

## F — MENOR · la pestaña activa no se recuerda

`CaptureWorkspace` escribió **0 claves de sesión**. El lanzador vuelve siempre a EDITORES,
aunque el autor haya pasado la sesión en HERRAMIENTAS. Los filtros de la herramienta de
Seleccion sí se persisten; la pestaña no.

## G — MENOR · `RefreshActiveStates` repinta 29 botones para mostrar 5

Se recorre `_entryButtons` entero en cada refresco. En la pestaña HERRAMIENTAS sólo 5 están en
pantalla. No es un problema de rendimiento a esta escala — es una señal de que el refresco no
sabe que existen pestañas.

## H — MODERADO · Borrar no pide confirmación, contra la norma del proyecto

`BuildingsRuntimeEditor` confirma el borrado de UN edificio (`RequestDeleteWithConfirm`), y el
editor de Partículas pide DOS confirmaciones para vaciar una zona. La herramienta de Seleccion
borra **23 elementos de tres ficheros con una tecla y sin preguntar**.

Es deshacible, y la línea de estado dice «Ctrl+Z para deshacer» — pero es el único borrado del
proyecto que no pregunta, y es el que más se lleva por delante.

## I — MENOR · el recuento se lee mal

El panel dice `1 — 1 edificios`: el total y el desglose separados por una raya, y un plural
que no concuerda con el uno. Con un solo elemento la línea repite el mismo número dos veces.

## J — MENOR · contraste de los chips de dominio

El texto del chip se pinta con el TINTE DEL DOMINIO sobre `ACCENT_BG` (oro al 15 %). El azul de
Edificios sobre oro es el par de peor contraste de los tres. El tinte cumple su función en los
contornos del mundo, donde el fondo es el suelo; sobre el chip compite con el oro del acento.

## K — El hueco más grande: cero tests

**0 de los 13 ficheros nuevos tiene un solo test.** No hay nada que fije el contrato de
`ISelectionDomain`, ni la regla de que la caja más pequeña gana el clic, ni que un borrado se
deshaga en los tres dominios, ni que la tira de pestañas mida lo que dice medir.

Los dos defectos severos de arriba son exactamente lo que un test habría atrapado — el A no,
porque uGUI no hace layout en EditMode; el B sí, con cuatro líneas.

## No son hallazgos (comprobados y correctos)

- **Idioma.** Español sin acentos (`"Seleccion"`, `"anade"`), igual que Muerte, Economía,
  Skills y Controls, que suman 245 cadenas en español y **cero caracteres acentuados**.
- **Las cinco etiquetas de HERRAMIENTAS caben.** Medido: la más ancha es «Combat Ranges» con
  81 px sobre 85 disponibles.
- **El foco de teclado sigue a la pestaña.** Medido en las tres: `Act_Seleccion`,
  `Act_Pause Menu`, `Act_Tile`, todas visibles al recibirlo.
- **Combat Ranges y Debug HUD** hacen el viaje de ida y vuelta correctamente.
- **El panel de Seleccion se dibuja bien** y su línea de estado, aunque desborda su rect, se
  renderiza en dos líneas legibles dentro del panel.
- **Los contornos de selección se dibujan** en el mundo, en el color de su dominio.

## Adyacente, preexistente, fuera de esta pestaña

`Act_Dungeon NodeGraph` en EDITORES necesita **107 px de etiqueta en una celda de 85** con
`wrap=True` y `autoSize=False`, en una celda de 26 px de alto. Se parte en dos líneas dentro de
una celda que sólo tiene sitio para una.

## Arreglado el mismo día

| # | Hallazgo | Arreglo | Verificado |
|---|---|---|---|
| A | tira de pestañas a 180 px | `flexibleHeight = 0` en el `LayoutElement` de `Tabs` | medido en Play: **24 px**, botones 85x24 |
| B | Save Log de una sola dirección | `IsShowing` (raíz visible) en vez de `Instance != null`, en el toggle **y** en el botón | 5 toggles seguidos alternan; el botón reporta OFF con el panel cerrado |
| C | Map Backups no reclamaba Escape | `EscapeOwnership.Claim` en `Show`, `Release` en `Hide` **y en `OnDestroy`** | un solo lector de la pulsación |
| D | cabecera duplicando la pestaña | eliminada; la tira ya nombra la sección | `NoSection_RepeatsItsOwnTabLabelAsAHeader` |
| E | 180 px de vacío | el panel se dimensiona a la pestaña ABIERTA | medido: 280x299 → **280x127** en HERRAMIENTAS |
| F | pestaña no recordada | `CaptureWorkspace` guarda `tab`; `RestoreWorkspace` la parsea (`TryParse`, no cast) | — |
| G | 29 botones repintados para mostrar 5 | `RefreshActiveStates` salta las pestañas cerradas | — |
| H | Borrar sin confirmar | armado en dos pulsaciones, con recuento capturado; se desarma al cambiar la selección, al cerrar el editor y a los 4 s | vivo: «Confirmar borrado de 24», desarmar restaura la etiqueta |
| I | «1 — 1 edificios» | la línea es sólo el desglose, y el singular lo **declara cada dominio** | vivo |
| J | contraste de los chips | etiqueta en texto legible + franja del color del dominio a la izquierda | — |
| — | `UndoRow` con `flex=1.00` | mismo `flexibleHeight = 0`; era la misma trampa, latente | `EveryRow_ThatCarriesBothComponents_PinsItsFlexibleHeight` |
| — | altura del panel de Seleccion escrita a mano (330) | derivada de lo que el panel contiene | — |
| K | cero tests | **19 nuevos** en `MultiSelectSetTests` y `ToolsTabLayoutTests` | 8211/8211 en verde |

### Un defecto que sólo apareció al medirlo en vivo

Al arreglar el plural escribí la regla obvia — quitar la «s» final — y la línea salió
**«1 luce»**. Es correcta para *edificios* y *particulas* y falsa para *luces*, cuyo singular
es *luz*. Una regla acertada en dos de tres etiquetas es peor que tres declaraciones, porque
la equivocada sólo aparece cuando la selección tiene exactamente una luz. `ISelectionDomain`
declara ahora `LabelSingular`, y un test recorre todas las implementaciones exigiendo que la
declaren y que no coincida con el plural.

## Abierto

- **`Act_Dungeon NodeGraph` sigue desbordando** su celda (107 px de etiqueta en 85, `wrap`
  activo, celda de 26 px). Es de la pestaña EDITORES y preexistente; se arregla acortando la
  etiqueta o activando `enableAutoSizing`, que es lo que la nota de las teclas del teclado
  dibujado recomienda frente a truncar.
- **Sin overlay tutorial ni ayuda al pasar el ratón** en la herramienta de Seleccion, donde
  ocho de los otros editores sí la tienen. Hoy los gestos se explican sólo en la línea de
  estado al abrir.
- **Sin test del armado de borrado.** Se verificó en vivo; un fixture EditMode necesita montar
  el panel a mano y no se hizo.
- El compuesto con nombre (fase 2 de
  [`GENERAL_TOOLS_COMPOSITES_ROADMAP.md`](GENERAL_TOOLS_COMPOSITES_ROADMAP.md)) sigue sin
  empezar: sin él, un farol duplicado siguen siendo tres registros sueltos.

---

# Segunda pasada — el modelo de gestos de la herramienta de Seleccion

> Medida el 2026-09-09, a raíz de un informe del autor: «si comienzo el arrastre desde el area
> de un building, este selecciona al building, interrumpiendo el arrastre».
>
> El informe es exacto y el defecto es peor de lo que parece desde fuera.

## L — SEVERO · el gesto se decidía AL PULSAR

`OnSelectPressed` resolvía la intención en el instante en que bajaba el botón:

```text
pulsar sobre algo no seleccionado  ->  vaciar la seleccion, seleccionar ESO, y empezar a moverlo
pulsar sobre suelo vacio           ->  vaciar la seleccion y empezar un marco
```

Un marco sólo podía **empezar** sobre suelo vacío. Cuánto pesa eso se puede medir: se muestrea
una rejilla de 30x30 puntos y se pregunta en cada uno si una pulsación ahí sería tragada.

| Área alrededor del grupo denso | Esquinas de inicio que **podían** abrir un marco |
|---|---|
| 8 x 8 unidades | **1.6 %** |
| 16 x 16 | 4.2 % |
| 24 x 24 | 6.4 % |

Sobre la vista completa de la cámara el bloqueo era del 27.3 %, pero ese número engaña: el
marco sirve para encuadrar **grupos**, y un grupo es por definición donde las cosas se
amontonan. En el sitio donde la herramienta existe, funcionaba en una esquina de cada sesenta.

### El arreglo: la pulsación ARMA, no ejecuta

La intención la decide lo que el autor hace después:

```text
pulsar sobre algo YA SELECCIONADO   ->  arma un movimiento
pulsar sobre cualquier otra cosa    ->  arma un MARCO
soltar sin haberse movido           ->  era un clic
```

Sólo lo ya seleccionado reclama el arrastre, que es justo el caso en el que el autor ya dijo
qué objetos quiere. Todo lo demás deja el marco disponible.

La regla vive en una función pura, `ResolvePress(hitSomething, hitIsSelected, ctrlHeld)`,
extraída para poder fijarla sin escena: todo lo demás de una pulsación necesita editores
vivos, contenido vivo y un frame renderizado, mientras que la REGLA son tres booleanos.

**Medido después, con el mismo método: 100 % en las tres áreas.**

## M — SEVERO · cerrar el editor a mitad de arrastre movía cosas en silencio

`CancelDrag` limpiaba los flags y no devolvía nada a su sitio. Medido:

```text
antes        = (166.28, 57.59)
a mitad      = (171.28, 62.59)
tras cerrar  = (171.28, 62.59)   <-- se queda ahí
entradas de deshacer registradas = 0
```

El objeto quedaba movido, sin registro, y la siguiente escritura de cualquier tipo habría
guardado esa posición. **Escape cierra el editor**, así que el gesto que perdía datos era
precisamente el que un autor usa para abandonar un arrastre que no quería empezar.

`AbortGesture` devuelve ahora cada miembro a su origen capturado. Verificado: vuelve a
(166.28, 57.59) con 0 entradas de deshacer, que es lo correcto — no pasó nada.

## N — MODERADO · la seleccion se vaciaba al PULSAR

Segunda mitad del mismo error. Un marco que no atrapaba nada, o un gesto abandonado
desplazando la cámara, ya había destruido el grupo antes de que el autor viera resultado
alguno. El vaciado ocurre ahora al confirmar el clic o el marco, no al empezarlos.

## O — MODERADO · no existía el marco aditivo

Ctrl sobre un objeto alternaba y salía sin armar nada, así que no había forma de encuadrar
**añadiendo** a lo ya seleccionado. Y las dos mitades se contradecían: el vaciado pasaba al
pulsar y `CommitMarquee` sólo llamaba a `Add`, nunca vaciaba. Ahora Ctrl significa lo mismo en
las dos: en un clic añade o quita uno, en un marco añade lo atrapado. Se lee **al soltar**, no
al pulsar — el autor decide si extiende el grupo mirando lo que ha atrapado, no antes de
dibujarlo.

## P — MENOR · el marco parpadeaba en cada clic

`BeginMarquee` mostraba la caja y la actualizaba en el mismo frame de la pulsación, así que
todo clic dibujaba un rectángulo de tamaño cero bajo el cursor. La caja aparece ahora sólo
cuando el gesto YA es un arrastre.

## Verificado en vivo, gesto a gesto

| Gesto | Resultado |
|---|---|
| arrastre empezado **encima de un edificio** | 18 elementos atrapados (9 edificios, 9 particulas) |
| clic sobre un edificio | 1 seleccionado |
| arrastre desde un edificio **ya seleccionado** | movido (3.00, 2.00); Ctrl+Z lo devuelve |
| clic sobre suelo vacío | vacía la selección |
| cerrar el editor a mitad de arrastre | posición restaurada, 0 entradas de deshacer |

**8218/8218 en EditMode, consola limpia.** 7 tests nuevos en
`SelectionGesturePriorityTests`, incluidos dos que recorren las ocho combinaciones de
(hay algo / está seleccionado / Ctrl) y exigen que sólo una de ellas reclame el arrastre.

## Sigue abierto de esta pasada

- **No hay forma de cancelar un arrastre sin cerrar el editor.** Escape lo aborta porque cierra
  el editor entero, que es un efecto secundario más que un gesto. Un Escape que sólo cancele el
  gesto necesita que la herramienta reclame `EscapeOwnership` mientras arrastra, y eso compite
  con la única salida del editor.
- **El marco atrapa por solapamiento, no por contención**, sin nada en pantalla que lo diga. Es
  la convención habitual, pero un autor que espere contención se llevará vecinos.
- `ParticleSelectionDomain.Collect` hace `FindObjectsOfType(includeInactive: true)` en cada
  `HitTest`, es decir en cada pulsación: 188 objetos y un array nuevo. Irrelevante hoy, y es el
  tipo de coste que deja de serlo con diez veces el contenido.

---

# Tercera pasada — portapapeles, sombra de pegado, y navegar lo superpuesto

> Escrita el 2026-09-09 a partir de tres peticiones del autor: Ctrl+C / Ctrl+V sobre la
> seleccion, una sombra bajo el raton que muestre donde caera el pegado, y una forma
> profesional de elegir un elemento concreto entre varios superpuestos.

## Q — Ctrl+C / Ctrl+V como herramientas PROPIAS del editor

El patron ya existia y se siguio, en vez de inventar uno: los editores de Tile y de Buildings
declaran cada uno su `Copy` y `Paste` en su propio mapa con `requiresCtrl`, y NO como verbos
compartidos. La razon esta escrita en el catalogo: «copiar» no significa nada en catorce de
los diecisiete editores, y un verbo compartido es uno que todos tienen que responder.

Se anadio el mapa `Editor.Selection` al asset con dos acciones, y sus dos descriptores al
catalogo CERRADO — que es obligatorio, porque una accion en el asset sin descriptor es un test
rojo. Verificado en vivo:

```text
Editor.Selection: Copy  -> 1 binding, '<Keyboard>/c', 1 control
                  Paste -> 1 binding, '<Keyboard>/v', 1 control
```

**El owner es `"Seleccion"` — el `EditorName` EXACTO.** Es la cadena que
`InputContexts.Current` mete en el id de contexto y que `InputContextPolicy.IsLive` compara;
un desajuste es silencioso y mata todas las herramientas del editor, como ya paso una vez con
las 35 de golpe. El nombre del MAPA es un slug distinto a proposito, asi que compararlos entre
si no probaria nada.

Tres editores enlazan ahora Ctrl+C en la misma tecla y eso **no es un conflicto**: cada uno
esta vivo solo dentro de su contexto, que es la propiedad que le da un teclado entero a cada
editor.

## R — La sombra ES la posicion de pegado, no una decoracion

La sombra y el pegado leen **la misma aritmetica**: cada pieza en su offset capturado respecto
al ancla del grupo, y el ancla sobre el cursor. Medido:

```text
ancla de la sombra   (172.29, 52.78)
pegados              16
cayeron exactamente donde la sombra los dibujo   16 / 16
peor error                                       0.0000 unidades
```

Eso es lo que permite que este pegado no lleve correccion propia. El portapapeles de Buildings
sube su pegado medio alto del edificio ancla para que el SPRITE quede centrado en el cursor:
una suposicion sensata cuando no hay nada en pantalla contra lo que contrastarla, y una
discrepancia en cuanto lo hay.

Un edificio aporta **sus sprites reales**, capturados de los renderers vivos al copiar.
Reconstruirlos desde la plantilla volveria a rebanar la pagina del atlas, que es la trampa de
20 ms por llamada que este proyecto midio como el 60 % de su arranque entero. Una luz o un
emisor no tienen silueta, asi que aportan una caja translucida del color de su dominio.
Medido en un grupo de 16: **24 piezas** — 8 edificios por sus dos mitades, mas 7 cajas de
particulas y 1 de luz.

**El portapapeles guarda instantaneas, nunca los objetos fuente.** Es la regla que el
portapapeles de Buildings ya recoge, y aqui pesa mas porque un portapapeles sobrevive de
verdad a su origen: copiar un farol, borrarlo, pegarlo de vuelta. `CaptureForClipboard` es
deliberadamente distinto de las dos operaciones vecinas — el token de `Delete` solo tiene que
restaurarse UNA vez y en su sitio (el de un edificio es el propio objeto desactivado, que no
se puede pegar dos veces ni en otro sitio), y `Duplicate` copia de un objeto VIVO.

## S — Navegar lo superpuesto: dos mecanismos, ninguno redundante

**Clic repetido en el mismo punto baja al siguiente**, y da la vuelta. Es el gesto que usa la
propia vista de escena de Unity y no necesita modificador — lo cual importa, porque un
modificador hay que contarselo a alguien mientras que «no ha cogido el que queria, clico otra
vez» es algo que se prueba solo. Medido sobre la pila mas profunda en pantalla:

```text
3 superpuestos en (166.97, 59.10)
  clic 1 -> PE_torch_flame
  clic 2 -> Building_305
  clic 3 -> PE_forge_glow
  clic 4 -> PE_torch_flame     (vuelve al primero)
distintos alcanzados: 3 de 3
```

Y la linea de estado lo dice — `torch_flame — 1 de 3 aqui. Clic otra vez para el siguiente` —
porque el ciclo solo es descubrible si el primer clic admite que habia alternativas.

**La lista de seleccionados** es el mismo trabajo hecho despacio y con todo a la vista. El
ciclo gana cuando la pila tiene dos; la lista gana cuando tiene nueve. Una fila lleva la franja
de color de su dominio y el identificador de ESA colocacion (`Edificio 100 (#404)`,
`flowers_pollen_pink_soft`, `Luz Torch`), porque el trabajo de la lista es distinguir dos cosas
del mismo tipo apiladas en el mismo sitio. Un clic en una fila **aisla**: deja exactamente esa
seleccionada, que es la respuesta a «quiero esa, sola, para moverla». Verificado: 14
seleccionados, clic en la fila 2, queda 1 y es la que decia la fila.

El ancla va marcada, y esta al final de la lista porque el primary es el ultimo elegido.

## T — Un defecto que la captura revelo: la geometria restaurada aplastaba el panel

Al fotografiar el resultado, cada fila del panel medía **exactamente la mitad**: cabeceras a 9
en vez de 18, botones a 13 en vez de 26.

La causa no era el calculo de altura. El panel se restauraba a **330 px** desde el documento de
workspace — la altura que tenia antes de existir la lista y la fila de portapapeles — mientras
su contenido pedia 496. uGUI absorbio la diferencia **encogiendo cada fila proporcionalmente**
en vez de recortar, que es por lo que nada parecia lo bastante roto como para reportarlo.

Es la deriva de geometria persistida que el editor de Controls ya documenta. La altura de este
panel no es una preferencia — se deriva de lo que el panel contiene — asi que se re-deriva
tras restaurar. La posicion sigue siendo del autor. Medido despues: **510 px** y cada fila en
su altura declarada.

De paso, `STATUS_H` estaba en 26 y la linea de estado necesita **38** para sus tres lineas.

## U — Doble clic abre el editor de ese elemento, y ESC vuelve

El ciclo y la lista responden «cual de estos quiero». La pregunta que quedaba detras es «y
ahora que», porque la herramienta mueve, duplica y borra un grupo y no sabe nada de lo que
CADA cosa es: el `splitRatio` de un edificio, el preset de un emisor, el radio de una luz.
Hasta aqui eso costaba cerrar la herramienta, abrir el otro editor desde ESC y volver a buscar
en el mundo el objeto que ya se tenia seleccionado.

**Doble clic sobre algo YA seleccionado abre su editor, centrado en ese elemento.** Tres cosas
lo sostienen y ninguna es evidente.

**Solo cuenta lo YA seleccionado, no lo que haya bajo el puntero.** El primer clic del doble ya
selecciono algo y le dibujo su contorno, asi que el autor VE lo que va a abrir el segundo. La
regla alternativa — abrir lo que este encima — abriria la casa dentro de la cual esta la
farola, que es exactamente la pila de la que el ciclo existe para salir. El orden sigue siendo
el de `HitTestAll`, mas pequeño primero: medido en el mundo real, con la casa Y la luz
seleccionadas y el puntero sobre la luz, lo elegido es `light`.

**El doble clic se consulta ANTES del `HitTest`, y por eso no avanza el ciclo.** Son los mismos
dos clics: rapido significa «abre este», lento significa «dame el siguiente de la pila», y lo
unico que los separa es el intervalo. Consultarlo despues abriria el editor del elemento al que
el ciclo acababa de pasar, es decir, nunca el que el autor estaba mirando.

**La apertura se difiere UN FRAME.** Unity no ordena los `Update` de dos editores, asi que un
editor activado a mitad de frame puede recibir su propio `Update` despues — con el boton
izquierdo reportado como pulsado ESE frame, que en el modo de colocacion del editor de
edificios es una casa que nadie pidio. Un frame es invisible y elimina la carrera entera.

La vuelta es `GameEditorManager._returnTo`: un puntero de un solo uso que el lanzador consulta
antes de abrirse. **Se arma DESPUES del cambio**, y ese orden es todo el motivo de que
funcione: abrir destruye el editor anterior, cuyo `Deactivate` llama a `NotifyDeactivated`, que
borra el rastro — correctamente, porque un editor que se cierra a si mismo termina uno.
Armarlo antes es armar un puntero que la linea siguiente borra, y la funcion no se dispararia
jamas mientras el resto de la suite sigue en verde. Es la misma forma que el escudo teniendo
que hacer `Track` antes de reclamar la invulnerabilidad.

Y se consume UNA vez: el segundo ESC se comporta como siempre. Un ESC que volviese
indefinidamente seria un rastro que sobrevive a minutos de trabajo no relacionado.

Verificado en vivo, mundo cargado, los tres dominios:

```text
edificio   armado=True abre=Buildings Editor  foco en el objeto=True  vuelta=Seleccion
particula  armado=True abre=Particles Editor  foco en el objeto=True  vuelta=Seleccion
luz        armado=True abre=Lighting Editor   foco en el objeto=True  vuelta=Seleccion

ESC        activo=Seleccion  seleccion intacta=1  rastro=null  segundo ESC=False
pila       casa + luz seleccionadas, puntero en la luz  ->  elegido: light
nada sel.  mismo punto, seleccion vacia            ->  no arma nada
```

El foco va SIEMPRE despues de `Activate`: la activacion siembra el modo y el inspector del
editor destino, asi que una seleccion escrita antes queda sobrescrita y el autor aterriza en un
panel apuntando a nada.

## Estado

**8241 / 8241 en EditMode, consola limpia.** 13 tests entre
`SelectionGesturePriorityTests` y `SelectionClipboardContractTests` — incluidos los que fijan
que el owner sea el `EditorName` exacto y que `CaptureForClipboard` no se colapse sobre
`Delete` ni sobre `Duplicate` — mas 14 en `SelectionOpenEditorTests`.

Ese ultimo fichero pina lo que es PURO y lo que se rompe en SILENCIO, que no es el gesto: el
rastro de vuelta entero (armar, consumir una vez, borrarse al abrir por cualquier otra via, al
cerrar a gameplay, al cerrarse el editor solo, al desregistrarse el destino, y el rechazo de
volver al editor recien abierto), el caso del `Deactivate` que se auto-notifica — que es la
trampa de orden y la unica razon de que el falso editor exista —, un seam de foco por editor
por reflexion (son privados-por-partial, no hay otra forma de ver que desaparece uno) y un
escaneo de la fuente del lanzador exigiendo que consulte el rastro ANTES de abrirse: si abre
primero, el rastro es codigo muerto.

## Sigue abierto

- **La sombra no rota ni se refleja**, igual que el compuesto no lo hace: el arte de edificios
  es direccional y un espejo honesto necesitaria arte espejado por plantilla.
- ~~**No hay forma de vaciar el portapapeles desde el panel.**~~ **ARREGLADO.** `ClearClipboard`
  existia y ningun boton lo llamaba — el helper-sin-llamador que este proyecto documenta una
  docena de veces. El boton `Vaciar` vive AHORA en la propia linea que describe el
  portapapeles, no junto a Copiar y Pegar, porque es el control de ESE estado: solo significa
  algo mientras hay algo copiado, y **se oculta cuando no lo hay** (un boton que no hace nada
  es un control informando de un estado que no tiene). Verificado en vivo:

  ```text
  en reposo        "portapapeles vacio"        boton oculto   sin sombra
  tras Ctrl+C      "portapapeles: 10 elementos" boton visible  13 piezas, visible
  tras Vaciar      "portapapeles vacio"        boton oculto   0 piezas, oculta
  ```

  Y un test fija exactamente el defecto que tenia: un escaneo de la fuente del panel exigiendo
  que algo llame a `ClearClipboard`, porque el fallo no era la funcion sino que nadie la
  invocaba.
- ~~**No hay forma de abrir el editor de un elemento seleccionado.**~~ **ARREGLADO** — ver U.
- **El doble clic solo abre, nunca crea.** Sobre suelo vacio no hace nada, que es lo correcto
  aqui y distinto de lo que hace en otros editores.
- **La lista se reconstruye entera** cuando cambia el recuento o el ancla. Esta capada a 50
  filas por eso; por encima de esa cifra el recuento es el unico readout honesto.
