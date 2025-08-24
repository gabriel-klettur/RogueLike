# roguelike_game

Lógica de juego, bucle principal y capa de orquestación que consume el motor `roguelike_engine`. Aquí viven el ECS (entidades/componentes/sistemas), los managers del bucle, la configuración de entrada y hechizos, la carga de mapas/ítems y la integración con UI/editores.

## Objetivos
- **Orquestación del juego**: inicializar, correr y cerrar el bucle (`Game.run()`), enrutar eventos y renderizar.
- **ECS modular**: sistemas desacoplados por dominio (input, física, combate, render, inventario, etc.).
- **Integración con el motor**: reutilizar cámara, mapa, edificios, minimapa, capas Z y utilidades del engine.
- **Productividad**: soporte a editores in‑game (tiles, buildings, map, inventory, spells, entities FSM) y perfiles de rendimiento.

## Punto de entrada y ejecución
- Ejecuta con Python:
  - `python launcher.py` (recomendado; configura logging y ruta `src/`)
  - Alternativa directa: `python src/roguelike_game/main.py`
- Entrypoint `src/roguelike_game/main.py`:
  - `main()` hace: `init_pygame()` → `create_screen()` → `configure_window()` → `init_performance_tools()` → `create_game()` → `run_game_loop()`.
  - Crea `Game` desde `managers/core/game.py`.
- `launcher.py` inicializa logging del engine (`roguelike_engine.log_config.init_logging`).

## Arquitectura general
- **Bucle principal** (`managers/core/`):
  - `game.py` define `Game` con etapas: `handle_events()`, `update()`, `render()` y ciclo `run()` usando `loop_manager.py`.
  - `update_manager.py` coordina: prioridad de editores, seguimiento de cámara al jugador, actualización de entidades (buildings), y `minimap.update(...)`.
  - `render_manager.py` expone `renderer.render_game(...)` consumido por `Game.render()`.
  - `events.py`, `initializer.py`, `shutdown_manager.py`, `state.py` completan la orquestación.
- **ECS** (`ecs/`):
  - Componentes en `ecs/components/...` (habilidades, AI, combate, inventario, etc.).
  - Sistemas en `ecs/systems/...` con subdominios: `input/`, `physics/`, `combat/`, `rendering/`, `inventory/`, `items/`, `experience/`, `fsm/`, `debug/`, `audio/`, `particles/`, `core/`.
  - Núcleo en `ecs/core/`: `manager.py`, `component_registry.py`, `system_registry.py`, `spatial_index.py`, `spawn_manager.py`.
  - Managers de ejecución ECS en `managers/ecs/`: `loader.py`, `runner.py`, `spawner.py`.
- **Mapas y mundo**:
  - `map/`: `loader.py`, `generator.py`, `collision.py`, `pathfinding.py`, `renderer.py`, `item_drop_manager.py`.
  - `z_layer/assigner.py` integra el orden de capas de render/actualización.
- **Jugadores y menús**:
  - `player/`: `player_manager.py`, `class_selector_manager.py`.
  - `menu/` (MVC): `controller/menu_manager.py`, `controller/menu_handler.py`, `Model/menu_model.py`, `Events/menu_events.py`.
- **Editores integrados** (`managers/editors/`): `tiles_editor_manager.py`, `buildings_editor_manager.py`, `map_editor_manager.py`, `inventory_editor_manager.py`, `items_editor_manager.py`, `entities_editor_manager.py`, `spells_editor_manager.py`.
- **Utilidades**: `utils/benchmark.py` registra timings y guarda benchmarks al cerrar.

## Integración con roguelike_engine
- Cámara: `roguelike_engine.camera.camera.Camera` (seguimiento al jugador; ver `update_manager.py`).
- Mapa y Tiles: modelos/vistas y render por chunks desde `roguelike_engine.map.*` y `roguelike_engine.tile.*`.
- Edificios: `roguelike_engine.buildings.*`.
- Minimap: `roguelike_engine.minimap.minimap.Minimap`.
- Capas Z: `roguelike_engine.z_layer.*` para estado, persistencia y render.

## Sistemas y ejemplos reales (ECS)
- **Input** (`ecs/systems/input/input_system.py`):
  - Lee bindings dinámicos de `config/input_config.py` (JSON externo en `data/config/input_bindings.json`).
  - Soporta recarga de hechizos en caliente (F4 → `config/spells_config.py: reload_spells()`).
  - Atajos in‑game observables en código:
    - Click izq.: `WantsToCastSpell('fireball')` en flanco ascendente.
    - Click medio: `WantsToCastSpell('laser_beam')`.
    - Click der.: dash (flanco ascendente, suprimido sobre panel inventario).
    - ALT: `show_all_drops`.
  - Respeta bloqueos/arrastres UI (`roguelike_ui.ui_blocker.is_blocked`, `InventoryUISystem`).
  - Gating por editores: desactiva hechizos/ataque cuando `buildings_editor_active` o `item_editor_state.visible`.
