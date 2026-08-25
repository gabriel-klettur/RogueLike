# Ciclo día/noche — auditoría y hoja de ruta

> Auditoría completa del subsistema día/noche de Valkur, con puntuación rigurosa 0–10 y
> el plan de trabajo para llevarlo a calidad profesional.
>
> Fecha: 2026-08-25 · Método: 4 auditorías paralelas (núcleo temporal, ruta de render URP 2D,
> editores/HUD/audio, consumidores gameplay) + verificación manual de los hallazgos críticos.
> Unity 2022.3.62f1, URP 14.0.12, espacio de color Linear.

## Resumen ejecutivo

**Puntuación global: 2.0 / 10.**

El andamiaje es bueno; lo que sostiene no se renderiza. Hay un editor en juego pulido (F2),
un reloj HUD dibujado proceduralmente, tres efectos de clima reales y un modelo temporal con
API limpia. Pero **el ciclo día/noche no ilumina un solo píxel del mundo**, por tres fallos
independientes que se apilan, cada uno de los cuales bastaría por sí solo para anularlo:

1. **La "Global Light 2D" de la escena no es global.** Está serializada como
   `m_LightType: 3` = `Point` con radio de **1 unidad de mundo** en el origen
   (`MainGameplay.unity:652`).
2. **Todo el mundo es unlit por construcción.** `ApplyUnlitFallbackIfNeeded()` estampa
   `Sprite-Unlit-Default` en todos los `TilemapRenderer` **sin condición alguna**
   (`WorldGridBuilder.cs:58-85`). Edificios, entidades, drops, proyectiles y VFX hacen lo
   mismo por su cuenta.
3. **La máscara de sorting layers de la luz excluye 9 de 16 capas**, incluidas 7 de las 8
   capas de tilemap (`MainGameplay.unity:659`).

Lo que el jugador percibe hoy como "noche" es exactamente: una viñeta uGUI de alfa 0.30,
29 de 154 presets de partículas que multiplican su `startColor` con un suelo por canal de 0.25,
y 10 antorchas que **tampoco dibujan nada** (ver §4). Nada más.

La causa raíz de los tres fallos de luz es la misma y es trivial: **tres constantes de enum
equivocadas**. En URP 14 `Light2D.LightType` es `Parametric=0, Freeform=1, Sprite=2, Point=3,
Global=4` (`Light2D.cs:41-57`). El código usa `1` creyendo que es Global (es Freeform) y `2`
creyendo que es Point (es Sprite).

## Estado: Fase 0 completada (2026-08-25)

La fase de reparación está hecha y **verificada en Play Mode con capturas reales**, no por
lectura de código. Lo que cambió:

| Cambio | Archivo | Efecto medido |
|---|---|---|
| Reflexión eliminada, API URP tipada en las 3 rutas de luz | `GameplaySceneSetup.Systems.cs`, `DayNightCycle.cs`, `WorldLightLoader.cs` | La clase entera de bug (constantes de enum equivocadas) deja de ser expresable: el compilador la rechaza. `Valkur.Gameplay` ya referenciaba `Unity.RenderPipelines.Universal.Runtime`, así que la reflexión no compraba nada |
| `EnsureGlobalLight2D` adopta y repara en vez de salir por `return` | `GameplaySceneSetup.Systems.cs` | La luz autorizada en escena se corrige sola si vuelve a desviarse |
| Luz de escena `m_LightType: 3` (Point, radio 1) → `4` (Global) | `MainGameplay.unity:652` | La luz global existe por primera vez |
| Máscara de sorting layers reconstruida: 9 ids obsoletos → 12 capas de mundo | `MainGameplay.unity:659` + `AmbientLitSortingLayers` | UI_World y Overlay quedan fuera a propósito: barras de vida y reglas de editor siguen legibles a medianoche |
| `ApplyUnlitFallbackIfNeeded` → `ApplyTilemapMaterial`, ahora sondea de verdad | `WorldGridBuilder.cs` | **10 de 10 `TilemapRenderer` en `Sprite-Lit-Default`** |
| Edificios y entidades pasan por un único dueño de la decisión lit/unlit | nuevo `Core/Rendering/WorldSpriteMaterials.cs` | 34 sprites de edificio en pantalla, todos lit |
| Nuevo shader `Valkur/SpriteHDRTintLit` | `_Project/Shaders/SpriteHDRTintLit.shader` | Mismo contrato HDR + flash que el unlit, pero recibe luz 2D. El flash se aplica DESPUÉS de la iluminación: un impacto en una cueva negra tiene que verse igual |
| Antorchas: `Sprite` (2) → `Point` (3), y blend style 0 → **1 (Additive)** | `WorldLightLoader.cs` | Las luces colocadas dibujan por primera vez, y **suman** fotones en vez de multiplicar |
| `falloff` acotado a `[0,1]` y los 3 presets migrados | `LightPresetDefinition.cs` + 3 assets | Los presets dejan de ser idénticos en falloff |
| Radios e intensidades retuneados con el juego delante | 3 assets | 500 px (15.6 unidades) saturaban media pantalla; medido, una antorcha lee bien a ~5 unidades |
| Ventana de encendido derivada de las bandas de fase | `DayNightCycle.cs` | Cierra los ~88 s reales de noche cerrada sin antorchas |
| Guarda de epsilon en la escritura del Light2D | `DayNightCycle.cs` | Se acabó el `PropertyInfo.SetValue` por frame durante las bandas planas |
| Reset `SubsystemRegistration` en `EntitySpriteHelper` | `EntitySpriteHelper.cs` | Bug latente de Domain-Reload-OFF que estaba ahí desde antes |

### Medición

| Métrica | Antes | Después |
|---|---|---|
| Luminancia media a mediodía (`t=0.50`) | — | 0.358 |
| Luminancia media a medianoche (`t=0.92`) | — | 0.050 |
| Píxeles que responden a la luz global | ~0 % | **99.9 %** de los juzgables (los 760 que no son las barras de `UI_World`, por diseño) |
| `TilemapRenderer` iluminados | 0 de 10 | 10 de 10 |
| Luces puntuales que rasterizan algo | 0 de 10 | 10 de 10 |

### Correcciones a la auditoría, a la luz de lo medido

- **El banding no es el problema que §2 anticipaba.** Con la noche a intensidad 0.15 el frame
  conserva **254 niveles de verde distintos** de 256. El mundo son sprites de color rico, no
  degradados amplios, así que el multiplicador de 8 bits no colapsa nada visible. El dithering
  de la Fase 1 baja de prioridad.
- **La cifra de radio sí era un problema real**, y solo se pudo ver una vez que las luces
  dibujaban: a 15.6 unidades de mundo una antorcha reventaba a blanco la mitad del viewport.

### Estado de los tests

**EditMode: 5906 / 5906 en verde, 0 fallos** (106 s), con el Editor en reposo. El lint de
convenciones también pasa: `OK — 47231 files / 601 folders, no convention violations`.

Los tres fallos que este trabajo provocó por el camino, todos legítimos y todos corregidos:

1. `DomainReloadStaticResetTests.BaselineHasNoStaleEntries` — 4 entradas del baseline dejaron
   de describir la realidad porque los campos se arreglaron o desaparecieron
   (`EntitySpriteHelper._playerSprite`, `._monsterSprite`, `._unlitSpriteMaterial`,
   `BuildingObject.s_urpSpriteMat`). Eliminadas: el backlog de estáticos baja de 4.
2. `DomainReloadStaticResetTests.NoNewStaticEscapesTheSubsystemRegistrationRule` — la nueva
   tabla `AmbientLitSortingLayers`. Marcada `[SelfHealingStatic]`: array de literales que nadie
   muta después de init.
3. `AssetConventionsTests.HardRules_ResourcesRoot_OnlyContainsWhitelistedEntries` —
   `DayNightProfile.asset` no estaba en la lista blanca de `Resources/`. Añadido a las dos
   listas que el comentario dice mantener sincronizadas.

