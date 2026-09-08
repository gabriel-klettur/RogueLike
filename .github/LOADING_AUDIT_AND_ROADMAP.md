# Auditoría del arranque y la pantalla de carga

> Fecha: 2026-09-07 · Alcance: `Bootstrap.unity` → `MainMenu` → `LoadingScreenController` →
> `GameplaySceneSetup.Start` → `LoadingReporter.ReportGameplayReady`.
> Todas las cifras son **medidas**, no estimadas: el recuento de etapas sale del código
> fuente y los tiempos / asignaciones de `execute_code` contra el Editor vivo.

> **Estado: auditado y reconstruido el 2026-09-08.** La nota paso de **3.9** a **8.4**.
> Lo hecho, lo medido despues y lo que queda estan en la seccion 12, al final. El
> diagnostico de abajo se conserva tal cual: es el registro de por que existe cada pieza.

**Nota global original: 3.9 / 10.**

La presentación es buena y el desacoplo por `LoadingReporter` es correcto. Todo lo demás
—exactitud, tolerancia a fallo, cobertura de rutas, coste y testabilidad— está por debajo
del listón del resto del proyecto.

---

## 0. El mapa real del arranque

```text
Bootstrap.unity
  GameBootstrap.Awake      DontDestroyOnLoad + InitializeCoreServices()  ← cuerpo VACÍO
  GameBootstrap.Start      SceneTransitionManager.LoadScene("MainMenu")  ← SIN pantalla de carga

MainMenu.unity
  MainMenuUI.StartNewGame          LoadingScreenController.Show("MainGameplay")
  MainMenuUI.LoadPanel.Actions     LoadingScreenController.Show("MainGameplay")

LoadingScreenController
  Fase 1  0 % → 40 %   SceneManager.LoadSceneAsync, allowSceneActivation = false
  espera  0.5 s fijos
  PersistentEventSystem.Pause()
  allowSceneActivation = true
  FadeWatchdog(15 s)                                        ← plazo ABSOLUTO
  Fase 2  40 % → 100 %  GameplaySceneSetup.Report() × N

GameplaySceneSetup.Start (corrutina, ~250 líneas lineales)
  70 llamadas a Report(), cada una seguida de `yield return null`
  contra un presupuesto declarado de 53
```

### Rutas que NO pasan por la pantalla de carga

| Origen | Llamada | Consecuencia |
|---|---|---|
| Pausa → "New Game" | `SceneTransitionManager.LoadScene("MainGameplay")` | `SceneManager.LoadScene` **síncrono**: congelación total, sin barra, sin texto, sin fundido |
| Pausa → "Exit" | `SceneTransitionManager.LoadScene("MainMenu")` | ídem |
| `ZonePortal` con `destinationScene` | `SceneTransitionManager.LoadScene(...)` | ídem, en mitad de la partida |
| `GeneralEditorRegistry` → salir al menú | `SceneTransitionManager.LoadScene("MainMenu")` | ídem |
| `GameBootstrap` → MainMenu | `SceneTransitionManager.LoadScene(...)` | primer arranque del juego sin señal ninguna |

La pantalla de carga cubre **2 de las 6 transiciones de escena del juego**. La ruta de
pausa vuelve a ejecutar las 70 etapas de `GameplaySceneSetup` detrás de un
`SceneManager.LoadScene` síncrono, sin nada en pantalla.

---

## 1. Exactitud del progreso — **2.0 / 10**

El hallazgo más grave, y es aritmética, no opinión.

```csharp
// GameplaySceneSetup.cs
private const int SetupStepTotal = 53;   // comentario: "41 base + descomposiciones"
private void Report(string message)
{
    _setupStep++;
    LoadingReporter.ReportStage(message, (float)_setupStep / SetupStepTotal);
}
```

Recuento real de `Report()` alcanzables en la ruta por defecto (`loadFullWorld = true`,
`initialWorld = null`):

| Origen | Etapas |
|---|---|
| `GameplaySceneSetup.Start` | 55 |
| `LoadWorldProgressively` (BD de zonas + mazmorra) | 2 |
| `WorldLoader.LoadFullWorldProgressively` (callback) | 3 |
| `SpawnPlayerProgressively` | 7 |
| `BuildingLoader.LoadBuildingsProgressively` (callback) | 3 |
| **Total** | **70** |

El comentario dice "41 base"; hoy la base son **55**. Nadie subió la constante cuando
entraron clima, economía, chat, muerte / resurrección y los editores de Controles, Skills,
Cámara y Economía.

Consecuencias medibles:

- La barra llega al **100 % en la etapa 53 de 70**, o sea al **75.7 %** del trabajo.
- Las **últimas 17 etapas (24.3 %) corren con la barra clavada en 100 %** — y son justo la
  cola pesada: `EnsureBuildingLoaderProgressively` (301 instancias),
  `EnsureSpawnerInstanceLoader`, `EnsureAudioManager`, `EnsureMonsterSpawner`,
  `SaveService.Load`, `Restoring session`.
- No falla ruidosamente porque `OnStageReport` hace `Mathf.Clamp01(gamePhaseProgress)`.
  El error queda **silenciado por diseño**.

