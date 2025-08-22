# Spawners: Esquema de datos y comportamiento en runtime

Este documento describe el formato de los archivos en `data/spawners/` y cómo el motor interpreta cada propiedad.

Rutas relevantes:
- Plantillas: `data/spawners/spawners_templates.json`
- Olas: `data/spawners/spawners_waves.json`
- Instancias: `data/spawners/spawners_instances.json`
 - Guía de desarrollador (detalles): `docs/developer_guide/ecs/spawner.md`

Sistemas/Componentes:
- `SpawnerPlacementSystem` (carga y crea entidades)
- `SpawnerTriggerSystem` (activa/desactiva por proximidad o arranque automático)
- `SpawnerRuntimeSystem` (gestión de oleadas, cooldown, loop)
- `SpawnSystem` (convierte `SpawnRequest` en entidades del juego)
- `SpawnerConfig`, `SpawnerState`, `SpawnRequest`

## Resumen rápido de secciones
- 1) Plantillas: Define la plantilla base del spawner (tipo, trigger, política y olas inline o por referencia).
- 2) Olas: Catálogo reutilizable de secuencias de olas (listas de spawns) que las plantillas pueden referenciar con `waves_id`.
- 3) Instancias: Colocación de spawners en el mundo (zona, tile) con posibilidad de overrides por dot-notation.
- 4) Triggers: Condiciones de activación: proximidad (radio y auto_start) y auto.
- 5) Policy: Parámetros de comportamiento (cooldown, modo, loop/restart_on_done y flags futuros).
- 6) Estructura de olas y spawns: Formato de cada ola y entradas de spawn (qué, cuántos y cómo se distribuyen).
- 7) Ciclo de vida: Cómo progresa el spawner entre olas, aplica cooldown y reinicia si hay loop.
- 8) Render/Debug: Qué muestra la superposición de depuración en el mapa (estado, wave, cd, loop).
- 9) Componentes: Referencia rápida de `SpawnerConfig`, `SpawnerState` y `SpawnRequest`.
- 10) Checklist: Pasos prácticos para definir y probar un spawner.
- 11) Limitaciones: Alcance actual y features aceptadas pero aún no aplicadas.

---

## 1) Plantillas: `spawners_templates.json`
Archivo JSON que contiene una lista de plantillas de spawner.

Campos por plantilla (MVP):
- `id` (string): identificador único de la plantilla.
- `spawner_type` (string): "invisible" | "building". MVP usa "invisible".
- `spawn_radius` (int | string) OPCIONAL: controla el modo de colocación de spawns.
  - Si falta, `null` o `0`: modo clásico "centro → espiral" (center-first spiral).
  - Si es un entero > 0: modo "aleatorio en círculo" dentro de ese radio (en tiles) alrededor de `anchor_tile`.
  - Si es string en {`"random"`, `"aleatorio"`, `"aleatoreo"`}: modo aleatorio con radio igual a `spread_fallback_max` de cada spawn (por ola).
- `spawner_shape` (string) OPCIONAL: forma del área aleatoria cuando se usa `spawn_radius`. Valores: `"circle"` (default) o `"square"`.
  - `"circle"`: los puntos aleatorios se eligen dentro de un círculo de radio `spawn_radius` (tiles).
  - `"square"`: los puntos aleatorios se eligen dentro del cuadrado circunscrito de lado `2*spawn_radius+1` tiles, centrado en `anchor_tile`.
- `defend_spawn` (bool) OPCIONAL: si `true`, los NPCs spawneados "defienden" el área del spawner.
  - La forma del área de defensa respeta `spawner_shape` (`"circle"` por defecto o `"square"`).
    - circle: radio en píxeles.
    - square: medio lado en píxeles (el lado completo es `2*defend_radius_px`).
  - Radio (px):
    - Si `spawn_radius` es entero > 0: `defend_radius_px = spawn_radius * TILE_SIZE`.
    - Si `spawn_radius` es `"random"`/`"aleatorio"`/`"aleatoreo"`: se usa el `spread_fallback_max` de la entrada de spawn activa (en tiles) convertido a píxeles.
  - En runtime, los NPCs patrullan alrededor del área (círculo o cuadrado) y (si `defend_leash=true`) hacen "leash" en persecución para no salir de la forma definida.
