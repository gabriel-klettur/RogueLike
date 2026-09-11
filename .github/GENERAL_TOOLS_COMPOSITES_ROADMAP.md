# General Tools — selección multi-dominio y compuestos de mundo

> Diseño escrito el 2026-09-09, antes de escribir una línea de código.
> Medido sobre `Scripts/Gameplay/Editors/`, los tres loaders de mundo y los
> tres ficheros de instancias en `StreamingAssets/`.
>
> **Qué se construye:** una pestaña `HERRAMIENTAS` en el panel de ESC, y
> dentro de ella la primera herramienta — un editor de **selección
> multi-dominio** capaz de seleccionar edificios, emisores de partículas y
> luces a la vez, moverlos, borrarlos, duplicarlos y **guardarlos como un
> compuesto con nombre** que se coloca después como una sola pieza.

## 1. El problema, dicho con precisión

Un farol de calle en Valkur no es un objeto. Son **tres**: un
`BuildingObject` con el poste, una luz autorada en `light_instances.json`, y
un emisor de polvo en `particles_instances.json`. Cada uno se coloca en un
editor distinto, y los editores son **mutuamente exclusivos**
(`GameEditorManager.OpenExclusive` cierra el anterior). Para duplicar ese
farol treinta metros más allá hay que abrir tres editores, colocar tres
cosas y alinearlas a ojo. No hay ningún gesto en el proyecto que exprese
"esto es una cosa".

Esa es la carencia. No es un lanzador de acciones que falte: es que el
mundo no tiene noción de **compuesto**.

## 2. Inventario medido — qué existe ya

### 2.1 El precedente que ya resuelve un tercio del problema

`BuildingsRuntimeEditor.Clipboard.cs` **ya hace copiar/pegar de grupo**, y
lo hace bien. Sus cuatro decisiones son directamente reutilizables y este
documento las adopta sin cambios:

| Decisión | Por qué | Dónde se reutiliza |
|---|---|---|
| El portapapeles guarda **instantáneas por valor**, nunca los objetos fuente | una referencia viva pega lo que el original se ha vuelto, o lanza cuando se borra | `CompositeMember` |
| El grupo se guarda como **offsets desde un ancla**, no posiciones absolutas | posiciones absolutas hacen que el pegado caiga encima de los originales | §4.3 |
| Se copia **exactamente el conjunto de campos que el serializador escribe** | copiar un subconjunto produce un duplicado que se ve igual y se comporta distinto | §4.2 |
| El test lee **la fuente del propio serializador** para las claves de override | un campo nuevo no puede olvidarse en silencio | §8 |

### 2.2 Estado por dominio

| Dominio | Multi-selección | Undo | Portapapeles | API utilizable desde fuera |
|---|---|---|---|---|
| Buildings | sí — `BuildingSelectionSet` | `ExecutePersistedEdit` | grupo completo | parcial (ver §5.3) |
| Particles | **no** — `_selectedInstanceId` | `ExecutePersistedEdit` | no | `ParticleInstanceSerializer` |
| Lighting | **no** — `_selectedLight` | `_undo.Record` | no | **completa** — ver abajo |

`WorldLightLoader` ya expone todo lo que un portapapeles necesita, sin tocar
`LightingRuntimeEditor`:

```text
CollectActiveLights(List<LightHandle>)   enumerar
FindNearestLight(worldPos, maxRadius)    picar
CaptureLight(go)   -> LightSnapshot      instantánea por valor
RestoreLight(snapshot) -> GameObject     reconstruir
RegisterRuntimeLight / MoveLight / RemoveLight
SaveAll(authoredRemovals, force)
```

`LightHandle.persistent` distingue las luces **autoradas** de las
**derivadas** (las que emite un edificio-farol por su
`BuildingTemplateData.lightPresetKey`). Esa distinción es obligatoria — ver
§6.1.

## 3. Los tres espacios de coordenadas — medido en los loaders

Los tres ficheros graban la misma forma de registro: `zone` + `rel_x` +
`rel_y`. La conversión real, leída del código y no supuesta:

| Dominio | Fuente | A mundo |
|---|---|---|
| Lights | `WorldLightLoader.cs:1280` | `x = off.x + rel_x/32` · `y = off.y + (zoneH−1) − rel_y/32` |
| Particles | `ParticleInstanceSerializer.cs:44` | `x = off.x·t + rel_x/32` · `y = off.y·t + (zoneH−1)·t − rel_y/32`, con `_tileSize = 1` |
| Buildings | `BuildingLoader.Spawning.cs:46` | `x = off.x + (rel_x + effW/2)/32` · `y = off.y + (zoneH−1) − (rel_y + effH)/32` |

