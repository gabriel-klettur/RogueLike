 # Plan B — Multi‑mundo con swap por portal

 Documento de diseño para habilitar “mundos alternativos” (carpetas de datos independientes con sus propias zonas, overlays, colisiones y edificios) y viajar entre ellos mediante portales. El objetivo es aislar datos y escalar a campañas/biomas múltiples sin inflar un único world.

 ---

 ## 1) Resumen ejecutivo

 - Separar datos por “mundo” bajo `data/worlds/<world_id>/...`.
 - Introducir `WorldProfile` y `WorldService` para activar un mundo (cambiar rutas) y recargar mapa de forma segura.
 - Extender `TeleportComponent`/`TeleportSystem` para soportar saltos entre mundos y zonas/coords de destino.
 - Ajustar loaders/managers para resolver rutas relativas al mundo activo, cachear por mundo y reetiquetar zonas/colisiones tras el swap.

 Criterios de aceptación:
 - Portal que cambia a otro mundo y posiciona al jugador correctamente, con cámara y colisiones coherentes.
 - Overlays/Collisions/Buildings se leen únicamente del mundo activo.
 - Cache de mapas invalidada correctamente por mundo.
 - Persistencia guarda/restaura `current_world` y estados por mundo.

 ---

 ## 2) Principios de diseño

 - Separación de datos por contexto (world) para aislar contenido y mejorar la mantenibilidad.
 - Inversión de dependencias: runtime consulta rutas a través de `WorldProfile`/`global_map_settings`, no rutas absolutas.
 - Backward compatible: mundo “base” migra desde `data/map` al perfil `data/worlds/base`.
 - Idempotencia en swap: múltiples teleports hacia el mismo mundo producen el mismo estado de rutas.

 ---

 ## 3) Estructura de carpetas propuesta