Ese último fallo destapó además una **divergencia previa**: `CameraFeelProfile.asset` estaba en
la lista del test de C# pero no en la del script de Python, así que el lint llevaba tiempo
marcándolo. Sincronizado de paso.

Nota metodológica: una pasada intermedia mostró 5 fallos en TileEditor / MapEditor /
ParticlesEditor que **desaparecieron en la pasada limpia**. Eran contaminación por orden de
ejecución mientras el Editor se usaba en paralelo (el total de tests creció de 5866 a 5906 a
mitad de sesión). El error "Releasing render texture that is set as Camera.targetTexture!" lo
emite `EditModeRunner.cs:246`, el propio framework de Unity, no código del proyecto.

### Decisión de nivel de noche (tomada)

La noche autorizada original (`nightIntensity = 0.15`, luminancia media 0.050) era
deliberadamente muy oscura — la premisa "la noche es negra para que las antorchas importen".
Con solo 10 luces en todo el mundo y ninguna en el pueblo, eso dejaba el pueblo ilegible.

**Decidido: subir la noche a 0.32 con un azul algo más claro** `(0.30, 0.36, 0.62)`, el registro
de Stardew / Terraria — luminancia media 0.115, se ve todo pero es noche sin ambigüedad. Las
luces colocadas pasan a ser acento en vez de necesidad, lo que también quita presión a la
autoría de la Fase 2. El valor vive ahora en la rampa del perfil, no en un literal de C#.

## Estado: Fase 1 completada (2026-08-25)

El modelo cromático pasa de dos literales en C# a un asset editable.

**Nuevo `DayNightProfile`** (`Scripts/Data/World/DayNightProfile.cs`, asset en
`Resources/DayNightProfile.asset`): un `Gradient` de 8 claves para el color ambiente más dos
`AnimationCurve` para intensidad y viñeta, y las 4 bandas de fase como datos. `DayNightCycle`
lo consume cuando existe y cae a los dos keyframes antiguos cuando no, con un warning que
dice exactamente qué se pierde.

Esto elimina el techo de §2: **el amanecer cálido y la golden hour ya no son inalcanzables.**

### La rampa autorizada

| t | hora | color | intensidad | viñeta | |
|---|---|---|---|---|---|
| 0.00 | 00:00 | (0.30, 0.36, 0.62) | 0.32 | 0.28 | noche |
| 0.19 | 04:33 | (0.34, 0.38, 0.74) | 0.34 | 0.25 | hora azul |
| 0.25 | 06:00 | (0.94, 0.62, 0.54) | 0.53 | 0.12 | **amanecer cálido** |
| 0.30 | 07:12 | (0.97, 0.81, 0.74) | 0.80 | 0.02 | mañana |
| 0.50 | 12:00 | (1.00, 0.99, 0.97) | 1.00 | 0.02 | mediodía ≈ identidad |
| 0.76 | 18:14 | (0.93, 0.70, 0.54) | 0.72 | 0.09 | **golden hour** |
| 0.81 | 19:26 | (0.74, 0.48, 0.59) | 0.48 | 0.19 | **malva del ocaso** |
| 0.92 | 22:04 | (0.30, 0.36, 0.62) | 0.30 | 0.28 | noche |

Luminancia media medida sobre el frame real: amanecer 0.207 · mediodía 0.348 · golden 0.255 ·
ocaso 0.174 · noche 0.107.

**Por qué esta vez no lava.** El intento de 2026 anterior teñía sin bajar la luz, así que un
tono cálido se sumaba a los blancos y producía sepia. Aquí la intensidad cae junto con el
calor — de 1.00 a 0.53 en el amanecer, a 0.48 en el ocaso — que es como se comporta la luz
real. Un frame cálido es además un frame más oscuro, y eso lo lee el ojo como hora del día en
vez de como filtro.

### Dos trampas que aparecieron al medir

- **El suavizado de tangentes sobrepasa.** `AnimationCurve.SmoothTangents` arrastra la
  pendiente de la rampa entrante más allá de la clave que debía ser el techo: la subida
  0.80 → 1.00 llegaba a **1.05** al mediodía. Con `HDREmulationScale: 1` eso no florece,
  recorta a blanco y blanquea el frame entero. `Smooth()` ahora aplana las tangentes de las
  claves de meseta, y `Sample` clampa a `MaxIntensity`. Máximo medido sobre las 24 h: 1.000.
- **`warmth` habría teñido dos veces.** Con un gradiente que ya lleva el color, el
  `nightWarmth = -0.10` heredado enfriaba la noche por segunda vez. Ahora vale 0 por defecto y
  queda como empujón vivo del autor, no como parte de la definición.

### Los sliders del editor F2 siguen mandando

`GetPhaseLook` / `SetPhaseLook` enrutan al perfil: leen la meseta de Día o de Noche y la
reescriben. Verificado en vivo — escribir un verde inconfundible en Noche devolvió ese verde,
la luz a `t=0.92` pasó a `(0.100, 0.550, 0.300)` y **Día quedó intacto**. Sin esto los sliders
habrían quedado editando campos que ya nadie lee, que es justo el tipo de control que aparenta
funcionar sin hacer nada. La escritura es en memoria; persistirla a disco sigue siendo Fase 6.

### Re-puntuación tras Fases 0 y 1

| Bloque | Antes | Ahora | Qué falta |
|---|---|---|---|
| 1. Núcleo temporal | 4.2 | **5.0** | persistencia del tiempo (0) y determinismo (3) intactos |
| 2. Modelo cromático | 1.7 | **5.8** | grading por fase sigue en 0 — es Fase 3 |
| 3. Ruta de render | 1.1 | **4.9** | post-procesado 1, sombras 0, buffer a media resolución 3 |
| 4. Luces colocadas | 2.6 | **5.6** | densidad sigue en 1: 10 luces en todo el mundo |
| 5. Atmósfera y contenido | 1.7 | 1.7 | sin tocar — Fase 4 |
| 6. Acople con gameplay | 0.0 | 0.0 | sin tocar — Fase 5 |
| 7. Herramientas y HUD | 3.3 | **3.9** | autoría sigue sin persistir (0) |
| **Global** | **2.0** | **4.3** | |

Las secciones §1–§7 de abajo conservan la puntuación **original** a propósito: son la línea
base contra la que se mide el progreso, no un estado vivo.

## Estado: Fase 2 completada (2026-08-25)

Objetivo: que la noche tenga fuentes de luz propias. El resultado principal no es más código,
es que **las farolas del mundo por fin alumbran**.

### Las luminarias se iluminan solas

El mundo ya tenía una familia entera de props `Buildings/lights/` — 33 templates de braseros,
farolas, apliques y linternas — que eran **pura decoración**: una farola dibujada a brillo pleno
a medianoche que no emitía nada, con las luces reales del mundo en otra zona del mapa.

Ahora un `BuildingTemplateData` puede declarar `lightPresetKey`, y cada colocación de ese prop
genera su propia Light2D vía `WorldLightLoader.RegisterDerivedLight`. La luz hereda gratis la
puerta día/noche, el parpadeo y el culling por viewport que ya tenían las luces autoradas, y al
autor no le cuesta nada más que colocar el prop.

- **32 de 33 templates configurados.** El único excluido es `lantern_iron_glass`: mirando los
  33 sprites, es el único cuyo arte está apagado. Los demás ya se dibujan ardiendo.
- **11 pares con cambio de sprite día/noche.** El arte viene en parejas
  (`lamp_post_ornate` ↔ `lamp_post_ornate_lit`), así que `litAssetPath` hace que la luminaria
  arda más fuerte de noche y se apague de día. Colocar la variante `_lit` da un prop siempre
  encendido; colocar la base da uno que sigue al ciclo.
