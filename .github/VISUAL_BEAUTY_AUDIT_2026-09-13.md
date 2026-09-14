# Belleza del mundo — auditoría visual y cinco ideas para un roguelike épico

> Auditoría del ASPECTO del juego en marcha: lo que el jugador ve en el mundo, no en los
> paneles (esos ya tienen sus auditorías propias: HUD, inventario, minimapa, grimorio, menús).
> Puntuación rigurosa 0–10 por eje, hallazgos con `fichero:línea`, y cinco ideas con su
> diseño de implementación sobre ESTE pipeline (URP 14 2D Renderer, Unity 2022.3.62f1, Linear).
>
> Fecha: 2026-09-13 · Método: tres barridos paralelos del código (pipeline de render, mundo,
> entidades y combate) más tres capturas reales del Lobby en Play Mode a 08:35, 19:38 y 00:32
> (`unity/Valkur/Captures/beauty_audit_{day,dusk,night}.png`, sin seguimiento en git).
> Una cuarta captura con tormenta no llegó a hacerse: otra sesión reinició Play Mode a mitad.

## Resumen ejecutivo

**Puntuación global: 3.4 / 10.**

El juego es NÍTIDO y está BIEN SENTIDO, pero es PLANO. Los cimientos que cuestan meses ya
están: escalera de PPU con snap a píxel, atlas sin comprimir, un director de cámara con
trauma, kick, lead y hit-stop, un ciclo día/noche que por fin ilumina, clima real en tres
capas con nieve que se acumula por copo, y una biblioteca de VFX de hechizos que ya sabe
dibujar una columna de ki y un embudo de vórtice. Lo que falta es exactamente lo que
distingue una captura "correcta" de una captura que alguien pone de fondo de pantalla:

1. **No existe ni una sombra en todo el juego.** Ni de entidad, ni de edificio, ni de nube,
   ni de antorcha. `ShadowCaster2D` está cableado (`BuildingObject.Light.cs:94-113`) y
   muerto: los cuatro presets de luz traen `castsShadows: 0`. Un mundo sin sombras no tiene
   sol, y un mundo sin sol no tiene hora aunque el reloj diga las 19:38.
2. **No existe el cielo.** Nada se dibuja por encima del mundo: cero nubes, cero rayos,
   cero luna, cero niebla. La noche es un multiply por color más una viñeta
   (`DayNightVignetteOverlay.cs:22`). Medido en la captura de las 00:32: la antorcha del
   patio apenas tiñe cuatro losas, y las ventanas de la casa siguen apagadas.
3. **Nada se mueve salvo lo que tiene FSM.** Los árboles son dos sprites estáticos
   (`BuildingObject.cs:122-127`), el agua es un tile fijo (no hay ningún `AnimatedTile` en
   el proyecto), no hay fauna, no hay hierba que se aparte, no hay polvo bajo los pies.
   Las 196 partículas ambientales colocadas viven en 13 zonas; el resto del mundo está quieto.
4. **El post-proceso es un grade sin bloom.** `ScreenGrade.shader` hace viñeta, contraste,
   saturación y dither (medido 0.215 ms), y el buffer HDR está activado
   (`UniversalRP.asset:26`) — así que cada VFX aditivo por encima de 1.0 llega al framebuffer
   con energía de sobra y NADA la hace florecer. Es la palanca más barata del documento.
5. **Golpear no deja marca.** Los números de daño son un TMP a tamaño 4 en un solo color,
   sin crítico ni elemento (`FloatingDamageSpawner.cs:14-16`, `:49-52`); los ocho estados
   alterados son un tinte; una muerte oscurece el último frame; no hay sangre, quemadura ni
   huella en el suelo.

Ninguno de los cinco es un bug. Son capas que no se han construido, y las cinco ideas del
final son esas capas, en el orden en que más cambian la pantalla por milisegundo gastado.

## Estado: Fases 0 a 7 implementadas (2026-09-13)

Cuatro commits el mismo día, cada uno con consola limpia, suite del área en verde y
captura real de verificación. Lo que hay ahora en `main`:

| Fase | Commit | Qué se envió | Medido |
|---|---|---|---|
| 1 Bloom | `2efcda70e` | `Hidden/Valkur/ScreenBloom` dentro de `ScreenGradeFeature`, pirámide dual-Kawase a media resolución, compuesto antes del grade | Umbral fijo 1.0: delta 20/255 sobre una antorcha de noche. Umbral que sigue a la ambiente BASE (1.0 de día, ~0.48 de noche, suelo 0.45 + 0.22 de margen para charcos de luz) |
| 2 Sombras | `2efcda70e` | `SunModel` (puro), `SunShadowCaster` (el propio sprite cizallado desde los pies + blob de contacto), `CloudShadowLayer` (quad multiply bajo `Projectiles`), `SkyStyle.asset` | 649 casters vivos en el Lobby; a las 08:00 skew -0.76 al oeste. Trampa cazada: `SetPropertyBlock` sin `GetPropertyBlock` pisó `_MainTex` y la sombra fue el RECT entero |
| 4 Mundo | `b884ebc90` | `_VALKUR_SWAY` en ambos shaders HDR (`ValkurWind.hlsl`), `WindSway` publicado desde el tick del clima, `BuildingWindSway` por categoría, `FootstepEmitter` + `FootstepDust`, sombras y polvo en monstruos | 184 de 324 copas se mecen; 78 % de píxeles de copa cambian entre dos fotogramas contra 16 % del tronco |
| 3 Noche | `1d4909d95` | `PlayerLantern` (Point aditiva, contra-escalada, a los pies), `FireflyField` | A 0.62 y al pecho el cuerpo se iba a blanco; a 0.24 y a los pies el personaje se lee y el suelo también |
| 6 Zona | `1d4909d95` | `ScreenFade` (Core/UI) en el swap de interiores, `ZoneBannerHUD` + `ZoneBannerRules` | La regla de 200 téxeles cruzaba tres cuartos de pantalla; ahora mide la palabra |
| 7 Momentos | `21784f382` | Crítico en `Health` y en las cuatro rutas de acción, números con sombra y color por elemento en `UI_World` sobre el span de las barras, `StatusEffectVisuals`, `_Dissolve` en los shaders HDR, `WorldPickup.Launch`, `BossPhaseController.Burst`, antorcha 0.55/160 px | 1529/1529 en Combat+Enemies+WorldDrops+Spells+Player+AI+Lighting |

