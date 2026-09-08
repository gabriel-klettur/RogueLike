# Entorno de entrenamiento de IA para PvM — estudio de viabilidad

> Fecha: 2026-09-07. Medido contra el árbol de trabajo actual, no contra la documentación.
> Objetivo del encargo: un entorno aparte donde probar PvM, entrenar IA que aprenda a jugar,
> y producir **players simulados y monstruos simulados por niveles de experiencia**, entrenados
> contra sí mismos y contra jugadores reales.

## 0. Veredicto en una frase

Es viable, y el trabajo duro **no es el aprendizaje automático**: es que Valkur no tiene una
separación entre simulación y presentación, ni una capa de intención entre el input y el
`PlayerController`. Con esas dos cosas resueltas, el resto (self-play, tiers, ladder) es
maquinaria conocida. Sin ellas, cualquier framework de RL que se enchufe encima entrena contra
un simulador que no se puede reproducir dos veces igual.

La buena noticia, y es el hallazgo que cambia el presupuesto: **la superficie de aleatoriedad
que decide combates es de ~12 llamadas, no de 370.**

---

## 1. Lo que se midió

| Hecho | Medida | Dónde |
|---|---|---|
| ML-Agents instalado | **No** | `unity/Valkur/Packages/manifest.json` |
| Llamadas a `UnityEngine.Random` totales | 370 | `Scripts/**` |
| ...de las cuales **deciden combate** | **~12** | ver 1.1 |
| ...cosméticas (VFX, visuales de hechizo) | resto, 48 ficheros solo en `Spells/Visuals` | |
| `Time.deltaTime` / `Time.time` | 437 sitios | |
| Corrutinas / `IEnumerator` | 133 | |
| `WaitForSeconds` | 32 | no son inyectables por dt |
| Consultas `Physics2D.*` | 85 | |
| Tests EditMode / PlayMode | 742 / 17 ficheros | `Assets/Tests/` |
| `Time.timeScale` leído por | 23 sitios | |

### 1.1 La superficie de RNG que importa

Todo lo demás es adorno. Estas son las que cambian quién gana:

```text
Combat/Mechanics/CritResolver.cs                  Random.value >= chance       crítico sí/no
Combat/StatusEffects/StatusApplicationFactory.cs  Random.value > app.chance    aplica estado sí/no
Enemies/FSM/FSMDodge.cs                           Random.value > chance        esquiva sí/no
Enemies/FSM/FSMDodge.cs                           Random.value < 0.5f          flanco izq/der
Enemies/FSM/StateMachine.cs                       Random.value >= stopProb     transición autorizada
Enemies/FSM/States/AttackState.cs                 Random.Range(0, totalWeight) qué variante de ataque
Enemies/FSM/States/StrollState.cs                 3 sitios                     deambular ambiental
Enemies/NPCAutoCast.cs                            3 sitios                     jitter de periodo
Enemies/MonsterSpawner.cs                         Random.insideUnitCircle      posición de aparición
Combat/Lifecycle/SpawnStabilizer.cs               Random.insideUnitCircle      empuje anti-solapamiento
Spells/Executors/DashExecutor.Leap.cs             Random.insideUnitCircle      dispersión del salto
```

Once sitios sustituibles por un `SimRandom` sembrado. Ese es el coste real del determinismo,
no una reescritura del proyecto.

### 1.2 Lo que YA está listo y no hay que construir

- **`StateMachine.Update(float dt)`** ya recibe el dt por parámetro. El cerebro del monstruo es
  paso-inyectable hoy mismo.
- **`Health` ya emite la señal de recompensa atribuida**: `OnDamagedBy(int, GameObject)`,
  `OnDamaged`, `OnDeath`, `OnDamageBlocked`. Un agente sabe cuánto daño hizo y a quién sin
  instrumentar nada.
- **`SpellCaster` ya expone la acción programáticamente**: `TryCastByKey(key, dir)`,
  `BeginCharge`, `ReleaseCharge`, `CurrentPhase`. Un agente no necesita sintetizar teclas.
- **`DevConsole.Execute(string)` es público** — superficie de control ya alcanzable desde tests
  y desde `execute_code`.
- **`IProfileDb`** ya tiene el patrón de repositorio para historial de runs; grabar trazas de
  jugador reutiliza esa forma en vez de inventar otra.

### 1.3 Los tres bloqueadores reales

