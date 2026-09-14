# Seed World — generación procedural del mundo

Fecha: 2026-09-13. Estado: **Fase 1 hecha** (vista previa); fase 2 pendiente.

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
| 2 | Relieve (acantilados como capas), ríos, pintado de terreno autotile por bioma, **hornear a un mundo nuevo** (`Worlds/<slug>/`, nunca el base) | Caminar por un mundo generado | Pendiente |
| 3 | Rejilla de estructuras + ciudad jigsaw (plaza, calles, parcelas) con los ~50 edificios pixel-art; colisión automática; validación de solapes y conectividad | Ciudades generadas | Pendiente (bloqueo de arte: calles y muros) |
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
