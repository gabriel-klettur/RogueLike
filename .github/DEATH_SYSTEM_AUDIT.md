# Auditoría del sistema de muerte y resurrección

> Auditado 2026-09-07, medido en Play Mode sobre la sesión viva. Nota inicial **2.8 / 10**.
> **Reconstruido el mismo día. Nota actual: 8.1 / 10.** El registro de la reconstrucción está al
> final; el diagnóstico original se conserva íntegro porque es lo que explica cada decisión.

## Resumen ejecutivo

El sistema de muerte está **bien construido y no está conectado**. Las siete piezas
(controlador, estado espíritu, visuales, gris de mundo, camino, cadáver, drop) existen,
se instancian en bootstrap y funcionan. Lo que no existe es **el altar**.

`ResurrectionZoneAutoBinder` busca edificios con `templateId == 249`
(`Buildings/portals/portal_stone_arch`). El mundo enviado contiene **301 edificios
colocados y ninguno con esa plantilla**. Consecuencia en cadena, toda silenciosa:

| Pieza | Qué hace sin altar | Medido en vivo |
|---|---|---|
| `ResurrectionZoneAutoBinder` | escanea 60 s, avisa una vez, se apaga | `bound=0`, `enabled=False` |
| `ResurrectionZone` | nunca se crea | `count = 0` |
| `SpiritAltarPathHighlighter` | `FindNearestAltar` da null → `HideAllMarkers()` | `SpiritPathMarkers.childCount = 0` |
| `DeathSequenceController.Revive()` | nadie lo llama nunca | `phase = Spirit`, indefinido |

Estado de la sesión en el momento de la auditoría: jugador en `Spirit`, `hp = 0/216`,
a **(175.12, 65.86)**, con su cadáver y todo su inventario y monedas en **(205.04, 1.62)** —
64 unidades atrás, sin marcador de ninguno de los dos. 673 renderers desaturados.
Única salida: `resurrect` en la DevConsole.

Los dos síntomas que reportaste son **un solo fallo de datos**, no dos bugs de código.

## Puntuaciones