```
data/
  worlds/
    base/                           # Mundo migrado desde data/map (por defecto)
      zones/
        zones.json                  # Índice de offsets por zona del mundo base
        overlays/
          lobby.overlay.json
          dungeon.overlay.json
          ...
      collisions/
        lobby.json
        dungeon.json
        ...
      buildings/                    # Si aplica, por zona o por mundo
        lobby.buildings.json        # (opcional) formato actual del editor
        ...
      assets/                       # (opcional) sprites/tilesets específicos del mundo
        tilesets/...
    forest/
      zones/
        zones.json
        overlays/
          forest_entry.overlay.json
          ...
      collisions/
        forest_entry.json
      buildings/
        ...
      assets/
        ...
```

 Nota: `data/map` queda deprecado. Mantener un symlink o fase de migración automática para no romper herramientas hasta que todo el pipeline apunte a `worlds/`.

 ---

 ## 4) Modelado de mundo: nuevas APIs

 - `roguelike_engine/worlds/profile.py`
  - `class WorldProfile:`
    - `world_id: str`
    - `base_dir: Path` → `data/worlds/<world_id>`
    - `zones_index: Path` → `<base_dir>/zones/zones.json`
    - `overlays_dir: Path` → `<base_dir>/zones/overlays`
    - `collisions_dir: Path` → `<base_dir>/collisions`
    - `buildings_dir: Path` → `<base_dir>/buildings`

 - `roguelike_engine/worlds/service.py`
  - `class WorldService:`
    - `current: WorldProfile`
    - `registry: dict[str, WorldProfile]` (carga perezosa por descubrir carpetas en `data/worlds/*`)
    - `activate(world_id: str) -> None`:
      - Setea `current` y actualiza `global_map_settings` (rutas), invalida caches necesarias.
    - `list_worlds() -> list[str]`
    - `discover() -> dict[str, WorldProfile]`

 - Integración con settings: `roguelike_engine/config/map_config.py`
  - Añadir a `MapSettings`:
    - `current_world: str = "base"`
    - `worlds_dir: Path = Path(DATA_DIR) / 'worlds'`
    - `@property ZONES_INDEX` pasa a resolverse dinámicamente desde `WorldService.current.zones_index`.
    - Nuevos properties: `overlays_dir`, `collisions_dir`, `buildings_dir` desde el perfil activo.
  - Añadir `set_world(world_id: str)` que actualiza `current_world`, refresca `zone_offsets` y límites.

 ---

 ## 5) Cambios por módulo (impactos y exactitud)

 1) `roguelike_game/managers/map/loader.py (MapLoader)`
 - Clave de cache → incluir `current_world`: `map_{world}_{map_name}.pkl`.
 - Invalidación de cache → mirar `overlays_dir` y `zones_index` del mundo activo.
 - Al `load(map_name)`: no fijar `use_zones_json=True` de forma global si un editor forza otro modo; mantener, pero siempre apuntando al índice del mundo activo.

 2) `roguelike_engine/map/model/overlay/overlay_manager.py`
 - Resolver overlays desde `global_map_settings.overlays_dir`.
 - Guardar/leer por zona en la carpeta del mundo activo.

 3) `roguelike_game/managers/map/collision.py (CollisionManager)`
 - Usar `global_map_settings.collisions_dir` en lugar de `DATA_DIR/map/collisions`.
 - Mantener auto‑generación si no existe JSON de colisiones para la zona en el mundo activo.

 4) `roguelike_engine/config/map_config.py (MapSettings)`
 - Inyectar `WorldProfile` activo para:
   - `ZONES_INDEX` dinámico.
   - Nuevos properties `overlays_dir`, `collisions_dir`, `buildings_dir`.
 - `refresh_zone_offsets()` invalida cache cuando cambia el mundo.
 - Mantener sentinelas y auto‑expand igual.

 5) `roguelike_game/ecs/components/items/teleport_component.py`
 - Extender a:
   - `dest_world: str | None = None`
   - `dest_zone: str | None = None` (opcional, por legibilidad)
   - `dest_x: int`, `dest_y: int` (coordenadas de tile globales dentro del mundo destino)
 - Backward compatible: si `dest_world` es `None`, teleporta en el mundo actual.

 6) `roguelike_game/ecs/systems/items/teleport_system.py`
 - Flujo:
   1. Detectar activación.
   2. Si `dest_world` y `dest_world != WorldService.current.world_id` → `WorldService.activate(dest_world)`.
   3. Pedir a `MapManager` re‑load/swap (ver abajo) y reubicar jugador en `(dest_x, dest_y)`.
   4. Recentrar cámara y recalcular colisiones.

 7) `roguelike_game/managers/map/__init__.py (MapManager)`
 - Añadir método `swap_world_and_spawn(world_id: str, tile_pos: tuple[int,int])`:
   - `WorldService.activate(world_id)`.
   - `reload_map()` o reconstruir `MapManager` si la semántica actual lo requiere.
   - `spawn_player(tile_pos)` y `collision_manager.load(self)`.
   - Invalidar vistas (`view.invalidate_cache()`).
 - Asegurar que `tiles_by_zone` y `solid_tiles` se recalculan tras swap.

 8) `roguelike_engine/map/controller/map_controller.py` y `map_service.py`
 - Verificar que la construcción de `Map` usa `global_map_settings.zone_offsets` del mundo activo.
 - No asumir rutas fijas de `data/map`.

 9) Minimap/Render pipeline
 - Que la vista “chunked” ya opere con el `Map` resultante; no necesita cambios si `MapManager` actualiza referencias en sitio.

 10) Persistencia (Save/Load)
 - Guardar `current_world` y estados por mundo:
   - `save: { current_world, worlds: { <world_id>: { player_pos, npc_states, ... } } }`.
 - Al cargar: `WorldService.activate(saved.current_world)` y `MapManager.deserialize_state(worlds[current_world])`.

 11) Editores (Map/Buildings/Overlays)
 - Selección de mundo activo en UI de editores.
 - Los editores deben usar `global_map_settings.*_dir` y `ZONES_INDEX` dinámico para leer/escribir.

 ---

 ## 6) Flujo de teletransporte (end‑to‑end)

 1. Jugador colisiona con portal (ECS detecta proximidad).
 2. Sistema lee `TeleportComponent`:
    - Si `dest_world` es `None` o igual al actual → mover a `(dest_x, dest_y)` en el mismo mundo.
    - Si `dest_world` distinto → `MapManager.swap_world_and_spawn(dest_world, (dest_x,dest_y))`.
 3. `swap_world_and_spawn`:
    - `WorldService.activate(dest_world)` → cambia rutas, invalida caches.
    - `MapLoader.load(map_name)` reconstruye `Map` para el mundo activo (cache por mundo).
    - Recalcula `tiles_by_zone`, `collision_layers`, `solid_tiles`.
    - Reposiciona jugador, recentra cámara, invalida caches de render.

 ---

 ## 7) Migración de datos

 Paso 1 — Crear estructura `data/worlds/base` y mover contenido de `data/map`:
 - `data/map/zones/zones.json` → `data/worlds/base/zones/zones.json`.
 - `data/map/zones/overlays/*` → `data/worlds/base/zones/overlays/*`.
 - `data/map/collisions/*` → `data/worlds/base/collisions/*`.
 - `data/buildings/*` si existía por mundo → `data/worlds/base/buildings/*`.

 Paso 2 — Configurar default:
 - `MapSettings.current_world = "base"`.
 - `MapSettings.worlds_dir = Path(DATA_DIR) / 'worlds'`.

 Paso 3 — Compatibilidad temporal (opcional):
 - Mantener `data/map` como alias/symlink hacia `data/worlds/base` durante la transición de editores.

 ---

 ## 8) Plan de implementación incremental

 1) Infraestructura de mundos (sin cambiar gameplay):
 - Añadir `WorldProfile`, `WorldService`, y nuevas properties en `MapSettings`.
 - Adaptar `overlay_manager`, `CollisionManager`, `MapLoader` a rutas del mundo activo.
 - Migrar datos a `worlds/base` y validar carga y render sin teleports cross‑world.

 2) Teleport cross‑world:
 - Extender `TeleportComponent` y `TeleportSystem`.
 - Implementar `MapManager.swap_world_and_spawn`.

 3) Persistencia:
 - Ampliar Save/Load con `current_world` y estados por mundo.

 4) Editores:
 - Añadir selector de mundo y rutas dinámicas para leer/escribir assets de mundo.

 5) Pruebas y estabilización.

 ---

 ## 9) Pruebas (mínimas)

 - Carga del mundo base tras migración; equivalencia visual con antes del cambio.
 - Cache por mundo: modificar overlay en `forest` invalida solo cache de `forest`.
 - Teleport dentro del mismo mundo: mueve a coordenadas sin recarga.
 - Teleport cross‑world: swap, spawn en destino, colisiones correctas, cámara correcta.
 - Persistencia: guardar en `forest`, cargar y reaparecer en `forest` con estados.
 - Editores: guardar overlay/colisiones en el mundo seleccionado, reflejado en runtime.

 ---

 ## 10) Riesgos y mitigaciones

 - Cache inconsistente entre mundos → clave de cache con `world_id`; invalidación por directorio del mundo activo.
 - Fugas de referencias tras swap → `reload_map` actualiza referencias; opción de reconstruir `MapManager` si necesario.
 - Ruptura de editores por rutas fijas → centralizar rutas en `MapSettings`/`WorldService` y actualizar editores.
 - Rendimiento en mundos grandes → chunked view ya presente; medir FPS y usar perfiles por mundo.

 ---

 ## 11) “Cómo defender este diseño” (checklist)

 - Objetivo/aceptación: viajar entre mundos aislados, datos por mundo, cache coherente, persistencia por mundo.
 - Justificación: separación de preocupaciones y escalabilidad; evita inflar un único world.
 - Impacto rendimiento/memoria: caches por mundo, carga bajo demanda; coste de swap acotado al cambio de mundo.
 - Extensibilidad: añadir nuevos mundos es crear carpetas y datos; sistemas consultan rutas dinámicas.
 - Riesgos: coordinación de rutas y caches; mitigado con `WorldService` único y pruebas dirigidas.

 ---

 ## 12) Glosario rápido

 - Mundo (world) — Conjunto aislado de zonas y datos. Úsalo para campañas/biomas. Ej: `base`, `forest`.
 - Perfil de mundo (WorldProfile) — Descripción de rutas de un mundo. Úsalo para resolver overlays/collisions/buildings.
 - Swap de mundo — Cambio del perfil activo con recarga segura del mapa. Úsalo al cruzar portales entre mundos.
 - Índice de zonas (zones.json) — Offsets por zona dentro de un mundo. Úsalo para fusionar el mapa global del mundo.
  - Invalidación de cache — Borrar cache cuando cambian overlays/zones. Úsalo para ver cambios de datos en runtime.