Todo lleva interruptor de sesión en `look` (`look bloom|clouds|sunshadows|sway|lantern|fireflies|footsteps on|off`) y su afinación en `Resources/SkyStyle.asset`.

### Lo que sigue abierto tras esta pasada

- **Agua viva y suelo mojado (Idea 3)**: no tocado. El modelo a copiar es `SnowSplatMap`.
- **Ventanas emisivas y sombras URP de luna (Idea 4, mitad)**: no tocado; necesita una máscara por template y el caster desde la rejilla de colisión.
- **Fauna**: no hay arte de pájaros ni mariposas; las luciérnagas son el único ser vivo nuevo.
- **Sonido de pasos por superficie**: el catálogo no tiene ids; `FootstepEmitter` deja la costura (`GroundKind`) y no llama a nada.
- **Grade y calima por zona, cine de jefe con letterbox**: no tocado.
- **Orden del crítico contra la mitigación**: `CritResolver` multiplica ANTES de la defensa, como hacía ya en el melee enviado. Multiplicar después (como hace la vulnerabilidad) es más coherente y es una decisión de balance pendiente, no un fallo.
- **`light_instances.json` con `lobby`/`Lobby`**: es un fichero escrito por el editor de Lighting; se corrige desde él, no a mano.
- **Sombra de nube sobre `Overhead` y niveles 9-15**: el quad va bajo `Projectiles` para no atenuar lo emisivo; los tiles pintados por encima quedan sin sombra de nube.

## Lo que ya está bien (y no hay que tocar)

| Pieza | Por qué cuenta |
|---|---|
| Nitidez pixel-perfect: escalera `SnapOrthoSize`, `CameraPixelSnap`, atlas RGBA32 | Cada texel es un número entero de píxeles; nada bailotea al moverse |
| `CameraFeelDirector`: trauma, kick, lead con deadzone, hit-stop, 14 cues | El combate PESA aunque no se vea sangre |
| Clima: lluvia/nieve/viento en 3–5 capas de profundidad, rayo global, nieve acumulada | Es el único subsistema del mundo que ya lee como "un lugar", no como "un tile" |
| VFX de hechizos: flourish por familia, `VortexFunnelFX`, `KiAuraFX`, `FlameConeFX`, `DashStreakFX` | Las formas dicen lo que hacen; la dirección de arte HD-sobre-pixel funciona |
| Mundo espiritual (`SpriteDesaturate` con el altar en color) | Un ejemplo de lo que da tener UN dueño del look de toda la escena |
| Antorchas con flicker (`WorldLightLoader.cs:411-431`) y blend Additive | Suman fotones en vez de multiplicar; la base correcta para todo lo nocturno |

## Puntuación por eje

Escala: 0 = no existe · 3 = existe pero no se nota · 5 = correcto · 7 = bueno · 9 = referente.