- **Nuevo preset `Candle`** (radio 1.8 u, intensidad 0.22) para que un candelabro no alumbre
  como una hoguera: solo había Torch / Lamp / Magic, y el más pequeño era demasiado grande.

### Las luces derivadas no ensucian los datos autorados

`LightInstance.persistent` distingue una luz escrita en `light_instances.json` de una
reconstruida desde su fuente. `SaveAll` salta las segundas, porque si no cada guardado
duplicaría un poco más las luminarias.

Verificado con round-trip real: 15 luces en memoria (10 autoradas + 5 derivadas) →
`SaveAll` escribió **10**, y los 10 registros salieron **byte a byte idénticos** a los que
entraron. Se hizo copia de seguridad del fichero antes de la prueba.

### Niveles retuneados con el juego delante

A los valores que traían, las cinco farolas reventaban su núcleo a blanco puro en cuanto el
brillo pintado del sprite `_lit` se sumaba a la luz aditiva. Medido sobre el frame real, la
mitad de intensidad y 0.8× el radio lee como charco cálido:

| Preset | Intensidad | Radio | |
|---|---|---|---|
| Torch | 0.70 → **0.35** | 160 → **128 px** (4.0 u) | braseros, antorchas, apliques de fuego |
| Lamp | 0.60 → **0.30** | 192 → **154 px** (4.8 u) | farolas, linternas de cristal |
| Magic | 0.80 → **0.40** | 128 → **102 px** (3.2 u) | linternas azules |
| Candle | — | **56 px** (1.8 u) | candelabros (preset nuevo) |

Resultado sobre el pueblo a medianoche: `avgLum` 0.140, **0.00 % de píxeles reventados**, y la
ratio noche/día en la zona sube a 0.292 frente al 0.111 del ambiente puro — esa diferencia es
exactamente la luz que las farolas devuelven.

### Sombras 2D: renderizan bien, pero la geometría del caster no sirve

Se implementó el camino completo — `castsShadows` / `shadowStrength` en el preset,
`ShadowCaster2D` sobre la mitad *footprint* de cada edificio sólido (URP autogenera la forma
desde el `Renderer`, sin reflexión), y las luminarias excluidas porque su propia luz nace
encima de su footprint.

**Corrección de una conclusión anterior.** La primera medición dijo "no renderizan nada" y era
**inválida**. `ShadowCaster2D.IsLit()` no usa `pointLightOuterRadius`: usa
`light.boundingSphere.radius`, un campo no serializado que escribe **solo `Light2D.LateUpdate()`**
(`Light2D.cs:175`, `:373-388`, `:439`). La luz de sondeo se creaba y se renderizaba dentro de la
misma llamada, sin que pasara un frame, así que su `boundingSphere` seguía siendo el valor por
defecto: centro `(0,0,0)`, **radio 0**. Con radio 0 `IsLit` no puede ser cierto jamás. Y un
`hasShadow` falso y una sombra de área cero producen **ambos** un delta de exactamente 0.0000
(`LightingUtility.hlsl:71-77`), así que aquella medición no podía distinguir un caso del otro.
La medición de escena completa, la que dio **0.0006 y no cero**, era la pista de que el pase sí
producía píxeles.

Repetida en condiciones — luz creada en una llamada, medida en otra, con
`boundingSphere.radius = 10.000` verificado — sobre un recorte cerrado del pueblo:

| | Sombras ON | Sombras OFF |
|---|---|---|
| Luminancia media del recorte | 0.3163 | 0.3345 |
| Píxeles que cambian > 0.01 | **11.01 %** | — |
| Delta máximo por píxel | **0.5685** | — |

**Las sombras funcionan.** Lo que no funciona es la forma del caster. URP la deriva de los
*bounds* del `Renderer`, o sea un rectángulo que incluye todo el margen transparente del sprite,
así que cada edificio proyecta una cuña rectangular de borde duro. En la captura los artefactos
grandes vienen de los edificios grandes, no de props pequeños, de modo que filtrar por tamaño no
lo arregla.

Se quedan en **`castsShadows = false`** en los cuatro presets: sin ningún caster creado, coste
cero. Para que fueran viables harían falta siluetas reales — la vía natural sería usar como
caster la **rejilla de colisión pintada** que `BuildingCollisionLoader` ya genera por celda, que
es la huella verdadera del edificio en el suelo. Y aun así conviene recordar que el género no las
usa: ni Stardew Valley, ni Graveyard Keeper, ni CrossCode proyectan sombras dinámicas de
edificios en vista cenital, porque un sprite visto desde arriba no tiene una geometría de
oclusión coherente.

De paso se corrigió una trampa real en el enganche: esperaba 2 frames fijos y pillaba **0 de
170** edificios porque `BuildingLoader` reparte el spawn. Ahora espera a que el recuento se
estabilice, y con eso engancha los **137** casters elegibles solo. Verificado.

### Re-puntuación tras Fases 0, 1 y 2

| Bloque | Auditoría | Tras F0+F1 | Ahora | Qué falta |
|---|---|---|---|---|
| 1. Núcleo temporal | 4.2 | 5.0 | 5.0 | persistencia del tiempo (0) |
| 2. Modelo cromático | 1.7 | 5.8 | 5.8 | grading por fase (0) — Fase 3 |
| 3. Ruta de render | 1.1 | 4.9 | 4.9 | post-procesado (1), sombras (0) |
| 4. Luces colocadas | 2.6 | 5.6 | **7.4** | densidad sigue baja fuera del pueblo |
| 5. Atmósfera y contenido | 1.7 | 1.7 | **3.0** | luciérnagas, niebla, audio por fase |
| 6. Acople con gameplay | 0.0 | 0.0 | 0.0 | sin tocar — Fase 5 |
| 7. Herramientas y HUD | 3.3 | 3.9 | 3.9 | autoría sin persistir (0) |
| **Global** | **2.0** | **4.3** | **4.7** | |

## Estado: Fase 3 completada (2026-08-25)

Grading de pantalla en un solo blit. Es la mitad del look que un `Light2D` en Multiply **no puede
producir**: un multiply oscurece y tiñe, pero no puede drenar saturación, no puede recontrastar
lo que acaba de aplastar, y no puede hacer dithering. Esas tres cosas son la diferencia entre
"la pantalla se ha puesto más oscura y más azul" y "es de noche".

### Qué se construyó

| Pieza | Qué hace |
|---|---|
| `Shaders/ScreenGrade.shader` | Viñeta, contraste en LogC alrededor del gris medio ACEScc, lift/gamma/gain, saturación y dither ordenado 4×4 — en el orden que usa el propio UberPost de URP, para que el look siga siendo portable |
| `Core/Rendering/ScreenGradeFeature.cs` | `ScriptableRendererFeature` inyectada en `AfterRenderingPostProcessing` **sin offset** (Renderer2D compara ese valor por igualdad literal) |
| `Core/Rendering/ScreenGradePass.cs` | Un blit con el swap de buffer de color de URP |
| `Core/Rendering/ScreenGradeSettings.cs` | Los valores vivos. Estático porque la feature debe vivir en `Valkur.Core` y el ciclo vive en `Valkur.Gameplay`: Gameplay puede referenciar Core, nunca al revés |
| `DayNightProfile` | Dos curvas nuevas, saturación y contraste, consumidas por el pase |

**No es un Volume de URP.** El proyecto mantiene `renderPostProcessing` apagado porque UberPost
cuesta ~18 ms/frame en una GPU media incluso con el Volume a peso 0. Las renderer features se
despachan desde `RenderSingleCamera` independientemente de ese flag, así que esto corre sin
reactivar el stack — y el Volume de la secuencia de muerte sigue funcionando igual (verificado:
con post-procesado activado el frame sigue renderizando, 0.1079 vs 0.1097 de luminancia media).

### La rampa de grading

