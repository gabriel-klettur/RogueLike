# Resumen Técnico del Roguelike

Este documento resume, con detalle, todas las piezas que componen nuestro roguelike: arquitectura por paquetes, sistemas del motor, lógica de juego, editores integrados, y cómo organizamos los datos entre JSON y SQLite. El objetivo es servir de mapa técnico para onboarding, revisiones y planificación.

---

## Visión general de la arquitectura

- **Paquetes**:
  - `src/roguelike_engine/` — Motor reutilizable: mapa/tiles, edificios, cámara, minimapa, capas Z, caché, consola/diagnóstico, audio e input.
  - `src/roguelike_game/` — Juego: bucle principal, ECS (entidades/componentes/sistemas), managers de orquestación, carga de mapas/ítems, menús y editores in‑game.
  - `src/roguelike_editors/` — Editores dedicados (tiles, edificios, spawners, entidades, inventario, items, spells, lighting, map, particles) con MVC ligero.
- **Entrada principal**:
  - `launcher.py` prepara logging del engine y ejecuta `roguelike_game.main: main()`.
- **Activos y datos externos**: `assets/` (imágenes/audio) y `data/` (JSON, caché, mundos, vendors, etc.).
- **Persistencia relacional**: `roguelike_engine.db` (SQLAlchemy) y migraciones Alembic en `alembic/`.

---

## Motor: `roguelike_engine`

- **Mapa y Tiles**
  - MVC por dominio: `map/{controller,model,view}/`, `tile/{controller,model,view}/`.
  - Render por chunks (`map/view/chunked_map_view.py`) para escalar mapas grandes.
  - Carga/gestión de assets de tiles (`tile/utils/assets.py`, `utils/loader.py`).
- **Edificios**
  - Modelo/vista/controlador de edificios con colisiones y “outline” (hitbox/contorno) en `buildings/`.
- **Cámara y Minimap**
  - `camera/camera.py` con posición/zoom y transformaciones básicas.
  - `minimap/minimap.py` configurable y acoplado a los tiles visibles.
- **Capas Z (Z‑Layer)**
  - Orden de actualización/dibujo y persistencia (`z_layer/state.py`, `logic.py`, `render.py`, `persistence.py`).
- **Consola y Diagnóstico**
  - Consola in‑game con MVC (`console/*`) y overlay de debug (`diagnostics/overlay/*`).
- **Input**
  - Normaliza teclado/ratón (`input/{keyboard,mouse,events}.py`).
- **Audio**
  - Servicio de audio (`audio/{api,service,backend_pygame,events,cache,config}.py`) para música/FX con backend Pygame.
- **Caché y utilidades**
  - Caché memoria/archivo (`cache/*`), utilidades de carga y pantalla de loading (`utils/*`).
- **Mundo y persistencia**
  - Estado de mundo y slots con persistencia (`world/*`).
- **Logging**
  - `log_config.py` inicializa consola y “rotating file handler” (véase `launcher.py`).

---

## Juego: `roguelike_game`

- **Bucle principal y managers (managers/core)**
  - `Game.run()` orquesta `handle_events()` → `update()` → `render()`.
  - `update_manager.py` prioriza editores visibles, sigue al jugador con la cámara y actualiza minimapa.
  - `render_manager.py` compone el frame (incluye overlays de editores y consola).
  - `events.py`, `initializer.py`, `loop_manager.py`, `shutdown_manager.py`, `state.py` completan la orquestación.
- **ECS (ecs/)**
  - Núcleo: `ecs/core/{manager,component_registry,system_registry,spatial_index,spawn_manager}.py`.
  - Componentes: `ecs/components/...` (AI, combate, inventario, habilidades, chat/NPC vendor, etc.).
  - Sistemas: `ecs/systems/...` (input, physics, combat, rendering, inventory, items, experience, fsm, audio, particles, debug, core).
  - Render: barras de vida, nameplates, animaciones, trails, efectos, etc.
- **Mapas y mundo**
  - `map/{loader,generator,collision,renderer,pathfinding,item_drop_manager}.py`.
  - Integración de capas Z con `z_layer/assigner.py`.
- **Jugador y menús**
  - `player/{player_manager,class_selector_manager}.py`.
  - Menú con MVC en `menu/`.