| # | Eje | Nota | Estado medido | Palanca |
|---|---|---|---|---|
| 1 | Nitidez y fidelidad de píxel | **8** | PPU en escalera, snap, atlas sin comprimir, `PixelPerfectCamera` sustituido a propósito (`CameraSetup.cs:165-168`) | Ninguna urgente |
| 2 | Sensación de cámara | **7** | Trauma/kick/lead/hit-stop; zoom de rueda con snap. Sin modo cine, letterbox fijo 2:1 (`AspectRatioEnforcer.cs`) | Letterbox bajo demanda para jefes y diálogos |
| 3 | VFX de hechizos | **7** | Ver tabla anterior. Falta bloom que los remate, y 47 de 83 hechizos con `range: 0` | Bloom (Idea 5) |
| 4 | Clima | **7** | Lluvia/nieve/viento reales, rayo, nieve acumulada por copo. Sin suelo mojado, sin charcos, sin niebla | Suelo mojado (Idea 3), niebla baja (Idea 4) |
| 5 | Feedback de golpe (flash, hit-stop, knockback, sacudida) | **7** | `CombatFeedback.HitFlash.cs`, impulso 4 N, chispas direccionales en la barra. Ningún `CameraFeel.Cue` explícito en combate: solo por eventos globales | Cue por tipo de golpe |
| 6 | Ciclo día/noche (luz global) | **6** | Ilumina de verdad desde 2026-08-25; gradiente de 8 claves. Pero la noche es multiply+viñeta: sin luna, sin sombras, sin ventanas | Idea 1 y 4 |
| 7 | Luces colocadas | **5** | 4 presets con flicker, 31 luces, **en 3 zonas de ~30**. `lobby`/`Lobby` partidas por mayúscula en `light_instances.json`. Ni cookies ni sombras ni ventanas | Ventanas emisivas, luces por regla |
| 8 | Muerte y cadáveres | **4** | `GrayscaleDeath` conserva el tono; el mundo espiritual es bello. Sin caída, sin disolución; `DeathDropSystem` no dibuja nada al morir | Disolución + estallido de botín |
| 9 | Variedad del suelo / repetición de tiles | **4** | Corner16 en 7 packs. En la captura de día la hierba repite su patrón cada 2 tiles; `ObjectsLow/Decorations/WallsBottom/OverheadDetails` **vacíos** en el Lobby | Variantes de tile, decoración procedural |
| 10 | Post-proceso | **3** | Viñeta, contraste LogC, saturación, dither. `InverseGamma` siempre 1 (`DayNightCycle.cs:703`). Sin bloom, sin LUT, sin grano, sin aberración | Idea 5 |
| 11 | Presencia del personaje | **3** | `DashStreakFX` y `FacingIndicator` excelentes; `CursorImpactFX`. Sin sombra, sin polvo, sin pasos, sin respiración | Idea 2 |
| 12 | Números de daño | **3** | `fontSize = 4f` fijo, sin fuente, sin contorno; 3 colores; `wasCrit` existe y nadie lo pasa (`CritResolver.cs:28`) | Crítico grande, color por elemento, contorno |
| 13 | Recogida y botín | **3** | Moneda con imán y bob; `XpOrb` bien vestido. `WorldPickup` sin imán, sin brillo, sin haz de rareza | Haz de rareza, imán, estallido |
| 14 | Jefes | **3** | Barra con fantasma de daño y pips de fase. Sin intro, sin tarjeta de nombre, sin flash de fase (`BossPhaseController.cs` no tiene ningún VFX) | Cine de jefe (Idea 5) |
| 15 | Nivel / misión / eventos del mundo | **3** | Todo ocurre en el HUD (`PlayerHUD.Motes.cs:125-160`); en el mundo, nada. Completar una misión es un toast | Pilar de luz al subir, fanfarria de misión |
| 16 | Vida ambiental (fauna, luciérnagas, motas) | **2** | 153 presets; ambientales = polen y hojas. 196 instancias en 13 zonas, 52 hojas + 82 polen. **Sin fauna**: los pájaros solo se oyen (`DayNightAmbientAudio.cs:21`) | Idea 2 |
| 17 | Estados alterados visibles | **2** | Ocho tintes de `SpriteTintStack` y un glifo de 7x8 texels. `ThrallMarkEffect` no tiene NINGÚN visual | Partículas por estado |
| 18 | Transiciones (zona, interior, escena) | **2** | Solo el fade-OUT de la pantalla de carga; entrar en una casa es un corte seco (`WorldTransitionService.cs`); ninguna cartela de zona; `ZonePortal` es un collider sin sprite | Fundido, cartela, portal visible |
| 19 | Interiores | **2** | Salas desnudas, una por puerta (`BUILDING_DOORS_ROADMAP.md`) | Mobiliario, luz de hogar, polvo en rayos |
| 20 | Sincronía audio-visual | **2** | Solo `BossBeatChoreographer`; nada del mundo reacciona a la música ni al rayo salvo la luz global | Pulso de luces con el compás del jefe |
| 21 | Agua | **1** | Existe como TERRENO (`WaterTileIndex.cs:33`) para pescar. Sin shader, sin cáusticas, sin espuma, sin ondas, sin reflejo. `PP_water_flow_*` autorados y **0 colocados** | Idea 3 |
| 22 | Vegetación y viento | **1** | Árboles = edificios estáticos; el viento solo mueve precipitación (`WeatherWind.cs`). Hojas colocadas a mano, no ancladas a ningún árbol | Idea 2 |
| 23 | Decals y huellas en el suelo | **1** | `FloorDecals` es un tilemap pintado a mano: 10 celdas en el Lobby, 23 en Forest. Cero decals en runtime | Idea 3 (charcos) + sangre/quemadura |
| 24 | Borde del mundo | **1** | Negro puro `CameraSetup.cs:81-83`, elegido para ocultar costuras de chunk. Sin niebla, sin degradado, sin abismo | Banda de niebla al borde |
| 25 | Cielo y atmósfera (nubes, sol, luna, rayos, niebla) | **0.5** | Cero código de cielo en todo `Scripts/`. Lo único "de cielo" es `SkyFlash`, un boost transitorio de la luz global | Idea 1 |
| 26 | Sombras (entidad, edificio, nube, antorcha) | **0** | Ninguna. `ShadowCaster2D` cableado y desactivado por datos; sin blob, sin proyección, sin AO en la base de los muros | Idea 1 |

Media aritmética de los 26 ejes: **3.4**. Los seis ejes por encima de 6 son todos de
"sensación" (cámara, golpe, hechizos, clima, nitidez); los diez por debajo de 2.5 son todos
de "lugar" (cielo, sombras, agua, vegetación, fauna, suelo, borde). El juego se SIENTE mejor
de lo que se VE, y esa asimetría es la noticia buena: la mitad cara ya está pagada.

## Detalle de los hallazgos que más pesan

### El sol no proyecta nada

- `WorldLightLoader.cs:366` arranca `AttachShadowCastersOnce()` solo si `ShadowsInUse`, y
  `ShadowsInUse` (`:1362-1372`) es verdadero solo si algún preset autoriza `castsShadows`.
  Los cuatro (`LightPreset_{Candle,Lamp,Magic,Torch}.asset`) dicen `0`. La luz global tiene
  `m_ShadowIntensityEnabled: 0` (`MainGameplay.unity:709`).
- `DAY_NIGHT_AUDIT_AND_ROADMAP.md` ya midió por qué se apagaron: URP deriva la forma del
  caster de los BOUNDS del renderer, así que cada edificio arroja una cuña rectangular. La
  respuesta no es encender lo que hay, es darle a URP la silueta de verdad (Idea 1 y 4).
- Sin sombra bajo los pies, el personaje FLOTA sobre el empedrado en las tres capturas; es
  lo primero que cualquier artista señala en la de las 08:35.

### La noche no tiene fuentes

- 31 luces en el mundo, en tres zonas. Con `zones_database.json` creciendo solo, cada zona
  nueva nace a oscuras a las 22:00 con nada que diga "aquí vive alguien".
- Las ventanas de la casa del Lobby están pintadas en el sprite como cristal oscuro y siguen
  oscuras a las 00:32. Una ventana encendida es la señal más barata de "noche habitada" que
  existe, y ningún template la puede expresar: `BuildingTemplateData` tiene `lightPresetKey`
  (un punto de luz) pero no una máscara emisiva.
- El jugador no lleva luz. A medianoche fuera de las tres zonas iluminadas está a `minIntensity
  = 0.08` y no ve dónde pisa; eso no es "noche", es "monitor apagado".

### El HDR está encendido y nadie lo cobra

- `UniversalRP.asset:26` HDR on, precisión 1; `m_RenderPostProcessing: 0` en la cámara a
  propósito (UberPost cuesta ~18 ms). `ScreenGradeFeature` demostró que un pass propio
  cuesta 0.215 ms. Un bloom dual-Kawase a media resolución en ese mismo feature cuesta
  0.3–0.5 ms y hace que CADA aditivo del proyecto (antorchas, hechizos, orbe de XP,
  `WeaponSwapFlashFX`, futuras ventanas) florezca sin tocar ni un prefab.