| hora | saturación | contraste | viñeta |
|---|---|---|---|
| 00:00 | 0.72 | 1.10 | 0.62 |
| 06:00 | 0.86 | 1.01 | 0.25 |
| 12:00 | **1.00** | **1.00** | 0.04 |
| 18:43 | 0.90 | 1.04 | 0.26 |
| 22:04 | 0.71 | 1.10 | 0.62 |

El contraste se aplica en **LogC**, no en lineal: contrastar en lineal aplasta las sombras de un
frame nocturno a negro puro, mientras que en espacio log conserva el pie de la curva, que es
justo lo que importa a intensidad ambiente 0.3.

### Medición

- **Mediodía es no-op.** `avgLum` 0.3459 con grade frente a 0.3460 sin él, y **cero píxeles**
  difieren más de 0.05. El día sigue leyéndose en colores nativos, como estaba diseñado.
- **La noche sí cambia.** `avgLum` 0.1094 frente a 0.1402, y la saturación media del frame baja
  visiblemente: sin grade la escena es *día con el brillo bajado* — el naranja de los árboles y
  el verde de la hierba siguen saturados. Con grade el croma se drena y lee como visión nocturna.
- **Coste: 0.215 ms/frame** (mediana, A/B intercalado de 9 rondas × 40 frames a 1920×960;
  0.109 ms comparando mejor caso contra mejor caso). Para escala: los ~18 ms de UberPost que
  motivaron apagar el stack.

La primera implementación hacía copia + blit de vuelta y medía 1.6 ms. La variante de un solo
blit con swap hace el mismo dibujo leyendo y escribiendo un target HDR de 64 bits la mitad de
veces.

### La viñeta uGUI se retira

`DayNightVignetteOverlay` era un sprite RGBA de **64×64 estirado a pantalla completa** — él mismo
una fuente de banding: 64 téxeles de degradado radial ampliados a 1080p y cuantizados a 8 bits
antes de llegar al frame. Ahora se desvanece automáticamente cuando detecta que la feature está
instalada (`ScreenGradeSettings.FeaturePresent`), porque dos viñetas oscurecen los bordes dos
veces. Se conserva como respaldo para un renderer que no lleve la feature.

## Re-puntuación tras Fases 0-3

| Bloque | Auditoría | Ahora | Qué falta para el 10 |
|---|---|---|---|
| 1. Núcleo temporal | 4.2 | **7.5** | persistir la hora en el save (0); preview en Edit Mode |
| 2. Modelo cromático | 1.7 | **9.0** | nada estructural; queda afinar la rampa con ojos de artista |
| 3. Ruta de render | 1.1 | **8.0** | sombras con silueta real; buffer de luz a resolución completa |
| 4. Luces colocadas | 2.6 | **8.0** | densidad fuera del pueblo (autoría, no código) |
| 5. Atmósfera y contenido | 1.7 | **3.0** | luciérnagas/niebla, audio por fase, clima acoplado |
| 6. Acople con gameplay | 0.0 | **0.0** | spawners, IA, vendedores, música, minimapa |
| 7. Herramientas y HUD | 3.3 | **5.5** | persistir la autoría; fusionar Ctrl+F3 en F2 |
| **Global** | **2.0** | **6.4** | |

Los dos bloques que más pesan ahora son atmósfera (3.0) y acople con gameplay (0.0). Ninguno es
un problema de render: son contenido y conexiones.

### Otro arreglo, fuera del subsistema

La suite de tests de partículas **no sembraba la aleatoriedad en ningún sitio** — cero
apariciones de `randomSeed` o `useAutoRandomSeed` en sus 25 ficheros — mientras sus asertos
comparan centésimas de unidad de mundo. En dos pasadas completas consecutivas fallaron **tests
distintos** (`torch_embers` por 0.065 u, luego `water_fountain_small` por 0.024 u), y ambos
pasaban aislados. `ParticleTestDeterminism.PinRandomness` fija ahora la semilla en los 12 puntos
donde esas cuatro fixtures construyen un emisor, preservando el estado de reproducción porque
otra fixture asserta justamente que `ApplyPreset` deja el sistema sonando. Tres pasadas completas
seguidas en verde después del arreglo.

Una suite que parpadea no distingue una regresión real del ruido, que es exactamente para lo que
esas fixtures existen.

## Blindaje de las fases mediante tests (2026-08-25)

Las fases quedaron como se querían, así que el siguiente riesgo no es afinarlas: es **perderlas
otra vez sin enterarse**. Todo lo que las rompió durante meses falló en silencio — sin excepción,
sin error en consola, solo un mundo a brillo de mediodía a las 03:00. Estos tests convierten cada
uno de esos fallos silenciosos en un fallo ruidoso.

### `DayNightPhaseLookTests` — 14 tests sobre el asset que se envía

Lee `Resources/DayNightProfile.asset`, no la rampa que construye el código. La distinción importa:
alguien puede arrastrar una clave del gradiente en el Inspector y aplanar el amanecer sin tocar
una línea, y solo un test que lea el **asset** se entera.

Pinea **características, no literales** — "el amanecer lidera con rojo", no "el amanecer es
exactamente (0.94, 0.62, 0.54)". Un test de literales falla en cada retoque deliberado y acaba
borrado en una semana; uno de características falla solo cuando la fase deja de ser esa fase.

Lo que cubre: los cinco momentos del panel F2; que la intensidad sube por el amanecer y baja por
el ocaso **sin invertirse en ningún punto** (una rampa puede tener los extremos bien y hundirse en
medio, y eso se ve como un parpadeo del sol); que el día es al menos el doble de brillante que la
noche; que la viñeta se abre a mediodía y se cierra de noche; y que la saturación llega a 1 al
mediodía y baja de noche sin sobrepasar nunca 1.

El más importante: **`TheDayHasARealWarmPeak_NotJustAWhiteToBlueLerp`**. Interpolar entre blanco
diurno y azul nocturno solo puede viajar por el segmento recto entre ambos, así que `r - b` no
puede superar nunca su valor en blanco. Si ese test falla, la rampa ha colapsado a una
interpolación y **todas** las fases cálidas han desaparecido — que es exactamente el estado del
que se partió.

### `DayNightPipelineWiringTests` — 10 tests sobre la fontanería

Cada aserto corresponde a un defecto que se envió de verdad:

- Las constantes de `Light2D.LightType` valen lo que el proyecto supone. Si una actualización de
  URP las renumera, falla a gritos en vez de apagarse el mundo otra vez.
- La escena lleva **exactamente una** luz Global (cero = el ambiente no llega a nada; más de una =
  URP registra un error de luz global duplicada y elige arbitrariamente).
- La máscara de sorting layers de esa luz cubre **todas** las capas que el bootstrap ilumina, y
  toda capa de tilemap renderiza en una de ellas. Un renderer lit en una capa fuera de la máscara
  no sale tenue: sale **negro**.
- `WorldSpriteMaterials` elige lit cuando hay luz ambiente y unlit cuando no — la decisión exacta
  que un método llamado `ApplyUnlitFallbackIfNeeded` respondía "unlit" incondicionalmente.
- El blend style 1 sigue siendo Additive, porque las luces colocadas entran ahí precisamente para
  **sumar** fotones.
- La feature `ScreenGrade` sigue instalada en el renderer y con su shader asignado, y el shader
  compila.
- Ningún preset de luz tiene un `falloff` fuera de `[0,1]` (URP lo clampa y los presets dejan de
  distinguirse entre sí) ni el radio interior pegado al exterior.

Si uno de estos falla, las fases han desaparecido **aunque todos los tests de fase sigan en
verde**: la rampa se calcula bien y se entrega a nada.

### `TimeWeatherPhaseShortcutTests` — 11 tests sobre el panel F2

