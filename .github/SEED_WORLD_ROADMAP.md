# Seed World — generación procedural del mundo

Fecha: 2026-09-13 / 2026-09-14. Estado: **Fases 1 a 5 hechas** (vista previa, mundo jugable, pueblos, poblacion, mundo en vivo)
y **separado del juego**: es un laboratorio APAGADO por defecto hasta que se integre, y cada viaje fuera de Pepitoria
lleva billete de vuelta (ver "Separacion del juego" al final).

## Qué es

Un editor runtime (**ESC -> Seed World**) donde se configura un perfil de generación
(semilla, tamaño, clima, biomas, estructuras, población), se ve el mapa resultante al
instante, y se construye el mundo con los sistemas que Valkur ya tiene.

No es Minecraft en 2D. Es un roguelike 2D visto desde arriba que toma las **ideas de
generación** de Minecraft que sobreviven a perder la dimensión vertical:

| Idea de Minecraft | Adaptación en Valkur |
|---|---|
| La semilla ES el mundo | Semilla por partida; texto o número (el texto se hashea con FNV-1a) |
| Chunks bajo demanda | La ZONA (50x50) es el chunk: `SeedWorldLiveStreamer` pinta las cercanas y suelta las lejanas — fase 5 |
| Clima multi-noise + tabla de biomas | 4 ruidos: elevación, temperatura, humedad, rareza. Cada bioma de tierra es un PUNTO en el plano temperatura/humedad y gana el más cercano ponderado por su peso |
| Altura | Elevación -> océano / costa / tierra / montaña; los acantilados serán capas + layer jumps — fase 2 |
| Features por bioma | Árboles de 10 familias, rocas, menas, flora — fase 4 |
| Rejilla de estructuras | Regiones + sal + separación mínima — fase 3 |
| Aldeas jigsaw | Plaza + calles + parcelas, reutilizando el encaje por puertas de `DungeonBuilder` — fase 3 |
| Terreno adaptado a estructura | Aplanar y limpiar la huella de la ciudad — fase 3 |
| Solo guardar cambios | Un slot en vivo guarda la semilla; solo una zona EDITADA llega a disco, como overlay normal — fase 5 |
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
| 4 | Caminos entre estructuras, recursos por bioma, población, dificultad por distancia al inicio | Mundo jugable | Hecha (sin caminos entre pueblos) |
| 5 | Modo en vivo: la partida guarda semilla + perfil y genera chunks bajo demanda con deltas | Mundo nuevo en cada partida | Hecha (zona = chunk; "Partida con semilla" en el menu, solo con el laboratorio) |

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

## Fase 4 — poblacion (hecha, 2026-09-14)

```text
Data/WorldGen/
  WorldEncounters / WorldEncounterSite   campamentos: fuera de pueblos, dificultad = distancia al inicio
  WorldTrees / WorldTreeSite             arboles: rejilla con jitter, familia segun bioma
  WorldTowns.ResidentTiles               casillas de calle alrededor de la plaza para los vendedores
Gameplay/World/Generation/
  SeedWorldPopulation                    presets de spawner (vendedores + escalera hostil) y plantillas de arbol
```

- **Vendedores solo en el pueblo inicial.** Cada uno es un personaje unico con persona y memoria; cuatro
  Gatitas en cuatro pueblos seria un error de continuidad, no una poblacion.
- **Encuentros con dificultad por distancia**: `dificultad = distancia al inicio / distancia maxima posible`,
  asi que va de 0 a 1 en cualquier tamano de mundo. Elige el preset por su posicion en una escalera ordenada
  por su propio `levelBonus` (+-1 paso), le suma hasta +3 niveles por distancia, y el cubil del dragon solo
  aparece por encima de 0.8. Nunca a menos de 14 tiles del borde de un pueblo ni a menos de 30 del inicio.
- **Spawners como filas v2 con SNAPSHOT del preset**, por `SpawnerInstanceSerializer`. Una fila que solo
  nombrara el preset se congelaria contra el; y una config v2 con solo el `levelBonus` leeria el resto como
  valores por defecto de clase y perderia el roster. Test: los tiles relativos a zona vuelven al sitio planeado.
- **Arboles por bioma**: la taiga da arboles de invierno, la selva tropicales, el pantano de pantano, un
  parche corrupto corruptos, y un 4 % de los comunes es antiguo. Son las plantillas de la tala (HarvestNode),
  asi que el mundo generado ya es talable. Colision de 1 tile en el tronco: un bosque que no se puede cruzar
  es un muro.