### Nada crece ni se mueve

- `grep sway|bend|rustle|foliage` sobre `Scripts/`: cero. Un árbol es `BuildingObject`, dos
  sprites, y `Sprite-Lit-Default`. Sin un shader propio no hay forma de mecerlo, y el shader
  propio ya existe: `SpriteHDRTintLit` es el que llevan los edificios con nieve. Un
  `_SwayAmp` más en ese shader alcanza a las 1176 plantillas.
- `WeatherWind` ya calcula UNA ráfaga por fotograma para toda la escena. Es la señal que
  falta enchufar a la vegetación, al humo de las chimeneas y a las hojas colocadas.

### Golpear no deja rastro

- `FloatingDamageSpawner.OnDamaged(int)` es la única ruta; el crítico se resuelve en
  `CritResolver` y se pierde ahí. Tamaño 4 fijo sin contorno: en la captura de noche un
  número rojo sobre un edificio rojo no se lee.
- Estados: `BurnEffect.cs:55-60` es un tinte naranja. Un enemigo ardiendo debería arder.
- `DeathDropSystem.cs` deja pickups en el suelo sin un solo píxel de estallido; la moneda
  hereda el bob de `CoinPickup`, el resto no tiene ni imán.

## Cinco ideas para un roguelike épico

Cada idea es una CAPA con un único dueño, diseñada para el pipeline que hay, con su coste en
milisegundos y las trampas de este repositorio que la esperan. Orden = impacto por coste.

### Idea 1 — El cielo existe: sombras de nubes, sol que gira y rayos entre las copas

**Qué ve el jugador.** Manchas de sombra grandes y blandas cruzan el mapa despacio en la
dirección del viento; entre ellas, el sol pinta charcos de luz cálida. Al amanecer cada
árbol, casa, poste y personaje tira una sombra LARGA hacia el oeste que se acorta hasta ser
un blob a mediodía y se alarga hacia el este al atardecer. Bajo las copas del bosque, haces
de luz oblicuos con motas de polvo. En un día nublado las nubes se juntan hasta apagar el sol;
en una tormenta desaparecen bajo el gris de `WeatherGrade`. De noche, nada de esto — la noche
es de la Idea 4.

**Por qué es épica.** Es lo que convierte "un tilemap" en "un paisaje bajo un cielo". Es
también la señal de HORA más fuerte que existe: una sombra larga dice "amanecer" sin mirar
el reloj del HUD. Y las nubes dan al mapa un movimiento lento y grande que ninguna partícula
puede dar, el que hace que una captura quieta parezca viva.

**Cómo se construye aquí.**

- `CloudShadowLayer` (`World/Sky/`): UN quad que sigue a la cámara, un sorting layer nuevo
  `SkyShadow` insertado por encima de `Overhead` y por debajo de `UI_World`, material
  multiply (`DstColor / Zero`), **unlit y en `AmbientUnlitSortingLayers` con su razón**
  (una sombra iluminada por la luz que oscurece es un bucle). El shader muestrea 2 octavas
  de ruido de valor por POSICIÓN DE MUNDO (no de pantalla, o las nubes se pegan al cristal)
  desplazado por `WeatherWind` y `_Time`. Tres entradas: cobertura (0.15 en despejado, 0.9
  con `Rain/Snow` activo), contraste (0 de noche, máximo a mediodía, leído del gradiente de
  `DayNightProfile`), y `IsIndoors` → 0. Coste: un full-screen quad, ~0.1 ms.
- `SunShadowCaster` (`Combat/WorldUI/` para entidades, `World/Buildings/` para edificios):
  un `SpriteRenderer` hijo con el MISMO sprite que el cuerpo, shader `SpriteShadowProjected`
  (color sólido negro, alfa 0.35, y una matriz de CIZALLA en el vértice anclada en los pies:
  `x' = x + y * tan(azimut)`, `y' = y * cos(elevación) * squash`). Dirección y longitud
  salen de una sola función `SunVector(timeNormalized)` en `DayNightCycle`. Se dibuja en la
  capa del cuerpo, un orden por debajo, y se desactiva de noche y bajo techo. Para el
  edificio, la sombra se proyecta desde su mitad FOOTPRINT (el tronco), no desde la copa, o
  el árbol arroja la sombra de su corona desde el suelo. Coste: un sprite por entidad visible,
  sin luz adicional.
- `CanopyRays` (`World/Sky/`): sprites aditivos alargados bajo cada `BuildingObject` cuyo
  template declare `castsCanopyRays` (árboles grandes), con máscara de ruido que respira,
  ángulo del mismo `SunVector`, alfa por cobertura de nubes (invertida) y hora. Motas: un
  `PP_dust_rays` nuevo, aditivo, lento, solo en el haz.

**Trampas conocidas que esperan aquí.** `Z_SKY` NO es un `sortingOrder` (tres apariciones
ya). Un sorting layer nuevo se añade por el objeto vivo de TagManager fuera de Play Mode, con
`SetDirty` + `SaveAssets`, o se pierde al siguiente Play. La máscara de la luz ambiente es
DERIVADA: la capa nueva queda lit por defecto y hay que ponerla en la denylist. Y una sombra
proyectada del sprite ANIMADO cambia de forma por frame: es correcto (la sombra del brazo
sube con el brazo) y es lo que la hace parecer real.

### Idea 2 — El mundo respira: viento en la vegetación, polvo, fauna y una sombra bajo cada pie

**Qué ve el jugador.** Las copas se mecen con la ráfaga que ya mueve la lluvia, cada árbol
con su fase; cuando sopla fuerte, caen hojas DEL ÁRBOL, no del cielo. La hierba alta se
aparta al pasar y vuelve. Cada paso levanta una voluta de polvo del color del suelo (tierra,
piedra, nieve, agua) con su sonido. Pájaros picoteando en el prado que levantan el vuelo
cuando te acercas; mariposas sobre las flores de día; luciérnagas sobre el agua y bajo los
árboles de noche. Y una sombra elíptica suave bajo cada criatura, que la ancla al suelo.

