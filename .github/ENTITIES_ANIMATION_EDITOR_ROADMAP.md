# Editor de Entidades — pestaña de Animaciones (roadmap)

**Fecha:** 2026-09-12 · Alcance: ver, medir y ordenar las animaciones de cualquier entidad
(monstruos, NPC y jugadores) desde el Editor de Entidades (ESC → Entidades).

> **Estado: fases 0 a 5 construidas el mismo día.** Lo entregado y lo que cambió respecto a
> este plan está en la sección 11, al final. La fase 6 (reordenar frames) sigue fuera, por la
> razón que da la propia sección 7.

Método: lectura del editor y del modelo de animación en Unity, más una lectura del tag
`archive/python-legacy-2026-05-06` para saber qué había antes. Cada afirmación va con su
fichero y su línea.

---

## 1. Resumen

Hoy no hay ningún sitio donde ver una animación de una entidad que no sea el juego corriendo.
El único visor que anima de verdad es la vista previa del Editor de Hechizos, y solo sirve para
el jugador vivo. El Editor de Entidades no muestra ni un frame: su sección Assets es una
etiqueta de texto.

Python tenía menos modelo de datos y más visor: una cuadrícula 3x3 con las ocho direcciones
animándose a la vez, por estado. Nunca dejó reordenar frames ni cambiar la velocidad.

Lo que falta, entonces, no es un modelo nuevo: es la pantalla. El modelo ya distingue estado,
variante, loadout, multiplicador por entidad, por estado y por variante, y `holdLastFrame`.
Nada de eso es visible ni editable fuera del Inspector de Unity.

La única pieza que falta de verdad en el modelo es la **duración por frame**, y la recomendación
es no construirla (sección 8).

---

## 2. Lo que existe hoy

| Sitio | Qué muestra | Sirve para monstruos |
|---|---|---|
| Editor de Entidades → Assets | una etiqueta `Idle Sprite` con un nombre de sprite | no, muestra `—` |
| Editor de Hechizos → vista previa | animación real, por estado y variante, con direcciones y transporte | no, solo el jugador vivo |
| Inspector de Unity | listas crudas de sprites y los multiplicadores | sí, sin vista previa |
| Panel Animaciones del editor FSM | nombres de estado a nombres de animación | escribe un fichero que nadie lee |
| Informes de los importers | recuentos por estado en consola | sí, sin imagen |

Detalle medido:

- La sección Assets de un monstruo es **una** fila:
  `EntitiesRuntimeEditor.Interaction.cs:591-593`. Para los monstruos construidos con listas de
  frames, `idle.south` es nulo y la fila imprime un guion — que además está mal codificado y
  sale como `â€”` (`:592`). El mismo `idle.south` alimenta la miniatura del picker (`:127`), así
  que esas entidades tampoco tienen icono.
- Para un jugador hay cuatro filas de solo lectura: `Idle Sprite`, `Scale`, `Sheet Frames` y
  `Anim Speed` (`:626-655`). `Sheet Frames` es un total, no un desglose por estado.
- Las secciones del panel de propiedades son Identidad, Stats, IA, Spawn, Auto-Cast y Assets
  (`EntitiesEditorUIBuilder.Properties.cs:46-51`). No hay sección de animación.
- Lo editable son nueve stats y el auto-cast (`EntitiesRuntimeEditor.Interaction.cs:523-537`,
  `:556-589`). Ningún campo de animación se lee ni se escribe.
- La vista previa del Editor de Hechizos clona el `DirectionalAnimator` del jugador vivo
  (`SpellPreviewService.Character.cs:37-73`). No hay ruta desde una `MonsterDefinition`.

---

## 3. Lo que había en Python

Tag `archive/python-legacy-2026-05-06`, prefijo `python/`.

- **Editor de Entidades (F5):** pestañas por estado
  (`idle`, `walk`, `chase`, `attack`, `death`, `damage`, `casting`) y una cuadrícula 3x3 con las
  ocho direcciones, cada celda un `Animator` en bucle. Doble clic abría un selector de asset para
  esa celda. No había play/pausa, ni velocidad, ni índice de frame, ni reordenado.