El panel son dos arrays paralelos que solo un comentario mantiene alineados: las etiquetas y horas
en `CYCLE_ROWS`, y los tiempos en `CYCLE_NORMALIZED_TIMES`. Inserta una fila en uno y no en el otro
y todos los botones de debajo saltan a la hora equivocada, con la interfaz leyéndose correctamente.

Y un botón puede ser internamente coherente y aun así mentir: "Dawn 05:30" solo es cierto mientras
las 05:30 caigan dentro de la banda de amanecer. Mover una banda convierte la etiqueta en ficción
sin nada que lo detecte. Los tests asertan el viaje de ida y vuelta completo: botón, hora escrita,
y la fase que el ciclo **realmente** reporta ahí. Más que la ventana de encendido de antorchas
coincide con las bandas, que es donde había 35 minutos de noche cerrada con todo apagado.

### Un bug real que apareció por el camino

Con el editor F2 abierto en la partida, abrir el editor de iluminación Ctrl+F3 desde el menú ESC
lanzaba `NullReferenceException` en `BuildPresetsPanel` **antes de dibujar un solo preset**.
`Activate` lo capturaba y registraba una línea, así que el panel simplemente no aparecía.

Causa: `UIFactory.MakeScrollView` ya pone un `VerticalLayoutGroup` y un `ContentSizeFitter` en el
contenido que devuelve. Ambos son `[DisallowMultipleComponent]`, así que un segundo `AddComponent`
devuelve **null** en vez de lanzar — y la línea siguiente lo desreferencia. El fallo aparece una
línea después del error, en una línea de aspecto inocente. Estaba en los dos paneles con lista
(presets e instancias). `AssetThumbnailGrid` ya resolvía la misma trampa destruyendo el grupo
previo: alguien la sufrió antes y el editor de iluminación nunca recibió el arreglo.

Arreglado con get-or-add, barrido el resto del proyecto en busca del mismo patrón (no había más),
y cubierto por `LightingEditorUIBuilderTests`, que construye la UI entera llamando a `BuildAll`
directamente — ahí la excepción propaga y tumba el test, en vez de que se la trague el `try/catch`
de `Activate`.

**Total: 40 tests nuevos.** Suite completa 5966/5966 en verde.

## Escala de puntuación

| Nota | Significado |
|---|---|
| 0 | No existe, o existe y produce cero efecto observable |
| 1–2 | El código existe pero es inerte o incorrecto |
| 3–4 | Funciona parcialmente, con defectos visibles |
| 5–6 | Correcto pero anodino; no aporta identidad visual |
| 7–8 | Bueno, nivel profesional |
| 9–10 | Referencia; argumento de venta del juego |

## 1. Núcleo temporal — 4.2 / 10 (peso 15 %)

| Aspecto | Nota | Evidencia |
|---|---|---|
| Modelo de tiempo y API pública | 6 | `t ∈ [0,1)`, `HourOfDay`/`MinuteOfDay`, 3 eventos estáticos, control completo (`SetTimeNormalized`, `Pause`…). Limpia y suficiente. `DayNightCycle.cs:105-142` |
| Determinismo / reproducibilidad | 3 | Acumulación float de `Time.deltaTime` escalado, sin paso fijo ni ancla de reloj. Dos partidas a distinto FPS divergen. Sin contador de días. `DayNightCycle.cs:294` |
| Persistencia del tiempo | 0 | `GameSaveData` no tiene ningún campo de tiempo. `Persist => false`. Toda recarga vuelve a `0.35` (08:24). `DayNightCycle.cs:264,277` |
| Higiene domain-reload | 9 | Los 3 delegados estáticos y el `_instance` se resetean en `SubsystemRegistration`. Cumple la convención del proyecto. `DayNightCycle.cs:256-262` |
| Coste por frame | 3 | Dos `PropertyInfo.SetValue` por frame, sin guarda de epsilon ni throttle, boxeando `Color` + `float` ≈ 3 asignaciones GC/frame incluso pausado o en pleno mediodía constante. `DayNightCycle.cs:397-398` |

## 2. Modelo cromático — 1.7 / 10 (peso 20 %)

| Aspecto | Nota | Evidencia |
|---|---|---|
| Keyframes de color | 2 | **Solo dos** para las 24 h: Día `(1,1,1)@1.00`, Noche `(0.20,0.25,0.45)@0.15`. Amanecer y ocaso son lerps puros entre ambos, así que el color solo puede viajar por el segmento recto RGB blanco→azul: **ningún tono cálido es alcanzable en ningún momento del ciclo**. `DayNightCycle.cs:71-86` |
| Interpolación / rampa | 5 | `Mathf.SmoothStep` sobre la fracción de banda: C1-continua, sin saltos. Pero Día y Noche son planos (40 % y 34 % del ciclo sin ninguna expresión). `DayNightCycle.cs:456-488` |
| Espacio de color y precisión | 2 | Lerp RGB por canal de literales float, sin conocimiento de gamma pese a que el proyecto es Linear. La noche efectiva es un multiplicador de 8 bits `(7,10,18)`: cualquier degradado colapsa a ~7–18 escalones por canal. Sin dithering en ningún punto. |
| Warmth (amanecer/ocaso cálidos) | 0 | `dayWarmth = 0.00`, `nightWarmth = -0.10`, y se interpola *entre esos dos*, así que `ApplyWarmth` solo puede restar rojo y sumar azul. El comentario de clase promete "warm pinkish" y "orange tint"; la implementación no puede producirlos. `DayNightCycle.cs:75,84,500-508` |
| Datos como ScriptableObject | 1 | Todo el tuning vive en inicializadores de campo C#, y como el componente se crea con `AddComponent` en runtime **no hay ningún asset ni instancia de escena que un diseñador pueda editar**. Los mismos literales están duplicados a mano en `TimeWeatherEditor.Settings.cs:194-195`. Contrasta con `LightPresetDefinition`, que sí es SO. |
| Grading por fase (saturación/contraste) | 0 | No existe. El único "grade" disponible es un multiply por canal, que puede oscurecer pero no desaturar ni recontrastar. `GoldenMorning`, `GoldenEvening` y `BlueHour` están declarados en el enum y **nunca se producen**. `DayNightCycle.cs:105,443-446` |

## 3. Ruta de render URP 2D — 1.1 / 10 (peso 25 %)

Este es el bloque que hunde el sistema entero.

