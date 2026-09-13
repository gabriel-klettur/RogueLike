# Padding transparente en los sprites de edificios, y las sombras que salen mal

> Qué rompe, cuánto rompe, dónde está cada caso en el mundo, y la lista de PNG a recortar.
> Fecha: 2026-09-13 · Medido sobre los 1256 PNG de `Resources/Buildings/` y las 324
> colocaciones de `StreamingAssets/Buildings/buildings_instances.json`.

## La causa, confirmada

`SpriteShadowProjected` cizalla el sprite desde una LÍNEA DE PIES que
`SunShadowCaster.RefreshFootLine` toma de `sprite.bounds.min.y` — el borde inferior del
RECT del PNG, no de la tinta. Si el PNG lleva filas transparentes abajo, la línea de pies
queda por debajo de la base real del edificio y pasan dos cosas a la vez:

- la sombra **se despega**: su base cae `m · (1 − squash)` por debajo de la del edificio;
- la sombra **se alarga y se desplaza**: cada fila de tinta se cizalla desde más abajo, así
  que el corrimiento lateral crece en `m · skew` (hasta 1.15·m al amanecer y al ocaso).

Medido en vivo sobre el peor caso, `Buildings/shops/ukranian_super_2` en `zone_100_50`:

| | valor |
|---|---|
| PNG | 1024 × 1536, **368 filas transparentes abajo** (24.0 %) |
| ancla / línea de pies | `y = 90.34` |
| base real del edificio | `y = 94.43` |
| error | **4.09 unidades de mundo**, es decir 4 tiles |

En la captura aislada (solo ese edificio proyectando, el resto de casters apagados) la
sombra sale de la mitad derecha de la tienda y cruza la muralla hacia el este, sin tocar la
base del edificio: `unity/Valkur/Captures/iso_marked.png` (magenta = los pixeles que cambian
al encender esa unica sombra; linea roja = la linea de pies que usa el shader, linea verde =
la base real).

## Antes de recortar: el recorte cambia el TAMAÑO y la POSICIÓN

Esto es lo que más importa del informe. `BuildingObject.Apply` hace
`transform.localScale = effH / baseH`, donde `baseH` son los píxeles del PNG y `effH` es
`scale.y` de la instancia (o `originalScale.y` de la plantilla). El alto en el mundo es
`effH / 32` **sin depender de baseH**, y el ancla es el borde inferior-centro del PNG completo.

Así que al recortar `b` filas de abajo de un PNG de alto `h`, sin tocar nada más:

- el edificio se ve **`h / (h − b)` veces más alto** — en `ukranian_super_2`, un **+31 %**;
- y **baja** hasta apoyar su base en el ancla, donde antes flotaba `b` píxeles por encima.

La compensación exacta, si recortas:

```
recorte abajo de b filas (alto h):   scale.y y originalScale.y  ->  x (h - b) / h
                                     rel_y  ->  NO CAMBIA
recorte de l + r columnas (ancho w): scale.x y originalScale.x  ->  x (w - l - r) / w
                                     rel_x  ->  + (effW_viejo - effW_nuevo)/2   [recorte simétrico]
```

Hay que aplicarlo a `BuildingTemplate_<id>.asset` y a cada fila de
`buildings_instances.json` que use esa plantilla. Puedo dejarte una herramienta que lo haga
sola a partir del PNG recortado; dilo y la escribo.

**Alternativa sin tocar arte**: hornear la base de la tinta en la plantilla
(`inkBottomNormalized`) y que `SunShadowCaster` la use como línea de pies. Mismo patrón que
`castMuzzle`. Cero riesgo de layout y arregla los 1256 de una vez, pero no te ahorra el
padding en el atlas.

## 1. Lo que YA está colocado en el mundo (ordenado por gravedad)

De las 324 colocaciones, **79 tienen padding abajo**. `hueco` es la separación vertical entre la sombra y el edificio a mediodía, en tiles; `corrim.` es el desplazamiento lateral extra al amanecer/ocaso, en tiles.

### Graves — hueco >= 0.5 tiles (10 colocaciones)