| # | Eje | Nota | Por qué |
|---|---|---|---|
| 1 | Alcanzabilidad del punto de revivir | **0** | Cero altares en el mundo. El sistema entero cuelga de un dato que nadie colocó |
| 2 | Garantía de no-callejón-sin-salida | **0** | El jugador queda espíritu para siempre. No hay temporizador, ni respawn de emergencia, ni botón. Solo consola |
| 3 | Diagnosticabilidad del fallo | **2** | Dos `LogWarning` en una consola que nadie mira en juego. Nada en pantalla. Ningún test sobre datos enviados |
| 4 | Configuración como dato vs constante mágica | **1** | `249` escrito a mano en `ResurrectionZoneAutoBinder` **y** en `SpiritWorldGrayscale.altarTemplateId`; deben coincidir y nada lo comprueba. Además el catálogo tiene `grave_altar_candles` y `shrine_idol_arcane`, que son lo que un altar debería ser |
| 5 | Guía de navegación del espíritu | **2** | Línea recta que atraviesa muros — pero el espíritu **no** atraviesa muros (la máscara excluye NPC/Projectile/Pickup, no World/Building). El camino promete una ruta que no se puede andar. Sin flecha fuera de pantalla, sin distancia, sin brújula al cadáver |
| 6 | Mecánicas de forma espíritu | **5** | Movimiento, intangibilidad frente a NPC y FSM, exención de editores abiertos: todo correcto. Falta velocidad propia, paso a través de muros, y límite de tiempo |
| 7 | Bucle de recuperación de cadáver y botín | **3** | Suelta todo (inventario + monedas). El cadáver es un cuadrado rojo procedural de 8 px. Los objetos caídos **nunca se limpian**, ni al revivir ni al morir otra vez: se acumulan |
| 8 | Coste real de la muerte / integración con guardado | **2** | `GameStateCollector` rechaza guardar con `hp <= 0`, y `GameStateRestorer` sube cualquier `hp==0` a `maxHp`. O sea: morir, salir y recargar **deshace la muerte entera** y devuelve el inventario. La penalización es opcional para el jugador |
| 9 | Presentación visual | **6** | El gris por swap de material (no tinte multiplicativo) es la decisión correcta y está razonada. Banner + flash + kick de cámara existen. Pero el banner dice "Encuentra el altar para revivir" señalando algo que no existe |
| 10 | Audio | **0** | Cero sonido en todo el flujo: ni muerte, ni entrada a espíritu, ni resurrección |
| 11 | Arquitectura y propiedad única | **7** | Un solo controlador dueño de máscaras, gris y cadáver; fases explícitas; red de seguridad por polling si se pierde el evento. Es la parte fuerte |
| 12 | Extensibilidad | **2** | Un `templateId` global. No hay altar por zona, ni por interior, ni por mazmorra, ni checkpoints. Morir dentro de un interior te deja buscando un altar del mundo base |
| 13 | Cobertura de tests | **1** | Un único fichero (`SpiritWorldGrayscaleTests`). Cero tests de `DeathSequenceController`, `ResurrectionZone`, del camino, del drop, del cadáver — y **cero tests sobre los datos enviados**, que es justo donde está el fallo |
| 14 | Rendimiento | **5** | `FindObjectsOfType<ResurrectionZone>` cada 0.25 s, `FindObjectOfType<BuildingLoader>` cada 0.25 s durante 60 s, y 673 swaps de `sharedMaterial` en un frame al morir |
| 15 | Integración con permadeath | **3** | Borra el autoguardado en `OnPlayerDied` y luego el flujo de espíritu sigue como si nada: el jugador anda buscando un altar en una partida ya borrada |
| 16 | Interacción con modos e input | **6** | El espíritu supera la suspensión por editor abierto, y el `Update` sigue bien gobernado. Correcto |
| 17 | Honestidad del camino de trampa | **3** | `ForceRevive` dispara **los dos** eventos, así que `XpLossOnDeathSystem` cobra el 10 % de XP por usar el cheat de consola. El comentario dice que los dos eventos existen precisamente para distinguirlos: la distinción está escrita y es inerte |

Media: **2.8 / 10**.

## Los cinco defectos por orden de daño

1. **Ningún altar colocado.** Un dato ausente apaga siete sistemas sin que nada falle.
   El patrón exacto que este repo ya documenta (`SPAWNER_COORDINATE_SPACE_DRIFT`): cada
   mitad es internamente coherente y solo la composición está mal.
2. **La muerte no tiene salida.** Aunque el altar existiera, un altar inalcanzable
   (interior, otra zona, muro delante) deja al jugador atrapado igual. Falta la red:
   temporizador máximo en espíritu que revive en el último punto seguro.
3. **El identificador del altar es una constante en dos ficheros.** Debe ser un flag en
   `BuildingTemplateData` (`isResurrectionAltar`) o una entrada de catálogo, y un test
   sobre datos enviados que exija al menos un altar por mundo cargable.
4. **Recargar deshace la muerte.** Mientras el guardado rechace `hp<=0`, morir no cuesta
   nada a quien salga al menú.
5. **El camino miente.** Recta que atraviesa muros por los que el espíritu no pasa. O el
   espíritu atraviesa muros (excluir World y Building de su collider), o el camino usa
   `PathFinder`. Ambas son defendibles; la mezcla actual no.

## Plan sugerido (no ejecutado)

- **Fase 0 — desatascar hoy.** Colocar uno o más altares en el mundo enviado, y hacer que
  el binder falle **ruidosamente** si no encuentra ninguno al terminar de cargar el mundo.
- **Fase 1 — red de seguridad.** Tiempo máximo en espíritu, revivir en el último punto
  seguro, y un aviso en el banner cuando no hay altar alcanzable.