- **Física** (`ecs/systems/physics/`): `movement_collision_system.py`, `facing_system.py`, `player_facing_system.py`.
- **Combate** (`ecs/systems/combat/`): `hitbox_system.py`, `explosion_system.py`, `combat/melee/...`, `spells/...`.
- **Render** (`ecs/systems/rendering/`): `render_system.py`, `health_bar_system.py`, `nameplate_system.py`, `animation_system.py`, `trail_system.py`, `experience_render_system.py`, `grayscale_render_system.py`, etc.
- **Inventory & Drops** (`ecs/systems/inventory/`): `inventory_ui_system.py`, `inventory_init_system.py`, `inventory_pickup_system.py`, `inventory_transfer_system.py`, `drop_drag_system.py`, `death_drop_system.py`, `map_load_drops_system.py`.
- **Items** (`ecs/systems/items/`): `item_factory.py`, `consume_system.py`, `teleport_system.py`.
- **FSM** (`ecs/systems/fsm/`): `fsm_system.py`, `state.py`, `states/...` y puente de animación `anim_bridge.py`.

## Flujo de actualización y render
- `Game.handle_events()` → `managers/core/events.py`.
- `Game.update()` prioriza editores visibles y propaga flags a `state`.
- `update_manager.update_game(...)`:
  - Si un editor está activo, actualiza solo ese editor y permite panning de cámara (Map Editor).
  - Si no, la cámara sigue al jugador vía `ecs.ecs_world.player_entity` y componente `Position`.
  - Actualiza `buildings` y `minimap` con la posición del jugador y tiles visibles.
- `Game.render()` llama a `renderer.render_game(...)` y luego dibuja overlays de editores y consola.
- `Game.update_ecs()` / `render_ecs()` ejecutan sistemas ECS (pausados si un editor relevante está visible).

## Configuración
- `config/input_config.py`: bindings de teclado. Fuente JSON por defecto: `data/config/input_bindings.json`.
- `config/spells_config.py`, `config/spells_defaults.py`: definición/carga de hechizos. Recarga manual: F4.
- `config/players_config.py`: parámetros de jugador/clases.

## Estructura (resumen por carpetas)
- `managers/core/`: `game.py`, `events.py`, `initializer.py`, `loop_manager.py`, `render_manager.py`, `shutdown_manager.py`, `state.py`, `update_manager.py`.
- `managers/ecs/`: `loader.py`, `runner.py`, `spawner.py`.
- `ecs/core/`: `manager.py`, `component_registry.py`, `system_registry.py`, `spatial_index.py`, `spawn_manager.py`.
- `ecs/components/`: submódulos `abilities/`, `ai/`, `combat/`, etc.
- `ecs/systems/`: submódulos `input/`, `physics/`, `combat/`, `rendering/`, `inventory/`, `items/`, `experience/`, `fsm/`, `debug/`, `audio/`, `particles/`, `core/`.
- `map/`: `loader.py`, `generator.py`, `collision.py`, `renderer.py`, `pathfinding.py`, `item_drop_manager.py`.
- `player/`: `player_manager.py`, `class_selector_manager.py`.
- `menu/`: MVC (controller/model/events).
- `z_layer/`: `assigner.py`.
- `items/`: `loader.py`.
- `utils/benchmark.py`.

## Cómo extender
- **Nuevo Sistema ECS**:
  - Crea el sistema en `ecs/systems/<dominio>/mi_sistema.py` con método `update(...)` y/o `render(...)` según convenga.
  - Regístralo/ordénalo en `ecs/core/system_registry.py` (etapa de update/render apropiada).
  - Asegúrate de que los componentes requeridos estén en `ecs/components/...` (y opcionalmente registrados en `component_registry.py`).
- **Spawns y entidades**:
  - Usa `managers/ecs/spawner.py` y `ecs/core/spawn_manager.py` para crear entidades con sus componentes.
- **Map/Ítems/Drop**:
  - Amplía `map/` o `ecs/systems/inventory/` según la mecánica (p.ej., drops contextuales en `map_load_drops_system.py`).
- **Integración con editores**:
  - Si tu feature necesita pausar/suprimir input, mira los flags que propaga `Game.state` (`buildings_editor_active`, `spells_editor_visible`, etc.).

## Dependencias
- Ver `requirements.txt`. Claves: `pygame`, `tcod`, `pygame-menu`, `pydantic`, `jsonschema`, `pytest`.
- Activos/JSON externos en `assets/` y `data/` (colisiones, bindings, etc.).

## Tests
- Pruebas en `tests/` (fixtures y paquetes por módulo). Ejecuta con `pytest`.

## Licencia
- Ver `LICENSE` en la raíz del repositorio.