**B1 — El jugador no tiene capa de intención.** `PlayerController.Update` lee
`InputBindingResolver` / `MouseInputManager` **en línea**, dentro de `PollCombatActions`,
`PollTraversal` y `ReadInput`. No hay ningún punto donde un agente pueda decir "quiero moverme
al noreste y lanzar el slot 3". Las dos salidas malas son sintetizar eventos del InputSystem
(frágil, y este proyecto ya documenta el bug de pérdida de eventos de 2022.3) o duplicar la
lógica de combate en el agente (dos implementaciones que divergen). La salida buena es 3.1.

**B2 — El tick del monstruo se recorta y se apaga.** `FSMMonsterBrain.Update` hace
`Mathf.Min(_pendingDt, MaxCatchUpSeconds)` con `MaxCatchUpSeconds = 0.5f`, y antes de eso
consulta `EntityCulling.ShouldUpdate`, que responde a **visibilidad de cámara**. En un entorno
sin cámara y a `timeScale` alto, los monstruos o no piensan o piensan a un octavo de velocidad.
Ambas cosas son correctas en el juego y letales en el entrenamiento.

**B3 — 32 `WaitForSeconds` y 133 corrutinas.** Una corrutina avanza con el frame real, no con un
dt inyectado. Bajo paso fijo funcionan; bajo `timeScale` variable o bajo simulación manual de
física, no. Hay que inventariarlas y decidir cuáles están en el camino de la simulación.

---

## 2. La decisión que casi todo el mundo se salta

**El aprendizaje por refuerzo optimiza para GANAR, no para ser de un nivel concreto.**

El encargo pide "IAs por niveles de experiencia". Un agente entrenado a tope y luego debilitado
con ruido aleatorio no juega como un novato: juega como un experto con un tic nervioso. Comete
errores en los momentos equivocados. Un jugador humano lo detecta en treinta segundos.

Las tres respuestas conocidas, en orden de calidad:

1. **Instantáneas de la curva de entrenamiento (recomendada).** Durante el self-play se congela
   un checkpoint cada N pasos. El checkpoint del 5 % del entrenamiento *es* un novato genuino:
   se posiciona mal, malgasta maná, persigue de más — errores de novato, no ruido. Sale gratis
   del entrenamiento que ya se está haciendo. El único trabajo extra es medir su Elo para
   colocarlo en el escalón correcto.
2. **Restricción de recursos.** Entrenar agentes distintos con hechizos capados, cooldowns
   inflados o retraso de reacción mayor. Da niveles honestos pero cuesta un entrenamiento por
   nivel.
3. **Ruido / retraso / tope de APM sobre un agente experto.** El más barato y el que peor se
   siente. Sirve como capa fina *encima* de (1), no como sustituto.

Y una nota que vale para las tres: **el realismo humano no es el nivel de habilidad.** Un agente
que observa el estado del mundo en el mismo frame en el que actúa reacciona en 0 ms y se siente
inhumano incluso jugando mal. Hace falta un búfer de retraso de observación (100-250 ms) y un
tope de acciones por segundo, aplicado a **todos** los niveles.

---

## 3. Arquitectura propuesta

### 3.1 Capa de intención — `PlayerIntent` (el cambio que desbloquea todo)

```csharp
// Scripts/Core/Input/PlayerIntent.cs — Valkur.Core
public struct PlayerIntent
{
    public Vector2 Move;          // normalizado, magnitud 0..1
    public Vector2 Aim;           // dirección en el mundo, no posición de ratón
    public bool    PrimaryHeld;   // el mantener, no el flanco
    public bool    SecondaryPressed;
    public bool    MiddleHeld;
    public bool    DashPressed;
    public int     SpellSlot;     // -1 = ninguno, 0..23
    public bool    SpellHeld;     // para los cargables
    public bool    StanceToggle;
}

public interface IIntentSource { PlayerIntent Read(); }
```

Dos implementaciones:

- **`HumanIntentSource`** — el ÚNICO sitio del proyecto que lee `InputBindingResolver`,
  `MouseInputManager` y `InputContextPolicy` para el jugador. Esto **refuerza** la regla cardinal
  del pipeline de input en lugar de romperla: hoy esas lecturas están esparcidas por 1090 líneas
  de `PlayerController.Movement.cs`; después están en un fichero.
- **`AgentIntentSource`** — lo rellena una política. Cero conocimiento de teclas.

`PollCombatActions` deja de preguntar "está pulsado el botón" y pregunta "la intención dice
primario". La compuerta de postura (`PlayerStance`), `InputContextPolicy.IsLive` y las tres
salidas anticipadas se quedan **exactamente donde están** — son reglas del juego, no del input,
y `StanceGateTests` sigue pinchando el mismo orden.

