# Caminar y correr: auditoría y diseño de la skill Carrera

Fecha: 2026-09-14. Estado: **implementado el mismo día** (fases 0-5 y parte de la 6; ver "Estado de la implementación" al final).

Decisiones tomadas con el usuario:

- Se entra en carrera por **impulso automático**: caminar sostenido acumula impulso y se pasa solo a correr. No hay tecla nueva.
- Correr **cuesta resistencia**: se revive el componente `Energy`, hoy inerte.
- La skill **Carrera** (0-100 %) mejora cuatro cosas: arranque más rápido, velocidad de carrera, resistencia y giro sin perder impulso.
- Representación gráfica en **cuatro sitios**: fila nueva en la barra sobre la cabeza del personaje, barra en el panel HUD, marca en el suelo y partículas de carrera.

---

## Parte 1. Auditoría

### 1.1 Assets de locomoción: el arte de carrera existe y el jugador nunca lo reproduce

Todos los personajes jugables tienen arte de carrera propio, importado y enlazado al estado `chase`. Cada carpeta tiene 8 frames por lado, espejados a 8 direcciones.

| Personaje | Caminar | Correr (`chase`) | Sprint | Locomoción en loadouts |
| --- | --- | --- | --- | --- |
| dwarf | `walking` | `running` | `charging_sprint` | `armed`: walk + run |
| barbarian | `armed_walking_2` | `armed_running_2` | – | ninguno (la base ya lleva el hacha) |
| elven | `walking` | `run` (+ `run_punch`) | – | ninguno |
| mague | `walk`, `walk_2`, `walk_3` (variantes) | `run` (+ `run_spellcast`) | – | `armed`: 4 walks + `staff_run` |
| valkyrie | `walk` | `run` | `charging_sprint` | `armed` y `greatsword`: walk + run cada uno |
| vampire | `walking` | `running` (+ `running_attack`) | – | ninguno |

**Hallazgos:**

- **A1. El jugador no corre nunca.** `PlayerController.Movement.cs:586` y `:982` solo eligen `Walk` o `Idle`. A `Chase` solo se llega si un hechizo lo nombra (`TryParseAnimState`, `:729`). Hay 6 personajes y 4 loadouts de arte de carrera que nadie ve.
- **A2. El ritmo de la animación no depende de la velocidad.** Los frames avanzan a `frameInterval` fijo (0,15 s) sea cual sea la velocidad. La velocidad base va de 4 (dwarf) a 7 (valkyrie, vampire), así que los pies patinan distinto en cada clase. Al añadir un multiplicador de carrera patinarían mucho más.
- **A3. Hay carreras preparadas y sin enviar:**
  - barbarian: `barbarian_walking` / `barbarian_running` sin arma. Es el loadout desarmado, retenido para que el hacha no aparezca y desaparezca.
  - elven: `elf_archer_*` y `elf_bard_*` (idle/walk/run). Se retuvieron porque "necesitan un sistema de loadouts", y ese sistema ya existe.
  - vampire: `vampire_running_3`, una segunda toma de carrera.
  - `staging/` también guarda arte viejo de la primera ola (`*_run_8f*`) que ya no aplica.
- **A4. El monstruo sí corre**, porque `ChaseState`, `AlertChaseState` y `DodgeState` ponen `Chase`. `red_dragon` no tiene arte de carrera y usa la caminata de reserva. Eso está bien, pero su ritmo tampoco depende de la velocidad.

### 1.2 Código de movimiento