| hueco | corrim. | zona | x, y | PNG | px abajo | % | asset |
|---:|---:|---|---|---|---:|---:|---|
| 3.19 | 4.70 | `zone_100_50` | 239.2, 90.3 | 1024x1536 | 368 | 24.0 | `Buildings/shops/ukranian_super_2` |
| 1.30 | 1.91 | `lobby` | 164.4, 91.2 | 1024x1024 | 108 | 10.5 | `Buildings/shops/blacksmith` |
| 1.07 | 1.57 | `zone_100_50` | 226.7, 80.9 | 1024x1536 | 286 | 18.6 | `Buildings/portals/portal` |
| 0.74 | 1.09 | `zone_100_50` | 237.6, 57.1 | 1024x1024 | 73 | 7.1 | `Buildings/houses/curse_house_topdown` |
| 0.65 | 0.96 | `lobby` | 195.6, 91.8 | 1024x1536 | 119 | 7.7 | `Buildings/shops/alchemy_tower` |
| 0.63 | 0.93 | `lobby` | 192.9, 74.0 | 1024x1536 | 69 | 4.5 | `Buildings/shops/banco` |
| 0.61 | 0.89 | `lobby` | 185.7, 72.1 | 1024x1536 | 63 | 4.1 | `Buildings/temples/catholic` |
| 0.57 | 0.84 | `Forest` | 142.8, 76.7 | 1024x1024 | 72 | 7.0 | `Buildings/houses/curse_house_iso` |
| 0.54 | 0.80 | `Forest` | 125.5, 74.8 | 1024x1536 | 89 | 5.8 | `Buildings/totems/totem_destruido` |
| 0.50 | 0.74 | `Forest` | 129.1, 90.2 | 1024x1024 | 297 | 29.0 | `Buildings/forest_decoration/natural/petalos_rosados_2` |

### Visibles — hueco 0.15 a 0.5 tiles (39 colocaciones)

| hueco | corrim. | zona | x, y | PNG | px abajo | % | asset |
|---:|---:|---|---|---|---:|---:|---|
| 0.44 | 0.65 | `Forest` | 114.4, 73.4 | 1024x1024 | 227 | 22.2 | `Buildings/forest_decoration/natural/Flor_silvestre_azul` |
| 0.43 | 0.63 | `Forest` | 104.8, 94.6 | 1024x1024 | 308 | 30.1 | `Buildings/forest_decoration/natural/seta_blanca` |
| 0.39 | 0.57 | `Forest` | 140.4, 95.0 | 1024x1024 | 308 | 30.1 | `Buildings/forest_decoration/natural/seta_blanca` |
| 0.38 | 0.56 | `lobby` | 211.5, 94.8 | 1024x1024 | 48 | 4.7 | `Buildings/shops/ukranian_super_1` |
| 0.38 | 0.57 | `zone_100_50` | 205.2, 91.6 | 1024x1024 | 47 | 4.6 | `Buildings/shops/jewlery_shop` |
| 0.37 | 0.55 | `lobby` | 160.9, 73.4 | 1024x1024 | 39 | 3.8 | `Buildings/gardens/garden_5` |
| 0.36 | 0.53 | `Forest` | 125.5, 74.6 | 1024x1536 | 59 | 3.8 | `Buildings/totems/totem_cargando_energia` |
| 0.35 | 0.52 | `lobby` | 178.8, 97.7 | 540x540 | 39 | 7.2 | `Buildings/vegetation/tree_6` |
| 0.35 | 0.51 | `lobby` | 170.9, 97.9 | 540x540 | 39 | 7.2 | `Buildings/vegetation/tree_6` |
| 0.34 | 0.50 | `lobby` | 192.5, 97.8 | 540x540 | 39 | 7.2 | `Buildings/vegetation/tree_6` |
| 0.34 | 0.50 | `dungeon` | 155.0, 123.8 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.33 | 0.49 | `Forest` | 114.8, 95.6 | 1024x1024 | 280 | 27.3 | `Buildings/forest_decoration/natural/champinones_agrupados_marrones` |
| 0.32 | 0.47 | `lobby` | 199.1, 98.4 | 540x540 | 39 | 7.2 | `Buildings/vegetation/tree_6` |
| 0.31 | 0.45 | `dungeon` | 166.5, 127.8 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.31 | 0.46 | `dungeon` | 146.8, 140.5 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.29 | 0.42 | `lobby` | 168.0, 63.0 | 1024x1024 | 54 | 5.3 | `Buildings/shops/healer` |
| 0.28 | 0.42 | `dungeon` | 170.8, 133.8 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.26 | 0.39 | `lobby` | 170.8, 84.1 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.26 | 0.38 | `Forest` | 125.5, 74.6 | 1024x1536 | 42 | 2.7 | `Buildings/totems/totem_forest` |
| 0.25 | 0.37 | `Forest` | 120.6, 92.6 | 1024x1024 | 182 | 17.8 | `Buildings/forest_decoration/natural/mariposa_rama_caida` |
| 0.25 | 0.37 | `lobby` | 175.1, 66.7 | 1024x1024 | 48 | 4.7 | `Buildings/portals/portal_stone_arch` |
| 0.25 | 0.37 | `zone_100_50` | 220.9, 68.0 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.25 | 0.36 | `lobby` | 187.2, 82.7 | 251x324 | 12 | 3.7 | `Buildings/vegetation/tree_10` |
| 0.24 | 0.36 | `zone_100_50` | 223.9, 96.1 | 1024x1024 | 37 | 3.6 | `Buildings/shops/healer_1` |
| 0.24 | 0.36 | `zone_100_50` | 211.8, 67.4 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.23 | 0.34 | `lobby` | 185.6, 65.7 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.23 | 0.33 | `Lobby` | 175.2, 89.9 | 1024x1024 | 53 | 5.2 | `Buildings/statues/statue_olim_01` |
| 0.22 | 0.32 | `lobby` | 196.3, 65.1 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.22 | 0.32 | `dungeon` | 186.3, 130.9 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.22 | 0.32 | `dungeon` | 191.1, 136.4 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.22 | 0.32 | `dungeon` | 197.1, 132.8 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.22 | 0.32 | `dungeon` | 187.4, 140.0 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.22 | 0.32 | `dungeon` | 196.0, 141.7 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.22 | 0.32 | `dungeon` | 189.4, 125.6 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.22 | 0.32 | `dungeon` | 179.3, 138.9 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.21 | 0.31 | `lobby` | 175.2, 98.7 | 1024x1024 | 10 | 1.0 | `Buildings/castles/castle_2` |
| 0.19 | 0.28 | `zone_100_100` | 210.2, 143.5 | 456x626 | 25 | 4.0 | `Buildings/vegetation/tree_1` |
| 0.18 | 0.27 | `zone_100_50` | 216.4, 65.3 | 1024x1024 | 17 | 1.7 | `Buildings/houses/orden_house_2` |
| 0.16 | 0.23 | `lobby` | 185.6, 92.4 | 1024x1024 | 16 | 1.6 | `Buildings/temples/satanist` |