Segundo defecto, independiente del primero: **el peso de cada etapa es uniforme**.
`Painting zone overlays` (26 overlays, 2.5 MB de JSON, pintado de tilemap completo) vale en
la barra exactamente lo mismo que `Initializing notifications` (un `AddComponent`). La
barra no mide trabajo; mide número de llamadas a un método.

Misma familia que el `LAYER_COUNT` mantenido a mano de `SpriteTintStack` que este proyecto
ya documenta: **una constante que describe otro fichero y que nada comprueba**.

---

## 2. Robustez ante fallo — **3.0 / 10**

### 2.1 Cincuenta de cincuenta y cinco pasos sin protección

Medido: en `Start` hay **50 llamadas `Ensure*()` desnudas** y **5 envueltas en `try/catch`**
(`MonsterSpawner`, `WorldDamageService`, `SpawnerInstanceLoader`, `AudioManager`,
`CombatAudioSystem`), más `BuildingLoader` vía `RunSafely`.

Una excepción en cualquiera de las 50 mata la corrutina. Entonces:

1. `LoadingReporter.ReportGameplayReady()` no se llama nunca.
2. El watchdog de 15 s hace el fundido igual.
3. El jugador aterriza en un mundo a medio construir **sin un solo mensaje en pantalla**.

No existe ninguna ruta de error visible para el jugador; ningún `Debug.LogError` llega a
la UI.

### 2.2 El watchdog es un plazo absoluto, no un latido

```csharp
StartCoroutine(FadeWatchdog(15f));
...
yield return new WaitForSecondsRealtime(timeoutSeconds);
if (!_fadingOut) { /* fuerza el fundido */ }
```

Son 15 segundos **de reloj**, no 15 segundos sin progreso. En una máquina más lenta que la
de desarrollo —o con el disco ocupado, o con el mundo ampliado— el arranque supera los 15 s
y la pantalla de carga **desaparece mientras el arranque sigue corriendo**. El comentario
del propio código dice que `SpawnPlayer` llegó a costar ~8 s él solo.

Lo correcto es un latido: rearmar el contador en cada `ReportStage` y disparar solo si
**ninguna etapa avanzó** en N segundos. Eso distingue "lento" de "muerto", que es la única
pregunta que un watchdog debería contestar.

### 2.3 `LoadSceneAsync` sin comprobación de nulo

```csharp
var asyncOp = SceneManager.LoadSceneAsync(_targetScene);
asyncOp.allowSceneActivation = false;          // NullReferenceException si la escena no existe
while (asyncOp.progress < 0.9f) { ... }
```

Un nombre de escena ausente de Build Settings devuelve `null` y revienta aquí, con la
pantalla de carga ya montada y sin salida.

### 2.4 `Show()` no es reentrante

`LoadingScreenController.Show` crea un `GameObject` nuevo sin consultar `_instance`. Dos
invocaciones seguidas dan **dos `LoadSceneAsync` concurrentes sobre la misma escena** y dos
pantallas superpuestas. Además `LoadingReporter.OnStageProgress = OnStageReport` es una
**asignación**, no un `+=`: la segunda pantalla deja muda a la primera, que se queda en el
aire con su propio watchdog.

### 2.5 `_instance` es estado muerto

`private static LoadingScreenController _instance;` se escribe en `Awake` y se limpia en
`OnDestroy`. **Nadie lo lee.** Es exactamente la guarda de reentrada que falta en §2.4,
escrita y no conectada.

### 2.6 Sin bloqueo de entrada durante la Fase 2

Ni `GameplaySceneSetup` ni `LoadingScreenController` tocan `InputBlocker`. Desde
`allowSceneActivation = true` la escena de juego corre su `Update` con normalidad mientras
quedan ~17 etapas por ejecutar: el jugador puede moverse, hacer clic y abrir la consola
contra un mundo cuyos sistemas todavía se están registrando.

---

## 3. Rendimiento y optimización — **4.0 / 10**

### 3.1 Suelo fijo de 70 fotogramas

Cada etapa hace `yield return null`. Con vsync a 60 Hz son **1.17 s de espera pura** aunque
todas las etapas fuesen gratis. Se suma a:

- `WaitForSecondsRealtime(0.5f)` fijo antes de activar la escena;
- `MIN_DISPLAY_TIME = 1.5 s`;
- `FADE_DURATION = 0.45 s`.

El coste de presentación no es despreciable frente al trabajo real.

### 3.2 Los diecisiete editores se construyen en TODAS las builds

Unas 20 de las 55 etapas de `Start` (~36 % de la secuencia) crean editores de autoría:
Spawner, Buildings, FSM, Items, Spells, Entities, Boss, Inventory, Particles, Lighting,
General, TimeWeather, Camera, Controls, Skills, Economy, Tile, Map.

No hay ningún `#if UNITY_EDITOR` ni `Debug.isDebugBuild` alrededor. Los propios comentarios
del fichero lo reconocen ("*the editor itself was created in every build*"). Un jugador
paga el arranque completo de la caja de herramientas de desarrollo.