- **B1. La velocidad es plana e instantánea.** Se aplica `_rb.velocity = input * CurrentMoveSpeed` en cada `FixedUpdate` (`Movement.cs:184-186`), sin aceleración ni frenada.
- **B2. `IsMoving` mira el INPUT, no el desplazamiento** (`PlayerController.cs:94`). Un jugador empujando contra una pared "se mueve". Para impulso y ganancia de skill hace falta desplazamiento real.
- **B3. No existe sprint ni resistencia.** Tampoco hay acción de input libre para eso: el Shift izquierdo es el modificador de acordes y el derecho, los Ctrl y Espacio son el Dash.
- **B4. `Energy.cs` es inerte.** Su doc dice "consumed by sprinting", pero **ningún código lo añade al jugador**. `ItemConsumer` lo busca con `GetComponent` y siempre recibe null, así que la comida con `energy` no hace nada aunque la ficha del ítem lo anuncie ("+N de energía").
- **B5. `DashAbility` es código muerto que el HUD sigue leyendo.** `TryDash` no tiene llamadores, porque el dash es el hechizo `dash`. Aun así `HudDashPip` y `WorldDashBar` leen su `CanDash` y su cooldown, así que el pip del dash **no muestra el cooldown real**. Está deducido del código y no se ha medido en vivo. `IsDashing` sigue bloqueando el movimiento en `Movement.cs:162`.
- **B6. Hay campos autorados que nadie lee:** `PlayerDefinition.dashCharges` y `maxDexterity` (este último solo es una etiqueta en el selector de clase).
- **B7. `SlowEffect` modifica `Rigidbody2D.drag`**, pero la velocidad del jugador se sobrescribe en cada paso. Muy probablemente la ralentización no afecta al jugador. Hay que medirlo.
- **Reutilizable:**
  - `FootstepEmitter` + `FootstepDust` ya están en el jugador y en los monstruos. Emiten por distancia (zancada 0,62 u) y detectan el tipo de suelo.
  - `NoiseEvents.LoudnessFootstep`.
  - `DirectionalAnimator.FrameIntervalFor` compone tres multiplicadores de ritmo (entidad × estado × variante).
  - `WorldBarRig` ya pinta filas de vida, maná y el pip del dash.

### 1.3 Sistema de skills

- `SkillCategory` solo tiene `Gathering` y `Crafting`. `SkillsHUD` pinta esas dos categorías fijas (`SkillsHUD.cs:192-193`) y el detalle usa ternarios de dos ramas (`SkillsHUD.Detail.cs:41, 61-75, 104`). Una tercera categoría no se vería o saldría etiquetada "Recolección".
- `SkillAvailability.IsTrainable` solo da por viva una skill si tiene nodos o recetas, así que Carrera saldría "pronto".
- `PlayerSkills.TryGain(def, difficulty, wrongTool)` guarda en décimas y se persiste en `gatheringSkillKeys/Tenths`. **Una clave nueva no necesita cambiar el save.**
- `SkillFeedback` junta ganancias en 1,1 s y fuerza el vaciado si llega otra clave. Si las ganancias de carrera se intercalan con las de tala, se partirían los toasts.

---

## Parte 2. Diseño

### 2.1 Modelo: el paso (gait) como máquina pura