Beneficio lateral que justifica el refactor por sí solo: **grabar la intención de un jugador real
cada tick es la traza de imitación.** Sin esta capa no hay dataset humano; con ella sale gratis.

Riesgo declarado: `BindingConstructionGuardTests` e `InputCentralizationGuardTests` tendrán que
aceptar `HumanIntentSource` como sitio legítimo, igual que aceptan los cuatro helpers del núcleo.

### 3.2 El entorno — `Valkur.Sim` (asmdef nuevo)

`Scripts/Sim/`, referencia a Core + Data + Infrastructure + Gameplay. **Nadie referencia Sim**,
así que no puede contaminar el juego.

```text
Sim/Arena/     ArenaBuilder      construye un recinto sin tilemap, sin luces, sin VFX
               ArenaSpec         semilla, tamaño, roster de monstruos, clase del jugador, nivel
               ArenaEpisode      reset / step / done / métricas
Sim/Clock/     SimClock          paso fijo, física manual, sin Time.timeScale
Sim/Random/    SimRandom         el reemplazo sembrado de los 11 sitios de 1.1
Sim/Obs/       ObservationBuilder   estado del mundo -> vector
Sim/Act/       ActionDecoder        vector -> PlayerIntent
Sim/Policy/    IAgentPolicy      Decide(obs) -> action. Transporte-agnóstico
               ScriptedPolicy    los tiers heurísticos de la fase 2
               RemotePolicy      el puente a Python
Sim/Record/    TraceRecorder     (obs, intent, reward) de jugadores REALES y de agentes
Sim/Eval/      Ladder            Elo / TrueSkill, matriz de tasas de victoria
```

**`IAgentPolicy` es la pieza clave de diseño**: el entorno no sabe si detrás hay una heurística
en C#, una red en Python o un humano grabado. Eso es lo que permite empezar sin Python y añadirlo
después sin rehacer el entorno — y lo que permite que la fase 2 tenga valor aunque la fase 3
nunca se haga.

### 3.3 Determinismo — el contrato

| Fuente de deriva | Respuesta |
|---|---|
| RNG | `SimRandom` sembrado por episodio; los 11 sitios de 1.1 lo consultan. Los cosméticos se quedan como están (no se ejecutan: sin renderizado) |
| dt variable | `Physics2D.simulationMode = Script`, `Physics2D.Simulate(FIXED_DT)` manual desde `SimClock`. Sin `Time.timeScale` — el timeScale no acelera, solo distorsiona |
| Recorte de catch-up | `FSMMonsterBrain` necesita un modo sim que reciba el dt en vez de leer `Time.deltaTime` |
| `EntityCulling` | forzar `_forcedActive` en la arena, o el monstruo no piensa sin cámara |
| Corrutinas | inventariar las 32 `WaitForSeconds`; las del camino de simulación pasan a temporizadores por dt |
| Orden de iteración | nada de `GetComponentsInChildren` sin ordenar en el camino de decisión |

Prueba de aceptación, y es la única que vale: **el mismo `ArenaSpec` con la misma secuencia de
acciones produce el mismo hash de estado final, 1000 veces.** Un test PlayMode que falla el día
que alguien mete un `Random.value` en el camino de daño.

### 3.4 Observación y acción

**Observación — egocéntrica y relativa.** Coordenadas absolutas hacen que el agente memorice el
mapa en vez de aprender a combatir.

```text
Propio (~20 flotantes)   hp%, maná%, postura, dash listo, fase de casteo,
                         cooldown por slot conocido (normalizado 0..1),
                         velocidad relativa a la propia mirada
Entidades (K=6 más       por cada una: distancia (log), ángulo relativo (sin/cos),
cercanas, ~14 c/u)       velocidad relativa, hp%, está casteando, fase de telegrafía,
                         embedding de tipo (one-hot corto), es aliado
Geometría (16 rayos)     distancia a pared, abanico de 360° — esto es lo que enseña a no
                         acorralarse, y no sale de ninguna otra parte
```

**Acción — multi-discreta, no continua pura.**

```text
Movimiento   9 ramas   8 direcciones + quieto
Mirada       16 ramas  sectores de 22.5°  (continuo aquí no aporta: el juego no premia
                       precisión sub-grado, y discreto entrena mucho más rápido)
Hechizo      K+3 ramas ninguno / primario / secundario / medio / slot 0..K-1
Dash         2 ramas
```