---

## 13) Migración detallada de datos (data/map y data/buildings)

- data/map → data/worlds/base
  - Mover:
    - `data/map/zones/zones.json` → `data/worlds/base/zones/zones.json`
    - `data/map/zones/overlays/*.overlay.json` → `data/worlds/base/zones/overlays/*.overlay.json`
    - `data/map/collisions/*.json` → `data/worlds/base/collisions/*.json`
  - Opción temporal: symlink `data/map` → `data/worlds/base` para no romper herramientas hasta que todo apunte a `worlds/`.

- data/buildings → data/worlds/base/buildings
  - Si usáis persistencia split:
    - `data/buildings/buildings_templates.json` → `data/worlds/base/buildings/buildings_templates.json`
    - `data/buildings/buildings_instances.json` → `data/worlds/base/buildings/buildings_instances.json`
  - Colisiones de edificios (si se usan los JSON divididos):
    - `data/buildings/buildings_collisions_by_image.json` → `data/worlds/base/buildings/buildings_collisions_by_image.json`
    - `data/buildings/buildings_collisions_by_spawn_id.json` → `data/worlds/base/buildings/buildings_collisions_by_spawn_id.json`
    - `data/buildings/buildings_collisions_by_building_instance_id.json` → `data/worlds/base/buildings/buildings_collisions_by_building_instance_id.json`
  - Nota: durante la transición, `WorldService.activate('base')` puede reescribir dinámicamente estas rutas para que editores sigan funcionando sin cambios masivos inmediatos (ver §15).