**Por qué es épica.** Es la diferencia entre un decorado y un ecosistema. Un mundo que
reacciona al jugador (los pájaros huyen, la hierba cede, el polvo sube) le dice que ESTÁ
allí. Y es la capa que mejor envejece: cuanto más tiempo mira el jugador, más encuentra.

**Cómo se construye aquí.**

- Mecido: `SpriteHDRTintLit` gana `_SwayAmp` y `_SwayPhase`; el vértice se desplaza
  `sin(_Time.y * speed + worldX * 0.35 + phase) * amp * uv.y²` — la base no se mueve, la
  punta sí. `BuildingTemplateData.swaysInWind` (bool, por template, sembrado por categoría:
  árboles y arbustos sí, casas no). `WorldSpriteMaterials.WorldWithSnow` ya elige material
  por rol; gana el rol `Canopy`. La amplitud global la fija `WeatherWind.Gust` cada
  fotograma: viento Off = 0.3 texel de brisa, Heavy = 6 texels. Solo la mitad CANOPY se mece;
  el tronco es el suelo.
- Hojas ancladas: `LeafShedder` en el `BuildingObject` con `swaysInWind`, un emisor
  `PP_falling_leaf_canopy` PARENTADO a la copa (con el `LateUpdate` diferido que
  `ParticleProjectileVisual` ya documenta), tasa = ráfaga². Retira las 52 hojas colocadas a
  mano del Lobby, que caen de la nada.
- Hierba que cede: `GrassBendMap`, clon de `SnowSplatMap` (R8, sigue a la cámara): cada
  entidad estampa su posición cada fotograma con un disco de 1 u; el shader de la capa
  `ObjectsLow` (donde viven las matas) desplaza el vértice hacia fuera del centro del sello.
  Decae solo. Mismo coste que la nieve (~0.1 ms).
- Pasos: `FootstepEmitter` en `PlayerController` y en `FSMMonsterBrain`, disparado por
  velocidad > 0.3 cada `strideLength` unidades (no por frame de animación, que difiere por
  personaje). Pregunta el terreno bajo los pies a `TerrainCatalog`/`WaterTileIndex` y
  elige puff (`PP_step_dust_{earth,stone,snow,water}`) y SFX. Un sonido por superficie: hoy
  `CombatSfxConfigSO` no tiene locomoción.
- Sombra blob: `GroundShadowBlob`, un sprite elíptico de `ElementalSprites` (un rol nuevo,
  gradiente radial negro), alfa 0.28, anchura = `SpriteRenderer.bounds` × 0.6, en la capa del
  cuerpo un orden por debajo, seguidor de los pies. **Con la Idea 1**, el blob es la sombra
  proyectada a mediodía: `SunShadowCaster` interpola entre proyección y blob por elevación,
  y de noche queda solo el blob. Una sola pieza, dos aspectos.
- Fauna: `AmbientCritter` (`World/Fauna/`), NO FSM de monstruo — tres comportamientos de 40
  líneas: `Forager` (pájaro/gallina: picotea, huye volando fuera de pantalla si el jugador
  entra en 3 u, vuelve a los 30 s), `Flutter` (mariposa de día, luciérnaga de noche: vuelo
  por ruido integrado, el mismo `Drift` del vórtice), `Perch` (cuervo en un tejado que grazna
  al pasar). Se colocan por REGLA, no a mano: `FaunaSpawnRules` por terreno y hora (prado
  de día → 2–4 pájaros por chunk; agua + noche → luciérnagas), con presupuesto por pantalla.
  Se despawnean fuera de cámara. Arte: 4–8 frames por criatura, 16 px.

**Trampas conocidas.** Un emisor reparentado en `OnEnable` queda varado (defiere al
`LateUpdate`). Las hojas colocadas (`falling_leaf_30s` × 52) hay que retirarlas del JSON al
mismo tiempo que se anclan, o llueven hojas dobles. Y `Rigidbody2D` dormido no inicia
contactos: la huida del pájaro se decide por DISTANCIA polled, nunca por trigger.

### Idea 3 — Agua viva y suelo que se moja

**Qué ve el jugador.** El canal del Lobby ondula: dos capas de cáusticas que se cruzan,
espuma clara donde el agua toca la orilla, destellos del sol al mediodía y del color del
cielo al atardecer, anillos concéntricos cuando el jugador entra, cuando cae una gota,
cuando un proyectil impacta. Cuando llueve, el empedrado se OSCURECE y coge brillo, los
huecos bajos se llenan de charcos que reflejan las antorchas, y al parar la lluvia el suelo
se seca de fuera hacia dentro durante minutos. Cuando nieva, ya se acumula; ahora la lluvia
tiene su gemelo.

**Por qué es épica.** El agua es el material que más delata a un juego plano. Un charco que
refleja una antorcha es la captura que la gente comparte. Y el suelo mojado es lo que hace
que una tormenta se sienta DESPUÉS de haber pasado — hoy, en cuanto la lluvia para, el mundo
vuelve a estar exactamente como antes, y eso le quita al clima su consecuencia.

**Cómo se construye aquí.**

- `WaterSurface` (`World/Water/`): por chunk, `WaterTileIndex` ya sabe qué celdas son agua.
  Se hornea un R8 "distancia a la orilla" (0 en la orilla, 1 a 4 tiles adentro) y se dibuja
  UN quad por chunk en `FloorDecals + 1` con el shader `ValkurWater`: dos ruidos de cáustica
  desplazados en direcciones distintas, espuma = `1 - distancia` con umbral animado,
  destello = ruido fino × `SunVector` × cobertura de nubes (Idea 1), tinte = el ambiente de
  `DayNightProfile` (así el agua se vuelve naranja a las 19:38 y azul acero a las 00:32 sin
  tocarla). Coste: un quad por chunk visible, ~0.1 ms.
