# Seed World — generación procedural del mundo

Fecha: 2026-09-13 / 2026-09-14. Estado: **Fases 1, 2 y 3 hechas** (vista previa, mundo jugable, pueblos); fase 4 en curso.

## Qué es

Un editor runtime (**ESC -> Seed World**) donde se configura un perfil de generación
(semilla, tamaño, clima, biomas, estructuras, población), se ve el mapa resultante al
instante, y se construye el mundo con los sistemas que Valkur ya tiene.

No es Minecraft en 2D. Es un roguelike 2D visto desde arriba que toma las **ideas de
generación** de Minecraft que sobreviven a perder la dimensión vertical:

| Idea de Minecraft | Adaptación en Valkur |
|---|---|
| La semilla ES el mundo | Semilla por partida; texto o número (el texto se hashea con FNV-1a) |
| Chunks bajo demanda | `ChunkStreamer` (existe, apagado) con todas las capas — fase 5 |
| Clima multi-noise + tabla de biomas | 4 ruidos: elevación, temperatura, humedad, rareza. Cada bioma de tierra es un PUNTO en el plano temperatura/humedad y gana el más cercano ponderado por su peso |
| Altura | Elevación -> océano / costa / tierra / montaña; los acantilados serán capas + layer jumps — fase 2 |
| Features por bioma | Árboles de 10 familias, rocas, menas, flora — fase 4 |
| Rejilla de estructuras | Regiones + sal + separación mínima — fase 3 |
| Aldeas jigsaw | Plaza + calles + parcelas, reutilizando el encaje por puertas de `DungeonBuilder` — fase 3 |
| Terreno adaptado a estructura | Aplanar y limpiar la huella de la ciudad — fase 3 |
| Solo guardar cambios | `ChunkDelta` extendido a edificios, entidades, daño — fase 5 |
| Datapacks | `WorldGenProfile` (ScriptableObject) + el propio editor |

Lo que deliberadamente NO se copia: mundo infinito (el mundo es acotado por partida),
edición bloque a bloque de todo, cuevas 3D (las sustituyen mazmorras con entrada).

## Arquitectura: una sola cadena, tres usos

```text
WorldGenSettings + semilla
        |
   WorldClimate          puro, determinista, sin GameObjects (Valkur.Data.WorldGen)
        |
   WorldGenMap / plan
    /      |       \
Vista previa  Construir     Partida nueva
(textura)     (hornear a    (chunks bajo
              ficheros)     demanda)
```

**Regla dura:** la vista previa lee la MISMA función que después construye. Un overlay que
re-deriva lo que el generador "debería" hacer enseña lo que el autor escribió mientras el
juego hace otra cosa — el fallo que el overlay de áreas de hechizos existe para cazar.

## Límites medidos que condicionan el diseño

- **Y-sort:** `SortingConfig.MAX_SAFE_WORLD_Y = 317` unidades. Un mundo acotado de hasta
  **600 tiles de alto** centrado en 0 cabe sin desplazar el origen. `WorldGenSettings`
  clampa el alto a `MaxHeightTiles`.
- **Arte ausente para ciudades:** 0 tiles de calle/adoquín, 0 muros modulares, 1 de 1474
  plantillas con puerta, 0 aldeanos. Ver la auditoría del 2026-09-13 en la conversación que
  originó este documento. Las fases 1-2 no dependen de ese arte; la 3 sí.
- **Suelos de bioma:** no hay packs de nieve, desierto ni pantano. La vista previa ya
  distingue esos biomas; pintarlos en el mundo necesita arte (o un sustituto temporal).
- **Colisión de edificios generados:** `ApplyCollisionGrids` solo corre al cargar JSON; un
  edificio colocado por código debe llamar a `TryApplyGrid` — fase 3.

## Fases

| Fase | Contenido | Resultado visible | Estado |
|---|---|---|---|
| 1 | `WorldGenSettings`, `WorldGenProfile`, `FractalNoise2D`, `WorldClimate` (4 ruidos + tabla de biomas), `WorldGenMap`, editor Seed World con vista previa por capas, estadísticas y punto de inicio | Mapas distintos por semilla, sin construir nada | Hecha |
| 2 | Relieve (acantilados como capas), ríos, pintado de terreno autotile por bioma, **hornear a un mundo nuevo** (`Worlds/<slug>/`, nunca el base) | Caminar por un mundo generado | Hecha (sin acantilados, ver abajo) |
| 3 | Rejilla de estructuras + ciudad jigsaw (plaza, calles, parcelas) con los ~50 edificios pixel-art; colisión automática; validación de solapes y conectividad | Ciudades generadas | Hecha (calles de tierra, sin murallas: no hay arte) |
| 4 | Caminos entre estructuras, recursos por bioma, población, dificultad por distancia al inicio | Mundo jugable | Pendiente |
| 5 | Modo en vivo: la partida guarda semilla + perfil y genera chunks bajo demanda con deltas | Mundo nuevo en cada partida | Pendiente |