`LocomotionGait` (Gameplay/Player, clase C# pura sin MonoBehaviour, testeable como `MarketCycle`):

```text
Idle ──mover──► Walk ──impulso ≥ 1──► Run
  ▲               ▲  ◄──ruptura──────┘
  │               │
  └── Winded ◄────┴── energía agotada (no puede acumular impulso hasta recuperar el umbral)
```

- **Impulso (0..1).** Sube mientras hay desplazamiento REAL (velocidad medida ≥ 70 % de la pedida) y el rumbo no gira más que la tolerancia. La tasa es `1 / tiempoDeArranque(skill)`.
- **Qué rompe el impulso:**
  - **parar**: input nulo durante más de 0,12 s de gracia; un toque de tecla no debe resetear.
  - **giro brusco**: un cambio de rumbo mayor que `toleranciaGiro(skill)` en una ventana corta. Un giro suave solo resta impulso en proporción; un giro de 180° lo pone a 0.
  - **lanzar un hechizo que planta** (`IsPlantedByCast`), **recibir un golpe** (`Health.OnDamaged`), **stun o root**.
  - **chocar**: el desplazamiento real cae por debajo del umbral, por muro o por `ClampInputAgainstVoid`.
  - **energía a 0**, que además pasa a `Winded`.
- **Velocidad resultante:** `base × lerp(1, multCarrera(skill), curvaEaseInOut(blend))`. `blend` tarda unos 0,25 s en llegar a 1 al entrar en Run y en volver al salir, así que no hay escalón de velocidad.
- **Aceleración:** solo la entrada y salida de carrera se suaviza. Caminar sigue siendo instantáneo, porque la respuesta inmediata al input es parte del tacto actual y no se toca.
- **El dash no rompe la carrera.** Tampoco la usa: el dash no cuesta energía en esta fase.
- **Modo espíritu:** no se corre, `DeathTuning` ya define la velocidad del espectro.
- **Postura Paz/Guerra:** no afecta, porque correr no es combate. Pasa por el mismo lado del gate que `PollTraversal`.

### 2.2 Resistencia: revivir `Energy`

- `EntitySetup` añade `Energy` al jugador. Así la comida con `energy` empieza a funcionar, y **eso es un cambio de balance** que se señala en el PR.
- `Energy` es un par de enteros. Se añade un acumulador float interno para drenar a tasa continua sin cambiar su API pública (`TrySpend` / `Restore` siguen enteros).
- El máximo pasa a ser un **stat**: `StatKind.MaxEnergy`, con consumidor `Energy.SetMax` en `PlayerStats.Consumers.cs`, que es lo que exige `PlayerStatsWiringTests`. Base por clase, sembrada desde `PlayerDefinition`. Equipamiento y talentos pueden subirlo más adelante sin código.
- **Tasas** (valores a 0 % → 100 % de skill, en `LocomotionTuning`):
  - drenaje corriendo: 9/s → 4/s
  - regeneración caminando: 6/s → 10/s
  - regeneración parado: 14/s → 22/s
  - retardo de regeneración tras correr: 0,8 s → 0,4 s
- **Winded** entra con energía a 0 y sale al llegar al 35 % del máximo. Mientras dura, se camina normal y no se acumula impulso.
- Con máximo 100 y skill 0, se corren unos 11 s seguidos; a 100 %, unos 25 s.

### 2.3 La skill Carrera

- **Asset:** `GS_athletics`, clave estable `athletics`, nombre "Carrera". Va en la categoría nueva `SkillCategory.Physical = 2` ("Físico"), que se añade al final del enum para no renumerar.
- **Siembra:** entrada nueva en el `Roster` de `SkillContentSeeder`, con curva de ganancia propia al crearse.
- **Disponibilidad:** `SkillAvailability.IsTrainable` devuelve true para `Physical`. Se deriva de la categoría, no de un flag.
- **Qué cuenta como entrenar:** cada `N` unidades de **desplazamiento real en estado Run** (propuesta: 6 u) se hace una tirada `TryGain`.
  - La dificultad es el propio porcentaje actual, así la banda vale 1 y solo actúa la curva de decaimiento `(1 - s/110)^2`.
  - Correr contra un muro no entrena, porque cuenta desplazamiento y no input.
  - Correr en círculos sí entrena; es aceptable, igual que talar el mismo árbol.
  - Se aplica un **tope blando por minuto** para que dejar el personaje corriendo con un macro no sea la ruta óptima.
- **Objetivo de ritmo:** unas 0,5 h de carrera para llegar al 50 % y unas 5 h para el 100 %, parecido a tala. Se fija con una simulación pura en el test, igual que `WoodcuttingDataTests`, y se ajusta la definición, no el test.
- **Qué mejora, interpolado con `Ease` de 0 a 100 %** (todo en `LocomotionTuning`):

| Efecto | 0 % | 100 % |
| --- | --- | --- |
| Tiempo de arranque (caminar hasta correr) | 1,6 s (~3 pasos) | 0,5 s (~1 paso) |
| Multiplicador de carrera | x1,35 | x1,75 |
| Drenaje / regeneración | ver 2.2 | ver 2.2 |
| Tolerancia de giro sin pérdida | 25° | 70° |

- **Toasts:** `SkillFeedback` pasa a llevar una ventana de agrupado por clave, para que "+0,1 % Carrera" no parta los toasts de tala. Los hitos cada 10 % se mantienen.
- **UI de skills:**
  - tercer grupo "Físico" en `SkillsHUD.BuildTable`
  - rama `BuildPhysical` en el detalle, que muestra los valores **actuales** (arranque 1,1 s · carrera x1,52 · aguante 14 s) y los del siguiente hito
  - los ternarios de categoría pasan a un switch
  - hay que verificar que la tabla sigue cabiendo sin scroll con 7 filas

### 2.4 Animación: correr sin patinar

1. **Selección de estado.** `UpdateFacingDirection` elige `Chase` cuando el gait está en `Run` (con `blend > 0,5`) y `Walk` en otro caso. Chase ya está en la lista de estados que la locomoción puede sobrescribir, así que no hay estado nuevo ni se tocan las whitelists.
2. **Continuidad de fase.** Hoy `SetState` reinicia el índice de frame al cambiar de estado, y pasar de Walk a Chase haría "saltar" el pie. Se añade `SetState(..., preservePhase: true)`, que traduce el índice proporcionalmente (frame 4/8 de walk pasa a 4/8 de run). Los ciclos comparten pie de apoyo porque ambos saltan el frame 0; se verifica a ojo por personaje.
3. **Ritmo ligado a la velocidad.** Se añade un multiplicador de runtime `LocomotionRate` dentro de `FrameIntervalFor`, compuesto con los tres existentes y **sin sobrescribir** `statePacing`:
   - `LocomotionRate = velocidadReal / velocidadReferencia(estado)`, limitado a 0,7..1,5.
   - `velocidadReferencia` es la velocidad a la que el ciclo autorado no patina. Se guarda por estado en `EntityAssetConfig` (`locomotionReference`: walk, chase) y, si falta, vale la `basicSpeed` de la clase para walk y `basicSpeed × 1,5` para chase.
   - **Se mide, no se adivina:** herramienta Python `tools/atlas/measure_stride.py` que, a partir de los frames de apoyo del pie en el manifest, calcula la zancada en unidades por ciclo y propone la referencia. Se revisa a ojo en el panel Animation del Entities Editor.
4. **Monstruos, gratis:** el mismo `LocomotionRate` aplicado desde `FSMMonsterBrain` con la velocidad real arregla el patinaje de `red_dragon` y del resto. Es opcional y va en su propia fase.

### 2.5 Representación gráfica

Regla del proyecto: **un estado se lee, un evento se anuncia.** La barra lee la energía; la marca del suelo y las partículas anuncian eventos (arranque, entrar en carrera, agotarse). Nada late en reposo.

#### 2.5.1 Barra sobre la cabeza (`WorldBarRig`)

- Fila nueva **Energía** bajo la fila de recurso (maná + pip del dash). Usa `WorldBarRow.Resource` a 2 texels de alto y comparte el contorno con la fila de encima (`rowGapTexels = -1`), igual que vida y maná.
- Color ámbar-verdoso, distinto del maná azul **y en forma**: lleva muescas cada 25 %, que el maná no tiene. Así un jugador daltónico no depende del color.
- `SORT_STAMINA` entre `SORT_RESOURCE` y `SORT_PIP`, con `SORT_SPAN` derivado. `WorldBarStackTests` ya comprueba que todo cabe en el span.
- **Visibilidad:** solo el jugador. La fila entra en la regla de "algo que reportar" mientras la energía no está llena. Con la energía llena y sin moverse, la barra se desvanece con el resto (regla idle ya existente).
- **Eventos:**
  - pulso de chip al drenar en carrera
  - destello y "clic" de muesca al cruzar el 35 % de salida de Winded
  - temblor corto de la fila al agotarse
- **Impulso:** no tiene fila propia, porque se leería como una segunda barra de recurso. Lo muestra la marca del suelo (2.5.3).
- **Driver:** `WorldEnergyBar`, que sigue el patrón de `WorldDashBar` (Awake → `WorldBarRig.Ensure` → `EnableStamina`; Update → `SetStamina`).

#### 2.5.2 Barra en el panel HUD (abajo a la izquierda)

- `HudBar` nueva bajo el maná, con icono propio: una bota de 7x7 generada en `HudArt`, porque la regla es forma y color.
- Mismos colores y muescas que la fila del mundo, para que el jugador aprenda un solo código.
- `HudBar.SetValue(cur, max, change)` ya da el chip, el destello y el heartbeat. Se usa el heartbeat en Winded, **solo en el jugador**.
- Espacio: sumar `TopRowTexels` en `PlayerHudStyle` y re-sincronizar el `.asset`, porque un ScriptableObject conserva los valores con los que se creó.

#### 2.5.3 Marca en el suelo (`LocomotionGroundMark`)

- Objeto propio en `Combat/WorldUI/`. **No se toca `FacingIndicator`**, que tiene un solo trabajo y un guard de fuente que lo impide.
- Un **arco partido en segmentos** bajo los pies, aplastado con un `GroundPlane` padre como el resto de decals de suelo, que se llena con el impulso mientras caminas.
  - Número de segmentos = pasos de arranque según la skill (3 a 0 %, 1 a 100 %), de modo que la marca **enseña** la skill.
  - Cada segmento se enciende al completarse una zancada (evento, sincronizado con `FootstepEmitter`).
- **Al entrar en Run:** el arco se cierra en anillo, destella y se desvanece en 0,3 s. Corriendo no hay marca, porque el estado estable no debe pedir lectura.
- **Al romperse el impulso** con segmentos encendidos, se apagan en cascada inversa (0,15 s) y se aprende qué lo rompió.
- En Winded no aparece el arco: no hay impulso que mostrar.
- Blanco, aditivo y tenue, del mismo lenguaje que el chevrón. El orden de sorting se deriva del Y de los pies, detrás del cuerpo.

#### 2.5.4 Partículas de carrera

Todo por eventos, reutilizando piezas existentes:

- **Pisada de carrera:** `FootstepEmitter` recibe el gait. En Run la zancada pasa de 0,62 a unos 0,95 u, y `FootstepDust.Spawn` acepta `scaleMul`/`lifeMul` (x1,4) y un **empuje hacia atrás** opuesto a la velocidad. Nieve y agua heredan su look por `GroundProbe`.
- **Arranque:** una ráfaga de polvo en abanico detrás de los pies el frame en que se entra en Run (8-12 puffs, reutilizando `FootstepDust`).
- **Velocidad punta:** cuando la carrera ha llevado `blend = 1` durante más de 1,5 s, 1-2 **líneas de velocidad** aditivas muy tenues por segundo a los lados del cuerpo, en espacio de mundo y alineadas con la velocidad (versión mínima de `DashStreakFX`). Técnicamente es un estado. Se justifica solo si pasa la prueba de "no hay que mirarlo para saber algo"; si en captura compite con el combate, se corta.
- **Agotado:** 2-3 bocanadas de aliento sobre la cabeza al entrar en Winded, que también es un evento. Con frío (nieve) se ven blancas; con calor, gotas.
- **Giro brusco que rompe carrera:** derrape de polvo lateral en el lado exterior del giro.

#### 2.5.5 Sonido y sigilo

- Correr multiplica `NoiseEvents.LoudnessFootstep` (x1,8). Correr hacia un campamento lo despierta antes, y caminar es la herramienta de sigilo. Las piezas ya existen.
- Sonido: pisada más pesada y respiración al agotarse, con id de catálogo protegido por `HasSfx` y respaldo sintetizado (patrón `InventoryAudio`). Es opcional y va en la última fase.

### 2.6 Datos y dueños

```text
LocomotionTuning        Data/Player/        Resources/Player/LocomotionTuning.asset — cada número de 2.1-2.4 a 0 % y 100 %
LocomotionGait          Gameplay/Player/    máquina pura: impulso, estado, velocidad, rupturas
PlayerController.Locomotion.cs              la integración: lee gait, escribe velocidad, elige Walk/Chase
Energy (+ acumulador)   Gameplay/Player/    resistencia; StatKind.MaxEnergy
StatKind.MaxEnergy      Data/Progression/   + consumidor en PlayerStats.Consumers
SkillCategory.Physical  Data/Skills/        + GS_athletics sembrado
WorldEnergyBar          Combat/WorldUI/     driver de la fila de energía
LocomotionGroundMark    Combat/WorldUI/     el arco del impulso
FootstepEmitter/Dust    World/Ambience/     parámetro de paso
run                     DevConsole          sonda: gait, impulso, energía, skill, velocidad real vs referencia
```

- `LocomotionTuning` va bajo `Resources/` por la misma razón que `DeathTuning`: lo leen componentes añadidos con `AddComponent` que no tienen slot de inspector.
- **Gameplay no referencia UI:** la barra del panel HUD (`Valkur.UI`) lee `Energy` y el gait a través de `PlayerController`, como ya hace con maná y slots.

---

## Parte 3. Fases de implementación

| Fase | Contenido | Tests clave |
| --- | --- | --- |
| **0. Deudas** | Pip del dash: leer el cooldown del hechizo `dash` en vez de `DashAbility` (B5); medir `SlowEffect` en el jugador (B7); `IsMoving` real vs input (B2) sin cambiar su contrato actual | `HudDashPip` refleja cooldown del libro |
| **1. Núcleo** | `LocomotionTuning`, `LocomotionGait` puro, integración en el controller, `Energy` en el jugador + `MaxEnergy` + acumulador, Walk↔Chase con fase preservada, sonda `run` | Gait: arranque, 5 rupturas, Winded con histéresis; velocidad sin escalón; `PlayerStatsWiringTests` verde; energía drena/regenera |
| **2. Ritmo de animación** | `LocomotionRate` en `FrameIntervalFor`, `locomotionReference` por estado, `measure_stride.py`, referencias medidas para los 6 personajes y sus loadouts | Rate compone con los otros 3 diales; límites; referencia por defecto |
| **3. Skill Carrera** | `SkillCategory.Physical`, seeder, `IsTrainable`, ganancia por distancia real con tope, `SkillFeedback` por clave, `SkillsHUD` grupo + detalle | Simulación 0→50 % ≈ 0,5 h y 0→100 % ≈ 5 h; muro no entrena; save ida y vuelta de la clave nueva |
| **4. Barras** | Fila de energía en `WorldBarRig` + driver, `HudBar` en el panel + icono, estilo re-sincronizado | Sorting dentro del span; visibilidad solo jugador; contraste ≥ 3:1 (`WorldBarContrastTests`); captura en vivo |
| **5. Marca y partículas** | `LocomotionGroundMark`, pisada de carrera, ráfaga de arranque, aliento, derrape, líneas de velocidad (candidatas a corte) | Nada emite en reposo; segmentos = pasos de arranque; capturas a `timeScale` bajo |
| **6. Assets y extras** | Enviar carreras retenidas (barbarian desarmado como loadout, elf archer/bard como loadouts, `vampire_running_3` como variante de chase), `LocomotionRate` para monstruos, ruido de carrera, audio | Rigs de loadout; datos enviados leídos de disco |

Cada fase cierra con consola MCP limpia y el suite EditMode verde, según las reglas del repo.

---

## Parte 4. Preguntas abiertas

1. **¿El dash debe costar energía?** Uniría las dos mecánicas de desplazamiento en un solo recurso, pero cambia el balance del combate. Propuesta: no en esta iteración.
2. **¿Caminar deliberado?** Para quien quiera sigilo sin perder el automático: una acción asignable "Mantener paso", enviada **sin binding**, igual que los toggles de editor retirados. Propuesta: fase 6, opcional.
3. **Interiores:** ¿se corre dentro de las casas? Propuesta: sí; el espacio corto ya limita el impulso de forma natural.
4. **Carrera en combate:** ¿se permite acumular impulso con enemigos cerca? Propuesta: sí; las rupturas por golpe y por lanzar hechizos ya lo regulan.
5. **Clases:** ¿máximo de energía y referencia de carrera distintos por clase (valkyrie ágil, dwarf tanque)? Propuesta: sí, vía base de `MaxEnergy` por `PlayerDefinition`.

---

## Estado de la implementación (2026-09-14)

**Hecho:**

- Fase 0: `HudDashPip` y `WorldDashBar` leen el cooldown del hechizo `dash` del libro (B5). `IsMoving` conserva su contrato; el impulso usa desplazamiento medido (B2).
- Fase 1: `LocomotionTuning` (`Resources/Skills/`), `LocomotionGait` puro, `PlayerController.Locomotion.cs`, `Energy` revivido con `StatKind.MaxEnergy` (base desde `maxDexterity`), Walk↔Chase con fase conservada, sonda `run`.
- Fase 2: cuarto dial de ritmo (`LocomotionRate`) en `DirectionalAnimator`, también en monstruos desde `FSMMonsterBrain`. Campos `walkReferenceSpeed`/`runReferenceSpeed` en `EntityAssetConfig` y `tools/atlas/measure_stride.py`.
- Fase 3: `GS_athletics` (categoría `Physical`), siempre entrenable, ganancia por distancia real con tope por minuto, grupo "Físico" y detalle en `SkillsHUD`, `SkillFeedback` agrupa por clave.
- Fase 4: fila de energía en `WorldBarRig` (`WorldEnergyBar`) y barra con icono de bota en el panel HUD.
- Fase 5: `LocomotionGroundMark` (arco de impulso), `RunFx` (ráfaga de arranque, derrape, aliento, líneas de velocidad), pisada de carrera más larga y `NoiseEvents` al correr.

**Medido y no aplicado:** las velocidades de referencia de `measure_stride.py` no son fiables en el arte actual (dwarf caminar 0,34 u/s frente a velocidad 4; vampiro corre más lento de lo que camina). Se usan las referencias derivadas.

**No hecho, y por qué:**

- `vampire_running_3`: se retuvo por calidad (la otra toma tiene mejor zancada y márgenes), no por falta de sistema.
- Loadouts desarmado del barbarian y archer/bard del elf: son loadouts de arma, no locomoción. Exigen hechizos de alternancia, acciones de input en `ValkurInputActions` y descriptores en `InputActionCatalog`, ficheros que editan otras sesiones a la vez. Queda como trabajo propio.
- Preguntas abiertas 1-5: sin cambios (el dash no cuesta energía; sin acción "Mantener paso").