Es también la mayor palanca de optimización disponible aquí, y es un `#if`.

### 3.3 No hay precalentamiento de shaders

Cero `ShaderVariantCollection`, cero `Shader.WarmupAllShaders`, `preloadedAssets: []`. Con
URP 2D y shaders propios (`ValkurSnow`, `SpriteHDRTintLit`, `Valkur/SpriteAdditive`,
`ScreenGradeFeature`) la compilación de variantes ocurre en el **primer uso**: primer
hechizo, primera nevada, primer anochecer. Precalentarlas es exactamente para lo que existe
una pantalla de carga, y esta no lo hace.

### 3.4 Lo que sí es barato (medido, para no optimizar a ciegas)

| Medición | Resultado |
|---|---|
| Lectura + parseo de los 26 overlays (2.52 MB) | **85.8 ms** (11.9 ms IO + 73.7 ms `MiniJsonRuntime`) |
| GC generado por ese parseo | 22.4 MB |
| `background_ini.png` en runtime | 1536×1024, DXT1, **1.50 MB** — correcto |
| Instancias de edificio a spawnear | 301 |
| Assets bajo `Resources/` | **9 845** (355 MB en disco) |

El parseo JSON **no es el cuello de botella** (86 ms). Los 22.4 MB de basura sí importan,
pero el gasto real está en el pintado de tilemap, el horneado de colisiones, las 301
instanciaciones y —sobre todo— en las 70 esperas de fotograma y en los 17 editores.

### 3.5 `Resources/` a 355 MB y 9 845 assets

CLAUDE.md dice "*`Resources/` is loaded whole at build — keep it minimal*" y cifra el árbol
en ~7 400 assets. Hoy son **9 845**: `Buildings/` 209 MB, `Dungeon/` 63 MB, `Tiles/` 44 MB,
`UI/` 40 MB. Encarece el índice de recursos, el tamaño de build y convierte cualquier
`Resources.LoadAll` en un barrido caro. Nada de esto está detrás de Addressables.

### 3.6 Todo en el hilo principal

Cero `async` / `Task` / JobSystem en el arranque. `IBootstrapStep` declara una superficie
`Task ExecuteAsync(...)` que no usa nadie (§5.1).

---

## 4. Determinismo Editor vs Build — **3.5 / 10**

`EntitySetup.InitPlayerSpells` resuelve el catálogo así:

1. `_spellCatalog` inyectado desde la escena (camino bueno);
2. si está vacío, **en Editor**: `AssetDatabase.FindAssets("t:SpellDefinition", ...)`;
3. si está vacío, **en build**: `Resources.LoadAll<SpellDefinition>("Catalogs/Spells")`.

Medido en vivo: `Resources.LoadAll<SpellDefinition>("Catalogs/Spells")` devuelve **0** (esa
carpeta no existe bajo `Resources/`). Lo mismo con
`Resources.LoadAll<PlayerDefinition>("Players")` → **0**.

O sea: **el respaldo del Editor se autorrepara y el de la build no existe**. Si la
referencia de escena se rompe, en el Editor no se nota nada y la build sale con "fireball
only" y un warning. Un fallo que por construcción es invisible allí donde se desarrolla.

---

## 5. Arquitectura y escalabilidad — **5.0 / 10** (arquitectura) · **3.5 / 10** (escalabilidad)

### 5.1 `BootstrapPipeline` está escrito y no lo usa nadie