- **Bug encontrado y arreglado de paso:** cambiar de map slot dejaba vivos los monstruos persistentes del
  mundo anterior (los vendedores estan exentos del despawn por distancia). La primera prueba tuvo 12
  vendedores: los 6 del lobby habian seguido al jugador. `MonsterSpawner.DespawnAllForWorldSwap` los retira en
  `ClearAllSpawnedWorldContent` (salvo aliados y entidades colocadas, que tienen dueno propio).
- Medido (400x400, semilla 1337): 97 edificios, 631 arboles, 18 spawners (6 vendedores + 12 campamentos),
  cargar el slot 1.8 s. Consola limpia salvo un aviso previo del mundo base (instancia 214 en `zone_200_-50`).

### Abierto tras la fase 4

- Caminos entre pueblos, puentes sobre rios.
- Misiones procedurales y personas nuevas (los pueblos que no son el inicial no tienen habitantes).
- Monstruos por bioma (hoy el preset depende de la distancia, no del terreno).
- ~~Fase 5 (mundo en vivo por chunks)~~: hecha, ver abajo.

## Fase 5 — mundo en vivo (hecha, 2026-09-14)

```text
Data/WorldGen/
  WorldTerrainGrid.BuildRegion        el suelo de un RECTANGULO, identico al del mundo entero
Gameplay/World/Generation/
  SeedWorldPlan                       lo que se decide una vez: clima, plan, zonas, nombres, spawn
  SeedWorldZoneBuilder                vertices -> overlay (JSON para hornear, arbol ya parseado en vivo)
  SeedWorldBaker.BakeLive             slot SIN suelo: zonas, edificios, arboles, spawners y marcador
  SeedWorldLiveWorld                  abre un slot en vivo y genera una zona a peticion
  SeedWorldLiveStreamer               pinta las zonas cercanas (jugador + camara) y suelta las lejanas
  SeedWorldLauncher                   construir + cargar: lo comparten el editor y la consola
Core/WorldStreamingSignals            "esto es una descarga, no un borrado" (para el minimapa)
seedworld [nueva [semilla]]           DevConsole: estado del streamer, o partida nueva
ESC -> Seed World -> Modo EN VIVO / HORNEADO
```

- **La ZONA es el chunk.** Todo el juego ya habla en zonas de 50x50 (overlays, nombres, la unidad de guardado
  del Tile editor, edificios y spawners por zona), asi que el streaming por zona hace que cada sistema vea un
  mapa normal cuyas zonas lejanas estan en blanco. El `ChunkStreamer` antiguo solo conoce UNA capa de tiles y
  nada de lo demas, y no es el vehiculo.
- **La semilla ES el guardado.** Un slot en vivo guarda el marcador (`live: true` + ajustes), la lista de zonas,
  edificios, arboles y spawners — y ni un tile. El suelo de una zona que el jugador no visita no se calcula
  nunca; el de una que revisita se recalcula identico.
- **Solo se guardan los cambios, con el mecanismo que ya existia.** Una zona EDITADA la guarda el Tile editor
  como un overlay normal en `MapOverrides/<slot>/`, y desde entonces el streamer lee ese fichero en vez de
  generar. Antes de soltar una zona se vuelcan las ediciones pendientes, o limpiar sus tiles se llevaria los
  trazos sin guardar. Reconstruir el slot borra esos ficheros: una edicion del mundo anterior taparia el
  suelo del nuevo.
- **La reparacion de transiciones tuvo que volverse LOCAL, y es lo que hace posible todo lo demas.** El barrido
  en su sitio (Gauss-Seidel) daba un resultado que dependia de DONDE empezaba el recorrido, que es justo lo que
  cambia entre una region y el mundo. Ahora cada pasada lee la anterior y escribe una nueva (Jacobi), con
  "el primero en orden de recorrido gana" para dos reescrituras del mismo vertice — una regla que solo mira a
  los vecinos. Tras 6 pasadas un vertice depende solo de los que estan a 6 pasos, asi que una region con 7 de
  margen coincide con el mundo en cada vertice pedido. `SeedWorldLiveTests` lo comprueba en TODOS los vertices
  de todas las zonas de 4 semillas, y compara cada zona en vivo con el fichero que hornea la misma semilla.