- **Integraciones clave**
  - Hot reload de spells desde `config/spells_config.py` (F4).
  - Benchmarks por decorador y logger (`utils/benchmark.py`).

---

## Editores: `roguelike_editors`

Editores in‑game con estructura MVC (carpetas `controller/`, `model/`, `view/`, `events/`, `services/` y paneles por herramienta).

- **Tiles Editor (`tiles/`)**
  - Herramientas de toolbar: `select`, `brush`, `eyedropper`, `view`, `view_layers`, `view_collisions`, `delete`, `default`.
  - Paneles: `tiles_picker_panel` (selector), `layers_panel`, `size_panel`, `tiles_view_panel`, `tiles_collision_panel`, `tiles_title`, tutoriales.
  - Comportamiento verificado por tests (`tests/roguelike_editors/tiles/test_tiles_toolbar_panel.py`):
    - Toggling de herramientas, apertura/cierre de picker y layers.
    - Modo colisiones: ciclo off → only → overlay, con picker de colisiones.
    - Acciones batch para `delete` y `default` (start/flush brush) y drag del toolbar con clic derecho.
- **Buildings Editor (`buildings/`)**
  - Paneles: `buildings_add_remove_panel`, `buildings_colliders_panel`, `buildings_picker`, `buildings_properties_panel`, `buildings_tool_bar_panel`, `buildings_tutorial_panel`.
  - Controladores/modelo/vista dedicados y guía en `README_BUILDINGS.md`.
- **Spawner Editor (`spawner/`)**
  - Diferenciación intencional de controladores:
    - `SpawnerManagerController` (lista de plantillas; toolbar `spawner_manager`).
    - `SpawnersManagerController` (propiedades del template seleccionado; subpanel dentro del Manager).
  - Z‑Order documentado (`README_z-order.md`) y `z-order.json` para la vista.
  - Paneles: `spawner_instances_panel`, `spawner_instance_properties_panel`, `spawner_templates_panel`, `spawner_template_properties_panel`, toolbars y tutorial.
  - Validaciones de “visuals” vía script de sanity (`tests/spawner_visuals_sanity.py`).
- **Entities Editor (`entities/`)**
  - Gestión de catálogo de entidades, selección de assets, propiedades y paneles de título/toolbar/tutorial.
- **Otros editores presentes**
  - **Inventory (`inventory/`)**
    - Archivos núcleo: `editor_controller.py`, `editor_model.py`, `editor_view.py`, `editor_events.py`, `data_controller.py`.
    - Paneles: `inventory_title/`, `left_panel/`, `right_panel/`. Guía: `README_INVENTORY.md`.
  - **Items (`items/`)**
    - Archivos núcleo: `items_editor_controller.py`, `items_editor_models.py`, `items_editor_view.py`, `items_editor_events.py`.
    - Paneles: `items_title_panel/`, `items_tool_bar_panel/`, `items_picker_panel/`, `items_properties_panel/`, `items_add_remove_panel/`, `items_instances_panel/`, `items_tutorial_panel/`.
    - Directorios de soporte: `services/`, `rendering/`. Guía: `README_ITEMS.md`.
  - **Spells (`spells/`)**
    - Archivos núcleo: `spells_editor_controller.py`, `spells_editor_models.py`, `spells_editor_view.py`, `spells_editor_events.py`.
    - Paneles: `spells_title_panel/`, `spells_tool_bar_panel/`, `spells_picker_panel/`, `spells_properties_panel/`, `spells_add_remove_panel/`, `spells_tutorial_panel/`.
    - Directorios de soporte: `services/`. Guía: `README_SPELLS.md`.
  - **Lighting (`lighting/`)**
    - Archivos núcleo: `lighting_controller.py`, `lighting_state.py`, `lighting_view.py`, `lighting_events.py`.
    - Paneles: `panels/`. Directorios de soporte: `services/`.
  - **Map (`map/`)**
    - Archivos núcleo: `map_editor_controller.py`, `map_editor_state.py`, `map_editor_view.py`, `map_editor_events.py`.
    - Paneles: `map_title_panel/`, `map_tool_bar_panel/`, `map_tutorial_panel/`.
    - Módulos relacionados: `commands/`, `events/`, `services/`, `view/`. Guía: `README_MAP.md`.
  - **Particles (`particles/`)**
    - Archivos núcleo: `particles_controller.py`, `particles_model.py`, `particles_view.py`, `particles_events.py`.
    - Paneles: `particles_title_panel/`, `particles_tool_bar_panel/`, `particles_picker_panel/`, `particles_properties_panel/`, `particles_add_remove_panel/`, `particles_spells_list_panel/`, `particles_tutorial_panel/`.
    - Directorios de soporte: `services/`.
  
  Todos siguen la estructura MVC con `controller/`, `model/`, `view/` y `events`/`*_events.py`, además de servicios auxiliares. Se integran con el bucle del juego cuando están visibles.

