# Catálogo de Patrullas (patrols.json)

Este directorio define patrones reutilizables de patrullaje para los monstruos.
El catálogo principal es `data/entities/behaviour/patrols.json`, mientras que la asignación de un patrón a cada clase de monstruo se hace en `data/entities/new_monsters.json` mediante la propiedad `patrol`.

- Catálogo: `data/entities/behaviour/patrols.json`
- Asignación por monstruo: `data/entities/new_monsters.json` (campo opcional `patrol`)
- Generación de puntos: `src/roguelike_game/factories/monster/behaviour_loader.py`
- Construcción de entidad: `src/roguelike_game/factories/monster/builder.py`
- Consumo en runtime: `src/roguelike_game/ecs/systems/fsm/states/monster/patrol_state.py` (recorre `PatrolRoute.points` en bucle)

## Cómo funciona

1. Al crear un monstruo, `MonsterBuilder` lee `patrol` desde `MONSTER_DEFS` (expuesto por `config.py`).
2. `behaviour_loader.build_patrol_points(px, py, patrol_cfg, TILE_SIZE)` genera la lista de waypoints (coordenadas en píxeles) a partir de:
   - el patrón (`id` o `type`),
   - `params` del monstruo (si existen),
   - y los `default_params` del catálogo.
3. Se construye el componente `PatrolRoute(points=...)` con estos waypoints.
4. `PatrolState` mueve al monstruo de punto a punto y reinicia al llegar al final (patrulla en bucle).

Notas:
- Las distancias en `params` están en tiles; se convierten a píxeles usando `TILE_SIZE` dentro de `behaviour_loader.py`.
- Si no se define `patrol` o el patrón es desconocido, se usa un fallback lineal de dos puntos (ida y vuelta).

## Estructura del campo `patrol` por monstruo

```json
"patrol": {
  "id": "circle",          // o "type": "circle" (alias)
  "params": {                // parámetros específicos del patrón
    "radius_tiles": 4,
    "points": 16,
    "clockwise": true
  }
}
```

- `id` (o `type`): nombre del patrón. Debe existir en `patrols.json`.
- `params`: opcional; si no se indica, se usan los `default_params` del catálogo.

## Catálogo de patrones y parámetros

El catálogo (`patrols.json`) incluye 5 patrones con `default_params`. A continuación se documenta cada uno.

### 1) line (Linear/Ping-Pong)
- Descripción: movimiento lineal ida y vuelta entre dos puntos.
- Parámetros:
  - `axis` (string, default `"x"`): eje principal del desplazamiento. Valores: `"x"` o `"y"`.
  - `length_tiles` (number, default `5`): longitud de la línea en tiles.
- Notas: equivalente funcional a un ping-pong entre `(px,py)` y `(px + length, py)` (o `(px, py + length)` si `axis="y"`).

### 2) circle (Circular)
- Descripción: ruta circular alrededor del punto de spawn.
- Parámetros:
  - `radius_tiles` (number, default `4`): radio en tiles.
  - `points` (integer, default `16`): número de waypoints a distribuir en la circunferencia.
  - `clockwise` (boolean, default `true`): si `true`, recorrido horario; si `false`, antihorario.
- Notas: más puntos generan curvas más suaves pero más waypoints a recorrer.

### 3) square (Cuadrado/Rectángulo)
- Descripción: perímetro rectangular centrado en el spawn.
- Parámetros:
  - `width_tiles` (number, default `6`): ancho en tiles.
  - `height_tiles` (number, default `6`): alto en tiles.
  - `points_per_edge` (integer, default `4`): cantidad de puntos por borde.
- Notas: el rectángulo se centra en `(px,py)`. Los puntos se reparten en sentido horario para cada borde.

### 4) zigzag (Zigzag)
- Descripción: progresión con alternancia lateral.
- Parámetros:
  - `segments` (integer, default `6`): cantidad de tramos/codos.
  - `step_tiles` (number, default `3`): avance por segmento a lo largo del eje principal.
  - `amplitude_tiles` (number, default `2`): amplitud de la oscilación perpendicular.
  - `axis` (string, default `"x"`): eje principal (`"x"` avanza horizontal y oscila vertical; `"y"` invierte roles).
- Notas: genera `segments + 1` puntos.

### 5) figure_eight (Ocho)
- Descripción: dos bucles formando un “8”.
- Parámetros:
  - `radius_tiles` (number, default `3`): radio de cada bucle.
  - `points_per_loop` (integer, default `12`): waypoints por bucle.
  - `gap_tiles` (number, default `2`): separación entre centros de los bucles.
- Notas: el bucle izquierdo se recorre horario y el derecho antihorario para formar el lazo cruzado.



## Ejemplos de uso por monstruo

En `data/entities/new_monsters.json` bajo cada clase:

```json
"patrol": { "id": "line", "params": { "axis": "x", "length_tiles": 5 } }
"patrol": { "id": "circle", "params": { "radius_tiles": 4, "points": 16, "clockwise": true } }
"patrol": { "id": "square", "params": { "width_tiles": 6, "height_tiles": 6, "points_per_edge": 4 } }
"patrol": { "id": "zigzag", "params": { "segments": 6, "step_tiles": 2, "amplitude_tiles": 1, "axis": "x" } }
"patrol": { "id": "figure_eight", "params": { "radius_tiles": 4, "points_per_loop": 16, "gap_tiles": 2 } }
```

## Buenas prácticas y consideraciones

- **Unidades**: todos los parámetros con sufijo `_tiles` están en tiles. La conversión a píxeles se realiza internamente usando `TILE_SIZE`.
- **Bordes/colisiones**: patrones grandes pueden atravesar muros/obstáculos; ajusta radios/ancho/alto para tu mapa o agrega validaciones de path si lo requieres.
- **Suavidad**: aumentar `points`/`points_per_edge` mejora la suavidad pero incrementa waypoints a recorrer.
- **Fallback**: sin `patrol` válido, se usa una línea simple de dos puntos para no romper el flujo de juego.
- **Extensión**: para añadir un nuevo patrón:
  1. Agrega su entrada en `patrols.json` con `default_params`.
  2. Implementa la generación en `behaviour_loader.py` (switch por `id`).
  3. Asigna el patrón en `new_monsters.json`.

## Depuración

- Verifica `PatrolRoute.points` en el mundo para una entidad si necesitas inspeccionar rutas.
- `PatrolState` reinicia al llegar al último waypoint; si ves “saltos”, revisa `speed`, `dt` y densidad de puntos.