- `defend_leash` (bool) OPCIONAL: por defecto `true`. Si `false`, los defensores no hacen leash (perseguirán libremente fuera del radio de defensa).
- `trigger` (objeto): ver sección Triggers.
- `policy` (objeto): ver sección Policies.
- `waves` (lista de objetos) OPCIONAL si usas `waves_id`:
  - Cada ola es `{ "spawns": [ SPAWN_ENTRY, ... ] }`. Ver sección Spawns.
- `waves_id` (string) OPCIONAL: referencia a un conjunto de olas definido en `spawners_waves.json`.
- `visible_in_game` (bool) OPCIONAL: si `true` y `building_id` está definido, el spawner se vinculará al edificio indicado en runtime para visualización (sin sprites propios).
- `building_id` (int) OPCIONAL: id persistente del `Building` usado como visual del spawner.

Ejemplo mínimo de plantilla inline:
```json
{
  "id": "barbol_periodic_no_stack",
  "spawner_type": "invisible",
  "spawn_radius": 0,
  "visible_in_game": true,
  "building_id": 113,
  "trigger": { "type": "proximity", "radius": 5, "auto_start": true },
  "policy": {
    "mode": "periodic",
    "cooldown_s": 10.0,
    "max_active": 0,
    "persistent": false,
    "restart_on_done": true
  },
  "waves": [
    { "spawns": [ { "kind": "monster", "id": "barbol", "count": 1, "spread_radius": 2 } ] }
  ]
}
```

Ejemplo con radio aleatorio fijo (tiles):
```json
{
  "id": "barbol_random10",
  "spawner_type": "invisible",
  "spawn_radius": 10,
  "trigger": { "type": "auto" },
  "policy": { "mode": "periodic", "cooldown_s": 3.0 },
  "waves": [ { "spawns": [ { "kind": "monster", "id": "barbol", "count": 3 } ] } ]
}
```

Ejemplo con radio aleatorio dinámico por ola (`"random"` toma `spread_fallback_max` de cada spawn):
```json
{
  "id": "barbol_random_by_wave",
  "spawner_type": "invisible",
  "spawn_radius": "random",
  "trigger": { "type": "proximity", "radius": 6, "auto_start": true },
  "policy": { "mode": "periodic", "cooldown_s": 5.0 },
  "waves": [
    { "spawns": [ { "kind": "monster", "id": "barbol", "count": 2, "spread_fallback_max": 6 } ] },
    { "spawns": [ { "kind": "monster", "id": "barbol", "count": 2, "spread_fallback_max": 12 } ] }
  ]
}
```

Ejemplo modo mixto (proximidad inicial + cooldown fijo entre olas):
```json
{
  "id": "survival_10",
  "spawner_type": "invisible",
  "visible_in_game": true,
  "building_id": 113,
  "trigger": { "type": "proximity", "radius": 10, "auto_start": true },
  "policy": {
    "mode": "periodic",
    "cooldown_s": 1.0,
    "proximity_initial_only": true,
    "between_waves_cooldown_s": 10.0,
    "restart_on_done": false
  },
  "waves_id": "waves_survival_10"
}
```

Notas:
- También se acepta `waves` como string JSON (o literal Python) y se intentará parsear.
- Si existen `waves` inline y `waves_id`, tiene prioridad `waves_id`.

---

## 2) Olas: `spawners_waves.json`
Diccionario por id. Cada valor puede ser:
- Lista de olas: `"waves_id": [ { "spawns": [...] }, ... ]`
- Objeto con `waves`: `"waves_id": { "waves": [ ... ] }`