Checklist de verificación de migración:
- `data/worlds/base/zones/zones.json` abre y contiene todas las zonas esperadas (incluye lobby/dungeon normalizados).
- Overlays y collisions por zona existen y se corresponden.
- Plantillas/instancias de edificios están presentes en `worlds/base/buildings` si se usan edificios.

---

## 14) Cambios en lectura/escritura de editores (src/roguelike_editors)

- Overlays/Tiles/Map Editor
  - Los editores consumen `overlay_manager`, que delega en `JsonOverlayStore`.
  - Acción requerida: tras hacer `JsonOverlayStore` world-aware (ver §16.1), los editores quedarán apuntando al mundo activo sin cambios adicionales.

- Buildings Editor
  - Hoy usa rutas fijas desde `roguelike_engine.config.config`:
    - `BUILDINGS_TEMPLATES_PATH`, `BUILDINGS_INSTANCES_PATH`, y JSONs de colisión.
  - Para multi-mundo, proponemos dos fases:
    1) Fase transición (rápida, sin romper editores):
       - `WorldService.activate(world_id)` actualizará dinámicamente las rutas de persistencia de edificios vía monkeypatch centralizado (ver §16.3), reutilizando el mecanismo `_sync_paths_to_helpers()` existente.
       - Sin cambios en UI.
    2) Fase UI (mejora):
       - Añadir selector de mundo en los editores para elegir `current_world` y llamar a `WorldService.activate()`.
       - Mostrar rutas activas en la barra de estado para trazabilidad.

- Validaciones a añadir en editores:
  - Mostrar advertencia si `ZONES_INDEX` o directorios de overlays/collisions no existen en el mundo activo.
  - Ofrecer “Crear estructura de mundo” si faltan carpetas.

---

## 15) Cambios necesarios en engine y game (archivo por archivo)

15.1) Engine — overlays
- Archivo: `src/roguelike_engine/map/model/overlay/json_store.py`
  - Cambiar `self.zones_dir = Path(DATA_DIR) / "map" / "zones" / "overlays"` por `self.zones_dir = global_map_settings.overlays_dir`.
  - `load()`/`save()` mantienen lógica actual; sólo cambian las rutas al mundo activo.

15.2) Engine — configuración de mapa
- Archivo: `src/roguelike_engine/config/map_config.py`
  - Añadir a `MapSettings`:
    - `current_world: str` y `worlds_dir: Path`.
    - Properties dinámicos:
      - `ZONES_INDEX` → `WorldService.current.zones_index`.
      - `overlays_dir`, `collisions_dir`, `buildings_dir` → desde `WorldService.current`.
    - Método `set_world(world_id: str)` que actualiza `current_world` y hace `refresh_zone_offsets()`.

15.3) Engine — collisions
- Archivo: `src/roguelike_game/managers/map/collision.py`
  - Sustituir `collisions_dir = Path(DATA_DIR) / "map" / "collisions"` por `collisions_dir = global_map_settings.collisions_dir`.
  - Mantener auto‑generación si no hay fichero por zona.

