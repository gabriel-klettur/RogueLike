# Auditoría del sistema de muerte y resurrección

> Fecha: 2026-09-07 · Medido en Play Mode sobre la sesión viva, no inferido del código.
> Nota global: **2.8 / 10**.

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