Ejemplo:
```json
{
  "waves_survival_10": [
    { "spawns": [ { "kind": "monster", "id": "barbol", "count": 2, "spread_radius": 3, "spread_fallback_max": 12, "min_px_distance": 24 } ] },
    { "spawns": [ { "kind": "monster", "id": "barbol", "count": 3, "spread_radius": 3, "spread_fallback_max": 12, "min_px_distance": 24 } ] }
  ]
}
```

---

## 3) Instancias: `spawners_instances.json`
Lista de instancias que colocan spawners en el mapa.

Campos por instancia:
- `template_id` (string): referencia a la plantilla.
- `zone` (string): zona del mapa (se usa para offsets globales).
- `tile` ([tx, ty]): tile local dentro de la zona.
- `overrides` (objeto) OPCIONAL: aplica cambios a la plantilla con dot-notation:
  - `trigger.*` (ej.: `trigger.radius`)
  - `policy.*` (ej.: `policy.cooldown_s`, `policy.restart_on_done`)
  - `spawner_type`
  - `building_id`, `visible_in_game`

Ejemplo:
```json
{
  "template_id": "barbol_periodic_no_stack",
  "zone": "lobby",
  "tile": [10, 12],
  "overrides": {
    "policy.restart_on_done": true,
    "trigger.radius": 6,
    "visible_in_game": true,
    "building_id": 113
  }
}
```

Cálculos al cargar:
- `anchor_tile` global = `tile` local + offset de `zone` (`global_map_settings.zone_offsets`).
- `cooldown_frames` = `policy.cooldown_s` × `FPS` (por defecto 60).

---

## 4) Triggers soportados (MVP)
- `type`: "proximity" | "auto".
- `radius` (int, proximity): radio en tiles. Default si falta: 5.
- `auto_start` (bool, proximity): si true, el spawner se activa automáticamente cuando el jugador está dentro del radio y se desactiva al salir. Default: true.

Runtime (`SpawnerTriggerSystem`):
- Proximidad: Calcula la distancia en tiles entre `anchor_tile` y la posición del jugador. Si `auto_start=true`, `SpawnerState.started` refleja si el jugador está dentro del radio.
- Auto: `SpawnerState.started = true` de forma continua (no depende del jugador).
- Modo mixto (proximidad inicial): si `policy.proximity_initial_only=true` o se define `policy.between_waves_cooldown_s`, la proximidad solo se usa para iniciar la PRIMERA ola; después se ignora entre olas. El flag `SpawnerState.initial_proximity_done` indica si ya se consumió esa proximidad inicial.

---

## 5) Policy soportada (MVP)
- `mode` (string): p.ej. "periodic". Hoy es informativo (render/debug), no altera la lógica.
- `cooldown_s` (float): segundos entre olas/spawns. Default: 10.0.
- `max_active` (int): máximo de monstruos simultáneamente activos generados por este spawner. Si se alcanza, se pospone el spawn de la ola actual hasta que haya capacidad.
- `persistent` (bool): aceptado pero no aplicado en runtime (reservado para futuro).
- `restart_on_done` (bool): si true, al terminar todas las olas y quedar eliminados los monstruos de la última ola, el spawner reinicia.
- `restart_cooldown_s` (float): segundos de espera antes del reinicio del ciclo (si `restart_on_done`/`loop`/`repeat`). Default: usa `cooldown_s` si no se especifica.
- Sinónimos aceptados: `loop`, `repeat` (cualquier true equivale a loop).
- `advance_on` (string): controla cómo avanza entre olas.
  - `"clear"` (default): la siguiente ola sólo inicia cuando todos los mobs de la ola actual han sido eliminados.
  - `"cooldown"`: avanza de ola tras aplicar `cooldown_frames` aunque sigan vivos los mobs de la ola anterior. El fin de ciclo (y el reinicio) se retrasa hasta que no quede ningún mob activo del spawner.