15.4) Game — MapLoader (cache per‑world)
- Archivo: `src/roguelike_game/managers/map/loader.py`
  - Cambiar `cache_file = cache_dir / f"map_{map_name}.pkl"` por `map_{current_world}_{map_name}.pkl`.
  - Invalidación de cache: usar `global_map_settings.ZONES_INDEX` y `global_map_settings.overlays_dir` del mundo activo.

15.5) Game — Teleport cross‑world
- Archivos:
  - `src/roguelike_game/ecs/components/items/teleport_component.py`
    - Extender firma a: `dest_world: str | None = None`, `dest_zone: str | None = None`, `dest_x: int`, `dest_y: int`.
    - Backward‑compat: si `dest_world is None` → intra‑mundo.
  - `src/roguelike_game/ecs/systems/items/teleport_system.py`
    - Implementar flujo: si `dest_world != current`, llamar a `MapManager.swap_world_and_spawn(dest_world, (dest_x, dest_y))`; si no, mover en el mundo actual.

15.6) Game — MapManager (swap en sitio)
- Archivo: `src/roguelike_game/managers/map/__init__.py`
  - Añadir `swap_world_and_spawn(world_id, tile_pos)`:
    - `WorldService.activate(world_id)`.
    - `reload_map()` o reconstrucción conservando referencias de renderer/view si es posible.
    - `spawn_player(tile_pos)` + recarga de colisiones + `view.invalidate_cache()`.

15.7) Engine — map_service/map_controller
- Revisar que toda carga de zonas/overlays use `global_map_settings` (ya lo hace) y no rutas fijas a `data/map`.

15.8) Edificios (rutas)
- Archivo: `src/roguelike_engine/config/config.py` (fase transición):
  - Mantener constantes, pero permitir que `WorldService.activate()` las reescriba hacia `worlds/<id>/buildings/*.json` en runtime (ver §16.3).
  - A largo plazo, migrar a getters dinámicos desde `MapSettings` en lugar de constantes de módulo.

---

## 16) WorldService: activación y rutas dinámicas

16.1) Overlays/Collisions/Zones
- `WorldService.activate(world_id)` debe:
  - Actualizar `MapSettings.current_world`.
  - Actualizar propiedades dinámicas (`ZONES_INDEX`, `overlays_dir`, `collisions_dir`, `buildings_dir`).
  - Invalidar caches: `global_map_settings.refresh_zone_offsets()` y cualquier caché de loaders.

16.2) Cache de mapas por mundo
- `MapLoader` construye caché con prefijo del mundo y la invalida contra directorios del mundo activo.

16.3) Buildings (compatibilidad sin refactor masivo)
- Añadir utilitario `set_buildings_paths_for_world(profile)`:
  - Reescribe en runtime los paths de edificios hacia `profile.buildings_dir` en:
    - `roguelike_engine.config.config` (BUILDINGS_*).
    - Módulos ayudantes que capturaron constantes a la importación (usar lógica similar a `_sync_paths_to_helpers()`).
  - `WorldService.activate()` lo invoca tras cambiar de mundo.

---

## 17) Validación de editores tras migración

- Tiles/Overlays: abrir un overlay, editar y guardar; verificar que el archivo modificado cae en `worlds/base/zones/overlays`.
- Buildings: crear/editar instancia y guardar; verificar que `buildings_templates.json`/`buildings_instances.json` en el mundo activo cambian.
- Colisiones por zona: pintar colisiones y confirmar escritura en `worlds/base/collisions`.

---

## 18) Plan de trabajo paso a paso (orden sugerido)

1) Crear `WorldProfile`/`WorldService` + `MapSettings` dinámico (rutas por mundo).
2) Migrar datos a `data/worlds/base`.
3) Ajustar `JsonOverlayStore` y `CollisionManager` a rutas por mundo.
4) `MapLoader`: cache por mundo e invalidación.
5) Transición de Buildings: `set_buildings_paths_for_world()` + activación desde `WorldService`.
6) Implementar `TeleportComponent`/`TeleportSystem` cross‑world y `MapManager.swap_world_and_spawn`.
7) Persistencia de `current_world` y estado por mundo.
8) UI editores: selector de mundo (opcional), validaciones.
9) Suite de pruebas de §9 y validación manual de §17.