**Frecuencia de decisión: 10 Hz, no por frame.** Con física a 50 Hz eso es repetición de acción
x5. Tres razones y las tres importan: eficiencia de muestreo (episodios 5x más cortos en pasos),
estabilidad del gradiente, y **que el agente se sienta humano** — 60 decisiones por segundo es
inalcanzable para una persona y se nota al jugar contra ello.

### 3.5 Recompensa

```text
+ daño hecho / hp_max_del_objetivo        la señal densa que hace que aprenda algo
- daño recibido / hp_max_propio  x 1.5    asimétrico: sobrevivir vale más que matar
+ 1.0  por muerte del enemigo
- 1.0  por muerte propia
- 0.001 por paso                          empuja a terminar; sin esto, orbitar es óptimo
- pequeño castigo por maná gastado sin impacto
```

Trampas conocidas que hay que cerrar desde el principio:

- **Sin recompensa por daño hecho, el agente aprende a huir** y el episodio expira empatado.
- **Con castigo por paso demasiado alto, el agente aprende a suicidarse** para cortar pérdidas.
- **El agente encontrará bugs del motor y los explotará.** Esto es una *ventaja*: será el mejor
  cazador de exploits que este proyecto haya tenido. Este repositorio ya documenta una decena de
  defectos del tipo "cada número era coherente y solo discrepaba con la pantalla" — un agente
  optimizador los encuentra en horas.

### 3.6 Self-play y liga

Self-play ingenuo (el agente contra su copia más reciente) **colapsa**: los dos derivan hacia una
estrategia degenerada y olvidan cómo ganar a versiones antiguas. La respuesta estándar es una
**liga**: un depósito de checkpoints congelados, y el oponente de cada episodio se muestrea de
ahí con probabilidad proporcional a lo competitivo que sea.

Y eso mismo es la fábrica de niveles de la sección 2: **el depósito de la liga ES el catálogo de
tiers.** No hay trabajo adicional. Se etiqueta cada checkpoint con su Elo medido y se publican los
diez que mejor cubran el rango.

PvM es asimétrico y son **dos entrenamientos acoplados**, no uno:

- **Agente jugador** — el sparring y el oráculo de balance. Responde "este boss es imposible"
  sin esperar a que se queje un humano.
- **Agente monstruo** — lo que de verdad se envía en el juego. Aquí está el valor de producto:
  monstruos que aprenden a flanquear, a cortar la retirada, a castigar el cooldown del dash.

Se entrenan uno contra otro y ambos contra trazas de jugadores reales. Ojo con el equilibrio: si
el monstruo aprende mucho más rápido que el jugador, el jugador nunca ve un episodio ganable y no
aprende nada. La liga con muestreo por competitividad es también lo que arregla esto.

### 3.7 Jugadores reales en el bucle

Tres usos, en orden de coste:

1. **Clonación de conducta (barato, y lo primero).** Grabar `(obs, intent)` de partidas reales
   vía `TraceRecorder`. Preentrenar la política con aprendizaje supervisado antes de tocar RL —
   arranca desde algo que ya parece un jugador en vez de desde ruido. Le ahorra al RL las primeras
   decenas de millones de pasos.
2. **Trazas como oponentes en la liga.** Reproducir la intención grabada de un humano como
   política fija.
3. **Humano en vivo contra el agente** (caro, requiere el juego corriendo). Reservado para
   validación final: "se siente como jugar contra una persona" no lo responde ninguna métrica.

---

## 4. Plan por fases

### Fase 0 — Capa de intención (3.1)

`PlayerIntent`, `IIntentSource`, `HumanIntentSource`, refactor de `PollCombatActions` /
`PollTraversal` / `ReadInput`. Cero cambio de comportamiento; los tests existentes son la red.
**Es el prerrequisito de todo lo demás y también es la única fase que toca código de producción
del jugador.**

### Fase 1 — Arena determinista, sin nada de ML

`Valkur.Sim`, `SimClock`, `SimRandom`, `ArenaBuilder`, el test de hash reproducible, y una
política scriptada trivial para probar el lazo. Se conduce desde PlayMode y desde `DevConsole`.