| Aspecto | Nota | Evidencia |
|---|---|---|
| Light2D global correcto | 0 | La luz de la escena es `m_LightType: 3` (**Point**, radio 1 unidad, en el origen). El código que la crearía usa `Enum.ToObject(enumType, 1)` con el comentario "1 = Global" — en URP 14 el 1 es **Freeform**; Global es **4**. Y nunca se ejecuta, porque `if (FindObjectOfType(light2DType) != null) return;` sale antes. `DayNightCycle.ResolveGlobalLight()` busca `val == 1`, no lo encuentra jamás, y cae en `all[0]`. `GameplaySceneSetup.Systems.cs:58,80,100` · `DayNightCycle.cs:347` |
| Cobertura: materiales lit | 0 | **La condición bajo la que los tiles se fuerzan a unlit es: siempre.** El `IfNeeded` del nombre es vestigial; no hay ninguna sonda de Light2D en el método. Las 8 capas de tilemap renderizadas, edificios, jugador, monstruos, drops, orbes, proyectiles y VFX son todos unlit. `Valkur/SpriteHDRTint` declara `Lighting Off`. En el mundo abierto, ~0 % de los píxeles son lit-shaded; la ruta lit solo vive dentro de los prefabs de Catacombs (191 slots de material). `WorldGridBuilder.cs:63-83` |
| Máscara de sorting layers | 1 | Incluye ids `{0..8}` = Default, Ground, Entities, Projectiles, VFX, UI_World, Overlay (más 2 ids obsoletos que no corresponden a ninguna capa). Quedan fuera Background, FloorDecals, ObjectsLow, WallsBottom, Decorations, WallsTop, ObjectsHigh, Overhead, EntitiesOverhead. Se congeló cuando solo existían las 8 capas originales. |
| Blend styles utilizados | 2 | Los 4 slots están declarados, pero **solo el 0 (Multiply) lleva luces**. Un pipeline solo-multiply puede oscurecer pero nunca añadir fotones: no hay derrame cálido, ni charcos de luz coloreada sobre suelo oscuro. Los estilos 1 (Additive), 2 y 3 están libres y no cuestan nada hoy. `Renderer2D.asset:28-40` |
| Resolución del buffer de luz | 3 | `m_LightRenderTextureScale: 0.5` — el buffer de luz 2D se renderiza a media resolución y se sube por interpolación. En un mundo de 32 PPU eso es un suelo de suavidad/bloqueo visible. |
| HDR / headroom | 2 | `m_HDREmulationScale: 1` + `m_ColorGradingMode: 0` (LDR). Cualquier intensidad > 1 recorta a blanco plano en vez de florecer. El slider de brillo del editor F2 llega a 1.5 y esa mitad superior no hace nada útil. |
| Post-procesado | 1 | No existe ningún `VolumeProfile` en el proyecto ni ningún `Volume` en la escena. La cámara ships con `m_RenderPostProcessing: 0`. El único Volume es el de la secuencia de muerte, creado en runtime y activado bajo demanda para evitar los ~18 ms/frame de UberPostProcess documentados en `unity-performance/SKILL.md:114-116`. Sin bloom, sin tonemapping, sin color grading. La "viñeta" es un sprite uGUI de 64×64 estirado a pantalla completa. `GrayscaleVolumeController.cs:30-38` |
| Sombras 2D | 0 | Cero `ShadowCaster2D` en todo el proyecto; las dos únicas referencias son sondas de perf que solo cuentan instancias. La luz de escena tiene `m_ShadowIntensityEnabled: 0`. La noche no puede tener información direccional. |

## 4. Luces colocadas — 2.6 / 10 (peso 10 %)

| Aspecto | Nota | Evidencia |
|---|---|---|
| Renderizado de luces puntuales | 0 | `ApplyPresetToLight` escribe `Enum.ToObject(enumType, 2)` = **Sprite**, no Point. Una luz Sprite sin `lightCookieSprite` hace `mesh.Clear()` y dibuja una malla vacía. **Las 10 antorchas del mundo no rasterizan nada.** Esto anula la premisa de diseño entera ("la noche es oscura para que las antorchas importen") que justifica `nightIntensity = 0.15` y `minIntensity = 0.08`. `WorldLightLoader.cs:566` |
| Esquema de presets | 4 | `LightPresetDefinition` es un SO correcto (radius, intensity, falloff, color, flicker, centerScale). Pero `falloff` se autoriza en 1.6–2.2 y URP lo clampa a `[0,1]`, así que **los 3 presets son idénticos en falloff**; `Magic` tiene `centerScale = 1.00`, o sea inner == outer, sin degradado; y `radius` se documenta como "world units / px÷16" mientras el código lo divide por 32. |
| Flicker | 5 | Seno puro con fase aleatoria por instancia. Funciona, pero un seno no parece fuego; falta ruido. `WorldLightLoader.cs:191-209` |
| Acople día/noche | 3 | `SetActive` duro on/off, sin rampa. Además desincronizado de las bandas: la noche empieza en `0.84` (20:10) pero las antorchas no se encienden hasta `0.8646` (20:45) — **≈88 s reales con noche cerrada y todas las luces apagadas**, la ventana más fea del ciclo. |
| Densidad de luces | 1 | 10 luces en todo el mundo (9 Torch, 1 Magic). Insuficiente para que la noche tenga estructura aunque se arregle el render. |

## 5. Atmósfera y contenido — 1.7 / 10 (peso 10 %)

| Aspecto | Nota | Evidencia |
|---|---|---|
| Partículas atmosféricas por fase | 1 | `DayNightAtmosphericParticles` fue eliminado en `1d3594596` por leerse como clima. La decisión fue correcta en su momento, pero dejó el hueco vacío: no hay luciérnagas, ni motas de polvo, ni bruma baja. |
| Tinte ambiental en partículas | 4 | Funciona y está bien diseñado (sondeo a 2 Hz en vez de eventos, deliberado). Pero solo **29 de 154** presets optan por él, y el suelo por canal de `0.25` impide que la vegetación se oscurezca de verdad. `ParticleEmitter.AmbientLight.cs:32` |
| Clima visual | 6 | Real y decente: lluvia (1200 partículas, stretch billboard, fade de impacto), nieve (Perlin noise, "melt" por tamaño), viento; todos con texturas procedurales y caja de emisión adaptada al viewport cada frame. |
| Clima como sistema | 1 | Solo existe si un humano pulsa el panel F2. Sin autonomía, sin persistencia, sin tests, sin acople a nada. Nada lee `IsActive`. No hay niebla, ni nubes, ni tormenta real (Wind+Rain = "storm" es solo un comentario). |
| Audio ambiental por fase | 0 | `DayNightAmbientAudio` está escrito entero — dos `AudioSource` con crossfade de 4 s, mapa fase→clip — y es **completamente inerte**: los 4 `AudioClip` son `[SerializeField]`, el bootstrap hace `AddComponent` sin prefab ni instancia de escena, así que siempre son `null` y toda transición cruza de silencio a silencio. `AudioCatalog.asset` no contiene ninguna pista `ambient_dawn/day/dusk/night`. |
| Música día/noche | 0 | `MusicScopeOverride.ScopeType` solo tiene `{ Zone, Level, Biome }`. No existe ninguna pista nocturna. |
| Emisivos (ventanas, faroles) | 0 | `BuildingObject.Assembly.cs:206` asigna un único `Sprite-Unlit-Default` compartido. No hay segundo slot de sprite ni canal emisivo. |

## 6. Acople con gameplay — 0.0 / 10 (peso 10 %)

Ningún sistema de juego reacciona a la hora. Verificado uno a uno:

| Sistema | ¿Reacciona? | Dónde engancharía |
|---|---|---|
| Spawners de monstruos | No — `SpawnerTemplateData` no tiene ningún campo de tiempo/fase | Filtro de fase en `WaveSpawnEntry` + comprobación en `SpawnerInstance.UpdateActive` |
| IA / visión de NPC | No — `aggro_range` se fija **una vez** desde `EntityStats` | `FSMMonsterBrain.cs:95`, re-`SetContext` desde `OnPhaseChanged` |
| Música | No | `ScopeType.TimeOfDay` en `AudioCatalogSO.cs:174-187` |
| Vendedores / horarios | No | Sin campo de tiempo en `VendorConfigDefinition` |
| Quests | No | Sin campo de tiempo en `QuestDefinition` |
| Minimapa | No — paleta fija | `MinimapManager.Drawing.cs:40-46` |
| Cámara / exposición | No | Sin hook |
| Hechizos / auras | No | Sin hook |

Además hay **superficie ya construida y sin usar**: `OnLightingEnabledChanged` (cero suscriptores),
`AdvanceMinutes`, `SetMinuteOfDay`, `Pause()`/`Resume()` (cero llamantes pese a que el doc promete
"útil en mazmorras"), `HourOfDay` (solo leído por los tests) y 3 miembros muertos del enum `DayPhase`.

## 7. Herramientas de autoría y HUD — 3.3 / 10 (peso 10 %)