### Leves — hueco < 0.15 tiles (30 colocaciones)

Por debajo de un sexto de tile. No se ven; se arreglan solas si recortas el asset por otra razón.

Assets implicados: `Buildings/shops/magic_tower`, `Buildings/vegetation/tree_3`, `Buildings/others/fuente`, `Buildings/nature/tree_tropical_pixel_1`, `Buildings/gardens/flowers_9`, `Buildings/nature/tree_pine_conifer`

## 2. Lista de PNG a recortar

Rutas relativas a `unity/Valkur/Assets/_Project/Resources/Buildings/`, más `.png`.
Para la sombra **solo importa `abajo`**; las otras tres columnas son margen que puedes
recortar o no (ahorran atlas, pero cambian el ancho y piden la compensación en `scale.x`).

### Tier A — crítico: 10 % o más del alto es transparente abajo — 65 assets

| asset | PNG | abajo | % | arriba | izq | der | id |
|---|---|---:|---:|---:|---:|---:|---:|
| `forest_decoration/corrupto/monton_ramas_afiladas` | 1024x1024 | **330** | 32.2 | 309 | 172 | 177 | 103 |
| `forest_decoration/natural/seta_blanca` | 1024x1024 | **308** | 30.1 | 304 | 319 | 318 | 85 **(colocado)** |
| `forest_decoration/natural/seta_blanca` | 1024x1024 | **308** | 30.1 | 304 | 319 | 318 | 87 **(colocado)** |
| `forest_decoration/natural/piedras_musgo` | 1024x1024 | **298** | 29.1 | 333 | 216 | 244 | 112 |
| `forest_decoration/natural/petalos_rosados_2` | 1024x1024 | **297** | 29.0 | 278 | 225 | 193 | 84 **(colocado)** |
| `forest_decoration/natural/hojas_frescas` | 1024x1024 | **296** | 28.9 | 293 | 178 | 195 | 109 |
| `forest_decoration/natural/raiz_expuesta_suave` | 1024x1024 | **295** | 28.8 | 284 | 141 | 150 | 82 |
| `forest_decoration/natural/petalos_rosados_3` | 1024x1024 | **293** | 28.6 | 197 | 304 | 285 | 89 |
| `forest_decoration/corrupto/pozo_savia_corrupta` | 1024x1024 | **286** | 27.9 | 294 | 161 | 161 | 105 |
| `forest_decoration/natural/nido_huevos` | 1024x1024 | **286** | 27.9 | 288 | 184 | 191 | 110 |
| `forest_decoration/natural/champinones_agrupados_marrones` | 1024x1024 | **280** | 27.3 | 268 | 245 | 268 | 81 **(colocado)** |
| `forest_decoration/natural/champinones_agrupados_marrones` | 1024x1024 | **280** | 27.3 | 268 | 245 | 268 | 86 **(colocado)** |
| `forest_decoration/natural/petalos_rosados_1` | 1024x1024 | **277** | 27.1 | 268 | 265 | 285 | 111 |
| `forest_decoration/corrupto/semilla_reventada` | 1024x1024 | **260** | 25.4 | 285 | 223 | 225 | 107 |
| `shops/ukranian_super_2` | 1024x1536 | **368** | 24.0 | 149 | 29 | 24 | 190 **(colocado)** |
| `shops/ukranian_super_2` | 1024x1536 | **368** | 24.0 | 149 | 29 | 24 | 192 **(colocado)** |
| `shops/ukranian_super_2` | 1024x1536 | **368** | 24.0 | 149 | 29 | 24 | 199 **(colocado)** |
| `shops/ukranian_super_2` | 1024x1536 | **368** | 24.0 | 149 | 29 | 24 | 243 **(colocado)** |
| `forest_decoration/natural/Flor_silvestre_azul` | 1024x1024 | **227** | 22.2 | 245 | 320 | 320 | 83 **(colocado)** |
| `portals/portal_2` | 1024x1536 | **304** | 19.8 | 144 | 16 | 22 | 126 |
| `portals/portal_2` | 1024x1536 | **304** | 19.8 | 144 | 16 | 22 | 153 |
| `portals/portal_2` | 1024x1536 | **304** | 19.8 | 144 | 16 | 22 | 230 |
| `portals/portal_2` | 1024x1536 | **304** | 19.8 | 144 | 16 | 22 | 231 |
| `forest_decoration/corrupto/craneo_muzgo` | 1024x1024 | **194** | 18.9 | 238 | 204 | 253 | 99 |
| `portals/portal` | 1024x1536 | **286** | 18.6 | 136 | 12 | 18 | 125 **(colocado)** |
| `portals/portal` | 1024x1536 | **286** | 18.6 | 136 | 12 | 18 | 220 **(colocado)** |
| `portals/portal` | 1024x1536 | **286** | 18.6 | 136 | 12 | 18 | 232 **(colocado)** |
| `forest_decoration/corrupto/hojas_marchitas` | 1024x1024 | **186** | 18.2 | 335 | 124 | 134 | 101 |
| `forest_decoration/natural/mariposa_rama_caida` | 1024x1024 | **182** | 17.8 | 326 | 0 | 0 | 88 **(colocado)** |
| `forest_decoration/corrupto/raiz_retorcida` | 1024x1024 | **156** | 15.2 | 190 | 188 | 193 | 106 |
| `forest_decoration/corrupto/hongos_liminiscentes_venenosos` | 1024x1024 | **154** | 15.0 | 176 | 172 | 251 | 102 |
| `forest_decoration/corrupto/flor_carnivora` | 1024x1024 | **146** | 14.3 | 142 | 272 | 231 | 100 |
| `forest_decoration/corrupto/flor_carnivora` | 1024x1024 | **146** | 14.3 | 142 | 272 | 231 | 146 |
| `forest_decoration/corrupto/totem_podrido` | 1024x1024 | **142** | 13.9 | 146 | 212 | 238 | 108 |
| `forest_decoration/corrupto/piedra_runica_musgosa` | 1024x1024 | **136** | 13.3 | 149 | 211 | 175 | 104 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 217 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 223 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 224 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 225 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 226 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 227 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 228 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 229 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 245 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 246 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 247 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 250 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 251 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 252 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 253 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 254 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 256 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 264 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 265 |
| `combat/coliseo_2` | 1024x1024 | **128** | 12.5 | 66 | 54 | 58 | 96 |
| `portals/portal_dudgeon_2` | 1024x1024 | **123** | 12.0 | 114 | 118 | 130 | 128 |
| `portals/portal_dudgeon_2` | 1024x1024 | **123** | 12.0 | 114 | 118 | 130 | 206 |
| `combat/combat_training_yard` | 1536x1024 | **117** | 11.4 | 23 | 0 | 20 | 94 |
| `combat/training` | 1536x1024 | **117** | 11.4 | 23 | 0 | 20 | 97 |
| `gardens/garden_2` | 1024x1024 | **114** | 11.1 | 18 | 73 | 77 | 121 |
| `gardens/garden_3` | 1024x1024 | **114** | 11.1 | 30 | 33 | 34 | 122 |
| `portals/portal_runes_inactive` | 1024x1024 | **109** | 10.6 | 61 | 177 | 177 | 196 |
| `portals/portal_runes_inactive` | 1024x1024 | **109** | 10.6 | 61 | 177 | 177 | 205 |
| `shops/blacksmith` | 1024x1024 | **108** | 10.5 | 26 | 47 | 47 | 18 **(colocado)** |
| `vegetation/tree_5` | 256x256 | **26** | 10.2 | 3 | 13 | 5 | 142 |