**Es un único espacio**: píxeles relativos a la zona, 32 px por unidad de
mundo, Y medida hacia abajo desde la fila superior de la zona. Con
`_tileSize = 1`, partículas y luces son idénticas término a término.

Buildings difiere **sólo en el ancla**: su `rel` nombra la esquina
superior-izquierda del sprite mientras su `transform` está en la línea de
suelo, así que arrastra su propio tamaño efectivo como corrección. Esa
corrección **depende del edificio concreto**, incluido cualquier `scale`
override.

> **De aquí sale la decisión central del diseño y no es opinable:** un
> compuesto se guarda en **unidades de mundo, como offsets desde un ancla**,
> y cada dominio reconvierte por su propio serializador al colocar. Nunca en
> `rel`.
>
> Dos razones, y ninguna es teórica. Un compuesto puede **cruzar el borde de
> una zona** — tres miembros, tres orígenes distintos, y un offset en `rel`
> no significaría nada. Y la corrección de ancla de un edificio depende de
> su tamaño, así que un offset en `rel` entre un edificio y una luz mide dos
> cosas distintas.

`BuildingsRuntimeEditor.Clipboard` ya lo hace así
(`Offset = b.transform.position - anchorPos`) y ya recalcula
`ZoneName = DetectZoneAt(worldPos)` al pegar en lugar de heredarlo. El
compuesto hereda esa regla para los tres dominios.

## 4. Arquitectura

### 4.1 Dónde vive el catálogo de compuestos

**`StreamingAssets/Composites/composites.json`, escrito por
`ICompositeRepository`.** No un `ScriptableObject`.