---

## Datos: JSON vs SQLite

### JSON (fuente editable y assets de diseño)

- `data/worlds/<world>/`
  - `buildings/buildings_instances.json` + colisiones por instancia y por spawn.
  - `spawners/spawners_instances.json`.
  - `particles/particles_instances.json`.
  - `zones/zones.json` y `zones/overlays/*.overlay.json`.
- `data/buildings/`
  - `buildings_templates.json` y colisiones por imagen/instancia/spawn.
- `data/spawners/`
  - `spawners_templates.json`, `spawners_waves.json`, guía `FSM_spawners.md`.
- `data/spells/`
  - `spells.json`, `hud_spells.json`, `schema.json`.
- `data/tiles/` — `tiles.json` y estado del editor (`editor_tiles_picker_position.json`).
- `data/vendors/`
  - `economy/groups/vendor_*.json`, `inventory_seed/groups/*_default.json`, inventarios por vendedor (`inventory_vendor_*.json`), `registry/vendors.json`.
- `data/cache/` — cachés de mapa (`map_*.pkl`) y “thumbs” para tiles (PNG + metadatos JSON).
- `data/_pytest_active/` — base temporal de tests (`roguelike.sqlite3`) y mundos de prueba.
- Guardados: ficheros `partida_*.json` (si están habilitados en la build local).

Uso: los editores y scripts trabajan sobre JSON legible, versionable y fácil de fusionar; el juego puede leer JSON directo o un staging a SQLite para consultas más ricas.

### SQLite (modelo relacional para runtime, integridad e informes)

ORM en `roguelike_engine.db.models` y motor `db.engine`. Tablas principales:

- `spells` — hechizos y metadatos (`extra_json` conserva el payload original).
- `entities` — catálogo con campos aplanados (stats, AI, patrol, jugador) + `extra_json`.
- `entities_assets_set` — assets por “sets” (lista por acción, con índice y tint/scale).
- `entities_assets_no_set` — assets por acción/dirección sin sets.
- `entities_payload_archive` — copia íntegra del JSON de entidades (histórico y seguridad).
- `items` — catálogo de ítems con columnas normalizadas (iconos, escalas, gameplay) + `extra_json`.
- `item_prices` — precios de ítems (buy/sell) enlazados a `items`.
- `spawners_instances` — instancias de spawners colocadas en mapas.
- `spawner_templates` — plantillas: tipo/forma, políticas/triggers (JSON) y `waves_id`.
- `spawner_waves` — secuencias de oleadas por `waves_id` e índice.
- `building_instances` — edificios colocados (imagen, `spawn_id`, `zone_id`).
- `building_collisions` — geometrías WKT por instancia.
- `import_log` — trazabilidad de imports con `content_hash` e idempotencia.

Relación JSON→SQLite: los scripts de `scripts/migrate_json_to_sqlite/` importan y sincronizan (idempotente) catálogos e instancias. `ImportLog` asegura que no se re‑ingesten archivos sin cambios y soporta auditoría.

---

## Migraciones y scripts

- **Alembic (`alembic/versions/`)**
  - `b6bc709b38e0_initial_schema.py` — esquema base (entities, spells, spawners, building_instances/collisions, spawn_table_entries, import_log).
  - Evolución de entidades (flatten), payload archive y tablas de assets (p. ej. `f2a3b4c5d6e7_entities_assets_tables_and_backfill.py`).
  - Ítems y precios (`1a2b3c4d5e6f_add_items_and_item_prices.py`).
  - Limpiezas/renombres: drops de columnas extra JSON antiguas, merges de heads, etc.
- **Scripts de importación (`scripts/migrate_json_to_sqlite/`)**
  - `import_buildings.py`, `import_entities.py`, `import_items.py`, `import_spawners.py`, `import_spawner_templates_waves.py`.
  - Soporte a migraciones de esquema (`migrate_items_expand_schema.py`) y utilidades (`update_item_icon_paths.py`).