- `RippleMap`: R8 que sigue a la cámara (clon exacto de `SnowSplatMap`): cada evento
  (pie que entra, gota de `RainEffect` que expira sobre agua, impacto de `SpawnImpact`)
  estampa un anillo que se expande y se desvanece en el shader `SnowSplat` (ya tiene el pass
  `ScrollFade`). `ValkurWater` lo suma como desplazamiento de normal fingido.
- `WetSplatMap` + `_WetMap` en `ValkurSnow.hlsl` (renombrar a `ValkurGround.hlsl`): la
  lluvia estampa TODO el suelo visible en proporción a su densidad; el shader multiplica el
  albedo por `1 - 0.3 * wet` y suma un brillo especular `pow(ruido, 8) * wet * luz` del color
  del cielo. Charcos: donde `wet > 0.85` Y la celda está en `FloorDecals` marcada baja (una
  máscara nueva pintable en el Tile editor, `PuddleMask`, misma herramienta que la colisión),
  el shader dibuja un reflejo invertido de la luz global tintada y de las `Light2D` cercanas
  (el 2D Renderer ya escribe las luces a RT: `_ShapeLightTexture0` está disponible en el pass
  `Universal2D`, es leerla con `uv.y` invertida en el charco). Se seca por el mismo reloj de
  fase que la nieve (`SnowAccumulation.cs:50`).
- Nieve y agua ya comparten un modelo de "mapa que sigue a la cámara y sprite que sabe dónde
  está su cielo". Con el agua, el hielo es gratis: `Snow` heavy + `WaterSurface` → la orilla
  se congela desde el borde (`1 - distancia` otra vez).

**Trampas conocidas.** Los atlas de tiles llevan `enableRotation: 0` y `padding: 2` por
la nieve; el agua depende de las dos igual. Un `RenderTexture` sampleado por el mundo tiene
que respetar `ScreenGradeFeature`'s "cámara con `targetTexture` se salta el grade". Y el
`Sprite-Unlit-Default` no tiene `_SrcBlend`: el reflejo del charco va en el shader lit propio.

### Idea 4 — La noche tiene dueños: ventanas, farol, luna con sombras y luciérnagas

**Qué ve el jugador.** Al caer el sol, las ventanas de cada casa se encienden una a una en
un cálido que parpadea como una vela detrás del cristal, y su luz cae sobre la calle. El
jugador lleva un farol: un charco de luz cálido que se balancea al andar y que es la única
cosa que ve en un bosque a medianoche. La luna baña todo en azul acero y proyecta sombras
de VERDAD — cada casa arroja su silueta pintada, no un rectángulo — y las nubes de la Idea
1 se convierten en nubes que tapan la luna. Luciérnagas sobre el agua. Una niebla baja que
se arrastra por el prado al amanecer y se disipa con el primer sol. Y por encima de todo, el
bloom de la Idea 5 hace que cada fuente brille.

**Por qué es épica.** La noche es hoy el momento más FEO del juego (captura de las 00:32:
gris uniforme, una antorcha que apenas tiñe cuatro losas, una casa a oscuras). Con fuentes,
es el más bello: contraste, color y misterio gratis. Y es un cuarto del ciclo — 15 minutos
de cada hora de juego real.

**Cómo se construye aquí.**

- Ventanas: `BuildingTemplateData.emissiveMask` (un sprite hermano `<name>_emit.png` en el
  atlas, blanco donde hay cristal). El importador de props (`build_building_props.py`) lo
  genera por HEURÍSTICA sobre el sprite (píxeles azul-oscuro rodeados de marco en la mitad
  superior de las categorías `houses`/`shops`, revisados por ojo con un contact sheet, igual
  que las bocas de casteo) y el Buildings editor lo puede pintar a mano celda a celda con la
  misma herramienta de la rejilla de colisión. En runtime, `BuildingObject.Windows`: un
  `SpriteRenderer` aditivo con la máscara, color `(1, 0.72, 0.38)` × flicker `Candle`, activo
  de `DUSK_START` a `DAY_START` con un `stagger` por hash del `PlacementId` (no se encienden
  todas a la vez: eso es un interruptor, no un pueblo), más UNA `Light2D` `Freeform` con la
  forma de la ventana proyectando al suelo. Es la primera vez que el proyecto usa `Freeform`
  y `Sprite` lights, que URP ya trae y `WorldLightLoader` nunca ha pedido.
- Farol: `PlayerLantern` en el root del jugador: `Point` radio 4.5, `(1, 0.8, 0.55)`,
  intensidad `1 - ambientIntensity` (se apaga sola de día), balanceo de 0.15 u por el mismo
  reloj que la cámara, y un `Sprite` light en cono estrecho hacia `FacingIndicator` (el farol
  se lleva delante). Los monstruos NO lo llevan: en la oscuridad se ven por sus ojos — dos
  píxeles emisivos por template (`eyeGlow`), la versión barata y más aterradora.
- Sombras de luna de verdad: `ShadowCaster2D` con la SILUETA de la rejilla de colisión
  pintada (`BuildingCollisionLoader` ya la tiene por celda): se rasteriza el contorno con un
  marching squares mínimo y se escribe en `m_ShapePath` por `SerializedObject` en el editor
  (bake, `Valkur > Buildings > Bake Shadow Shapes`) o por reflexión en runtime. Con eso,
  `castsShadows: 1` en los presets deja de arrojar cuñas y `DAY_NIGHT_AUDIT` deja de tener
  razón. La luna es la luz global con `m_ShadowIntensityEnabled: 1` a intensidad 0.45 solo
  en la banda `Night`; de día `SunShadowCaster` (Idea 1) manda y las sombras URP se apagan
  (dos sistemas de sombra a la vez es un doble contorno).
- Luciérnagas: `PP_firefly` (aditivo, `Star` shape 2 px, parpadeo por curva de alfa con
  período aleatorio, vuelo por ruido lento) colocado por regla de la Idea 2: agua o bosque,
  `Night`, sin lluvia. Con bloom, cada una es una chispa.