- **Fase 2 — el altar como dato.** Flag en la plantilla, altar por zona, test sobre datos
  enviados. Retirar el `249` de los dos ficheros.
- **Fase 3 — el bucle.** Brújula al cadáver, cadáver con arte, limpieza de objetos caídos,
  coste de muerte que sobreviva a una recarga.
- **Fase 4 — pulido.** Audio, separar el evento de revivir real del cheat, tests del flujo
  completo.

---

# Reconstrucción (2026-09-07)

## La causa raíz, afinada

El diagnóstico de arriba decía "cero altares colocados". Es **más preciso y peor**: el arco de
piedra **sí estaba colocado**, en `lobby (693, 815)`, con plantilla **197**. El código buscaba la
**249**. Las dos son el mismo sprite, `Buildings/portals/portal_stone_arch`, y solo difieren en
`splitRatio` — y la instancia colocada **sobrescribe su ratio a 0.195, que es justo el de la 249**.
Alguien lo colocó deliberadamente como altar. El vinculador no vinculó nada, el camino no dibujó
nada y `Revive()` no lo llamó nunca nadie, mientras un altar perfectamente correcto estaba ahí de pie.

Ninguna mitad estaba mal. Solo la composición. Misma forma que `SPAWNER_COORDINATE_SPACE_DRIFT`,
y por eso la respuesta es la misma: **una lista, no un id**, y un test sobre los bytes enviados.

## Qué se construyó

| Pieza | Qué resuelve |
|---|---|
| `Data/Death/DeathTuning.cs` | Las 30 decisiones del flujo en un asset. Antes: dos literales `249` a mano, tres `[SerializeField]` en componentes sin inspector alcanzable, una máscara de capas en código y una penalización de XP en un tercer sitio |
| `ResurrectionAltarRegistry` | La única respuesta a "dónde se revive". Retira tres barridos `FindObjectsOfType` y las dos copias del número mágico |
| `DeathSequenceController.Rescue.cs` | **La garantía de que una muerte siempre tiene salida.** Dos relojes: sin altar (corto), con altar inalcanzable (largo) |
| `DeathLitter` | El botín de la muerte anterior deja de acumularse para siempre |
| `DeathStateSave` | Morir + salir + recargar deja de deshacer la muerte |
| `SpiritAltarPathHighlighter` (reescrito) | Ruta real opcional, tope de baldosas, y **la brújula al cadáver**, que no existía |
| `PlayerCorpseMarker` (reescrito) | El cadáver lleva el sprite del propio personaje, no un cuadrado rojo de 8 px |
| `DeathRuntimeEditor` (ESC → Muerte) | El 19º editor. Seis pestañas, estado en vivo, y el botón que arregla el fallo original sin saber ningún número |
| `DevConsole.Commands.Death.cs` | `death`, `altars`, `rescue`, `corpseloot` |
| 3 fixtures nuevas, 31 tests | Incluido el que compara el mundo enviado contra el asset enviado |

## El botón que importa

**Altares → "USAR EL EDIFICIO MÁS CERCANO"**. El fallo nunca fue elegir mal el id: fue que
elegirlo requería saber que existía un número dentro de un `.cs` y a qué edificio se refería.
Ponerse al lado del arco y pulsar una vez lee su plantilla, la añade y revincula el mundo entero.

## Notas después