## Fase 1 — detalle

```text
Data/WorldGen/
  WorldBiome             enum cerrado. Se AÑADE al final, nunca se renumera
  WorldBiomeInfo         nombre, punto de clima, color de vista previa
  WorldBiomeTable        la tabla: un WorldBiomeInfo por valor del enum
  WorldBiomeWeight       activado + peso, por bioma, en el perfil
  WorldGenSettings       todos los parámetros + Clamp()
  WorldGenProfile        ScriptableObject que envuelve los ajustes (presets)
  WorldSeed              texto/número -> int; FNV-1a; semilla aleatoria
  FractalNoise2D         fBm sobre ValueNoise2D (determinista, no Mathf.PerlinNoise)
  WorldClimate           Sample(x,y) y Classify(sample): la única respuesta
  WorldClimateSample     elevación, temperatura, humedad, rareza
  WorldGenMap            muestreo a resolución de vista previa + estadísticas + inicio
  WorldGenPalette        colores de las capas escalares
Gameplay/Editors/SeedWorld/
  SeedWorldRuntimeEditor (.UI, .Fields, .Preview, .Workspace)
  SeedWorldPreviewPointer   pasar el ratón por el mapa -> qué hay ahí
```

Decisiones:

- **Ruido propio, no `Mathf.PerlinNoise`.** Unity no garantiza que sea estable entre
  versiones; `ValueNoise2D` ya existía por esa razón.
- **Un canal de ruido por semilla derivada** (`seed` hasheado con el índice del canal), para
  que temperatura y humedad no sean la misma forma.
- **Biomas como puntos de clima con peso** (Voronoi ponderado en el plano temperatura /
  humedad). Desactivar un bioma lo quita de la tabla y su espacio lo reparten los vecinos — no
  queda un hueco ni un color "desconocido".
- **Océano, costa y montaña se deciden por elevación**; si están desactivados, esa celda cae
  a la clasificación de tierra.
- **Latitud:** el norte del mapa es más frío (dial), y la altura enfría.
- **Borde oceánico:** un dial que hunde los bordes para que el mundo acotado termine en mar y
  no en un corte.
- **La vista previa muestrea en centros de celda** a como mucho 320 px por lado; cada muestra
  es la misma llamada `Sample + Classify` que usará la construcción.
- **Deshacer / rehacer** por instantánea JSON de los ajustes.
- **Persistencia:** los ajustes viajan en el workspace del editor; guardar presets como asset
  es fase 2.

## Fase 1 — resultado medido

- **Vista previa:** 320x320 celdas (102 400 muestras) en **~145 ms** en caliente para un mundo
  de 400x400. El coste es por CELDA, no por tile: un mundo de 2048 de ancho cuesta lo mismo.
- **Variedad por semilla** (400x400, medida con nivel del mar 0.42 antes de bajarlo a 0.36), porcentaje de celdas:
  - 1337: agua 49, llanura 14, taiga 9, bosque otonal 6, montana 6, nieve 4.
  - 42: agua 42, llanura 13, nieve 13, taiga 4, desierto 3, selva 3, pantano 2.
  - 99999: agua 63, bosque 6, taiga 5, montana 5, corrupto 4.
  - Con el valor final 0.36, la semilla 1337 queda en tierra 52 % / agua 41 %.
- **Primer ajuste que hizo falta:** con `climateScale` 260 un mapa de 400 tiles cubria ~1.5
  celdas de ruido, asi que todo el mapa caia en una sola region climatica (humedad p90 = 0.39:
  cero bosque, selva ni pantano). Bajado a 110. La rareza muestreaba a 0.6x esa escala y era
  practicamente constante; ahora usa la misma escala que el clima.
- **Verificado en un fotograma real** (1600x800, Play Mode): los dos paneles, las capas de biomas y
  altura, la leyenda, la cruz del inicio y la pestana de biomas. Consola limpia.
- **Tests:** `WorldGenGeneratorTests` (20) y `SeedWorldEditorTests` (8), mas los guardas del
  proyecto (registro del General Editor, alcanzabilidad, colores a pelo, glifos de la fuente,
  estaticos con Domain Reload OFF, workspace, input) — 101 en verde.