### Tier B — visible: 5 a 10 % — 52 assets

| asset | PNG | abajo | % | arriba | izq | der | id |
|---|---|---:|---:|---:|---:|---:|---:|
| `vegetation/tree_11` | 233x333 | **32** | 9.6 | 0 | 0 | 18 | 164 |
| `vegetation/tree_11` | 233x333 | **32** | 9.6 | 0 | 0 | 18 | 165 |
| `vegetation/tree_11` | 233x333 | **32** | 9.6 | 0 | 0 | 18 | 166 |
| `vegetation/tree_11` | 233x333 | **32** | 9.6 | 0 | 0 | 18 | 167 |
| `vegetation/tree_11` | 233x333 | **32** | 9.6 | 0 | 0 | 18 | 168 |
| `vegetation/tree_11` | 233x333 | **32** | 9.6 | 0 | 0 | 18 | 169 |
| `vegetation/tree_11` | 233x333 | **32** | 9.6 | 0 | 0 | 18 | 170 |
| `vegetation/tree_11` | 233x333 | **32** | 9.6 | 0 | 0 | 18 | 171 |
| `vegetation/tree_11` | 233x333 | **32** | 9.6 | 0 | 0 | 18 | 172 |
| `vegetation/tree_11` | 233x333 | **32** | 9.6 | 0 | 0 | 18 | 173 |
| `vegetation/tree_11` | 233x333 | **32** | 9.6 | 0 | 0 | 18 | 283 |
| `vegetation/tree_11` | 233x333 | **32** | 9.6 | 0 | 0 | 18 | 295 |
| `vegetation/tree_11` | 233x333 | **32** | 9.6 | 0 | 0 | 18 | 304 |
| `vegetation/tree_11` | 233x333 | **32** | 9.6 | 0 | 0 | 18 | 312 |
| `vegetation/tree_11` | 233x333 | **32** | 9.6 | 0 | 0 | 18 | 49 |
| `vegetation/tree_11` | 233x333 | **32** | 9.6 | 0 | 0 | 18 | 9 |
| `others/guillotina` | 1024x1024 | **94** | 9.2 | 81 | 232 | 232 | 124 |
| `others/guillotina` | 1024x1024 | **94** | 9.2 | 81 | 232 | 232 | 150 |
| `others/guillotina` | 1024x1024 | **94** | 9.2 | 81 | 232 | 232 | 233 |
| `others/guillotina` | 1024x1024 | **94** | 9.2 | 81 | 232 | 232 | 244 |
| `shops/shop_alchemist_house` | 1024x1536 | **127** | 8.3 | 19 | 32 | 43 | 129 |
| `shops/shop_alchemist_house` | 1024x1536 | **127** | 8.3 | 19 | 32 | 43 | 260 |
| `shops/alchemy_tower` | 1024x1536 | **119** | 7.7 | 20 | 25 | 32 | 15 **(colocado)** |
| `vegetation/tree_azul` | 1024x1024 | **78** | 7.6 | 67 | 113 | 106 | 143 |
| `portals/portal_well_closed` | 1024x1024 | **74** | 7.2 | 54 | 74 | 116 | 195 |
| `vegetation/tree_6` | 540x540 | **39** | 7.2 | 39 | 81 | 75 | 27 **(colocado)** |
| `vegetation/tree_6` | 540x540 | **39** | 7.2 | 39 | 81 | 75 | 28 **(colocado)** |
| `vegetation/tree_6` | 540x540 | **39** | 7.2 | 39 | 81 | 75 | 29 **(colocado)** |
| `vegetation/tree_6` | 540x540 | **39** | 7.2 | 39 | 81 | 75 | 311 **(colocado)** |
| `houses/curse_house_topdown` | 1024x1024 | **73** | 7.1 | 48 | 74 | 90 | 194 **(colocado)** |
| `houses/curse_house_topdown` | 1024x1024 | **73** | 7.1 | 48 | 74 | 90 | 221 **(colocado)** |
| `houses/curse_house_topdown` | 1024x1024 | **73** | 7.1 | 48 | 74 | 90 | 307 **(colocado)** |
| `gardens/garden_tree_plaza` | 1024x1024 | **72** | 7.0 | 69 | 64 | 59 | 177 |
| `gardens/garden_tree_plaza` | 1024x1024 | **72** | 7.0 | 69 | 64 | 59 | 178 |
| `gardens/garden_tree_plaza` | 1024x1024 | **72** | 7.0 | 69 | 64 | 59 | 188 |
| `houses/curse_house_iso` | 1024x1024 | **72** | 7.0 | 68 | 59 | 56 | 193 **(colocado)** |
| `houses/curse_house_iso` | 1024x1024 | **72** | 7.0 | 68 | 59 | 56 | 200 **(colocado)** |
| `houses/curse_house_iso` | 1024x1024 | **72** | 7.0 | 68 | 59 | 56 | 201 **(colocado)** |
| `gardens/garden_tree_plaza` | 1024x1024 | **72** | 7.0 | 69 | 64 | 59 | 309 |
| `gardens/garden_1` | 1024x1024 | **71** | 6.9 | 16 | 0 | 6 | 120 |
| `totems/totem_destruido` | 1024x1536 | **89** | 5.8 | 80 | 184 | 156 | 138 **(colocado)** |
| `totems/totem_destruido` | 1024x1536 | **89** | 5.8 | 80 | 184 | 156 | 158 **(colocado)** |
| `totems/totem_sufriendo` | 1024x1536 | **88** | 5.7 | 24 | 181 | 175 | 140 |
| `totems/totem_sufriendo` | 1024x1536 | **88** | 5.7 | 24 | 181 | 175 | 174 |
| `shops/healer` | 1024x1024 | **54** | 5.3 | 50 | 72 | 71 | 17 **(colocado)** |
| `shops/healer` | 1024x1024 | **54** | 5.3 | 50 | 72 | 71 | 202 **(colocado)** |
| `shops/healer` | 1024x1024 | **54** | 5.3 | 50 | 72 | 71 | 270 **(colocado)** |
| `gardens/flowers_7` | 904x904 | **47** | 5.2 | 11 | 20 | 23 | 118 |
| `statues/statue_olim_01` | 1024x1024 | **53** | 5.2 | 17 | 232 | 247 | 13 **(colocado)** |
| `gardens/flowers_7` | 904x904 | **47** | 5.2 | 11 | 20 | 23 | 180 |
| `statues/statue_olim_01` | 1024x1024 | **53** | 5.2 | 17 | 232 | 247 | 239 **(colocado)** |
| `statues/statue_olim_01` | 1024x1024 | **53** | 5.2 | 17 | 232 | 247 | 310 **(colocado)** |