**Esta fase tiene valor propio aunque el proyecto se pare aquí**: da simulaciones de combate
headless para tuning de balance ("cuántos segundos aguanta un dwarf nivel 5 contra tres
`knight_red`", respondido mil veces en un minuto en vez de a mano), y da regresión de combate.

### Fase 2 — Tiers heurísticos + búsqueda evolutiva, todo en C#

`ScriptedPolicy` con parámetros (distancia preferida, umbral de dash, prioridad de hechizos,
retraso de reacción). Un algoritmo evolutivo sencillo sobre esos parámetros dentro del ladder de
la sección 3.6. Sin Python, sin torch, sin dependencias.

**Esto ya cumple una parte grande del encargo**: da players y monstruos simulados por niveles, y
da la infraestructura de liga y Elo que la fase 3 necesita. Si el techo de calidad resulta
suficiente, la fase 3 no hace falta.

### Fase 3 — RL de verdad

Solo si la fase 2 topa. Aquí sí hay una elección de herramienta:

| | ML-Agents | Puente propio a Python |
|---|---|---|
| Regala | PPO/SAC, self-play con Elo, curriculum en YAML, GAIL/BC para imitación, build headless | nada |
| Cuesta | fija versiones de `mlagents` + torch; su `Academy` se apropia del paso temporal, que es justo lo que la fase 1 acaba de tomar bajo control | escribir vectorización, liga, curriculum y Elo — aunque la fase 2 ya deja hechos los tres últimos |
| Encaja con | equipos simétricos; PvM son dos equipos, así que sirve | cualquier cosa; SB3 / CleanRL modernos |

Recomendación: **empezar por ML-Agents**, porque `IAgentPolicy` lo hace reemplazable y porque
regalar self-play con Elo y GAIL vale más que la incomodidad del paso temporal. Si el conflicto
del `Academy` con el `SimClock` resulta insalvable, se cae a `RemotePolicy` sobre sockets sin
perder el entorno.

**Rendimiento — medir, no estimar.** El número que decide si esto es un fin de semana o un mes es
*episodios por segundo*, y depende de cosas que no se pueden adivinar: coste de `Physics2D.Simulate`
con N cuerpos, arenas en paralelo por proceso vía escenas de física locales, y cuánto del árbol de
`Gameplay` se puede desactivar sin cambiar la simulación. Se mide al final de la fase 1 con
`Recorder`, antes de comprometerse a nada de la fase 3.

---

## 5. Riesgos, ordenados por lo que costarían

1. **El refactor de intención rompe el juego en un sitio sutil.** 1090 líneas y compuertas
   compuestas (aturdimiento, editor abierto, espíritu, postura Paz) que este repositorio ya
   documenta como frágiles. Mitigación: no cambiar el orden de las compuertas, apoyarse en
   `StanceGateTests`, y hacer la fase 0 como un cambio aislado y verificado con la suite completa.
2. **La simulación no coincide con el juego.** Si la arena desactiva algo que sí afecta al combate,
   el agente aprende otro juego. Mitigación: la arena reutiliza `EntitySetup` y `SpellCaster`
   reales, nunca reimplementa combate; y un test que compara un combate en arena contra el mismo
   combate en el mundo real.
3. **Determinismo perdido por una regresión.** El test de hash es lo que lo detiene.
4. **Los agentes de nivel bajo se sienten robóticos** aunque su Elo sea correcto. Es un problema
   de percepción, no de entrenamiento; se ataca con el retraso de reacción y el tope de APM, y se
   valida solo con humanos.
5. **Coste de cómputo.** Sin GPU y sin paralelismo, PPO sobre un juego 2D con esta observación
   necesita del orden de decenas de millones de pasos. La fase 2 existe en parte para no depender
   de esto.

---

## 6. Lo que NO se debe hacer

- **No sintetizar eventos del InputSystem para conducir al agente.** Frágil, lento, y este
  proyecto ya tiene documentado un bug de pérdida de eventos de Unity 2022.3 que lo haría
  intermitente de una forma imposible de depurar.
- **No usar `Time.timeScale` para acelerar el entrenamiento.** No acelera nada real, deforma cada
  corrutina y cada `Time.deltaTime` del proyecto, y choca de frente con el recorte de
  `MaxCatchUpSeconds`. Paso fijo y física manual.
- **No entrenar contra un solo monstruo ni en una sola arena.** Se obtiene un agente que sabe
  ganarle a `barbol` en una sala vacía.
- **No reimplementar el combate en el entorno.** El día que las dos implementaciones discrepen,
  el agente entrenará contra un juego que no existe — y esa discrepancia será silenciosa, que es
  la forma de fallo que este repositorio documenta una y otra vez.
