# Spawners: Esquema de datos y comportamiento en runtime

Este documento describe el formato de los archivos en `data/spawners/` y cómo el motor interpreta cada propiedad.

Rutas relevantes:
- Plantillas: `data/spawners/spawners_templates.json`
- Olas: `data/spawners/spawners_waves.json`
- Instancias: `data/spawners/spawners_instances.json`

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
- `trigger` (objeto): ver sección Triggers.
- `policy` (objeto): ver sección Policies.
- `waves` (lista de objetos) OPCIONAL si usas `waves_id`:
  - Cada ola es `{ "spawns": [ SPAWN_ENTRY, ... ] }`. Ver sección Spawns.
- `waves_id` (string) OPCIONAL: referencia a un conjunto de olas definido en `spawners_waves.json`.

Ejemplo mínimo de plantilla inline:
```json
{
  "id": "barbol_periodic_no_stack",
  "spawner_type": "invisible",
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

Ejemplo:
```json
{
  "template_id": "barbol_periodic_no_stack",
  "zone": "lobby",
  "tile": [10, 12],
  "overrides": {
    "policy.restart_on_done": true,
    "trigger.radius": 6
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

Derivados:
- `cooldown_frames` (int): precalculado en `SpawnerConfig` a partir de `cooldown_s` y `FPS`.
- `restart_cooldown_frames` (int): precalculado a partir de `restart_cooldown_s` (o `cooldown_s`) y `FPS`.

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
- Se busca en espiral desde `anchor_tile` hasta `spread_fallback_max`.
- Se descartan tiles sólidos (`map_manager.solid_tiles`) y colisiones de edificios (`building.collision_tiles`).
- Si hay `map_manager.is_walkable(tx, ty)`, se valida caminabilidad.
- Se evitan tiles ocupados por NPCs/jugador vivos y tiles ya reservados en el tick.
- Si `min_px_distance > 0`, se aplica chequeo en píxeles entre centros de tiles.

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
  - Si quedan más olas: se incrementa `current_wave_idx` y se aplica `cooldown_frames` antes de la siguiente.
  - Si no quedan más olas:
    - Si `policy.restart_on_done` (o `loop`/`repeat`) es true: se programa reinicio tras `restart_cooldown_frames`. Durante este tiempo, el spawner queda en estado `DONE`.
    - Si no: `finished = true`.

Notas para `advance_on`:
- `advance_on = "clear"` (default): el punto anterior aplica tal cual. Cada ola espera a que sus mobs mueran para pasar a la siguiente.
- `advance_on = "cooldown"`: tras spawnear una ola, se incrementa inmediatamente `current_wave_idx` y se aplica `cooldown_frames` para la siguiente ola, sin esperar a que la anterior muera. El ciclo sólo se considera finalizado cuando `active_entities` del spawner queda vacío; recién ahí se aplica `restart_cooldown_frames` (si hay loop/restart).

---

## 8) Render/Debug
- Habilitado por `config.DEBUG_SPAWNER`.
- Dibuja:
  - Ancla del spawner y círculo cian de proximidad (si `trigger.type == 'proximity'`).
  - Panel centrado (dentro del círculo) con:
    - `template_id`
    - Estado: `ON`/`OFF`/`DONE` y `wave X/Y`
    - `live/expected` y `cd` en segundos
    - `policy.mode` y `loop:on/off` (detecta `restart_on_done`/`loop`/`repeat`)

---

## 9) Componentes (referencia rápida)
`SpawnerConfig`:
- `template_id`, `zone`, `anchor_tile` (global), `spawner_type`, `trigger`, `policy`, `waves`, `cooldown_frames`, `restart_cooldown_frames`.

`SpawnerState`:
- `started`, `current_wave_idx`, `cooldown_remaining`, `spawned_entities` (no usado), `spawned_this_wave`, `current_wave_entities`, `expected_this_wave`, `finished`, `restart_cooldown_remaining`, `active_entities`.

`SpawnRequest`:
- `prototype` (string), `position` (tileX, tileY), `spawner_eid?`, `wave_idx?`.

---

## 10) Checklist para definir un spawner
1) Define una plantilla en `spawners_templates.json` (inline `waves` o `waves_id`).
2) (Opcional) Añade olas en `spawners_waves.json` y referencia con `waves_id`.
3) Coloca instancias en `spawners_instances.json` con `zone` y `tile`.
4) Ajusta `policy.cooldown_s` y `policy.restart_on_done` según el comportamiento deseado.
5) Activa `config.DEBUG_SPAWNER` para depurar visualmente.

---

## 11) Limitaciones actuales (MVP)
- Triggers soportados: `proximity` y `auto`.
- Sólo `spawn.kind = 'monster'`.
- `policy.persistent` aceptado pero no aplicado todavía.
- `spread_radius` hoy sólo afecta el `spread_fallback_max` por defecto; la búsqueda real es en espiral.
