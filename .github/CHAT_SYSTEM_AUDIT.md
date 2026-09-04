# Auditoría del sistema de chat con NPCs

> Fecha: 2026-09-03 · Ámbito: `Scripts/Gameplay/Chat/` (10 ficheros, 1998 LOC),
> `Scripts/Data/Quests/{NPCPersonaDefinition,ChatAssignmentCatalog}.cs`,
> `Scripts/Gameplay/Enemies/NPCInteractable.cs`, `Scripts/Gameplay/Vendors/`,
> `Tests/EditMode/Game/Chat/` (10 ficheros, 5118 LOC, 225 tests).
> Suite ejecutada durante la auditoría: **225/225 en verde, 4.6 s**.

## Veredicto en una línea

El código es de los mejor escritos del repositorio y el sistema **no existe dentro del
juego**: ninguna entidad viva es "chateable", el catálogo de personas está vacío, y el
campo que lo conectaría todo nunca se asigna. Es un subsistema completo, documentado y
probado, colgado del vacío.

## Notas por aspecto

| # | Aspecto | Nota | Resumen |
|---|---|---:|---|
| 1 | Alcance real en juego | **0.0** | `TryOpenChat` no puede devolver `true` jamás |
| 2 | Datos autorizados (personas / asignaciones) | **0.0** | Cero assets `NPCPersonaDefinition`; catálogo con `assignments: []` |
| 3 | Cableado / bootstrap | **1.5** | `_catalog` nunca se asigna; `EntityRegistry.RegisterNPC` sin llamadores |
| 4 | Continuidad conversacional (diseño) | **1.5** | La memoria se escribe y nunca se lee de vuelta |
| 5 | Localización | **1.0** | Cadenas en español a fuego + botón ES/EN inerte |
| 6 | UI del panel | **4.0** | Funcional, sin arrastre/redimensión, 9 colores crudos, fuera del kit |
| 7 | Burbujas de mundo | **5.5** | Correctas; offset fijo, sin pooling, sin ajuste a píxel |
| 8 | Provider / extensibilidad LLM | **6.0** | Interfaz limpia; el único provider ignora persona y memoria |
| 9 | Persistencia (memoria + logs) | **6.5** | Bien pensada; temporal compartido y `File.Replace` (trampa conocida) |
| 10 | Integración de input | **6.0** | `InputBlocker` correcto; Enter no respeta los 16 editores |
| 11 | Rendimiento | **7.0** | Sin coste medible; `TryOpenChat` asigna por llamada, irrelevante hoy |
| 12 | Robustez / errores | **7.5** | Null-safety y cancelación ejemplares |
| 13 | Cobertura de tests | **7.0** | 2.5:1 en volumen; **cero** asertos sobre datos embarcados |
| 14 | Arquitectura y capas | **8.0** | Separación limpia, sin ciclos, partials por aspecto |
| 15 | Calidad de código y documentación | **8.5** | Comentarios que explican el *porqué*, no el *qué* |

**Sistema como código: 7.2/10. Sistema como característica jugable: 0.5/10.**

**Global ponderado: 3.4/10.**

## Hallazgo crítico: la cadena rota

Cinco eslabones, cada uno verificado por separado. Basta uno para que el chat no
funcione; están rotos los cinco.

1. **`EntityRegistry.RegisterNPC` no tiene ni un llamador en producción.**
   `EntityRegistry.NPCs` está siempre vacío, así que el primer bucle de
   `ChatSystem.TryOpenChat` (`ChatSystem.cs:105`) no itera nada.
2. **Nada añade `NPCInteractable` a una entidad generada.** Grep por GUID
   (`06b05ece23e01604d9d47ba9439d0e33`) sobre `*.prefab`, `*.unity` y `*.asset`:
   **cero referencias**. Solo lo añaden los tests. El segundo bucle, sobre
   `EntityRegistry.Monsters`, descarta por tanto a todos los monstruos.
3. **`ChatSystem._catalog` nunca se asigna.** `GameplaySceneSetup.EnsureChatSystem`
   (`GameplaySceneSetup.Systems.cs:255`) hace `AddComponent<ChatSystem>()` sobre un
   `GameObject` nuevo, y ninguna escena contiene un `ChatSystem` (GUID
   `2ae0f051f0fc3c5498d525d5921270a3`: cero referencias). El `[SerializeField]` queda
   nulo y `GetPersona` no se llama nunca.