- **Editor FSM (F12):** la tabla estado → nombre de animación que hoy es `animation_map.json`,
  ya inerte.
- **Tiempo:** un único intervalo global de 0.15 s por entidad, sin fps por estado ni duración por
  frame. El orden de los frames lo fijaban las columnas del sheet y dos reglas escritas en el
  código: idle mantiene el frame 0 un segundo, walk se salta el frame 0.

Conclusión: la cuadrícula de ocho direcciones es lo único de Python que merece volver, y vuelve
mejor porque ahora hay variantes y loadouts que enseñar.

---

## 4. El modelo actual, que es lo que la pantalla tiene que contar

- **Orden de frames:** es el orden de la lista en el asset. Lo fija el pipeline de Python por el
  `index` del slice, y los importers lo copian sin reordenar
  (`PlayerFramesImporter.cs:492-517`, `MonsterFramesImporter.cs:294-353`).
- **Direcciones:** `CreateSetFromLinearFrames` corta la lista en ocho bloques **contiguos** de
  `n/8` en el orden S, SE, E, NE, N, NW, W, SW; lo que sobra se descarta en silencio
  (`DirectionalAnimator.SpriteSetBuilder.cs:42-141`). En el reparto de cuatro direcciones el
  orden de la tira es S, W, E, N (`:63-92`).
- **Velocidad:** el tiempo de un frame es
  `frameInterval / (multiplicador de la entidad × statePacing × multiplicador de la variante)`
  (`DirectionalAnimator.cs:103-104, 517, 533-538`). Los tres se multiplican; ninguno sustituye a
  otro. `frameInterval` vale 0.15 s y el `idleHoldTime` 1 s.
- **Reglas de reproducción** (`DirectionalAnimator.FrameLogic.cs:40-131`): idle mantiene el
  frame 0 un segundo y luego repite del 1 al final; walk y chase se saltan el frame 0; death se
  reproduce una vez y se queda en el último; una variante con `holdLastFrame` también; el resto
  repite entero.
- **Variantes:** de ataque, de lanzamiento y de estado, cada una con su multiplicador, su
  `holdLastFrame` y sus `spellKeys` reservadas (`Data/Player/EntityAssetConfig.cs:115-321`).
- **Loadouts:** sustituyen los estados que declaran, incluidas sus variantes (`:615`).
- **Caídas:** walk→idle, chase→walk, cast→walk, attack→cast, damage y death→idle
  (`EntityAnimationBinder.cs:93-133`). Un estado sin arte no se ve vacío, se ve con otra pose, y
  eso es exactamente lo que hoy no se puede saber sin leer el código.
- **La velocidad es balance, no presentación.** `GetStateLength` (`DirectionalAnimator.cs:586`)
  decide la ventana del ataque y del lanzamiento (`AttackState.cs:65`, `NPCCastState.cs:151`,
  `PlayerController.Movement.cs:639`), así que alargar una animación cambia el ritmo de golpes.
- **No hay duración por frame.** La única excepción es la pausa del frame 0 del idle.

---

## 5. Restricciones que condicionan el diseño

Cada una ya ha costado un fallo en este proyecto, y todas caen dentro de este trabajo.

1. **No queda ni una capa de física libre.** Las 32 están gastadas (`TagManager.asset`), y ya
   existen `ParticlePreview` y `SpellPreview`. La vista previa de entidades **reutiliza la capa
   `SpellPreview`** y se pone en su propia Y lejana. Es seguro porque `GameEditorManager` abre
   los editores en exclusiva: el de Hechizos y el de Entidades no pueden estar abiertos a la vez.
2. **El rig se construye con `EntityAnimationBinder.ApplyMonsterVisuals`**
   (`EntityAnimationBinder.cs:22-28`), que acepta cualquier `GameObject` con un `SpriteRenderer`
   dentro (`:83`). Es la misma ruta que usa el juego, así que la cadena de caídas, las variantes y
   el pacing se resuelven igual que en partida. Una segunda implementación respondería distinto
   el día que un estado se quede sin arte.
3. **La cámara de la vista previa no tiene luz cerca**, así que el material lit se ve negro. Hay
   que forzar `ElementalSprites.SharedUnlitMaterial`, como hace
   `SpellPreviewService.Character.cs:51-52`.