| Aspecto | Nota | Evidencia |
|---|---|---|
| Editor F2 Time & Weather (UX) | 7 | Bien construido y bien cableado: barra de menú, 4 paneles arrastrables, tutorial, hotkey rebindable, registro en `GameEditorManager` y en el lanzador ESC. Velocidad 1×–100× con sincronía en espacio log. |
| Editor Ctrl+F3 Lighting (panel ciclo) | 4 | Su parte única (catálogo de presets, spawn/select/delete, undo 50, `Ctrl+S`) es sólida; su panel de ciclo es redundante. |
| Coherencia entre editores | 2 | 6 de ~10 controles del panel de ciclo de Ctrl+F3 apuntan a las mismas propiedades que F2, **con valores distintos**: "Dawn" es 06:00 en uno y 05:30 en el otro; `RealSecondsPerDay` se edita continuo 60–7200 en uno y en 7 presets discretos en el otro. Peor: `ToggleAmbient` secuestra `MinIntensity` como interruptor falso y al restaurar escribe un `0.20f` cacheado que contradice el `0.08f` que el ciclo lleva de fábrica — **apagar y encender el ambiente sube silenciosamente el suelo nocturno**. |
| Persistencia de autoría | 0 | Sin `PlayerPrefs`, sin `StreamingAssets`, sin SO. Cada sesión de tuning se pierde al salir de Play. El editor es una UI de autoría sin salida de autoría. |
| Preview en Edit Mode | 0 | Sin `[ExecuteAlways]`. La Scene View muestra lo que quedara la última vez. |
| Reloj HUD | 7 | Enteramente procedural (anillo, disco, sol de 8 rayos, luna creciente por resta de discos, todo con AA), read-only, sin `GraphicRaycaster`, paleta fija a propósito para seguir siendo legible bajo cualquier tinte. Resta: su ventana de "día" (0.20–0.80) no coincide con las bandas del ciclo (0.18–0.84), así que el icono cambia hasta 58 minutos antes de tiempo, y `TrianglePointerSprite()` son 54 líneas sin usar. |
| Viñeta | 5 | Funciona, con suavizado exponencial independiente del framerate. Pero su RGB es el color de la luz verbatim, así que hereda todas las limitaciones de §2 y nunca puede aportar el borde cálido complementario que fingiría un sol bajo. Sprite de 64×64 estirado a pantalla completa: eso es en sí mismo un techo de banding. |
| Tests | 3 | Solo `DayNightCycleTests.cs` (clasificación de fases). Cero tests de `TimeWeatherEditor`, `WeatherManager`, `WorldLightLoader` o de la ruta de render. |
| Comentarios vs implementación | 2 | Al menos 5 comentarios mienten sobre el código que documentan: "1 = Global" (es Freeform) ×3, la viñeta dice ser "más fuerte en amanecer/ocaso" cuando es monótona Día→Noche, `LightPresetDefinition` documenta el radio en unidades de mundo cuando el código lo trata como píxeles, y `TimeWeatherEditor` sincroniza cada frame contra un escenario que `ToggleExclusive` hace imposible. |

## Puntuación agregada

| Bloque | Nota | Peso | Aporte |
|---|---|---|---|
| 1. Núcleo temporal | 4.2 | 15 % | 0.63 |
| 2. Modelo cromático | 1.7 | 20 % | 0.33 |
| 3. Ruta de render URP 2D | 1.1 | 25 % | 0.28 |
| 4. Luces colocadas | 2.6 | 10 % | 0.26 |
| 5. Atmósfera y contenido | 1.7 | 10 % | 0.17 |
| 6. Acople con gameplay | 0.0 | 10 % | 0.00 |
| 7. Herramientas y HUD | 3.3 | 10 % | 0.33 |
| **Global** | **2.0** | 100 % | **2.01** |

Leído de otra forma: la capa de autoría y UI puntúa ~5, la capa de render puntúa ~1. Se ha
construido un panel de control excelente para una máquina que no está enchufada.

## Por qué falló el intento anterior

El commit `cb8391c94` colapsó un modelo cinemático de 6–7 fases (con GoldenHour y BlueHour) a
dos keyframes porque producía "un lavado sepia uniforme y escenas ilegibles". Ese diagnóstico
era correcto **pero la causa se atribuyó al modelo de color, no a la ruta de render**.

Con la ruta actual, el único mecanismo disponible es un multiply de pantalla completa sobre un
mundo que ya está a brillo pleno y sin ninguna fuente aditiva que lo contrarreste. Bajo esa
restricción, *cualquier* modelo cromático rico produce un lavado: no hay forma de que un
multiply cálido oscurezca las sombras sin teñir también las luces. Por eso reducir a dos
keyframes "mejoró" las cosas — eliminó la expresión que no se podía sostener.

**La conclusión operativa: no tiene sentido volver a tocar el modelo de color antes de arreglar
la ruta de render.** Con luces aditivas sobre ambiente multiplicativo y un grade por fase, el
amanecer puede ser cálido sin lavar nada, porque el calor entra por la capa aditiva y por el
grade, no por el multiply.

## Hoja de ruta

Ordenada por relación impacto/coste. Cada fase es entregable y verificable por separado.

### Fase 0 — Reparación (impacto máximo, coste mínimo)

Nada aquí es diseño; es corregir constantes equivocadas. Es la fase que hace que el sistema
exista.

1. **Corregir las tres constantes de enum.**
   - `GameplaySceneSetup.Systems.cs:80` y `:100` — `Enum.ToObject(enumType, 1)` → `4` (Global).
   - `DayNightCycle.cs:347` — `if (val == 1)` → `val == 4`.
   - `WorldLightLoader.cs:566` — `Enum.ToObject(enumType, 2)` → `3` (Point).

   Mejor aún: sustituir los tres literales mágicos por una constante compartida y documentada
   (`Light2DTypes.Global = 4`, `Light2DTypes.Point = 3`), porque el error se repitió tres veces
   por copia.

2. **Arreglar la luz de la escena.** `MainGameplay.unity` tiene una Light2D añadida a mano que
   quedó en Point/radio 1. Debe ser Global, intensidad 1, blend style 0. Decidir un único dueño:
   o la escena la autoriza y el bootstrap solo valida, o el bootstrap la crea y la escena no la
   lleva. Hoy conviven mal (el bootstrap sale por `return` temprano por culpa de la de escena).

3. **Reconstruir `m_ApplyToSortingLayers`.** Incluir todas las capas de mundo
   (Background, Ground, FloorDecals, ObjectsLow, WallsBottom, Entities, Decorations, WallsTop,
   ObjectsHigh, EntitiesOverhead, Projectiles, VFX) y **excluir** deliberadamente UI_World,
   Overhead y Overlay, que deben seguir legibles a cualquier hora. Purgar los 2 ids obsoletos.
   Iluminar solo algunas capas produce el artefacto clásico de 2D: un muro blanco de mediodía
   sobre un suelo azul de noche.

4. **Convertir el mundo a lit.** `ApplyUnlitFallbackIfNeeded()` debe volver a hacer honor a su
   nombre: sondear si existe una Global Light2D válida y **solo entonces** dejar los tilemaps en
   `Sprite-Lit-Default`, cayendo a unlit únicamente si no la hay. Aplicar el mismo criterio a
   edificios y entidades. `Valkur/SpriteHDRTint` necesita una variante lit o pasar el tinte HDR
   por `MaterialPropertyBlock` sobre un shader lit.

5. **Arreglar los presets de luz.** Rango de `falloff` a `[0,1]` en `LightPresetDefinition`;
   `Magic.centerScale` a ~0.15; renombrar `radius` a `radiusPixels` o pasar a unidades de mundo
   y migrar los 3 assets; corregir los tooltips.

6. **Alinear la ventana de encendido de luces con las bandas de fase**, para cerrar los ~88 s
   reales de noche cerrada sin antorchas.

**Verificación de la fase 0:** con `mcp__unity__execute_code`, confirmar en Play que
`Light2D.lightType == Global`, que `TilemapRenderer.sharedMaterial.shader.name` contiene `Lit`,
y capturar un frame a `t = 0.5` y otro a `t = 0.92`. Si no se distinguen, la fase no está hecha.
Recordar el gotcha del proyecto: **una consola limpia no es una compilación exitosa** — validar
que el ensamblado nuevo está cargado antes de fiarse de cualquier medición.