4. **`ChatAssignmentCatalog.asset` está vacío** (`assignments: []`) y **no existe ni un
   solo asset `NPCPersonaDefinition`** en el proyecto. La ruta `Data/ChatPersonas/`
   que declara `CLAUDE.md` no existe en disco.
5. **`VendorNPC` tampoco se instancia nunca** (GUID `2bebce801a8eb1246b66702d82148f38`:
   cero referencias en prefabs y escenas). Era la única fuente de `NPCInteractable`
   vía `[RequireComponent]`. Tampoco hay assets `VendorConfigDefinition` ni carpeta
   `Data/Vendor/`.

Consecuencia observable: pulsar Enter siempre muestra la burbuja
`"No hay nadie cerca para hablar..."`. Y aunque se forzara `OpenChat(target)` desde
consola, `_activePersona` sería nulo, con lo que `GenerateReply` sale en su primera
línea (`ChatSystem.Messages.cs:74`) y el NPC **jamás responde**. El saludo tampoco se
muestra: está dentro del mismo `if (_activePersona != null)`.

## Hallazgos por severidad

### Altos

- **Los tests cubren el código, no el juego.** 225 tests en verde, y todos construyen
  su propio catálogo sintético, su propio `NPCInteractable` y su propio jugador.
  Ninguno abre `ChatAssignmentCatalog.asset`, ninguno comprueba que exista una persona
  embarcada, ninguno afirma que una entidad real sea chateable. Es exactamente la forma
  del incidente `SPAWNER_COORDINATE_SPACE_DRIFT`: *un test que ejercita solo una mitad
  no prueba nada; hay que afirmar sobre la composición y sobre los datos embarcados*.
  `GetDiscountLimit` es el caso extremo — **10 tests** fijan el comportamiento de un
  método con **cero llamadores en producción**.
- **La memoria se escribe y nunca se lee.** `NPCMemory.ephemeralHistory` guarda 12
  mensajes en disco, `OpenChat` los carga… y acto seguido hace `_history.Clear()`.
  `OfflineDialogueProvider.GenerateReplyAsync` recibe `memory` y la ignora por completo.
  De los cinco campos persistidos solo `hasGreeted` afecta a algo, y lo que hace es
  suprimir el saludo para siempre tras la primera visita. `friendshipScore` no se
  escribe nunca; `preferredLanguage` solo cambia la etiqueta de su propio botón.
- **Escritura "atómica" con nombre temporal compartido.** `NPCMemoryStore.Save` usa
  `path + ".tmp"` — un nombre fijo por fichero — y `File.Replace`. Ambas son las trampas
  que `CLAUDE.md` ya documenta para `WriteSerializedJsonAtomic`: dos escrituras
  solapadas abren el mismo handle (`Access to the path is denied`) y el `File.Replace`
  de Mono no es el `ReplaceFile` de Win32. Hoy no se solapa nadie porque solo hay un
  escritor y es síncrono; el día que un provider LLM guarde desde un `Task`, sí.

### Medios

- **Enter abre el chat desde cualquier contexto.** `ChatUI.Update` solo cede ante la
  DevConsole. `InputBlocker.IsAlwaysAllowedKey` deja pasar Enter siempre, y
  `GameEditorManager.AnyEditorActive` existe pero no se consulta: escribir en el buscador
  del editor de Items y pulsar Enter dispara `TryOpenChat`.
- **Escape no tiene protocolo de consumo.** 13 consumidores en producción lo sondean
  cada uno por su cuenta, `ChatSystem.Update` entre ellos. Cerrar el chat con Escape
  abre además el menú de pausa en el mismo frame. Es un defecto anterior al chat, pero
  el chat lo hereda.
- **El panel no es lo que dice ser.** `PANEL_MIN_W` / `PANEL_MIN_H` están declarados,
  documentados como "constantes de Python preservadas"… y no se usan en ninguna parte.
  La ventana redimensionable y arrastrable del original nunca se portó: el panel está
  clavado a `(20, 20)` con tamaño fijo.
- **UI fuera del sistema de diseño.** 9 literales `new Color(` crudos en
  `ChatUI.Builder.cs` más 2 en el resto, sin pasar por `UITheme`, sin `PanelChrome`, sin
  `DraggablePanel`. No lo cubre `EditorRawColorRatchetTests` porque el chat no es un
  editor, así que nada frena la deriva.