4. **`_frameIndex` no es el frame que se está viendo**, es el siguiente: se incrementa después de
   pintar (`DirectionalAnimator.FrameLogic.cs:82-130`). Una tira de frames que resalte
   `_frameIndex` señala siempre el frame equivocado.
5. **Los assets se marcan con `EditorUtility.SetDirty`, nunca con `Undo.RecordObject`**
   (`EntitiesRuntimeEditor.Interaction.cs:472-475`), y se guardan con un `SaveAssets` explícito
   (`EntitiesRuntimeEditor.cs:148`). La regla viene del incidente de las 193 plantillas de
   edificios.
6. **Un reimport reescribe las listas de frames y conserva el pacing por clave.** Editar
   velocidades sobrevive; reordenar frames no (sección 7, fase 6).
7. **uGUI no hace layout en EditMode**, Unity no llama a `Awake` en un componente añadido allí y
   `Object.Destroy` es un error. Cualquier reconstrucción de la tira de frames necesita la rama
   `Application.isPlaying ? Destroy : DestroyImmediate` que ya llevan los editores de Entidades y
   Edificios.
8. **El trinquete de colores crudos.** `EditorRawColorRatchetTests` mide `new Color(` por fichero
   contra `Tests/EditMode/Baselines/editor-raw-colors.txt` y solo permite bajar. La pestaña nueva
   usa `UITheme`.
9. **Nada de teclas nuevas.** `InputActionCatalog` es una tabla cerrada y `ValkurInputActions` es
   el único modelo de bindings. La pestaña se maneja con botones, como el editor de Misiones.
10. **La persistencia de sesión va por `IProvidesWorkspaceState`**
    (`EntitiesRuntimeEditor.Workspace.cs`), y no se restaura nada destructivo.

---

## 6. La pantalla que falta

Una pestaña **Animaciones** dentro del panel de propiedades del Editor de Entidades, con cinco
zonas:

1. **Escenario:** un `RawImage` con la entidad seleccionada animándose, más zoom y fondo.
2. **Selectores:** estado (los ocho), variante (con su nombre y sus `spellKeys` reservadas),
   loadout, y dirección — una cuadrícula 3x3 como la de Python, con una opción de ver las ocho a
   la vez.
3. **Tira de frames:** miniaturas numeradas de la dirección activa, con el frame en curso
   resaltado, y transporte: play, pausa, un frame atrás, un frame adelante, bucle y marcha atrás.
4. **Lectura de tiempos:** segundos por frame, fps, duración total, los tres multiplicadores por
   separado, la regla de reproducción que aplica y, para los estados de combate, la marca del
   windup y el ritmo de golpes real contra `meleeCooldown`.
5. **Edición y avisos:** los multiplicadores, `holdLastFrame`, `directionLayout`, y una lista de
   problemas detectados.

---

## 7. Fases

### Fase 0 — Los tres arreglos baratos (tamaño: pequeño)

Van primero porque son visibles y porque dos de ellos ensucian lo que la pestaña nueva va a
mostrar.

- El guion mal codificado, `â€”`, en `EntitiesRuntimeEditor.Interaction.cs:592` y en los otros
  tres sitios del directorio (`:77`, `:715`, `EntitiesRuntimeEditor.SelectionFx.cs:252`).
- La miniatura del picker vacía para las entidades hechas con listas de frames: caer a
  `idleSheets[0]` como ya hace la rama de jugador (`:257-271`).
- `CopyVariantsFrom` no copia `_stateSpeed` (`DirectionalAnimator.cs:404-429`), así que la vista
  previa del Editor de Hechizos reproduce el `statePacing` a la velocidad equivocada — el idle
  lento de Gatita se ve rápido.

### Fase 1 — El escenario: ver a cualquier entidad animarse (tamaño: medio)

Entrega: `EntityAnimationPreviewService` en `Gameplay/Editors/Entities/`, calcado del patrón de
`SpellPreviewService` (`:236-279`): raíz fuera de pantalla, cámara ortográfica que solo ve la capa
`SpellPreview`, `RenderTexture` de 384, `RawImage` en el panel.