- **Pinta por `OverlayLoader`, el camino de todos los mapas.** La capa Collision llega a `WorldCollisionBaker`
  por `tilemapTileChanged` (la ruta incremental de una brocha); lo unico extra es invalidar la cache de
  caminabilidad del `PathFinder`, que esa ruta no invalida por si sola.
- **Sigue a la CAMARA ademas del jugador.** Los editores desplazan y alejan la camara; con solo el jugador, un
  autor recorriendo un mundo en vivo con el Tile editor veia zonas negras con arboles de pie sobre la nada
  (capturado en vivo). Carga el vecindario del jugador y lo que toca la vista (+8 tiles, tope 4 zonas por lado),
  la zona del jugador al instante y el resto de mas cerca a mas lejos con 8 ms por fotograma; suelta lo que
  queda a mas de una zona de todo eso.
- **El minimapa no olvida lo explorado.** Soltar una zona limpia sus tiles, y `tilemapTileChanged` lo cuenta
  igual que un borrado: el minimapa la re-hornearia vacia. `WorldStreamingSignals.UnloadingTiles()` lo tapa
  (medido: el evento es SINCRONO en `SetTile` y `SetTilesBlock`, asi que basta un scope alrededor).
- **El pueblo inicial tiene altar de resurreccion.** Sin el, un mundo generado dejaba el rescate de la muerte
  como unica salida y el binder de altares avisaba al minuto de cada partida (visto en vivo). Se elige por la
  BANDERA (`ResurrectionAltarRegistry.IsAltar`, el unico predicado), el mas pequeno que cabe en una parcela (el
  arco de piedra 8x8; el mismo sprite tiene otra plantilla de 32x32), junto a una calle principal lo mas cerca
  posible de la plaza, con colision solo en los pilares: el arco se cruza. Medido: a 13.5 u del spawn.