### Tier C — menor: 2 a 5 % — 106 assets

| asset | PNG | abajo | % | arriba | izq | der | id |
|---|---|---:|---:|---:|---:|---:|---:|
| `shops/ukranian_super_1` | 1024x1024 | **48** | 4.7 | 16 | 24 | 40 | 189 **(colocado)** |
| `shops/ukranian_super_1` | 1024x1024 | **48** | 4.7 | 16 | 24 | 40 | 191 **(colocado)** |
| `portals/portal_stone_arch` | 1024x1024 | **48** | 4.7 | 56 | 163 | 164 | 197 **(colocado)** |
| `portals/portal_stone_arch` | 1024x1024 | **48** | 4.7 | 56 | 163 | 164 | 198 **(colocado)** |
| `portals/portal_stone_arch` | 1024x1024 | **48** | 4.7 | 56 | 163 | 164 | 219 **(colocado)** |
| `shops/ukranian_super_1` | 1024x1024 | **48** | 4.7 | 16 | 24 | 40 | 235 **(colocado)** |
| `portals/portal_stone_arch` | 1024x1024 | **48** | 4.7 | 56 | 163 | 164 | 248 **(colocado)** |
| `portals/portal_stone_arch` | 1024x1024 | **48** | 4.7 | 56 | 163 | 164 | 249 **(colocado)** |
| `shops/ukranian_super_1` | 1024x1024 | **48** | 4.7 | 16 | 24 | 40 | 271 **(colocado)** |
| `shops/ukranian_super_1` | 1024x1024 | **48** | 4.7 | 16 | 24 | 40 | 272 **(colocado)** |
| `shops/jewlery_shop` | 1024x1024 | **47** | 4.6 | 24 | 2 | 10 | 19 **(colocado)** |
| `shops/jewlery_shop` | 1024x1024 | **47** | 4.6 | 24 | 2 | 10 | 216 **(colocado)** |
| `gardens/garden_4` | 1024x1024 | **47** | 4.6 | 54 | 38 | 38 | 23 |
| `shops/jewlery_shop` | 1024x1024 | **47** | 4.6 | 24 | 2 | 10 | 238 **(colocado)** |
| `shops/banco` | 1024x1536 | **69** | 4.5 | 38 | 27 | 26 | 20 **(colocado)** |
| `temples/catholic` | 1024x1536 | **63** | 4.1 | 67 | 19 | 21 | 1 **(colocado)** |
| `temples/catholic` | 1024x1536 | **63** | 4.1 | 67 | 19 | 21 | 261 **(colocado)** |
| `temples/catholic` | 1024x1536 | **63** | 4.1 | 67 | 19 | 21 | 308 **(colocado)** |
| `statues/statue_dwarf_warrior` | 1024x1024 | **41** | 4.0 | 31 | 203 | 207 | 132 |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 162 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 185 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 186 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 187 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 2 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 212 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 213 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 242 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 257 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 266 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 267 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 268 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 276 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 277 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 278 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 279 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 280 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 281 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 287 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 306 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 32 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 34 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 35 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 36 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 37 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 38 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 39 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 40 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 41 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 42 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 43 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 44 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 45 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 51 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 52 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 55 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 56 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 58 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 59 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 65 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 66 **(colocado)** |
| `vegetation/tree_1` | 456x626 | **25** | 4.0 | 14 | 0 | 3 | 67 **(colocado)** |
| `gardens/garden_5` | 1024x1024 | **39** | 3.8 | 73 | 48 | 44 | 10 **(colocado)** |
| `totems/totem_cargando_energia` | 1024x1536 | **59** | 3.8 | 24 | 179 | 175 | 137 **(colocado)** |
| `totems/totem_cargando_energia` | 1024x1536 | **59** | 3.8 | 24 | 179 | 175 | 155 **(colocado)** |
| `vegetation/tree_10` | 251x324 | **12** | 3.7 | 20 | 5 | 5 | 255 **(colocado)** |
| `vegetation/tree_10` | 251x324 | **12** | 3.7 | 20 | 5 | 5 | 292 **(colocado)** |
| `vegetation/tree_10` | 251x324 | **12** | 3.7 | 20 | 5 | 5 | 294 **(colocado)** |
| `vegetation/tree_10` | 251x324 | **12** | 3.7 | 20 | 5 | 5 | 300 **(colocado)** |
| `vegetation/tree_10` | 251x324 | **12** | 3.7 | 20 | 5 | 5 | 303 **(colocado)** |
| `vegetation/tree_10` | 251x324 | **12** | 3.7 | 20 | 5 | 5 | 46 **(colocado)** |
| `vegetation/tree_10` | 251x324 | **12** | 3.7 | 20 | 5 | 5 | 47 **(colocado)** |
| `vegetation/tree_10` | 251x324 | **12** | 3.7 | 20 | 5 | 5 | 48 **(colocado)** |
| `vegetation/tree_10` | 251x324 | **12** | 3.7 | 20 | 5 | 5 | 53 **(colocado)** |
| `vegetation/tree_10` | 251x324 | **12** | 3.7 | 20 | 5 | 5 | 57 **(colocado)** |
| `vegetation/tree_10` | 251x324 | **12** | 3.7 | 20 | 5 | 5 | 60 **(colocado)** |
| `vegetation/tree_10` | 251x324 | **12** | 3.7 | 20 | 5 | 5 | 63 **(colocado)** |
| `vegetation/tree_10` | 251x324 | **12** | 3.7 | 20 | 5 | 5 | 79 **(colocado)** |
| `vegetation/tree_10` | 251x324 | **12** | 3.7 | 20 | 5 | 5 | 8 **(colocado)** |
| `shops/healer_1` | 1024x1024 | **37** | 3.6 | 16 | 91 | 125 | 16 **(colocado)** |
| `shops/healer_1` | 1024x1024 | **37** | 3.6 | 16 | 91 | 125 | 218 **(colocado)** |
| `shops/healer_1` | 1024x1024 | **37** | 3.6 | 16 | 91 | 125 | 234 **(colocado)** |
| `vegetation/tree_7` | 227x309 | **11** | 3.6 | 3 | 5 | 5 | 274 |
| `vegetation/tree_7` | 227x309 | **11** | 3.6 | 3 | 5 | 5 | 33 |
| `vegetation/tree_7` | 227x309 | **11** | 3.6 | 3 | 5 | 5 | 5 |
| `gardens/flowers_8` | 444x174 | **6** | 3.4 | 10 | 4 | 5 | 119 |
| `vegetation/tree_9` | 251x324 | **10** | 3.1 | 8 | 6 | 3 | 241 |
| `vegetation/tree_9` | 251x324 | **10** | 3.1 | 8 | 6 | 3 | 269 |
| `vegetation/tree_9` | 251x324 | **10** | 3.1 | 8 | 6 | 3 | 31 |
| `vegetation/tree_9` | 251x324 | **10** | 3.1 | 8 | 6 | 3 | 69 |
| `vegetation/tree_9` | 251x324 | **10** | 3.1 | 8 | 6 | 3 | 7 |
| `vegetation/tree_8` | 251x308 | **9** | 2.9 | 25 | 5 | 8 | 147 |
| `vegetation/tree_8` | 251x308 | **9** | 2.9 | 25 | 5 | 8 | 240 |
| `vegetation/tree_8` | 251x308 | **9** | 2.9 | 25 | 5 | 8 | 6 |
| `vegetation/tree_8` | 251x308 | **9** | 2.9 | 25 | 5 | 8 | 64 |
| `gardens/flowers_2` | 412x412 | **11** | 2.7 | 9 | 22 | 5 | 113 |
| `totems/totem_forest` | 1024x1536 | **42** | 2.7 | 41 | 160 | 168 | 154 **(colocado)** |
| `totems/totem_forest` | 1024x1536 | **42** | 2.7 | 41 | 160 | 168 | 156 **(colocado)** |
| `totems/totem_forest` | 1024x1536 | **42** | 2.7 | 41 | 160 | 168 | 157 **(colocado)** |
| `gardens/flowers_2` | 412x412 | **11** | 2.7 | 9 | 22 | 5 | 182 |
| `totems/totem_forest` | 1024x1536 | **42** | 2.7 | 41 | 160 | 168 | 90 **(colocado)** |
| `market/cart_bakery_bread` | 104x80 | **2** | 2.5 | 0 | 0 | 0 | 357 |
| `gardens/flowers_1` | 412x169 | **4** | 2.4 | 4 | 4 | 4 | 25 |
| `gardens/flowers_1` | 412x169 | **4** | 2.4 | 4 | 4 | 4 | 26 |
| `market/crate_eggplants_purple` | 55x42 | **1** | 2.4 | 0 | 1 | 0 | 365 |
| `gardens/flowers_6` | 904x904 | **20** | 2.2 | 23 | 11 | 47 | 117 |
| `gardens/flowers_6` | 904x904 | **20** | 2.2 | 23 | 11 | 47 | 179 |