- El rig es un `GameObject` con un `SpriteRenderer` y `ApplyMonsterVisuals` / `ApplyPlayerVisuals`
  encima. Nada más: el binder pone el `DirectionalAnimator`, las variantes y el pacing.
- Selector de estado, de variante y de loadout, resueltos contra el rig
  (`VariantCount`, `PacingOf`, `IsVariantReserved`, `ApplyLoadout`).
- Selector de dirección 3x3, con la celda central como conmutador de "ver las ocho".
- Encuadre automático desde los bounds del sprite, con zoom manual.

Criterio de aceptación: seleccionar `red_dragon` en el picker y ver su ataque animándose, con las
ocho direcciones, sin entrar en partida.

### Fase 2 — La tira de frames y el transporte (tamaño: medio)

Necesita cuatro miembros nuevos en `DirectionalAnimator`, todos pensados para que el rig de la
vista previa sea el único que los use:

```csharp
public int  DisplayedFrameIndex { get; }                                  // escrito en ApplyFrame
public Sprite[] FramesFor(AnimState state, Direction dir, int variant);   // el bucket ya resuelto
public bool Paused { get; set; }                                          // Update no avanza el reloj
public void ShowFrame(int index);                                         // scrub explícito
```

`DisplayedFrameIndex` es obligatorio por la restricción 4: `_frameIndex` apunta al siguiente.

La tira se construye con `FramesFor`, y mientras está en pausa la propia pestaña llama a
`ShowFrame`, así que el índice resaltado no depende de adivinar nada.

Criterio de aceptación: pausar en el frame 5 de ocho, ver la miniatura 5 resaltada y el escenario
mostrando ese mismo frame.

### Fase 3 — La lectura de tiempos (tamaño: pequeño)

Todo sale de datos que ya existen: `AnimationSpeedMultiplier`, `StateSpeedOf`, `PacingOf`,
`GetStateLength`, y las reglas de `FrameLogic`.

- Segundos por frame y fps efectivos, con los tres multiplicadores desglosados para que se vea
  cuál manda.
- Duración total del estado y la regla que aplica: bucle, una vez, hold, o el idle con su segundo
  de pausa. La regla es la que explica por qué un walk de ocho frames dura siete.
- Para attack y cast: la marca del windup sobre la tira y el ritmo de golpes que resulta frente a
  `meleeCooldown`. Es el caso del `knight_red` — alargar la animación le subió el daño por segundo
  un 25 % sin tocar ningún número de daño.
- Un aviso cuando el estado mostrado es una caída: "chase sin arte, se está viendo walk".

### Fase 4 — Editar las velocidades (tamaño: medio)

Editables desde la pestaña, persistidos con `SetDirty` más el guardado explícito del editor:

- `scaleConfig.animationSpeedMultiplier` de la entidad.
- `statePacing` por estado.
- El multiplicador y el `holdLastFrame` de cada variante.
- `directionLayout`.

Todo esto sobrevive a un reimport, porque los importers solo aplican estos valores al **crear** una
variante. Como cambiar la velocidad cambia el ritmo de golpes (sección 4), la fila de ritmo de la
fase 3 tiene que actualizarse en vivo mientras se arrastra el valor: es la mitad que convierte el
control en una decisión informada.

### Fase 5 — Validaciones (tamaño: pequeño)

Todas son fallos que hoy son silenciosos:

- Número de frames no múltiplo de ocho: los que sobran se descartan y nadie avisa.
- Un sprite que faltó en el import: la lista se acorta y **todos los bloques de dirección
  posteriores se desplazan**, así que la entidad mira hacia donde no debe.
- Estado vacío que está cayendo a otro.
- Las dos mitades espejadas descuadradas.
- Onion skin y una línea de suelo, para ver saltos del pivote entre frames — es lo que delata un
  ciclo de andar que "flota", el problema que `build_knight_frames.py` resuelve en el pipeline.

### Fase 6 — Reordenar frames (tamaño: grande, decidir antes de escribir código)

Reordenar en el asset funciona hasta el siguiente import, que reescribe la lista. Tres salidas:

| Opción | Qué implica | Veredicto |
|---|---|---|
| Escribir el orden de vuelta al manifest de Python | el manifest sigue siendo la verdad; hace falta una ruta de escritura desde Unity a `tools/atlas/generated/*.json` | la correcta, y la más cara |
| Que el importer conserve el orden editado | una lista de permutaciones por estado en el asset, aplicada tras copiar el manifest | media; añade un campo que hay que mantener |
| Reordenar y aceptar que se pierde | cero código nuevo, y una trampa esperando | no |

Sea cual sea, el reordenado es **por dirección**, y la mitad espejada tiene que seguir a su
original o la entidad mira a un lado y anda hacia el otro.

Recomendación: dejar la fase 6 fuera de la primera entrega. Las fases 1 a 5 ya contestan las dos
preguntas que motivaron esto — a qué velocidad va y en qué orden están los frames — y el
reordenado de verdad es un trabajo de pipeline, no de editor.

---

## 8. Lo que queda fuera, y por qué

- **Duración por frame.** Necesitaría un `frameDurations[]` en el modelo, cambios en `FrameLogic`
  y, sobre todo, en `GetStateLength`, que es quien fija el tiempo del daño. Los tres
  multiplicadores más `holdLastFrame` cubren lo que se ha necesitado hasta ahora. Si algún día
  hace falta, será por una animación concreta y con su medida delante.
- **Una línea de tiempo editable con claves.** Es un editor de animación, no un visor. Fuera de
  alcance.
- **`animation_map.json`.** El panel del editor FSM escribe un fichero que ningún código lee, y el
  mapeo real está en un `switch` de C# en `FSMMonsterBrain`. O se conecta o se borra, pero es un
  trabajo del FSM, no de este.

---

## 9. Tests

| Fixture | Qué fija |
|---|---|
| `EntityAnimationPreviewTests` | el rig se construye por `ApplyMonsterVisuals`, usa material unlit, vive en la capa `SpellPreview` y se destruye entero al cerrar |
| `DirectionalAnimatorTransportTests` | `DisplayedFrameIndex` es el frame pintado y no el siguiente; `Paused` congela; `ShowFrame` respeta las reglas de cada estado |
| `EntitiesAnimationPanelTests` | selectores de estado, variante, loadout y dirección; que una caída se anuncia; que la edición marca el asset sucio sin `Undo.RecordObject` |
| `ShippedEntityAnimationDataTests` | sobre los datos ya publicados: todo estado con lista de frames es múltiplo de ocho, ningún sprite nulo, ninguna variante sin frames |

La cuarta es la que más vale: mide los datos de verdad, no la pantalla, y es la única que puede
encontrar hoy una entidad mirando hacia donde no debe.

---

## 10. Orden de trabajo

Fase 0, luego 1, 2 y 3 — solo ver y medir, que es lo que se pidió. Después la 4 y la 5. La 6 se
decide cuando las otras estén en uso y se sepa si reordenar hace falta de verdad.

---

## 11. Lo construido (2026-09-12)

### Ficheros

| Fichero | Qué es |
|---|---|
| `Gameplay/Editors/Entities/EntityAnimationPreviewService.cs` | el escenario: cámara fuera de pantalla, RenderTexture, rigs, guías y la lectura de tiempos |
| `Gameplay/Editors/Entities/EntitiesEditorUIBuilder.AnimationPanel.cs` | el panel: escenario, selectores, pad 3x3, transporte, tira de frames, dials |
| `Gameplay/Editors/Entities/EntitiesRuntimeEditor.Animation.cs` | el cableado y todo lo que el panel dice |
| `Gameplay/Editors/Entities/EntitiesRuntimeEditor.AnimationEditing.cs` | los dials, el layout y las validaciones |
| `Gameplay/Player/DirectionalAnimator.Transport.cs` | pausa, paso, scrub y el índice del frame en pantalla |

Tocados: `DirectionalAnimator.cs` y `.FrameLogic.cs` (etiquetas de variante, `ApplyFrame`
indexado, la pausa), `EntityAnimationBinder.cs` (las etiquetas), `EntityAssetConfig.cs`
(`SetStateSpeedMultiplier`), y el editor (menú, dropdowns, ciclo de vida, workspace).