### Abierto tras la fase 1

- Guardar y cargar presets (`WorldGenProfile`) desde el editor.
- El agua domina en algunas semillas (63 %): el borde oceanico + la banda profunda pesan mucho;
  revisar al pintar el mundo real en la fase 2, donde el tamano de las islas se vera a escala.
- La nieve aparece alrededor de las montanas por el enfriamiento por altura. Es deliberado, pero
  hay que confirmarlo contra el arte (no hay pack de suelo de nieve).

## Fase 2 — construir el mundo (hecha, 2026-09-14)

```text
Data/WorldGen/
  WorldRivers / WorldRiver / WorldRiverRaster   rios: curvas cuesta abajo, rasterizadas a tiles
  WorldTerrainGrid                              terreno por VERTICE + reparacion de transiciones
Gameplay/World/Generation/
  SeedWorldTilePalette    que pares de terreno tienen pack Corner16; que tile tiene estas 4 esquinas
  SeedWorldBaker          escribe el map slot: una overlay por zona + zones.json + marcador
  SeedWorldBakeRequest / SeedWorldBakeResult / SeedWorldMarker
Gameplay/Editors/SeedWorld/SeedWorldRuntimeEditor.Build.cs   "Construir mundo" / "Volver al mundo base"
```

**Destino: un map slot, nunca el mundo base.** `Maps/<slot>.zones.json` + `MapOverrides/<slot>/<zona>.overlay.json`,
exactamente lo que escribe el Map editor, asi que cargarlo es `MapEditorManager.LoadMapSlot` y nada nuevo.
`default` se rechaza (es el mundo base). Un slot que existe sin `_seedworld.json` lo hizo una persona y se
rechaza tambien. Reconstruir un slot de Seed World pide segunda pulsacion y borra sus overlays viejas antes.

**Decisiones medidas:**

- **Los rios son DATOS, no una regla por punto.** Si un tile es rio depende de por donde bajo el agua, asi
  que se trazan una vez en espacio de TILES y la vista previa y la construccion rasterizan la misma lista.
  El coste es la longitud del rio (~20 ms por 8 rios), no el area.
- **Un rio se traza como CURVA, no eligiendo entre 4 vecinos.** La primera version elegia el vecino mas
  bajo y dibujaba reglas: tramos rectos de **52 a 70 tiles**. La segunda sumaba un giro con ruido y aun
  daba 35-67. Integrar un rumbo continuo (cuesta abajo + giro suave por distancia recorrida) y rasterizarlo
  bajo el tramo recto mas largo a **8-12 tiles** con 276-535 giros. Los saltos diagonales se rellenan por el
  tile mas bajo de los dos que comparten borde, o el jugador cruzaria por la esquina.
- **El terreno es por vertice** porque los 7 packs que existen son Corner16 (dual grid). Solo hay 7 pares
  dibujables: grass/dirt, grass/rock, rock/water, sand/grass, sand/rock, stone/lava, water/water_deep. **No
  hay arena/agua ni hierba/agua**, asi que una playa tocando el mar no tiene tile. `WorldTerrainGrid` repara:
  donde un par no se puede dibujar reescribe el lado de TIERRA con el siguiente terreno de la cadena de packs
  mas corta (arena -> roca -> agua). Las costas y los rios conservan su forma; es la orilla la que crece un
  borde rocoso. 400x400: ~3500 vertices reparados, ~390 celdas sin transicion posible (casi todas volcan).
- **Un pack = una hoja.** Un pack mezcla varias hojas del mismo par y el solver elegia por hash: la primera
  construccion salio como tablero de ajedrez de verdes distintos. `SeedWorldTilePalette` fija la primera
  hoja completa (orden ordinal) por pack, leida del NOMBRE del sprite para no necesitar un TileCatalog.
- **Los nombres de tile son la ruta bajo `Resources/Tiles/`**, no el nombre pelado: sand_rock guarda sus
  sprites una carpeta mas abajo y un nombre que el sondeo por categorias no encuentra cae en un
  `Resources.LoadAll` sincrono de todos los tiles. Test: todos los nombres cargan directos.
- **Colision** en celdas con 3+ esquinas de agua/lava y en cumbres de roca (`mountainLevel + 0.08`), usando el
  mismo tile del suelo en la capa Collision para no dibujar nada nuevo. Verificado en vivo: agua bloquea
  (`CollisionPhysics_All`), hierba no.