### Fase 1 — Modelo cromático dirigido por datos

1. **Nuevo `DayNightProfile` ScriptableObject.** Es la corrección de fondo a §2: un `Gradient`
   para el color ambiente y `AnimationCurve` para intensidad, alfa de viñeta, saturación y
   contraste. Un `Gradient` da claves ilimitadas y trae el editor de Unity gratis, así que
   resuelve de golpe "solo dos keyframes" y "nada es ScriptableObject". Un segundo `Gradient`
   para el tinte de la capa aditiva (el "sol") permite el amanecer cálido que hoy es imposible.
   Elimina también la duplicación de literales en `TimeWeatherEditor.Settings.cs`.

2. **Evaluar en espacio lineal** y añadir dithering temporal (o suficientes claves) para matar
   el banding de la noche.

3. **Subir `m_LightRenderTextureScale` a 1.0** y medir. En un juego 2D sin decenas de luces, el
   coste debería ser marginal frente a la ganancia de nitidez en el falloff.

### Fase 2 — Capa de luz aditiva

1. **Mover las luces colocadas al blend style 1 (Additive).** Es la receta estándar de
   iluminación 2D y exactamente lo que hoy falta: el ambiente multiplicativo oscurece, las
   antorchas aditivas devuelven fotones cálidos y forman charcos de luz. El estilo 1 ya está
   declarado y libre.

2. **Subir `m_HDREmulationScale` por encima de 1** para dar margen a intensidades > 1 y que una
   antorcha pueda "quemar" en vez de recortar a blanco.

3. **`ShadowCaster2D` en los colliders de edificios y muros.** El renderer ya permite
   `m_MaxShadowRenderTextureCount: 1`. Es lo que convierte la noche de un tinte plano en una
   escena con información direccional. Empezar por edificios (pocos, grandes, alto retorno)
   antes de tocar tiles.

4. **Densificar las luces del mundo.** 10 luces no bastan. Esto es trabajo de autoría con el
   editor Ctrl+F3, no de código.

### Fase 3 — Grading de pantalla

1. El bloqueo real es el coste de UberPostProcess (~18 ms documentados). En lugar de activar el
   stack completo de URP, escribir **un único `ScriptableRendererFeature`** que haga un solo blit
   full-screen: grade por fase (saturación/contraste/lift-gamma-gain) + viñeta + dither. Un blit
   ronda las décimas de milisegundo y además **sustituye el sprite uGUI de 64×64**, que es hoy
   un techo de banding por sí mismo. Esto da lo que un multiply nunca podrá dar: desaturar la
   noche y recontrastarla.

2. Si se quiere bloom en las antorchas, evaluarlo contra el coste medido con solo Bloom activo
   en LDR, no contra la cifra de Ultra que hay documentada.

### Fase 4 — Atmósfera y contenido

1. **Luciérnagas de noche, motas de polvo de día, bruma baja al amanecer.** Requiere un campo de
   puerta temporal en `ParticleVfxParams` + comprobación en el loader de instancias. Reintroducir
   con criterio lo que se quitó en `1d3594596`: el error anterior fue emitir una nube ancha en
   todo el viewport; lo correcto es poco, localizado y solo en las fases donde tiene sentido.

2. **Ventanas emisivas en edificios**, conmutadas por `OnLightsEnabledChanged` (segundo slot de
   sprite, o un sprite hijo en la capa VFX con material aditivo).

3. **Audio ambiental por fase de verdad**: enrutar los 4 clips por `AudioCatalog` en vez de
   `[SerializeField]`, y crear las entradas `ambient_dawn/day/dusk/night`. Hoy la maquinaria está
   escrita y suena a silencio.

4. **Clima**: añadir niebla y tormenta, implementar `ResolveAudioClip()` (el hook existe y ninguna
   subclase lo sobrescribe), acoplar el clima al ambiente (la lluvia oscurece y desatura) y darle
   autonomía con un planificador.

5. **Sol/luna en el mundo**, no solo como icono de HUD. Es lo que aporta la señal direccional
   que hoy no existe en ningún sitio.

### Fase 5 — Acople con gameplay

1. Filtro de fase en las oleadas de spawner; multiplicador de `aggro_range` nocturno vía
   `SetContext` desde `OnPhaseChanged`; horarios de vendedor; `ScopeType.TimeOfDay` para música;
   tinte del minimapa. Cada uno es pequeño; juntos son los que hacen que la noche *importe* en
   vez de solo verse distinta.

### Fase 6 — Persistencia y unificación de herramientas

1. **Serializar `timeNormalized` + `dayCount` en `GameSaveData`** (subir `schemaVersion`).
   Aplicar la política de snapshots defensivos antes de tocar el esquema de guardado.

2. **Dar salida de autoría al editor F2**: escribir sobre el asset `DayNightProfile` (ruta de
   Editor) o sobre `StreamingAssets/World/daynight_profile.json` vía `IRepository`, como el
   resto de editores en runtime.

3. **Fusionar el panel de ciclo de Ctrl+F3 dentro de F2.** Un solo dueño de `DayNightCycle`.
   Ctrl+F3 se queda con lo suyo: colocar y editar luces.

4. **Preview en Edit Mode** (`[ExecuteAlways]`), para no tener que entrar en Play para juzgar
   una paleta.

5. Limpiar la deuda: 3 miembros muertos del enum `DayPhase`, `TrianglePointerSprite()`, los
   comentarios que mienten, la ventana de "día" del reloj HUD desalineada con las bandas, y la
   guarda de epsilon que falta en la escritura por frame del Light2D.

## Orden recomendado

La fase 0 sola cambia el juego más que todo lo demás junto, y no requiere ninguna decisión de
arte. Es la única fase que debería ejecutarse sin discutir prioridades.

| Fase | Impacto visual | Coste | Riesgo |
|---|---|---|---|
| 0 · Reparación | Muy alto | Bajo | Bajo — corrige constantes; el riesgo es que al volver lit aparezcan tiles negros donde falte cobertura de luz |
| 1 · Modelo cromático | Alto | Medio | Bajo |
| 2 · Luz aditiva + sombras | Muy alto | Medio | Medio — hay que medir coste GPU de sombras |
| 3 · Grading de pantalla | Alto | Medio | Medio — presupuesto de frame |
| 4 · Atmósfera | Medio-alto | Alto (autoría) | Bajo |
| 5 · Gameplay | Bajo visual, alto de diseño | Medio | Medio — toca balance |
| 6 · Persistencia y herramientas | Nulo visual, alto de higiene | Medio | Medio — toca esquema de guardado |

## Registro de verificación

Los hallazgos marcados como críticos se verificaron a mano sobre los ficheros, no solo por
reporte de agente:

| Afirmación | Verificado en |
|---|---|
| La luz de escena es Point radio 1, máscara ids 0–8 | `MainGameplay.unity:652,659,678` |
| `Light2D.LightType`: Freeform=1, Sprite=2, Point=3, Global=4 | `Light2D.cs:41-57` (URP 14.0.12) |
| Bootstrap escribe el valor 1 creyendo que es Global | `GameplaySceneSetup.Systems.cs:80,100` |
| `DayNightCycle` busca el valor 1 como Global | `DayNightCycle.cs:347` |
| `WorldLightLoader` escribe el valor 2 (Sprite) para las antorchas | `WorldLightLoader.cs:566` |
| El unlit de tilemaps es incondicional | `WorldGridBuilder.cs:63-83` |
| La cámara no hace post-procesado | `MainGameplay.unity:200` |
| Cero `ShadowCaster2D`; solo referencias en sondas de perf | grep sobre `_Project` |
| No existe ningún `VolumeProfile` propio | grep sobre `*.asset` |