- **Sin localización, con un botón de idioma.** `"Escribe un mensaje..."`, `"Enviar"`,
  `"Cerrar (ESC)"` y `"No hay nadie cerca para hablar..."` van a fuego en español,
  mientras un botón ES/EN persiste una preferencia que ningún texto consulta.
- **`Mask` en lugar de `RectMask2D`** en el área de scroll: stencil buffer y un draw call
  extra donde bastaría un recorte rectangular.

### Bajos

- Comentario XML huérfano al final de `ChatSystem.cs` (`Close current chat` sin método
  detrás; `CloseChat` vive en el partial).
- `CANCEL_BUBBLE_TTL_MS` declarado y sin usar.
- Campos de persona inertes: `tone`, `maxSentences`, `verbosity`, `useEmoji`,
  `allowedItemTypes`. Y `NPCInteractable.dialogueKey`, que nadie lee.
- `ChatBubble._yOffset` es fijo (1.5) sea cual sea la altura de la entidad.
- `ChatBubble` no agrupa burbujas en pool: cada trozo de respuesta crea y destruye
  `GameObject` + `Image` + `TMP` + `CanvasGroup`.
- `ChatBubble` fija `_canvasRect.sizeDelta = (3, 1)` mientras mide el texto contra
  `_maxWidth / 0.02f` = 150. Los dos números describen lo mismo en unidades distintas y
  solo el segundo manda; el primero es ruido que invita a un error futuro.
- `CLAUDE.md:173` apunta a `Data/ChatPersonas/*.asset`, ruta que no existe.

## Lo que está bien, y merece decirse

- **Null-safety con criterio.** El comentario de `ResolvePlayerBubble` explica por qué no
  se llama `Ensure`: el nombre prometía una postcondición que la función no puede
  cumplir, y todos los llamadores se lo creyeron. Ese es el nivel de razonamiento del
  fichero entero.
- **Cancelación correcta.** `_replyCts` se cancela al enviar un mensaje nuevo y al cerrar
  el chat; `OperationCanceledException` se traga a propósito y cualquier otra excepción
  degrada a `"..."` en vez de dejar la conversación colgada.
- **`ChatInputGate` es defensivo de verdad.** Late-bind por corrutina, re-bind en
  `Update` para singletons perezosos, y un auto-sondeo que compara el `IsOpen` real
  contra el flag cacheado por si se perdió un evento. `OnDisable` reabre el input para
  que desactivar el objeto no congele al jugador.
- **Reset de estáticos donde toca.** `ChatPersistencePaths`, `ChatSessionLogger`,
  `ChatInputGate` y `NPCMemoryStore` traen su `SubsystemRegistration`, con Domain Reload
  apagado.
- **`ChatPersistencePaths.OverrideRoot`** deja que los tests escriban en un temporal en
  vez de ensuciar `persistentDataPath`. Es la razón de que 225 tests que tocan disco
  corran en 4.6 s sin efectos colaterales.

## Camino más corto a un 6/10 jugable

Por orden; los tres primeros son el 90 % del valor.

1. **Un asset de persona real y una asignación.** Crear
   `Data/ChatPersonas/vendor_gatita.asset` con `dialogueLines` y meterlo en
   `ChatAssignmentCatalog.asset` con el `entityName` exacto.
2. **Asignar el catálogo en el bootstrap.** `EnsureChatSystem` debe cargar el
   `ChatAssignmentCatalog` (por `Resources.Load` en subcarpeta, nunca con ruta vacía) y
   dárselo al `ChatSystem` recién creado.
3. **Hacer chateable a alguien.** `EntitySetup` debe añadir `NPCInteractable` a las
   entidades cuya `MonsterDefinition` lo declare, y llamar a `EntityRegistry.RegisterNPC`
   para las no hostiles.
4. **Un test de composición** que abra el catálogo embarcado, resuelva una persona por el
   nombre real de una entidad que el juego genera, y falle si el resultado es nulo.
   Sin él, el punto 1 vuelve a vaciarse sin que nadie se entere.
5. **Releer la memoria al abrir.** Sembrar `_history` con `ephemeralHistory` en lugar de
   limpiarla, y pasar la memoria al provider para que la respuesta dependa de ella.
6. Enter debe respetar `GameEditorManager.AnyEditorActive`.
7. Temporal con GUID en `NPCMemoryStore.Save`, antes de que exista un provider asíncrono.