- **El mundo se centra en el origen** (Y-sort simetrico). El punto de inicio se busca a nivel de TILE (anillos
  alrededor del de la vista previa) exigiendo un 3x3 caminable: la vista previa gruesa puede caer en un rio.
- **Zonas con nombre de su bioma** ("Taiga 1", "Oceano 12"): el nombre es lo que el juego ENSENA en el banner
  y el minimapa, y "sw_3_3" se leia como texto de depuracion.
- **Aviso de consola arreglado de paso:** BuildingLoader, SpawnerInstanceLoader y ParticleInstancesLoader
  avisaban "no hay fichero" en cualquier slot sin contenido. Un slot personalizado vacio es un estado normal;
  el aviso se queda solo para el mundo base.

**Medido (400x400, semilla 1337):** generar 305-322 ms, escribir 175-608 ms, 9.5 MB en 64 zonas, cargar el
slot 1.1 s. Consola limpia.

### Abierto tras la fase 2

- **Acantilados/relieve como capas**: no hay arte de acantilado; la montana es roca + colision en cumbres.
- **Suelos que faltan**: nieve, desierto, pantano y barro se pintan con roca/arena/tierra. Hay dos "roca" de
  arte distinto (rock_water oscura, grass_rock gris) y se nota la costura entre ellas.
- **Tamano en disco**: 2048x600 serian ~490 zonas y ~60 MB de JSON; la carga de overrides lo parsea todo.
  Antes de mundos grandes: streaming por chunks (fase 5) o un formato binario.
- **Construir congela el juego** (~1-2 s en 400x400) porque es sincrono.
- Guardar/cargar presets (`WorldGenProfile`) desde el editor sigue pendiente.

## Fase 3 — pueblos (hecha, 2026-09-14)

```text
Data/WorldGen/
  WorldTowns / WorldTown            sitios (centrados en zona) + plaza + 4 calles principales + laterales
  WorldTownLots                     llena calles y plaza: fuente, puestos, farolas, casas y tiendas
  WorldTownBuildingOption / WorldTownPlacement / WorldTownPieceKind
Gameplay/World/Generation/
  SeedWorldTownPalette              56 edificios curados POR RUTA (misma ola pixel-art)
```

- **Las calles son del plan; los solares, de la construccion.** Calles y plaza dependen solo de la semilla
  y el suelo, asi que la vista previa las dibuja (marron sobre la capa de biomas). Que edificio va en cada
  solar depende del TAMANO de las plantillas del catalogo, que es dato de Gameplay, y se decide al construir
  contra ese mismo plan. `WorldGenMap` y el baker comparten plan: nada depende de la resolucion de la vista
  previa (el pueblo inicial se busca desde el centro del mundo, no desde un inicio calculado a 320 px).
- **Paleta curada, no escaneo de carpeta.** El catalogo mezcla ~50 edificios pixel-art de 80-250 px con
  renders de 1024-1536 px y una casa isometrica junto a su copia cenital. Nombrados por RUTA (un reimport
  puede renumerar ids). Test: las 56 entradas resuelven.
- **Solapes imposibles por construccion:** un unico conjunto de ocupacion; cada edificio de calle reserva
  ademas un anillo de 1 tile, que es el pasillo entre vecinos que la rejilla de colision cerraria.
- **Colision en cada edificio** via `collision_override` inline (el 45 % inferior del sprite en casas;
  1 tile en farolas y puestos). Sin ella una plantilla no pintada no colisiona. En vivo: 97/97 con colision.
- **Pueblos centrados en zona.** La primera construccion partio el pueblo inicial por una frontera de zona y
  el banner anunciaba "Montana 4" junto a la fuente. Ahora un pueblo que cabe en una zona no cruza fronteras
  (test) y la zona toma el nombre del pueblo ("Pueblo inicial", "Pueblo 2").
- **El jugador empieza en la calle sur del pueblo inicial**, el unico sitio garantizado sin edificio.
- Medido (400x400, semilla 1337): 4 pueblos, 97 edificios, generar 312 ms, escribir 578 ms, cargar 1.3 s.

### Abierto tras la fase 3

- Murallas, puertas de ciudad, puentes y calles empedradas: no hay arte modular.
- Los edificios no tienen puerta ni interior (1 de 1474 plantillas declara puerta).
- Reconstruir un slot sobrescribe su `buildings_instances.json`: lo que un autor coloco a mano alli se pierde.
- Cambiar de slot reescribe `Data/Backups/map_editor_zones.json.bak` (fichero versionado): comportamiento
  previo del Map editor, pero cada prueba de Seed World lo ensucia.