- Niebla baja: `GroundFog`, quad que sigue a la cámara en `ObjectsLow`, alfa por ruido
  scrolled × `1 - |hora - amanecer|` × terreno prado/agua, color del cielo. La misma técnica
  que las nubes con el signo cambiado.

**Trampas conocidas.** `ShadowCaster2D.IsLit` lee `boundingSphere.radius`, escrito solo en
`Light2D.LateUpdate`: una luz creada y medida en la misma llamada da cero. Editar TagManager
o presets durante Play se pierde al Stop. Y el `_Color` de un `MaterialPropertyBlock` que
`PlayerSpiritVisuals` deja escrito corrompió los colores de las barras una vez: la ventana
emisiva se marca `SpiritTintExempt` desde el primer día.

### Idea 5 — Luz que sangra: bloom, atmósfera por zona y el cine de los grandes momentos

**Qué ve el jugador.** Todo lo que emite luz FLORECE: la antorcha tiene un halo, el vórtice
violeta sangra sobre el suelo, las ventanas de la Idea 4 se ven desde el otro lado de la
plaza, el orbe de XP palpita. Cada zona tiene su propio aire: el bosque verde y húmedo con una
calima azul entre los troncos, la mazmorra con contraste alto y un grano fino, el lobby
cálido y limpio; cruzar de una a otra funde el look en tres segundos y una cartela con el
nombre de la zona se escribe letra a letra y se disuelve. Entrar en una casa funde a negro y
vuelve. Un jefe entra con barras de cine, su nombre en la pantalla y una sacudida; cada
cambio de fase es un flash blanco, una onda expansiva y un golpe de cámara. Un crítico es un
número GRANDE, del color de su elemento, con contorno, que rebota. Un enemigo ardiendo arde,
uno envenenado gotea, uno congelado tiene escarcha. Un muerto se disuelve en cenizas del
color de su tinte y su botín sale despedido en arco con un destello.

**Por qué es épica.** Es la capa que multiplica a las otras cuatro: sin bloom, las ventanas y
las luciérnagas son píxeles; con bloom son luz. Y es la que convierte los MOMENTOS (jefe,
crítico, muerte, cruzar un umbral) en escenas que se recuerdan. Todo lo que hay debajo ya
funciona; esto es la puesta en escena.

**Cómo se construye aquí.**

- Bloom: `ScreenGradeFeature` gana un segundo pass ANTES del grade: umbral (`1.0` en HDR, así
  solo lo que ya es más brillante que blanco florece — ninguna textura pixel-art lo cruza, los
  aditivos sí), 4 niveles dual-Kawase a ½ → 1/16 de resolución, suma con `_BloomIntensity`
  0.35 y tinte por fase (cálido de día, frío de noche, leído de `DayNightProfile`). El
  `Renderer2D.asset` ya escribe HDR con `m_HDREmulationScale: 1`. Medir como se midió el
  grade: presupuesto 0.5 ms a 1600x800, se rechaza si pasa de 0.8.
- Atmósfera por zona: `ZoneLook` (ScriptableObject, `Data/World/`, uno por zona, sembrado
  neutro para las 30): grade delta (saturación, contraste, lift/gain, viñeta), color y
  densidad de calima (`ZoneHaze`, un quad multiply+add en `ObjectsHigh` con ruido lento), y
  el tinte del bloom. `ZoneManager.OnZoneChanged` no cambia nada de golpe:
  `ZoneLookBlender` interpola durante 3 s, el mismo patrón que `WeatherManager` usa al
  cruzar. Se autoriza desde ESC → Time & Weather, junto al clima de la zona.
- Cartela de zona: `ZoneBannerHUD` en el kit del HUD (`HudPixelFont` 5x7, misma gramática
  que `HudFloatText`), aparece en el primer `OnZoneChanged` de cada zona por sesión y en
  cada vuelta tras 5 min, se escribe letra a letra, 2.5 s, se disuelve en motas. Un
  `ScreenFade` (CanvasGroup negro en un canvas propio, sortingOrder por encima de todo,
  `Core/UI/`) para `WorldTransitionService` y la puerta de cada interior: 0.25 s a negro, swap,
  0.35 s de vuelta. Hoy es un corte seco.
- Cine de jefe: `CameraFeelPreset.Cinematic` ya existe; `BossIntroDirector` lo usa: barras
  de letterbox que bajan (2:1 → 2.35:1 dentro del viewport, que es un `Rect` de cámara y no
  toca el PPU), congelación del input 1.8 s, la cámara hace lead hacia el jefe, tarjeta de
  nombre con `HudPixelFont` grande, `SkyFlash` corto. `BossPhaseController.OnPhaseChanged`
  gana `PhaseBurstFX`: flash blanco en el cuerpo por `SpriteTintStack`, anillo `Ring`
  expandiéndose desde los pies a `radius / 0.39`, trauma 0.6, hit-stop 0.12.
- Combate legible: `FloatingDamageSpawner` recibe `DamageInfo` (cantidad, crítico, elemento)
  en vez de `int`; crítico = tamaño ×1.8, contorno, rebote y color del `ElementPalette`;
  todos con `HudPixelFont` 5x7 escalado a texel entero (el TMP a tamaño 4 sin fuente no
  pertenece a un juego pixel-perfect). `StatusEffectVisuals`: un emisor por tipo,
  parentado al root con el `LateUpdate` diferido, `PP_status_{burn,poison,frost,stun,root,
  mark}` (seis presets nuevos, ≤ 12 partículas cada uno), encendido/apagado por
  `StatusEffectManager` events. `DeathDissolveFX`: sobre `GrayscaleDeath`, en el último
  segundo del cadáver, `_Dissolve` (ruido con umbral que sube) en `SpriteHDRTint` con borde
  emisivo del color del tinte y un `PP_ash_motes` que sube; `DeathDropSystem` lanza cada
  pickup en un arco corto (`rb.velocity` aleatoria + gravedad falsa durante 0.4 s) con un
  `SpawnImpact` del color de su rareza, y `WorldPickup` hereda el imán y el bob de
  `CoinPickup` más un haz vertical aditivo tenue para Rare y superior.