- `proximity_initial_only` (bool): si `true`, la proximidad sólo se usa para iniciar la primera ola; entre olas se ignora la proximidad.
- `between_waves_cooldown_s` (float): cooldown fijo entre olas (segundos). Cuando está definido (>0), se usa este valor entre olas en lugar de `cooldown_s` y se ignora la proximidad.

Derivados:
- `cooldown_frames` (int): precalculado en `SpawnerConfig` a partir de `cooldown_s` y `FPS`.
- `restart_cooldown_frames` (int): precalculado a partir de `restart_cooldown_s` (o `cooldown_s`) y `FPS`.
- `between_waves_cooldown_frames` (int): precalculado a partir de `between_waves_cooldown_s` y `FPS`.

---

## 6) Estructura de olas y spawns
Cada ola:
- `spawns`: lista de entradas de spawn.

Entrada de spawn (MVP):
- `kind`: "monster". Otros tipos se ignoran por ahora.
- `id` (string): prototipo del monstruo. Default si falta: "barbol".
- `count` (int): cantidad a intentar spawnear. Default: 1.
- `spread_radius` (int): usado como referencia para el radio de dispersión; hoy influye en el fallback por defecto. Default: 3.
- `spread_fallback_max` (int): radio máximo de búsqueda en espiral alrededor de `anchor_tile`. Default: `max(spread_radius, 8)`.
- `min_px_distance` (int): distancia mínima en píxeles entre el centro del nuevo spawn y actores existentes/reservados (para evitar apelmazamiento). Default: 0.

Colocación (runtime):
- Si `spawn_radius` del template es entero > 0: se intenta primero "aleatorio en área" dentro de ese radio (tiles), respetando `spawner_shape` (`circle` o `square`). Si no hay lugar válido tras varios intentos, se cae a la espiral.
- Si `spawn_radius` es `"random"`/`"aleatorio"`/`"aleatoreo"`: se intenta "aleatorio en área" usando como radio el `spread_fallback_max` del spawn actual, respetando `spawner_shape`. Si falla, se cae a la espiral.
- Si `spawn_radius` no está definido o es 0: se usa la espiral clásica desde `anchor_tile` hasta `spread_fallback_max`.
- En todos los casos se descartan tiles sólidos/edificios, no caminables, ocupados o reservados, y se respeta `min_px_distance`.
Notas:
- El área de defensa (`defend_spawn`) usa la misma forma que `spawner_shape` (círculo o cuadrado).

Resultados por ola:
- Si no se pudo colocar ninguno (`placed = 0`), la ola se considera completada inmediatamente y se avanza.
- Telemetría en logs: colocados vs. intentados.

---

## 7) Ciclo de vida de las olas (runtime)
- Requisito: `SpawnerState.started = true` y al menos 1 ola disponible.
- `cooldown_remaining` decrece por frame; al llegar a 0, se intenta spawnear la ola actual.
- Tras spawnear, `expected_this_wave` = colocados y `spawned_this_wave = true`.
- La ola termina cuando `current_wave_entities` queda vacía (todos eliminados) o cuando no se colocó ninguno.
- Avance de olas:
  - Si quedan más olas: se incrementa `current_wave_idx` y se aplica `between_waves_cooldown_frames` (si > 0) o, de lo contrario, `cooldown_frames` antes de la siguiente.
  - Si no quedan más olas:
    - Si `policy.restart_on_done` (o `loop`/`repeat`) es true: se programa reinicio tras `restart_cooldown_frames`. Durante este tiempo, el spawner queda en estado `DONE`.
    - Si no: `finished = true`.

Notas para modo mixto (proximidad inicial):
- Entre olas, la proximidad no se evalúa; se espera el cooldown fijo (`between_waves_cooldown_s`/frames) y se inicia la siguiente ola automáticamente.
- En reinicio de ciclo (loop/restart): tras `restart_cooldown_frames`, se resetea el latch de proximidad (`initial_proximity_done=false` y `started=false`), por lo que se vuelve a requerir proximidad para iniciar la primera ola del nuevo ciclo.