- **Construir antes de que carguen los catalogos se rechaza.** Lanzado durante el arranque, el mundo salia con
  calles y sin una casa, un arbol ni un altar — y era un slot valido, asi que nada lo decia (medido: "4 pueblos
  con 0 edificios").
- **Nunca decide que el slot cambio por una sola senal.** `WorldGridBuilder.ClearGeneration` sube en cada
  borrado del mundo (cargar slot, interior, `reloadtiles`); con eso y con un sondeo de 1 s del slot activo
  vuelve a leer el marcador, y solo re-planifica si el marcador cambio (fecha de escritura).
- Medido (400x400, semilla 1337, en vivo): planificar 91-124 ms, escribir 2-9 ms, **286 KB** en disco (el
  horneado: 9.7 MB y 553 ms), cargar el slot 1.1 s con la ciudad y los 631 arboles. Una zona: generar 14-28 ms
  (peor 49 ms la primera vez), pintar 1 ms; soltarla ~8 ms, por eso se suelta UNA por fotograma. Arranque del
  juego directamente en un slot en vivo: 9 zonas pintadas antes de mover al jugador.
- Probado en vivo: costura entre zonas sin corte; ir a 3 zonas y volver pinta lo mismo; camara desplazada pinta
  su zona sin las intermedias; una edicion con el Tile editor se guarda al soltar la zona y vuelve del fichero.

### Abierto tras la fase 5

- ~~Menu principal~~: hecho, "Partida con semilla" (solo con el laboratorio encendido), ver abajo.
- Edificios, arboles y spawners se cargan todos al entrar (bien a 400x400; un mundo de 2048x600 serian ~5000
  arboles instanciados de golpe). Streaming por zona de esas capas = siguiente paso si se hacen mundos grandes.
- ~~El auto-brush del Tile editor no tiene la matriz `terrains` de una zona generada que nunca se guardo~~: hecho.
- ~~Caminos entre pueblos~~: hechos, ver "Caminos" abajo. Puentes, arte de nieve/desierto/pantano y habitantes fuera
  del pueblo inicial siguen abiertos.

## Caminos entre pueblos (2026-09-14)

```text
Data/WorldGen/
  WorldRoads / WorldRoad     arbol de caminos entre pueblos, busqueda en rejilla gruesa, trazado a tiles
  WorldGenMap.Roads / RoadTiles   el mismo plan para la vista previa, el suelo, los arboles y la construccion
ESC -> Seed World -> Pueblos -> Caminos SI/NO
```

- **Que pueblos:** arbol de expansion minima desde el pueblo inicial (Prim, empates por indice): une todos los pueblos
  sin dos caminos paralelos al mismo sitio. Un tramo sin paso por tierra (un lago, un rio sin puente) se omite y el
  pueblo de ese lado queda sin camino; no se busca otra conexion.
- **Por donde:** A* sobre celdas de 4x4 tiles (una muestra de clima por celda), enderezado por linea de vista y
  dibujado de vuelta a tiles con Bresenham, 2 tiles de ancho. Cada tile se comprueba otra vez a resolucion completa:
  agua y montana nunca se pavimentan, y donde una celda dejo pasar la esquina de un rio el camino se corta (no hay
  arte de puentes). Otros pueblos se rodean a distancia; los dos extremos solo se cruzan fuera de su interior.
- **Sale por una calle principal, en sus propias filas:** del final del brazo que mira al otro pueblo, recto durante
  `LotReach` (5) tiles antes de que empiece la busqueda. Asi el camino se une a la calle sin cortar un solar.
- **Los solares no pisan caminos:** los tiles de camino a menos de radio + 5 de un pueblo pasan a ser calles suyas
  ANTES de repartir solares (los solares llegan a radio + 4). Los arboles tampoco nacen en un camino ni al lado.
- **El suelo es tierra**, como las calles: `WorldTerrainGrid.StampRoads` pone en `dirt` las cuatro esquinas de cada
  tile. Donde ningun pack dibuja tierra contra el terreno de al lado (arena, roca) la reparacion de transiciones le
  crece un borde de hierba.
- **Un mundo en vivo escrito antes de los caminos sigue sin ellos.** Regenera su suelo desde los ajustes cada vez, y
  sus casas y arboles se colocaron sobre un plan sin caminos: el marcador pasa a formato 2 y un formato 1 planifica con
  `roadsBetweenTowns = false`.
- **Medido:** 400x400 por defecto (4 pueblos) = 3 caminos en las semillas 1337, 42, 99999 y 7, 494-908 tiles de
  camino, planificar entre -1 y +2 ms respecto a sin caminos (ruido). 2048x600 con 12 pueblos: 6 de los 11 caminos del
  arbol (los otros no encontraron paso por tierra dentro de su caja de busqueda), +20 ms. `WorldRoadTests` (8): arbol de brazo a brazo, sin tiles en agua/montana ni bajo un solar, independiente de
  la resolucion de la vista previa, suelo de tierra, sin arboles encima, apagable, coste acotado, y el mundo en vivo
  de formato 1. `SeedWorldLiveTests` sigue comparando cada vertice de cada zona en vivo con el mundo entero.

## Separacion del juego: laboratorio y billete de vuelta (2026-09-14)

**Decision del proyecto:** Seed World necesita bastante refinamiento antes de formar parte del juego, y
**Pepitoria es la ciudad principal**: desde ella se llega a todos los demas sitios y a ella se vuelve. Hasta que se
integre, nada del juego normal puede depender de Seed World, y mover al jugador fuera esta permitido solo si siempre
vuelve exactamente a donde estaba.

### El incidente que lo motivo

Las pruebas de la fase 5 (cambios de slot a `partida_1337`) corrompieron el mundo base el mismo dia:

- La copia de trabajo de zonas (`persistentDataPath/map_editor_zones.json`) paso de **45 a 126 zonas**: las 102
  zonas generadas quedaron apiladas sobre los offsets de Pepitoria y se perdieron **8 zonas reales** con sus edificios
  (la instancia 214 de `zone_200_-50` dejo de aparecer; la llame "aviso previo" y era dano propio).
- Un autoguardado de la partida registro al jugador en **"Pueblo inicial"**, una zona que el mundo base no tiene.
- `Maps/default.zones.json` contenia zonas de fixtures.

Reparado a mano desde la copia de 45 zonas de HEAD (`Data/Backups/map_editor_zones.json.bak`) tras un snapshot del
estado danado. La causa raiz (un persist escribia la copia de trabajo base con las zonas de OTRO mapa, y la fusion de
zonas archivadas leia el fichero base dentro del otro mapa) esta arreglada en `8e43955af`: las zonas vivas tienen
DUENO y un persist solo escribe el fichero de su dueno.

### El laboratorio (`SeedWorldLab`, apagado por defecto)

```text
SeedWorldLab            Gameplay/World/Generation/   el interruptor (PlayerPrefs valkur.seedworld.lab, por maquina)
SeedWorldBootGuard      Gameplay/World/Generation/   BeforeSceneLoad: una sesion nunca arranca dentro de un mundo generado
SeedWorldNewGame        Gameplay/World/Generation/   "Partida con semilla": la peticion cruza la carga de escena
WorldExcursion          Gameplay/World/Zones/        el billete de vuelta
MainMenuUI.SeededNewGame.cs  UI/MainMenu/            la fila del menu (solo con el laboratorio)
seedworld [lab on|off | nueva [semilla] | volver]    DevConsole
ESC -> Seed World -> Laboratorio ENCENDIDO/APAGADO, "Volver a Pepitoria"
```

- **Una sola puerta:** `SeedWorldLauncher.BuildAndLoad` rechaza PRIMERO con el laboratorio apagado, asi que el boton
  Construir, `seedworld nueva` y la partida con semilla se paran en el mismo sitio. El codigo sigue compilando con el
  juego y el editor abre y previsualiza (una vista previa no escribe nada).
- **El arranque no instala nada de Seed World.** El paso "Preparando los mundos en vivo" y
  `GameplaySceneSetup.SeedWorld.cs` se borraron; el streamer se crea al entrar en un mundo
  (`SeedWorldLiveStreamer.EnsureInstance`). `SeedWorldLabTests` lee las fuentes del arranque y solo admite el editor.
- **Una sesion nunca arranca dentro de un mundo generado:** `SeedWorldBootGuard` devuelve el puntero de slot a
  `default` si el slot activo tiene marcador `_seedworld.json`.
- **Machine state:** el interruptor es PlayerPrefs, y durante una ejecucion de tests se lee APAGADO salvo que el
  fixture lo fije. Los fixtures del menu cuentan filas; con el valor de la maquina estarian rojos solo en el ordenador
  de quien encendio el laboratorio.

### El billete de vuelta (`WorldExcursion`)

- **Casa = el PRIMER paso fuera de Pepitoria.** Mover el slot activo fuera del mundo base apunta donde estaba el
  jugador (posicion y zona). Saltar de otro mapa a otro mantiene la casa y solo cambia el destino.
- **Volver aterriza en el billete y lo gasta**, lo traiga quien lo traiga (`LoadMapSlot("default")`, `seedworld
  volver`, el boton del editor). `MapEditorBaseWorldIsolationTests.ATripOutOfPepitoria_ComesBackToTheExactSpotItLeftFrom`.
- **En disco** (`persistentDataPath/Maps/_excursion.json`, escritura atomica con temporal GUID) para sobrevivir a un
  cierre; **solo en memoria durante una ejecucion de tests**, con o sin scope de escritura: ningun fixture aparca ese
  fichero.
- **Una sesion que termina fuera arranca la siguiente en casa:** en `BeforeSceneLoad` el puntero vuelve a `default`
  y el billete se gasta; el guardado ya tenia la posicion de casa.

### Guardar fuera de Pepitoria (decision: "Guardar, posicion Pepitoria")

- Todo lo ganado fuera (inventario, XP, monedas) se guarda. La **posicion y la zona** guardadas son SIEMPRE las de
  casa: `SaveService.ResolvePersistablePlayerPosition` devuelve `PlayerPositionPersistence.AwayFromHome` antes que
  cualquier otra regla, y el rastreador de la ultima posicion del mundo base deja de muestrear mientras se esta fuera.
- El estado de muerte no se persiste fuera (`DeathStateSave.ShouldPersist`): un espiritu restaurado en casa
  caminaria hacia un cadaver cuya posicion no significa nada en el mundo base. **Limitacion aceptada:** morir fuera y
  cerrar el juego te deja vivo en casa.
- El minimapa guarda la niebla de cada mapa visitado con su propia clave (`map:<slot>`): con la clave del mundo, andar
  por un mundo generado exploraba la niebla de Pepitoria donde las coordenadas coinciden.

### "Partida con semilla" en el menu principal

- Fila justo despues de "Partida nueva", **solo si `SeedWorldNewGame.Available`** (laboratorio encendido). Abre el
  mismo selector de clase y, al confirmar, arma la peticion con semilla aleatoria (el kit del menu no tiene campo de
  texto; `seedworld nueva <semilla>` es la puerta para una elegida). La partida EMPIEZA en Pepitoria y, cuando el
  arranque termina, sale al mundo generado con billete: la casa del personaje nuevo es su punto de aparicion.
- **Continuar y Cargar retiran la peticion** antes de entregar la carga: una peticion espera al PROXIMO arranque, y
  una que quedara armada por un selector abandonado convertiria un Continuar en un viaje.
- **Defecto encontrado de paso:** `LoadingScreenController` ASIGNA `LoadingReporter.OnGameplayReady` al empezar (y lo
  limpia al destruirse), asi que una suscripcion hecha en el menu con `+=` quedaba borrada antes del arranque y la
  partida con semilla no habria salido nunca, con `IsPending` diciendo que si. Canal nuevo
  `LoadingReporter.OnGameplayReadyForSystems`, que la pantalla no asigna ni limpia;
  `SeedWorldLabTests.ASeededNewGame_SurvivesTheLoadingScreenTakingItsSignal` reproduce la asignacion.

### Un test no puede escribir el directorio `Maps` del usuario

`MapEditorMapSlots` era el unico escritor de ficheros del Map editor que `WorldDataWriteGuard` no cubria. El
repositorio de zonas rechazaba la copia de trabajo de un fixture, y el persist la ESPEJABA a `Maps/default.zones.json`
por esa clase: medido el 2026-09-14 a las 13:29 UTC, `MapEditorPortalsTests` (que no aparca nada) dejo sus dos zonas
`zone_portals_A`/`zone_portals_B` y su portal de prueba en el espejo del usuario — el arreglo anterior solo aparcaba
el fichero en dos fixtures. Ahora toda mutacion de `Maps/` (slots, `_active.txt`, borrar, renombrar) pasa por la
guarda, y los tres fixtures que ejercitan el directorio real a proposito lo aparcan y abren el scope.

### Abierto

- **Streaming por zona de edificios, arboles y spawners** para mundos grandes. No es solo instanciar por zona: el
  guardado del editor de Buildings enumera los edificios VIVOS de la escena (con guardas de recuento), asi que con
  zonas sin cargar borraria o rechazaria sus filas; el altar del pueblo inicial dejaria de existir para el registro
  de altares cuando su zona se suelta (y la muerte lejos de el pasaria al rescate corto); y los arboles talados se
  restauran por id de instancia. Hace falta que ese guardado escriba una TABLA (como `PlacedEntityService`) antes.
- ~~Auto-brush sobre zonas generadas nunca guardadas~~: hecho (`e2e14c40c`), el streamer vuelca y retira el terreno
  por vertice de cada zona que pinta.
- Caminos entre pueblos, puentes, arte de nieve/desierto/pantano, habitantes fuera del pueblo inicial.
- Cada partida con semilla crea un slot `partida_<semilla>` que nadie borra.
- **Salir a otro slot guarda la lista de zonas VIVA del mundo base**, y eso incluye zonas de la base de datos que la
  copia de trabajo no tenia: en la prueba en vivo entro `dungeon` (45 -> 46) y se reescribio
  `Data/Backups/map_editor_zones.json.bak` (versionado). Es el comportamiento previo de cualquier cambio de slot del
  Map editor, no de Seed World; se restauro a mano tras la prueba.

### Verificado en vivo (2026-09-14, partida real del usuario, restaurada despues)

- **Ida:** desde Pepitoria (142.15448, 66.79033, "Forest") a un mundo en vivo de semilla 1337 (8x8 zonas, 4 pueblos,
  96 edificios, 631 arboles; generar 142 ms, escribir 11 ms, cargar 1368 ms). Billete en disco con esa posicion
  exacta, puntero de slot movido, jugador en el pueblo inicial (25.5, 19.5).
- **Fuera:** clave de niebla del minimapa `map:<slot>`; 9 zonas pintadas con su terreno en el mapa del auto-brush;
  `SaveImmediately` escribio el autoguardado con la posicion y la zona de CASA, y el guardado de salida al parar Play
  tambien.
- **Sesion terminada fuera:** parar Play dejo puntero y billete en disco; la siguiente entrada en Play los retiro
  antes de cargar ninguna escena.
- **Menu:** con el laboratorio encendido la fila "Partida con semilla" aparece entre "Partida nueva" y "Opciones".
- Despues: laboratorio apagado, slot de prueba y su backup borrados, copia de trabajo, espejo, carpeta de la partida y
  el backup versionado devueltos byte a byte al estado previo. La vuelta a Pepitoria al punto exacto se cubre con
  `MapEditorBaseWorldIsolationTests.ATripOutOfPepitoria_ComesBackToTheExactSpotItLeftFrom`: la sesion de Play la
  tomo otra ejecucion de tests antes de probarla en vivo.