---

## Pruebas relevantes

- `tests/test_db_engine_models.py` — PRAGMAs de SQLite, `session_scope` (commit/rollback) y CRUD multi‑tabla.
- `tests/test_preflight_persistence.py` — saneamiento de persistencia/arranque.
- `tests/spawner_visuals_sanity.py` — coherencia entre `spawners_instances.json` y `buildings_instances.json` para “visuals”.
- Varios tests de combate, spells, render y vendors; y del Tiles Editor (toolbar, colisiones, toggles).

---

## Ejecución y empaquetado

- Desarrollo: `python launcher.py` (recomendado) o `python src/roguelike_game/main.py`.
- Instalación editable: `pip install -e .` + `pip install -r requirements.txt`.
- Hot‑reload de spells (F4) y benchmarking integrado.
- PyInstaller: spec con inclusión de `assets/` y `data/` (ver ejemplo en README raíz).

---

## Glosario rápido (términos clave)

- **Main loop (game loop)** — Ciclo por frame: procesa eventos, actualiza estado y dibuja. Úsalo para estructurar tiempo. Ej.: `while running: handle(); update(dt); render()`.
- **Delta time (dt)** — Tiempo entre frames. Normaliza movimiento/animaciones. Ej.: `pos += vel * dt`.
- **ECS (Entity–Component–System)** — Datos en componentes, lógica en sistemas. Escala con muchas entidades. Ej.: `RenderSystem` dibuja entidades con `Sprite`.
- **Z‑Layer (depth ordering)** — Orden de actualización/render por capas con persistencia. Ej.: UI > jugador > suelo.
- **Chunked rendering** — Dibujo por “trozos” del mapa visibles para rendimiento. Ej.: cargar/actualizar sólo chunks en viewport.
- **Spatial index** — Estructura para consultas por posición/área. Ej.: vecinos para colisiones.
- **Idempotency** — Repetir una importación no cambia el resultado. `ImportLog` lo garantiza con `content_hash`.
- **PRAGMA (SQLite)** — Ajustes de motor (journal_mode, synchronous). Se validan en tests.
- **Backfill** — Relleno histórico de tablas a partir de JSON existente. Usado al crear `entities_assets_*`.
- **WKT (Well‑Known Text)** — Formato textual para geometrías en `building_collisions.shape_wkt`.

---

## Cómo defender esta arquitectura

- **Objetivo y criterios**
  - Entregar un roguelike modular con editores in‑game, rendimiento aceptable en mapas grandes, y datos editables/seguros.
- **Justificación de diseño**
  - Separación Engine/Juego: desacopla rendering/infra de gameplay y acelera iteración.
  - ECS modular: facilita añadir sistemas/efectos sin tocar código central.
  - Editores con MVC: UI mantenible, pruebas por panel/herramienta y flujo batch seguro.
  - JSON como fuente editable + SQLite como staging relacional: versionable y con integridad/consultas.
- **Rendimiento/memoria**
  - Mapas por chunks, caché disco/memoria, Z‑layers con orden estable, y profiling continuo (benchmark/logger).
- **Extensibilidad y puntos de variación**
  - Dominios nuevos replican patrón MVC; sistemas ECS se registran en `system_registry`; nuevas tablas vía Alembic.
  - Spawners y entidades amplían assets y políticas sin romper el runtime (payload archive/extra_json).
- **Riesgos y mitigaciones**
  - Desfase JSON↔DB: `ImportLog` e importadores idempotentes. Tests de sanidad.
  - Coste de IO/Assets: “thumbs” y cachés; loader centralizado.
  - Complejidad UI: tests de editores y z‑order documentado.

---

## Pistas para contribuir

- Nuevo sistema ECS: crear en `ecs/systems/<dominio>/`, registrar en `ecs/core/system_registry.py`, documentar dependencias de componentes.
- Nueva tabla/columna: crear migración Alembic y actualizar modelos en `roguelike_engine.db.models`.
- Nuevo editor/panel: seguir patrón MVC de `roguelike_editors`, añadir tests si el panel afecta estado crítico.
- Nuevos datos: preferir JSON bajo `data/` y añadir importador si el runtime necesita consultas.