Notas para `advance_on`:
- `advance_on = "clear"` (default): el punto anterior aplica tal cual. Cada ola espera a que sus mobs mueran para pasar a la siguiente.
- `advance_on = "cooldown"`: tras spawnear una ola, se incrementa inmediatamente `current_wave_idx` y se aplica `cooldown_frames` para la siguiente ola, sin esperar a que la anterior muera. El ciclo sólo se considera finalizado cuando `active_entities` del spawner queda vacío; recién ahí se aplica `restart_cooldown_frames` (si hay loop/restart).

---

## 8) Render/Debug
- Habilitado por `config.DEBUG_SPAWNER`.
- Dibuja:
  - Ancla del spawner y círculo cian de proximidad (si `trigger.type == 'proximity'`).
  - Área de `spawn_radius` (si numérico): círculo verde si `spawner_shape = 'circle'` o cuadrado verde si `spawner_shape = 'square'`.
  - Panel centrado (dentro del círculo) con:
  - `template_id`
  - Estado: `ON`/`OFF`/`DONE` y `wave X/Y`
  - `live/expected` y `cd`/`rc`/`bwc` en segundos
  - `policy.mode`, `loop:on/off` (detecta `restart_on_done`/`loop`/`repeat`) y `shape:circle|square`
- Con `config.DEBUG` (F9): overlay general que incluye, entre otros:
  - Rutas de patrulla (`PatrolRoute`) con puntos y polilíneas.
  - Áreas de defensa `DefendArea`: círculo o cuadrado naranja (según `shape`) con etiqueta `shape` y `r=NNNpx`, y una línea que asocia al NPC.

Visualización en runtime (sin debug):
- Si `visible_in_game=true` y se define `building_id`, el `SpawnerPlacementSystem` vincula el spawner a un `Building` existente con ese id, marcando el objeto con metadatos (`_is_spawner_visual`, `spawner_instance_id`) para evitar duplicados. No se crean sprites propios; se reutiliza el edificio como visual y estos vínculos no se persisten en `buildings_data.json`.

---

## 9) Componentes (referencia rápida)
`SpawnerConfig`:
- `template_id`, `zone`, `anchor_tile` (global), `spawner_type`, `trigger`, `policy`, `waves`, `cooldown_frames`, `restart_cooldown_frames`, `between_waves_cooldown_frames`, `spawn_radius`, `spawner_shape`, `defend_spawn`, `defend_leash`, `visible_in_game`, `building_id`.

`SpawnerState`:
- `started`, `current_wave_idx`, `cooldown_remaining`, `spawned_entities` (no usado), `spawned_this_wave`, `current_wave_entities`, `expected_this_wave`, `finished`, `restart_cooldown_remaining`, `active_entities`, `initial_proximity_done`.

`SpawnRequest`:
- `prototype` (string), `position` (tileX, tileY), `spawner_eid?`, `wave_idx?`, `defend_center?` (px), `defend_radius_px?`, `defend_leash?`, `defend_shape?` (`"circle"|"square"`, default: `"circle"`).

---

## 10) Checklist para definir un spawner
1) Define una plantilla en `spawners_templates.json` (inline `waves` o `waves_id`).
2) (Opcional) Añade olas en `spawners_waves.json` y referencia con `waves_id`.
3) Coloca instancias en `spawners_instances.json` con `zone` y `tile`.
4) Ajusta `policy.cooldown_s`, `policy.restart_on_done`, `policy.proximity_initial_only` y `policy.between_waves_cooldown_s` según el comportamiento deseado.
5) (Opcional) Define `building_id` y pon `visible_in_game=true` para vincular el spawner a un `Building` existente como visual en el juego estándar (sin sprites propios).
6) Activa `config.DEBUG_SPAWNER` para depurar visualmente.

---

## 11) Limitaciones actuales (MVP)
- Triggers soportados: `proximity` y `auto`.
- Sólo `spawn.kind = 'monster'`.
- `policy.persistent` aceptado pero no aplicado todavía.