**Trampas conocidas.** Un cue de cámara tiene UN dueño: `CameraFeelDirector` ya decide el
peso de un casteo y un `PhaseBurstFX` que dispare el suyo dobla la sacudida. `TMP_Text.
GetPreferredValues` lanza en Edit Mode sin fuente. Y el conteo de capas aditivas es un dial
de brillo: seis emisores de estado sobre un cuerpo de 40 px, con bloom, es un cuerpo blanco
— presupuesto de alfa sumado por entidad, pinnado por test como el del embudo.

## Orden propuesto y presupuesto

| Paso | Qué | Coste estimado / frame | Por qué en este orden |
|---|---|---|---|
| 0 | Victorias rápidas (abajo) | ~0 | Dos tardes, todas visibles |
| 1 | Bloom en `ScreenGradeFeature` | 0.3–0.5 ms | Multiplica todo lo que venga después; una medición y un pass |
| 2 | Sombras: blob + proyección solar + nubes | ~0.2 ms + 1 sprite/entidad | La mayor subida de nota por línea de código; da HORA al mundo |
| 3 | Noche: ventanas, farol, luciérnagas | ~0.2 ms + luces | Convierte el peor cuarto del ciclo en el mejor |
| 4 | Viento en copas + polvo de pasos + fauna | ~0.1 ms + shader | El mundo empieza a reaccionar al jugador |
| 5 | Agua + suelo mojado | ~0.3 ms | El material que más delata; reutiliza el modelo de la nieve |
| 6 | Zona: grade, calima, cartela, fundidos | ~0.1 ms | Da identidad a 30 zonas que hoy son el mismo aire |
| 7 | Cine de jefe, crítico, estados, disolución | ~0.1 ms | Los momentos; todo lo anterior los enmarca |
| 8 | Sombras URP de luna con silueta real | 1 RT de sombra | Cierra la deuda de `DAY_NIGHT_AUDIT`; es la más cara y la última |

Presupuesto total nuevo: **≈ 1.5 ms** sobre un frame que hoy gasta 0.215 ms en look. Todo
medido con `Valkur > Profile` antes y después, como se midió el grade: un efecto que no
pasa la medición se queda fuera, no se "optimiza después".

### Fase 0 — victorias rápidas (cada una es una tarde o menos)

1. `light_instances.json`: unificar `lobby` / `Lobby` (4 luces huérfanas por mayúscula).
2. Presets de luz: subir `Torch` a intensidad 0.55 / radio 6 y medir la captura de las
   00:32 otra vez; hoy tiñe cuatro losas.
3. Colocar luces en las 27 zonas que no tienen ninguna (regla: una antorcha por puerta de
   `houses/`, una por `lights/` prop — `RegisterDerivedLight` ya existe).
4. Adjuntar `PP_portal_*` a `ZonePortalFactory` (el portal hoy es un collider invisible).
5. Colocar `PP_water_flow_*` sobre el canal del Lobby (autorados, 0 instancias).
6. `FloatingDamageSpawner`: pasar `wasCrit`; crítico ×1.6 y amarillo. Media hora.
7. `WorldPickup`: copiar el imán y el bob de `CoinPickup`. Una hora.
8. `BossPhaseController.OnPhaseChanged`: flash blanco por `SpriteTintStack` + trauma 0.5.
9. `WorldTransitionService`: fundido a negro de 0.25 s con un CanvasGroup. Dos horas.
10. `DayNightCycle.PublishScreenGrade`: usar `InverseGamma` (hoy forzado a 1) para que la
    noche pese en los medios y no solo en la viñeta.

## Lo que este documento NO propone

- **Post-proceso de URP (`renderPostProcessing`)**: UberPost cuesta ~18 ms medidos aunque
  no haga nada. Todo lo de arriba va en el feature propio.
- **Zoom punch**: rompe la escalera de PPU; `CameraFeelDirector.cs:28-30` ya lo rechaza.
- **Normal maps en el pixel-art**: `SpriteHDRTintLit` los declara y nada los autoriza. Un
  normal map sobre arte de 16 PPU se ve como plástico; las sombras proyectadas dan el volumen
  sin ese coste de autoría.
- **Iluminación por vértice de la hierba a mano** (tile por tile): la Idea 2 lo hace por
  mapa y llega a todo el suelo sin pintar nada.
- **Un cielo dibujado** (nubes de sprite por encima de la cámara): en vista cenital el cielo
  solo existe por sus CONSECUENCIAS (sombras, reflejos, color), y eso es lo que se propone.


---

## Revision 2026-09-13 (tarde): la noche volvia a estar demasiado clara

Reportado desde el juego: "antes estaba mas tetrico" y "la luz sobre el personaje de noche no pinta
nada". Medido A/B a medianoche en el lobby, mismo encuadre, capas encendidas contra apagadas:

| | luminancia media | pixeles muy oscuros |
|---|---:|---:|
| sin las capas nuevas, antorcha 0.35/128 | 0.0218 | 32.8 % |
| con bloom solo | 0.0235 (+8 %) | |
| con todo lo nuevo | 0.0262 (+20 %) | 26.4 % |

El bloom explicaba el 39 % del aclarado; farol, antorcha nueva y luciernagas el 61 %. De dia las
sombras de nube y sol OSCURECEN (-30 %), asi que no eran parte de la queja.

Revertido:
- **`PlayerLantern` borrado** (clase, test, interruptor `look lantern`). Dibujaba un disco gris
  sobre el cuerpo que convertia al personaje en un borron.
- **`LightPreset_Torch` de vuelta a 0.35 / 128 px.**
- **`DayNightCycle.BloomThresholdFor` fijo en blanco.** Bajarlo con la ambiente hacia que el suelo
  iluminado por una antorcha contara como emisivo. Coste aceptado: una llama de antorcha ya no
  hace halo a medianoche; los hechizos (aditivos por encima de blanco) siguen floreciendo.

Sin tocar: sombras de sol y nubes, viento, pisadas, luciernagas, y las luces COLOCADAS del lobby
(commit `8e481ab73`, dato de autor, no del sistema).