Tests: `EntityAnimationPreviewTests`, `DirectionalAnimatorTransportTests`,
`EntityAssetConfigPacingTests`.

### Lo que cambió respecto al plan, y por qué

- **Las etiquetas de variante viajan desde el binder.** El plan daba por hecho que la
  pantalla podría nombrar una variante; no podía. El binder DESCARTA las variantes sin
  frames, así que el índice del rig no es el del asset y no hay forma de recuperar la clave
  desde el animador. `SetVariants` acepta ahora una lista de etiquetas, y esa clave es
  también lo que permite ESCRIBIR el pacing en la variante correcta (fase 4): por posición
  se retunearía a la vecina.
- **`ApplyFrame` recibe el índice.** La fase 2 pedía un `DisplayedFrameIndex`, y el único
  sitio que puede darlo honestamente es el seam por el que todo frame llega al renderer.
  `_frameIndex` apunta al SIGUIENTE — cada rama lo incrementa después de dibujar — así que
  exponerlo habría señalado siempre el frame que aún no se ha visto.
- **Los dials acabaron en el panel de Animaciones, no en Propiedades.** Dos de los tres
  están scoped a lo que el escenario muestra: el multiplicador por ESTADO y el de la
  VARIANTE no significan nada sin los selectores justo encima.
- **`SetStateSpeedMultiplier` vive en `EntityAssetConfig`, no en el editor.** La lista es
  suya, y un escritor en otro ensamblado es cómo los dos acaban discrepando sobre qué
  significa una fila ausente. Escribir exactamente 1 BORRA la fila, porque 1 es lo que ya
  responde un estado sin fila.
- **La vista previa reutiliza la capa `SpellPreview`.** No queda ni una capa de física
  libre; los editores se abren en exclusiva, así que las dos escenas no pueden renderizar en
  el mismo frame. Escenarios a Y distinta de todos modos.
- **Onion skin solo en pausa**, y una línea de suelo siempre: a velocidad, un fantasma es
  otro cuerpo en movimiento y se lee como un fallo de render.

### Lo que sigue abierto

- **Reordenar frames (fase 6)**, por lo que dice la sección 7: el orden vive en el manifest
  de Python y el importer lo reescribe.
- **Duración por frame**, por lo que dice la sección 8: tocaría `GetStateLength`, que fija el
  tiempo del daño.
- **La tira muestra 16 frames** y avisa del exceso. Ninguna animación publicada pasa de ocho
  por dirección.
- **Los jugadores son de solo lectura** en los dials, como el resto del editor: solo los
  monstruos se editan aquí.

---

## 12. La línea de tiempo de lanzamiento (2026-09-12)

Segunda tanda, pedida tras ver el panel funcionando: **poder decir cuánto dura cada sprite, y
coordinar la animación de un lanzamiento con los tiempos del hechizo.**

### El hueco

Eran dos relojes que no se hablaban. El hechizo decide cuándo dispara — `SpellCaster` ejecuta al
agotarse `prepareDuration` — y la animación decidía cuánto duraba, por `GetStateLength`. Nada
reconciliaba las dos cosas, así que el frame en que se dibuja al personaje cuando le sale una
bola de fuego de la mano era el que cayera.

### El modelo

`AnimationTimeline`, en la VARIANTE (`CastVariant` / `AttackVariant`) — que es donde
`spellKeys` ya ata un arte a un hechizo en un personaje concreto. Nunca en el `SpellDefinition`:
un hechizo lo comparten todos los que lo lanzan y no sabe nada del arte de ninguno, el mismo
argumento por el que `castMuzzle` pertenece a la criatura.

- **Pasos** `{frame, duración}` con orden libre y repeticiones, así que `0,1,2,1,2,1,2,3` es un
  semibucle escrito y no ocho sprites duplicados.
- **Dos fronteras** parten el plan en wind-up / lanzamiento / recuperación. La primera **es** el
  instante en que el ejecutor dispara.
- **Tres modos por tramo**: `Estirar` (el tramo dura lo que la fase; las duraciones autorizadas
  son ratios), `Bucle` (a velocidad autorizada, repitiendo hasta llenar la fase) y `Congelar`
  (una pasada y el último frame aguanta).