| # | Eje | Antes | Ahora | Qué cambió |
|---|---|---|---|---|
| 1 | Alcanzabilidad del punto de revivir | 0 | **9** | 197 y 249 en la lista; el mundo enviado tiene 1 altar vinculado, verificado en vivo |
| 2 | Garantía de no-callejón-sin-salida | 0 | **9** | Dos relojes de rescate, cuatro modos, botón manual y comando de consola |
| 3 | Diagnosticabilidad | 2 | **9** | Estado en vivo en el editor, `death`/`altars` en consola, banner que dice la verdad |
| 4 | Configuración como dato | 1 | **9** | Un asset; cero literales de plantilla en código |
| 5 | Guía de navegación | 2 | **8** | Modo ruta real, tope por el extremo lejano, brújula al cadáver en otro color |
| 6 | Mecánicas de espíritu | 5 | **8** | Velocidad ×1.35, atraviesa muros (máscara medida: 24064), límite de tiempo |
| 7 | Cadáver y botín | 3 | **8** | Sprite del personaje, permanencia 120 s, barrido en la muerte siguiente |
| 8 | Coste real / guardado | 2 | **8** | Estado de muerte persistido en el bag de metadatos; recargar ya no lo deshace |
| 9 | Presentación visual | 6 | **8** | Banner con cuenta atrás y mensaje honesto cuando no hay altar |
| 10 | Audio | 0 | **6** | Tres enganches con `HasSfx`; siguen en silencio porque el catálogo no trae los clips |
| 11 | Arquitectura | 7 | **9** | Un dueño por pregunta: registro, tuning, litter, save |
| 12 | Extensibilidad | 2 | **8** | Lista de plantillas, revinculado en caliente, re-arme tras cambio de mundo |
| 13 | Tests | 1 | **8** | 31 tests nuevos; el de composición es el que habría cazado esto |
| 14 | Rendimiento | 5 | **8** | Registro en vez de tres barridos por frame |
| 15 | Permadeath | 3 | **7** | El restaurar espíritu ya no dispara `OnPlayerDied` y por tanto no borra el save recién cargado |
| 16 | Modos e input | 6 | **8** | Sin hotkey, verbos compartidos, sin bindings en C# |
| 17 | Honestidad del cheat | 3 | **9** | `LastReviveKind` da lector real a la distinción; `resurrect` ya no cobra XP |

Media: **8.1 / 10**.

## Verificado en vivo, no deducido

```
altars=1  bound=1                          (era 0)
phase=Spirit  isSpirit=True
trails: altar=46  corpse=52                (eran 0 y no existía)
distinct tints=2 -> 1.0,1.0,0.2 | 1.0,0.4,0.4
excludeLayers=24064                        (NPC+Proyectil+Pickup+World+Building)
grayscale active=True captured=632
rescueEta=71.3                             (contando)
--- tras pisar el altar ---
phase=Alive  lastReviveKind=Altar  hp=200/200
grayscale=False  excludeLayers=0
```

Tres defectos que apareció **la propia verificación** y se arreglaron: el contador de rastro
seguía informando de 70 baldosas después de revivir (un diagnóstico que sobrevive a lo que
describe es peor que ninguno); la brújula al botín moría en el frame en que el jugador se
levantaba — justo al empezar el único paseo para el que existe; y **el asset enviado se creó
ANTES de que ese arreglo moviera el valor por defecto**, así que llevaba `corpseLingerSeconds: 0`
mientras la clase decía 120. El código estaba bien y el dato no había llegado: una brújula que
se declaraba encendida y no podía apuntar a nada. `TheCorpseCompass_OutlivesTheRevive` lo fija
como composición, porque los dos campos son una sola decisión.

Y dos defectos de LAYOUT que solo un frame renderizado podía enseñar, los dos el mismo:
`GUARDAR / DESHACER / REHACER` partidos en dos líneas ("GUARD / AR"), y los cuatro botones del
enum de rescate igual ("NINGUN / O"). uGUI **envuelve** una etiqueta TextMeshPro en vez de
encogerla, así que la respuesta es truncar — una palabra a la que le faltó sitio se lee, dos
mitades apiladas no.

## Lo que sigue abierto

- **Audio**: los tres ids no están en `AudioCatalog.asset`. Es data, no código.
- **Altar por zona**: hoy la lista es global. Un interior sin altar propio depende del rescate.
- **Arte del altar**: el arco funciona pero no se lee como "aquí revives" sin una luz o un aura.
- **PlayMode**: los 31 tests son EditMode. El ciclo completo morir→altar→revivir está verificado
  a mano, no automatizado.