### Tier D — 1 a 2 % (75 assets)

Uno o dos píxeles de margen. Irrelevante para la sombra; se listan por completitud.

`castles/castle_1`, `castles/castle_1`, `totems/totem_riendo`, `totems/totem_riendo`, `signs/sign_alchemist_blue_flask`, `houses/orden_house_2`, `houses/orden_house_2`, `houses/orden_house_2`, `houses/orden_house_2`, `temples/satanist`, `props/net_drying_stand`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `vegetation/tree_3`, `gardens/flowers_5`, `gardens/flowers_5`, `gardens/flowers_5`, `castles/castle_2`, `vegetation/tree_4`, `vegetation/tree_4`, `gardens/flowers_9`, `castles/castle_2`, `gardens/flowers_9`, `gardens/flowers_9`, `nature/tree_cherry_blossom_small`, `nature/tree_enchanted_pixel_4`, `gardens/flowers_4`, `nature/tree_tropical_pixel_1`, `shops/magic_tower`, `signs/signpost_arrows_lantern`, `others/fuente`, `shops/shop_apothecary`, `others/fuente`, `shops/shop_apothecary`, `gardens/flowers_3`, `houses/building_bank_tall`, `nature/tree_corrupted_fantasy_alt_2`, `nature/tree_snowy_pixel_7`, `nature/tree_winter_1`, `nature/tree_snowy_conifer_5`, `nature/tree_snowy_conifer_8`, `nature/tree_pine_conifer`

### Sin padding abajo: 1176 de 1474 assets. Nada que hacer.

## 3. Dónde se concentra

| carpeta | assets con >= 2 % de padding abajo |
|---|---:|
| `vegetation` | 90 |
| `shops` | 23 |
| `combat` | 22 |
| `gardens` | 18 |
| `portals` | 17 |
| `forest_decoration/natural` | 13 |
| `forest_decoration/corrupto` | 11 |
| `totems` | 10 |
| `houses` | 6 |
| `others` | 4 |
| `statues` | 4 |
| `temples` | 3 |
| `market` | 2 |