`CastTimelineResolver` es una función pura que convierte plan + fases en una lista de
`{frame, segundos}`. Pura a propósito: la aritmética que decide cuándo ocurre el daño se prueba
sin escena, sin animador y sin Play Mode.

### Decisiones que costaron una discusión

- **Sin topes de estirado ni de compresión**, pedido explícitamente. Una preparación de 0,05 s
  sobre seis frames da 8 ms por frame y el resolvedor los da; el panel muestra la cifra en vez
  de que el sistema se niegue.
- **El plan es la verdad de AUTORÍA; el hechizo es la verdad de EJECUCIÓN.** En ejecución hay
  una sola fuente, así que no hay deriva posible. El botón "Write to spell" es el puente, y es
  un botón y no una sincronización automática porque escribe un asset COMPARTIDO: el panel
  cuenta cuántas entidades lanzan ese hechizo y lo dice antes del clic.
- **La recuperación no cubre el enfriamiento.** Un cooldown es cuándo se puede volver a lanzar,
  no tiempo que el lanzador pasa posando: aguantar los veinte segundos del `war_cry` dejaría al
  personaje fuera de su propio turno.
- **Un giro NO acaba el plan**; un cambio de estado o de variante sí. Los índices del plan son
  por dirección, así que girar a mitad de lanzamiento conserva el sitio.

### Lo que destapó

- **La ventana de animación se dimensionaba con el arte**, no con la fase. Con 1,2 s de
  preparación el personaje recuperaba la locomoción a mitad de carga y se iba andando mientras
  el hechizo seguía contando. Las fases son el suelo ahora.
- **`allowMovement` e `interruptible` llevaban inertes desde siempre** y empiezan a significar
  algo en cuanto hay windups largos. Cableados: el primero planta al lanzador durante wind-up y
  canalización, el segundo cancela por `Health.OnDamagedBy` — no por `OnHpChanged`, o una
  curación cortaría un lanzamiento.
- **Ningún dato existente se autorizó pensando que esos campos se leerían.** Los ocho hechizos
  con windup los elegí; los otros 96 heredan balance de valores puestos por defecto o por
  intuición. Medido antes de cablear: 38 quedan plantados y, salvo los ocho, todos con fases de
  0,4 s o menos; 32 interrumpibles con windup real, el mayor `lightball` con 0,5 s.
  `ShippedCastPhaseDataTests` lo acota: nadie puede autorizar una planta más larga que la más
  larga elegida a propósito sin que salga rojo con nombre y segundos.

### Ficheros

| Fichero | Qué es |
|---|---|
| `Data/Player/AnimationTimeline.cs` | el plan autorizado y sus fronteras |
| `Data/Player/CastTimelineResolver.cs` | plan + fases → lista de `{frame, segundos}`. Puro |
| `Gameplay/Player/DirectionalAnimator.Timeline.cs` | la reproducción del plan |
| `Gameplay/Editors/Entities/EntitiesEditorUIBuilder.TimelinePanel.cs` | las dos pistas sobre un eje |
| `Gameplay/Editors/Entities/EntitiesRuntimeEditor.Timeline*.cs` | dibujo y edición |

Tocados: `EntityAssetConfig` (el campo en las dos variantes), `EntityAnimationBinder` (lo
transporta), `DirectionalAnimator` (tabla por variante, reloj, `GetStateLength`),
`PlayerController.Movement` (instalación, ventana, planta, interrupción), `SpellCaster`
(`CurrentSpell`, `CancelPreparingCast`).

### Abierto

- **El efecto de preparación** — partículas durante el windup. Acordado dejarlo para su propia
  discusión; el enganche está reservado.
- **Los monstruos no instalan plan todavía.** `NPCCastState` sigue dimensionando su pose con
  `GetStateLength`, que ya responde con el total del plan, pero nadie se lo instala: el dragón
  puede tener línea de tiempo y hoy no la usaría.
- **El melé** (`hitFrame` contra `attackWindupSeconds`) es el mismo problema con otro nombre y
  el modelo ya lo admite: `AttackVariant` lleva el campo.