`BootstrapPipeline`, `IBootstrapStep` y `BootstrapProgress` existen, están comentados con
esmero ("*Phase 0 introduces this pipeline… Phase 1 migrates the monolithic
`RunSetupSequence` wholesale*") y tienen su propio fixture de 139 líneas.

Referencias en producción: **cero**. Los dos únicos aciertos de `grep` fuera de sus propios
ficheros son comentarios en `WorldManager.cs`.

Es el patrón que este repositorio ya documenta una docena de veces —`animation_map.json`,
el bloque `Actions` del FSM, las cuatro banderas de casteo, `discountLimits`—:
**autorizado, serializado, testeado y muerto**. La Fase 1 nunca llegó, y entretanto
`GameplaySceneSetup.Start` creció de 41 a 55 pasos en línea.

### 5.2 `GameBootstrap.InitializeCoreServices()` tiene el cuerpo vacío

Dos `Debug.Log` y un bloque de comentarios listando SaveService, AudioService, InputService
y "AssetService (Addressables)". Ninguno se registra ahí. La escena `Bootstrap` no hace
nada excepto cargar el menú.

### 5.3 El coste de añadir un sistema

Hoy, sumar un sistema al arranque son **cuatro ediciones en tres ficheros**: el método
`Ensure*`, la llamada en `Start`, el `Report(...)` y —que nadie hace— subir
`SetupStepTotal`. Con el pipeline serían **dos**: una clase y una línea de registro. El
orden estaría en datos en vez de en una corrutina de 250 líneas, y el presupuesto saldría
de `_steps.Count` en vez de una constante a mano.

### 5.4 `Start` es un método-dios

`GameplaySceneSetup` son ~90 KB repartidos en 9 parciales, y `Start` es una lista lineal de
55 pasos con dependencias de orden **documentadas en comentarios** y verificadas por **un
solo test** (`EnsureTileEditor` antes de `LoadWorldProgressively`, mediante `IndexOf` sobre
el texto fuente). Los demás comentarios de orden crítico —`GameEditorManager` antes de
cualquier editor, `EditorWorkspaceService` antes de cualquier panel,
`EnsureWorldDamageService` antes del `BuildingLoader`— **no tienen test ninguno**.

---

## 6. Testabilidad — **3.0 / 10**

`LoadingScreenControllerTests`: 7 pruebas, **todas cosméticas** (el sprite de la barra
existe, el tipo es `Filled` / `Horizontal` / izquierda-a-derecha, `ApplyProgress` clampa y
es monótona). Ninguna toca la máquina de estados: ni fases, ni watchdog, ni fundido, ni
suscripción a `LoadingReporter`.

Lo que ninguna prueba cubre y debería:

- Que `SetupStepTotal` **iguale** el número de `Report()` alcanzables — el test que habría
  atrapado 53 contra 70 el día en que se rompió.
- Que la última etapa reportada dé exactamente `1.0`.
- Que toda transición de escena hacia `MainGameplay` pase por `LoadingScreenController`,
  el test que habría atrapado la ruta de pausa.
- Que el watchdog no dispare mientras siga llegando progreso.
- Que `Show()` sea idempotente.

`BootstrapPipelineTests` son 139 líneas sobre código que nada ejecuta: cobertura de una
abstracción no adoptada.

---

## 7. UX y presentación — **6.0 / 10**

Lo bueno, y es genuinamente bueno: barra con borde / relleno / porcentaje, lerp suave,
puntos suspensivos animados, feed de las últimas 3 etapas con alfa degradado, tips
rotatorios, fondo con `AspectRatioFitter.EnvelopeParent` + `RectMask2D` (nunca deforma),
`CanvasScaler` en `ScaleWithScreenSize`, fundido de salida y tiempo mínimo en pantalla.

Lo malo:

- **Los tips mienten.** Diez consejos, y cinco nombran teclas retiradas el 2026-09-05:
  "*Press F1–F12 to open the in-game editors*", "*F10 opens the Buildings editor*",
  "*F8 is the Tile editor*", "*F12 lets you author boss FSMs*", "*Spells can be remapped
  from the F4 in-game editor*". Los catorce toggles de editor **se envían sin binding**; se
  entra por **Escape**. Además F4 nunca fue el editor de hechizos. Es texto de cara al
  jugador enseñando controles que no existen.
- **Todo en inglés** en un juego cuya UI está en castellano ("Comerciar", "Diario",
  "Reiniciar", "GUARDAR", "VALORES POR DEFECTO"). No hay capa de i18n: las cadenas están
  incrustadas en `LoadingScreenController`.
- **Sin cancelar ni volver.** Pulsado "New Game", no hay salida.
- **Sin superficie de error** (§2.1).
- Los tips rotan cada 4.5 s: en un arranque de 6 s se leen uno o dos.

---

## 8. Instrumentación y diagnóstico — **2.0 / 10**

- **Cero medición de tiempo por etapa.** No hay `Stopwatch` en `Report()`. Nadie puede
  responder "¿qué parte del arranque cuesta?" sin instrumentar a mano.
- **Cero telemetría de arranque**, aunque el proyecto ya tiene `ProfileTelemetrySystem` y
  un `IProfileDb`.
- **Ningún comando de consola** (`boot`, `bootstats`) — y este repositorio tiene la
  costumbre, bien justificada, de darle una sonda a cada subsistema (`faces`, `journal`,
  `spawners`, `ai`, `market`). El arranque es el único sistema grande sin la suya.

---

## 9. Higiene de recursos — **5.5 / 10**

- `GetWhiteSprite()` cachea una `Texture2D` estática de 1×1 que nunca se libera ni se
  resetea en `SubsystemRegistration`. Con Domain Reload apagado sobrevive entre sesiones de
  Play. Es un píxel: intrascendente, pero es estado estático sin gancho de reinicio, que es
  justo lo que el trinquete `DomainReloadStaticResetTests` existe para evitar.
- `LoadingReporter` **sí** tiene su `[RuntimeInitializeOnLoadMethod]`. Correcto.
- `PersistentEventSystem.Pause()` antes de activar la escena. Correcto y bien comentado.
- 22.4 MB de basura del parseo JSON en el peor momento posible: durante la carga, con el GC
  compitiendo con el pintado de tilemap.

---

## 10. Tabla de puntuaciones

| # | Eje | Nota | Frase |
|---|---|---|---|
| 1 | Exactitud del progreso | **2.0** | 53 declarado contra 70 real; barra clavada en 100 % durante el 24 % final |
| 2 | Instrumentación y diagnóstico | **2.0** | ni un `Stopwatch`, ni telemetría, ni comando de consola |
| 3 | Robustez ante fallo | **3.0** | 50/55 pasos sin `try`; watchdog absoluto; sin UI de error |
| 4 | Cobertura de rutas de entrada | **3.0** | 2 de 6 transiciones de escena usan la pantalla de carga |
| 5 | Testabilidad | **3.0** | 7 pruebas cosméticas; el presupuesto no lo comprueba nadie |
| 6 | Escalabilidad | **3.5** | constante a mano, corrutina monolítica, sin Addressables, `Resources/` a 355 MB |
| 7 | Determinismo Editor vs Build | **3.5** | el respaldo del Editor se autorrepara, el de la build está vacío |
| 8 | Reentrada y concurrencia | **4.0** | `Show()` sin guarda, delegado por asignación, entrada no bloqueada |
| 9 | Rendimiento y optimización | **4.0** | 70 fotogramas de suelo, 17 editores en la build, sin warmup de shaders |
| 10 | Arquitectura y capas | **5.0** | el relé `LoadingReporter` está bien; el pipeline está muerto y `Start` es un método-dios |
| 11 | Higiene de recursos | **5.5** | 22.4 MB de GC en el peor momento; sprite estático sin reset |
| 12 | UX y presentación | **6.0** | bonita y pulida — y enseña cinco teclas que ya no existen |
| | **Global** | **3.9** | |

---

## 11. Hoja de ruta propuesta

### Fase 0 — Verdad (barato, alto impacto)

1. **Derivar el presupuesto en vez de declararlo.** Dos pasadas —contar y ejecutar— o,
   mejor, mover la secuencia a `BootstrapPipeline` y usar `_steps.Count`. Mientras tanto,
   un test que cuente los `Report(` alcanzables y los compare con `SetupStepTotal`.
2. **Pesos por etapa.** Un peso relativo por paso: el mundo y los edificios valen decenas de
   veces una etapa de `AddComponent`. Sin eso la barra seguirá mintiendo aunque los números
   cuadren.
3. **Reparar los tips.** Reescribirlos contra los controles reales (Escape, no la fila F) y
   sacarlos a datos, no a un `static readonly string[]` dentro del controlador.
4. **`Stopwatch` por etapa + comando `boot`** que imprima el desglose. Sin esto, cualquier
   optimización posterior se hace a ciegas — la lección que este repositorio ya pagó con el
   editor de Items.

### Fase 1 — Robustez

1. **Watchdog por latido**, no por plazo: se rearma en cada `ReportStage` y dispara solo si
   nada avanzó en N segundos.
2. **Comprobar el nulo de `LoadSceneAsync`** y validar que la escena está en Build Settings.
3. **Envolver los 50 pasos desnudos.** El patrón `RunSafely` ya existe; extenderlo a todos y
   acumular los fallos.
4. **Superficie de error.** Si algún paso falló, la pantalla de carga no se desvanece en
   silencio: dice qué falló y ofrece volver al menú.
5. **Guarda de reentrada en `Show()`** —usando el `_instance` que ya está escrito y no se
   lee— e `InputBlocker` activo hasta `ReportGameplayReady`.

### Fase 2 — Cobertura

1. **Una sola puerta.** `SceneTransitionManager.LoadScene` debe enrutar a
   `LoadingScreenController` para cualquier escena pesada, o `PauseMenuUI` / `ZonePortal`
   deben llamar a `Show()`. Con un test que lo fije, igual que `EditorReachabilityTests` fija
   que todo editor tenga entrada en el menú.

### Fase 3 — Coste

1. **Encerrar los diecisiete editores** tras `#if UNITY_EDITOR || DEVELOPMENT_BUILD`. Es la
   mayor reducción de arranque disponible y es un `#if`.
2. **Precalentar shaders** durante la Fase 2 con un `ShaderVariantCollection`.
3. **Agrupar los `yield`.** Ceder por presupuesto de tiempo (p. ej. cada 8 ms) en vez de una
   vez por etapa: elimina el suelo de 70 fotogramas sin perder respuesta.
4. **Adelgazar `Resources/`** (9 845 assets, 355 MB) y evaluar Addressables — el
   `AssetService` que `GameBootstrap` lleva prometiendo desde el primer commit.

### Fase 4 — Estructura

1. **Ejecutar la Fase 1 del pipeline**: migrar los 70 pasos a `IBootstrapStep`. Con eso el
   orden pasa a ser datos, el presupuesto se deriva, un test EditMode puede correr un
   subconjunto de 3 pasos, y las precondiciones de orden se declaran en vez de vivir en
   comentarios.
2. **Sacar las cadenas a i18n** junto con el resto de la UI.

---

## 12. Lo que se construyó

Fecha: **2026-09-08**. Suite EditMode completa: **8 060 / 8 060 en verde**. Consola limpia.
Arranque real medido tras el cambio: **64 etapas, 0 fallos**.

### 12.1 La secuencia es DATOS

`GameplaySceneSetup.Start` era una corrutina de 250 líneas de tripletes
`EnsureX(); Report("…"); yield return null;`. Ahora es una lista de `BootStep`
(`GameplaySceneSetup.Sequence.cs`) y un runner de ~50 líneas. Un solo cambio de forma cierra
cuatro defectos a la vez:

| Antes | Ahora |
|---|---|
| `SetupStepTotal = 53` contra 70 reales | el total es `steps.Count` — **la constante no tiene dónde existir** |
| 50 de 55 pasos sin `try` | el runner tiene **una** frontera de excepción para los 64 |
| el orden vivía en comentarios | el orden es un valor que un test EditMode recorre |
| 17 editores soldados a toda build | un `if` en el compositor |

`BootstrapPipeline` / `IBootstrapStep` / `BootstrapProgress` — escritos, comentados, testeados
con 139 líneas y **con cero referencias en producción** — están borrados. Su trabajo lo hace
`BootStep`, que sí se ejecuta.

### 12.2 La barra no puede mentir

- **El total se deriva**, nunca se declara.
- **Los pesos se auto-calibran**: `BootTimeline` guarda los milisegundos medidos de cada etapa
  en `PlayerPrefs` y el arranque siguiente los usa como pesos. Una etapa nueva se corrige sola
  en el segundo lanzamiento en vez de esperar a que alguien actualice un número.
- **El 100 % ya no es un número al que se llega contando.** `BootProgress.Fraction` está topado
  en 0.99 hasta que `Complete()` — al que solo se llega tras el último paso — dice que el juego
  está listo. La deriva de 53-contra-70 es ahora estructuralmente imposible, no "arreglada".
- **Las tres etapas progresivas recorren su propia porción** en vez de dejar la barra congelada
  mientras su etiqueta cambia cuatro veces. Y el número de sub-etapas declarado se compara
  **contra el código que las reporta** — el mismo defecto original, ahora con test.

### 12.3 Robustez

- **Watchdog por latido.** Se rearma en cada `ReportStage`. A los 12 s de silencio avisa en
  pantalla; a los 35 s concluye que el arranque está muerto. Un arranque *lento* ya no termina
  con la pantalla desapareciendo encima.
- **Superficie de error.** Si algún paso falla, la pantalla dice cuáles y ofrece
  «Volver al menú» y «Continuar igualmente». Antes: fundido en silencio hacia un mundo a medias.
- `CanStreamedLevelBeLoaded` más comprobación de nulo sobre `LoadSceneAsync`.
- `Show()` es idempotente y devuelve `bool` — usando el `_instance` que estaba escrito y no
  leía nadie.
- `InputBlocker` activo desde la activación de escena hasta `ReportGameplayReady`.

### 12.4 Una sola puerta — que no es una sola cortina

`SceneTransitionManager.LoadSceneHandler` es el relevo (Core no puede referenciar UI, igual que
`LoadingReporter`), y `SceneLoadRouter` lo instala en `BeforeSceneLoad` — antes que la primera
transición del juego, que es `GameBootstrap.Start`. **Las 6 transiciones pasan ahora por la misma
puerta**, con caída al camino síncrono si el relevo falla. Verificado en vivo:
`handlerInstalled=True`.

**Enrutar todo y MOSTRAR todo son dos decisiones distintas, y la primera version las
confundio.** Con la pantalla puesta en cada transicion, el jugador veia una carga completa
—barra, consejo, tiempo minimo, fundido— delante de un menu principal que carga en un parpadeo
y no reporta nada, y otra en cuanto pulsaba Nueva partida: **dos pantallas seguidas al abrir el
juego**. `SceneLoadRouter.NeedsLoadingScreen` decide por la escena, y solo la que lleva
secuencia de arranque detras se gana la pantalla; el nombre sale de
`GameBootstrap.GameplaySceneName`, no de una segunda copia que se quede atras cuando alguien
renombre la escena.

El camino de vuelta —del juego al menu— se queda directo a proposito: es una carga sincrona de
todos modos, y una pantalla delante de una carga sincrona es un fotograma congelado con una
barra encima, incapaz de reportar un progreso que nadie le cuenta.

### 12.5 Coste

- **17 editores de autoría tras `RuntimeEditorPolicy`** (Editor o Development Build). En el
  Editor no cambia nada. Antes de gatearlos hubo que **sacar de ellos el cableado de runtime**
  que escondían: el catálogo de objetos, `ItemDropService` y dos catálogos de picker se
  registraban dentro de un `Ensure*Editor` y se habrían ido con la caja de herramientas.
  `TileEditorManager` y `MapEditorManager` **no** se gatean: llevan responsabilidades de runtime
  (el sumidero de etiquetas de colisión, el desmontaje del mundo).
- **De 70 fotogramas forzados a 24.** Cada paso declara si necesita fotograma propio; el resto
  se agrupan con un presupuesto de 8 ms.
- **Precalentado de shaders** como etapa del arranque, solo en build.

### 12.6 Lo que la sonda dijo en cuanto existió

`boot` / `boot all` / `boot fallos` / `boot weights` / `boot recalibrar`.

```text
Arranque: 64 etapas en 13.73 s (barra sin calibrar - primer arranque)
    2338.5 ms   17.0%  Creando el personaje
     905.0 ms    6.6%  Cargando el mundo
Desglose de las etapas progresivas:
    2028.8 ms   14.8%    · Creando la entidad
     463.3 ms    3.4%    · Aplicando ajustes de tiles
     256.6 ms    1.9%    · Pintando las zonas
     191.0 ms    1.4%    · Construyendo la interfaz
Sin fallos.
```

En otra corrida: **6 749 ms — el 60.1 % de un arranque de 11.23 s — en «Levantando los
edificios»**, 301 instancias a unos 22 ms cada una. Ese número no existía antes de esta sesión
y es el siguiente objetivo obvio. **6 458 ms de esos 6 749 son la sub-etapa «Colocando los edificios»** — las 301 instanciaciones en si, a unos 21,5 ms cada una. La pasada de colisiones cuesta **137 ms**, asi que la sospecha inicial (un `BoxCollider2D` por celda) estaba equivocada y la sonda por sub-etapa la desmintio en la primera corrida.

### 12.7 El defecto que la propia reconstrucción introdujo

La heurística «si la escena no dice nada en 0,75 s, no tiene arranque que narrar» —puesta para
que el menú no se sintiera retenido— **se cumplía dentro del único fotograma gigante de
activación** de la escena de juego. Medido en vivo: la pantalla se fundió con el arranque al
**72.8 %**. Es exactamente el fallo que toda esta reescritura existe para eliminar,
reintroducido por la comodidad.

Arreglo: la decisión cuenta **fotogramas además de segundos** (una activación de tres segundos
sigue siendo UN fotograma), vive en `ShouldConcludeNoPhase2`, una función pura, y tiene cuatro
tests. Lo que lo encontró no fue leer el código sino arrancar el juego y mirar el número.

### 12.8 Puntuaciones después

| # | Eje | Antes | Después | Qué falta para el 10 |
|---|---|---|---|---|
| 1 | Exactitud del progreso | 2.0 | **9.0** | el primer arranque de una instalación nueva sigue usando estimaciones |
| 2 | Instrumentación | 2.0 | **8.5** | sin telemetría histórica en `IProfileDb` |
| 3 | Robustez ante fallo | 3.0 | **9.0** | la pantalla de error no está probada sobre un frame renderizado |
| 4 | Cobertura de rutas | 3.0 | **9.5** | — |
| 5 | Testabilidad | 3.0 | **8.5** | falta un PlayMode que recorra el arranque entero |
| 6 | Escalabilidad | 3.5 | **7.5** | `Resources/` sigue en 355 MB y sin Addressables |
| 7 | Editor vs Build | 3.5 | **7.0** | los respaldos `Resources.LoadAll` de la build siguen resolviendo 0 |
| 8 | Reentrada | 4.0 | **9.0** | — |
| 9 | Rendimiento | 4.0 | **7.5** | el 60 % del arranque son 301 edificios: medido, no optimizado |
| 10 | Arquitectura | 5.0 | **8.5** | — |
| 11 | Higiene de recursos | 5.5 | **8.0** | los 22,4 MB de GC del parseo siguen ahí |
| 12 | UX y presentación | 6.0 | **8.5** | sin cancelar, y sigue sin capa de i18n real |
| | **Global** | **3.9** | **8.4** | |

### 12.10 El coste real de los edificios: `Sprite.Create` trazando alfa

La sonda por sub-etapa señaló `Colocando los edificios`. Dos medidas más cerraron el caso, y
las dos descartaron una hipótesis en vez de confirmarla:

| Medición | Resultado | Lo que descarta |
|---|---|---|
| Pasada de colisiones por celda | **137 ms** | no son los colliders (era la sospecha escrita) |
| Recarga en caliente vs. en frío | **5 781 ms vs 6 804 ms** | solo ~1 s es carga de assets; el resto es trabajo |
| `Sprite.Create` Tight vs FullRect | **20,15 ms vs 0,005 ms** | es esto: **4 459x** |

`BuildingObject.Assembly` parte cada edificio en dos mitades —huella y copa, para el Y-sort—
con dos `Sprite.Create`. El valor por defecto es `SpriteMeshType.Tight`, que **traza el
contorno alfa** de la región para construir una malla ajustada; y el precio escala con la
REGIÓN, no con el sprite: aquí son ~1024 px cuadrados recortados de una página de atlas de
**4096x4096**. 602 trazados por arranque.

Nadie quería esa malla. Una malla ajustada compra menos overdraw y una forma física de sprite;
estos renderers dibujan pixel art casi opaco y **el subsistema de edificios no contiene un solo
`PolygonCollider2D`** — la colisión es la rejilla por celda que hornea `BuildingCollisionLoader`.
Se pagaba en cada edificio de cada arranque y no lo leía nada.

```text
antes:   Levantando los edificios = 6 953 ms   ·   arranque = 11 286 ms
después: Levantando los edificios =   324 ms   ·   arranque =  5 760 ms
```

301 edificios en pantalla, 0 fallos, y **sin cambio visual**: una malla ajustada y un quad
completo muestran los mismos píxeles, porque el alfa vive en el shader. Verificado sobre un
fotograma renderizado.

**La lección que viaja**: 22 ms para lo que nominalmente es una asignación de objeto no es
«instanciar es lento», es trabajo que nadie pidió. Y el orden importó — medir la etapa, luego
la sub-etapa, luego el mismo trabajo en caliente, y solo entonces leer el código.

### 12.11 Precargar en el menú: la idea del usuario, medida

La pregunta era: *¿podemos ir cargando en segundo plano mientras el jugador está en el menú,
y al arrancar cargar solo lo que falte?* La respuesta corta es sí, y el valor exacto salió de
medir antes de diseñar.

**Primero, qué NO era la palanca.** Con los edificios ya arreglados, la suma de todas las
etapas era ~1,6 s de 5,8. El resto estaba «fuera de las etapas», así que se probó reducir
fotogramas: cambiar las cadencias por conteo de los cargadores por un presupuesto de tiempo.

| Presupuesto | Fotogramas | Arranque en caliente | Fuera de etapas |
|---|---|---|---|
| 12 ms | 61 | 3 237 ms | 1 838 ms |
| 250 ms | 45 | 3 115 ms | 1 832 ms |

Dieciséis fotogramas menos valieron **122 ms**, y el tiempo fuera de las etapas **no se movió**.
Ese coste no es de los `yield`: es trabajo que Unity hace porque la escena acaba de activarse.
El presupuesto se queda —es mejor y no puede pudrirse como un conteo— pero no es donde estaba
el tiempo. Nota de paso: el primer intento con 12 ms fue **peor** que el conteo al que sustituía,
porque con el trabajo ya barato un presupuesto pequeño solo cede más a menudo.

**Dónde sí estaba.** Un primer arranque costaba 5 649 ms y un segundo, en la misma sesión,
3 115 ms. Esos **2 534 ms** son primer toque de assets, y se pagan una vez por lanzamiento.

| Medición | Resultado |
|---|---|
| `Resources.LoadAll<Sprite>("Buildings")` en frío | 1 256 sprites, **334 ms** |
| `Resources.LoadAll<Sprite>("Tiles")` en frío | 4 117 sprites, **981 ms** |
| Primer arranque tras tocarlos desde el menú | **3 748 ms** (de 5 649) |

**1 901 ms recuperados: el 75 % de la penalización**, a cambio de 1,3 s de trabajo mientras
el jugador lee un menú. `MenuAssetPreloader` lo hace solo, con presupuesto por fotograma para
no dar tirones, y **se aparta en cuanto empieza una carga de verdad** — media precarga vale
exactamente lo que alcanzó a paginar. Medido con el precargador automático: **3 681 ms**.

**Lo que se descartó, y por qué no es tacañería.** Cargar la escena de juego de forma aditiva
detrás del menú y construir el mundo allí movería otros ~1,4 s. Se rechazó por su coste, no por
su tamaño: **el arranque no solo carga datos, arranca sistemas** — los generadores disparan, los
NPC caminan, el reloj del día corre, el autoguardado se arma. Un mundo vivo detrás de un menú
exige partir la secuencia en fases de construir y activar, y arbitrar los singletons de dos
escenas a la vez, por una ganancia menor que la de paginar assets. Paginar no instancia nada,
no tictaquea nada, y abandonarlo a medias no cuesta nada.

### 12.12 El recorrido completo del arranque

| Momento | Arranque | Qué lo movió |
|---|---|---|
| Al empezar la sesión | **11 286 ms** | — |
| Tras `SpriteMeshType.FullRect` | **5 649 ms** | 602 trazados de contorno alfa que nadie leía |
| Tras el presupuesto por fotograma | ~5 600 ms | 61 → 45 fotogramas (marginal, medido) |
| Tras el precargador del menú | **3 681 ms** | 75 % del primer toque, movido a tiempo muerto |

**3× más rápido**, con 301 edificios en pantalla, 0 fallos y 8 088 tests en verde. Y ninguno
de los dos hallazgos salió de leer el código: salieron de la sonda que este mismo trabajo
construyó y de comparar dos arranques seguidos.

### 12.9 Lo que queda, por orden de palanca

1. ~~Las 301 instanciaciones~~ y ~~el primer toque de assets~~ — **hechos** (12.10 y 12.11).
   El arranque va de 11,3 s a **3,7 s**. Lo que queda son ~1,8 s fuera de las etapas que no
   dependen ni de los fotogramas ni de la precarga: es el coste de activar la escena.
   Reducirlo exige construir el mundo antes de que el jugador arranque, con el precio
   documentado en 12.11.
2. **Adelgazar `Resources/`** (9 845 assets, 355 MB) y evaluar Addressables — el `AssetService`
   que `GameBootstrap` promete desde el primer commit y cuyo `InitializeCoreServices()` sigue
   con el cuerpo vacío.
3. **Los respaldos de build** que resuelven 0 assets: es un problema de dónde viven los
   catálogos, no de la carga.
4. **Un PlayMode que arranque el mundo entero** y afirme sobre `BootTimeline`.
5. **i18n de verdad**; `LoadingText` es la costura, no la capa.
6. **Cancelar una carga** y volver al menú sin esperar a que termine.