Es la única desviación de la regla cardinal 4 de `CLAUDE.md` ("Edit
ScriptableObjects, not external JSON") y hay tres razones medidas:

1. **Un `ScriptableObject` no se puede escribir fuera del Editor.**
   `ParticlesRuntimeEditor.PresetPersistence.cs:118` es explícito:
   `WritePresetAssetToDisk` está dentro de `#if UNITY_EDITOR` y devuelve
   `false` en cualquier build, con un aviso de "los cambios duran sólo esta
   sesión". La herramienta está detrás de
   `RuntimeEditorPolicy.AuthoringEditorsAvailable`, que **incluye las
   Development Builds**. Un catálogo en `.asset` sería inautorable justo en
   la mitad de los contextos donde la herramienta está encendida.
2. **Un compuesto es una plantilla sobre COLOCACIONES, no sobre arte.** Un
   `BuildingTemplateData` existe porque existe un sprite y lo genera un
   importador desde un manifiesto. Un compuesto lo hace un diseñador de
   nivel, en el mundo, a partir de cosas que acaba de colocar. Eso es
   exactamente para lo que existe el patrón `IRepository` en este proyecto.
3. **Los registros ya tienen forma.** Un miembro de un compuesto es un
   registro con la misma forma que los tres ficheros de instancias ya
   definen, y se resuelve por los mismos `template_id` / `preset_id` contra
   los mismos catálogos. Un `ScriptableObject` sería un espejo de eso.

`StreamingAssets` cumple la propiedad que hace falta: **se distribuye con la
build y se puede autorar en el Editor**, igual que los mapas, las instancias
de edificios y los sets de FSM.

### 4.2 Qué es un compuesto

```json
{
  "version": 1,
  "composites": [{
    "id": "farol_calle",
    "name": "Farol de calle",
    "category": "iluminacion",
    "anchor": "bounds_bottom_center",
    "members": [
      { "domain": "building", "dx": 0.00, "dy": 0.0,
        "template_id": 64, "overrides": {} },
      { "domain": "light", "dx": 0.00, "dy": 2.4,
        "preset_id": "Torch", "overrides": { "color": [255, 200, 140] } },
      { "domain": "particle", "dx": 0.05, "dy": 2.3,
        "preset_id": "dust_motes_warm", "scale_multiplier": 1.0, "config": {} }
    ]
  }]
}
```

`dx` / `dy` en **unidades de mundo** desde el ancla. Cada `overrides` es
byte a byte lo que el serializador de ese dominio escribe hoy — se reusan
sus emisores y sus lectores, no se reimplementan.

**Regla que hereda de `Clipboard.cs`:** lo que se captura es *todo lo que
una colocación es*, y esa lista no es un juicio — es exactamente el conjunto
de campos que el serializador de ese dominio escribe en un registro.
Capturar un subconjunto produce una copia que se ve igual y se comporta
distinto, que es el fallo que nadie reporta porque ambas mitades parecen
correctas.

### 4.3 El ancla

**Centro-inferior del rectángulo unión de los miembros**, calculado al
guardar.

No "el último seleccionado", que es lo que usa `BuildingSelectionSet` y es
correcto ahí: para una selección viva el primary es el que el autor acaba de
tocar, pero para una **plantilla guardada** eso es arbitrario y no
reproducible — guardar el mismo farol dos veces daría dos anclas distintas
según el orden de los clics.

El centro-inferior es determinista, no necesita campo autorado que se pueda
equivocar, y coincide con el gesto de colocación que ya usa todo picker del
proyecto: el cursor marca dónde van los pies. Es la generalización de la
corrección que `PasteClipboardAtCursor` ya hace
(`worldPos.y -= SourceWorldHeight * 0.5f`).

### 4.4 El picking — adaptadores, no una interfaz en los tipos de dominio

El editor nuevo hace su **propio** picking. Tres adaptadores, todos dentro
de la carpeta de la herramienta:

```text
Editors/Composites/Adapters/BuildingPickAdapter.cs
Editors/Composites/Adapters/ParticlePickAdapter.cs
Editors/Composites/Adapters/LightPickAdapter.cs
```

Cada uno responde `Enumerate()`, `PickAt(worldPos)`, `RectOf(member)`,
`Capture(member)`, `Spawn(snapshot, worldPos)` y `Remove(member)`.

**Por qué adaptadores y no una interfaz `IWorldPickable` en `BuildingObject`
/ `ParticleEmitter` / la luz:** el rectángulo de picking es una decisión de
EDITOR, no una propiedad del objeto. El de una luz no es su radio de
iluminación — es un asa de agarre. Poner esa noción en el tipo de dominio le
mete una dependencia que no necesita para nada más.

Los tres rectángulos ya existen y son públicos o reconstruibles:
`BuildingObject.TryGetWorldRect`, `ParticleFootprint`,
`WorldLightLoader.FindNearestLight`.

**Regla de desempate, tomada de `HitTestEmitter`:** dentro de una huella
gana a cerca de una, y **la huella más pequeña que contiene el cursor gana**.
Sin eso, un edificio de cuatro unidades se traga cada luz y cada emisor que
el autor coloque dentro, que es precisamente el caso del farol.

### 4.5 Atomicidad — tres ficheros, una operación

Colocar o borrar un compuesto escribe `buildings_instances.json` +
`particles_instances.json` + `light_instances.json`. Si escribe dos y falla
el tercero queda un farol sin luz y **nada lo dice**.

El orden es el de `CraftingService.TryCraft`, por la misma razón:

1. `RefuseWorldContentWrite` para los **tres** por adelantado. Dentro de un
   interior el mundo base está desmontado a propósito y una colocación ahí
   persiste el vacío.
2. Construir los tres dominios **en memoria**. Nada se guarda todavía.
3. Si cualquier miembro falla al construirse, destruir lo ya construido y
   abortar con un toast. Cero escrituras.
4. Sólo entonces, una única `UndoStack.LambdaCommand` cuyo `Do` guarda los
   tres ficheros y cuyo `Undo` quita los tres y vuelve a guardar los tres.

Un `ExecutePersistedEdit` por dominio serían **tres entradas de undo** para
un gesto, y deshacer una vez dejaría dos tercios del farol en pie.

## 5. Los tres acoplamientos que este diseño no puede evitar

### 5.1 La exclusividad prohíbe reutilizar el picking existente

El picking de cada editor vive dentro de ese editor y sólo corre mientras
está activo, así que la herramienta no puede delegar en ellos: para cuando
está abierta, los tres están cerrados. De ahí los adaptadores de §4.4. Es el
coste estructural principal del diseño y no tiene atajo.

### 5.2 Ningún editor puede estar abierto a la vez que la herramienta

Consecuencia directa de lo mismo. Un autor que quiera ajustar el color de
una luz **dentro** de un compuesto tiene que cerrar la herramienta, abrir
Lighting, ajustar, y volver. Aceptable en fase 1; §9 lo recoge como trabajo
abierto.

### 5.3 La rejilla de colisión CU vive dentro del editor de Buildings

`_colliderInstanceStore` está declarado `private readonly` en
`BuildingsRuntimeEditor.Updates.cs:199` y **no se referencia desde ningún
otro fichero del proyecto**. Es el `Dictionary<int, ColliderGridData>` que
guarda la rejilla pintada a mano de cada edificio con
`collider_scope: "CU"`.

Un compuesto que capture un edificio CU sin su rejilla produce una copia con
colisión distinta del original, en silencio.

**Solución:** dos métodos `internal` en `BuildingsRuntimeEditor` —
`TryGetInstanceColliderGrid(int id, out ColliderGridData)` y
`SetInstanceColliderGrid(int id, ColliderGridData)` — que llamen a
`EnsureColliderDataLoaded()` primero, porque el store está vacío si el
editor de Buildings no se ha abierto esta sesión. El singleton existe aunque
el editor esté inactivo (se crea en el arranque y se registra en
`GameEditorManager`), así que la llamada es segura.

Dos métodos, no una refactorización. Es el único punto donde la herramienta
alcanza dentro de otro editor, y queda escrito aquí para que se vea.

## 6. Trampas específicas de este dominio

### 6.1 Un compuesto NUNCA debe capturar una luz derivada

`WorldLightLoader.RegisterDerivedLight` crea las luces que emiten los
edificios-farol por su `BuildingTemplateData.lightPresetKey`, y las marca
`persistent = false`: **no están en `light_instances.json` y un guardado
nunca las escribe**.

Si el compuesto captura una de ellas, al colocarlo el edificio emite su luz
derivada *más* la copia autorada. Dos luces en un farol, y sólo una en el
fichero — así que la duplicación sobrevive a la recarga y no hay forma de
borrar la otra desde el editor de Lighting, que ya excluye las derivadas de
su lista por esta misma razón.

El filtro es `LightHandle.persistent`. Es una línea, y sin ella el defecto
es invisible hasta que alguien mira el JSON.

### 6.2 Un emisor colocado es dueño de su configuración

`particles_instances.json` es schema v4 y cada registro **lleva su propio
`config`** — la copia del preset con la que se colocó. Es copy-on-place
deliberado: editar un preset alcanza la SIGUIENTE colocación y ninguna de
las existentes.

Un compuesto tiene que capturar ese `config`, no el `preset_id` a secas.
Guardar sólo el id significa que el compuesto cambia solo cuando alguien
retoca el preset — exactamente el acoplamiento que copy-on-place existe para
quitar, reintroducido por la puerta de atrás.

### 6.3 Un compuesto colocado es una copia, no un enlace

Misma regla, ahora para el compuesto entero, y por el mismo argumento que ya
ganaron `ParticleInstanceConfig` y `SpawnerInstanceConfig`: colocar toma una
copia y desde ahí es independiente. Editar el compuesto alcanza la siguiente
colocación y ninguna existente.

El acoplamiento sigue disponible **a propósito**, como en Particles:
"Reaplicar compuesto → a ésta / a todas". Copy-on-place hace imposible una
retoca global por accidente; quitarlo del todo la haría imposible a
propósito, que es un error distinto.

### 6.4 Espejar un compuesto no es gratis y no entra

Ni rotación ni espejo en fase 1. `DirectionalAnimator` no voltea nada por
diseño y el arte de edificios es direccional: un farol espejado dibujaría el
sprite al revés. Un espejo honesto necesitaría arte espejado por plantilla.
Fuera de alcance, dicho aquí para que no vuelva como "obvio".

### 6.5 `MiniJsonRuntime` y las entradas malformadas

`composites.json` es un fichero editable a mano más, y los dieciocho loaders
que pasan por `MiniJsonRuntime` ya tienen su guardia de progreso (`_failed`,
`AtEnd`). El repositorio nuevo hereda esa protección por usar el mismo
parser — no escribir uno propio.

## 7. Plan por fases

### Fase 0 — la pestaña (independiente, entrega valor sola)

`GeneralEditorSection` gana `Tools`. `GeneralEditorManager.UI` gana una tira
de pestañas: **EDITORES** / **HERRAMIENTAS** / **JUEGO**.

Se mueven a HERRAMIENTAS las cuatro entradas que ya están mal colocadas:
`Combat Ranges`, `Debug HUD` y `Save Log` (hoy en DIAGNOSTICS, que no son
editores sino toggles) y `Map Backups` (hoy en GAME, que es un navegador y
no una acción de sesión).

`ComputePanelHeight` pasa a ser el **máximo** entre pestañas en vez de la
suma de las tres secciones. `EditorReachabilityTests` no cambia: recorre
`BuildEntries()`, que sigue siendo una lista plana con una enum de sección.

### Fase 1 — selección multi-dominio viva

Editor nuevo `CompositesRuntimeEditor` (`EditorName = "Composites"`),
registrado en `GameEditorManager`, con entrada en la pestaña HERRAMIENTAS,
detrás de `RuntimeEditorPolicy.AuthoringEditorsAvailable`.

- Los tres adaptadores de §4.4.
- `CompositeSelectionSet`, modelado sobre `BuildingSelectionSet` — set
  ordenado, primary el último, `Prune()` porque pertenecer no es ser
  elegible.
- Click para seleccionar, Ctrl+click para alternar, **arrastre de marco**
  para capturar todo lo que caiga dentro en los tres dominios.
- Mover el grupo, borrar el grupo, duplicar en sitio. Una unidad de undo
  cada uno, por §4.5.
- Los contornos ya existen: `LightOutlineRenderer`,
  `ParticleEmitterOutlineRenderer`, y el outline de Buildings.

### Fase 2 — el compuesto con nombre

- `composites.json` + `ICompositeRepository` + `JsonCompositeRepository`.
- "Guardar selección como compuesto" con nombre y categoría.
- Panel picker de compuestos, arrastrar-y-soltar al mundo, igual que el
  picker de Buildings.
- "Reaplicar compuesto → a ésta / a todas".

### Fase 3 — cohesión de la colocación (separable, y es la más cara)

Hoy, colocar un compuesto **se expande**: escribe un edificio, una luz y un
emisor, y la identidad de grupo desaparece. Volver a seleccionarlo es volver
a arrastrar un marco, que en una calle densa atrapa a los vecinos.

Darle cohesión cuesta un campo de esquema `group_id` en los **tres**
ficheros, más el campo en los tres componentes vivos, más los tres
serializadores. Los tres lectores lo toleran sin cambios (leen por
diccionario o `JsonUtility`, ambos ignoran claves desconocidas), pero los
tres **escritores** reconstruyen el registro desde el objeto vivo, así que el
id tiene que viajar en el componente o se pierde en el siguiente guardado.

Es el punto más caro del diseño entero y es genuinamente separable. Se decide
después de usar la fase 2, no antes.

## 8. Tests

Siguiendo la forma que `SPAWNER_COORDINATE_SPACE_DRIFT` dejó escrita —
**afirmar sobre la composición, no sobre una mitad**:

| Test | Qué fija |
|---|---|
| `CompositeCoordinateRoundTripTests` | capturar y colocar en otro sitio reproduce el layout relativo **en los tres dominios**, incluido un compuesto que cruza el borde de una zona |
| `CompositeFieldCoverageTests` | lee la fuente de los tres serializadores y falla si un campo que escriben no se captura — el patrón exacto de `BuildingsClipboardTests` |
| `CompositeAtomicityTests` | un miembro que falla al construirse no deja nada escrito; un undo quita los tres dominios |
| `CompositeDerivedLightTests` | una luz `persistent = false` nunca entra en un compuesto (§6.1) |
| `CompositeAnchorTests` | el ancla es el centro-inferior de la unión y no depende del orden de selección |
| `GeneralEditorTabTests` | cada entrada del registro cae en exactamente una pestaña; la altura del panel es el máximo, no la suma |
| `ShippedCompositeDataTests` | cada `template_id` / `preset_id` de `composites.json` resuelve contra su catálogo real — son cadenas y enteros que nadie valida al autorar, y fallan igual: una pieza que no aparece |

## 9. Lo que queda fuera, y por qué

- **Rotación y espejo** — §6.4.
- **Compuestos anidados** (un compuesto que contiene otro) — el mismo
  argumento que los interiores anidados: nadie lo ha pedido y multiplica el
  espacio de estados de la resolución.
- **Tiles dentro de un compuesto** — el editor de Tile pinta un tilemap por
  celda, no coloca instancias. Un "compuesto con suelo" es un problema
  distinto y más grande.
- **Spawners y entidades dentro de un compuesto** — encajan
  estructuralmente (`spawner_instances.json` tiene la misma forma), pero cada
  dominio añadido multiplica el trabajo de §4.5 y §8. Se añaden después de
  que los tres primeros funcionen, y el diseño de los adaptadores está hecho
  para que sea un fichero nuevo y nada más.
- **Editar una luz sin cerrar la herramienta** — §5.2.
