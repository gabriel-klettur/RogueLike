# roguelike_engine

Motor base y utilidades de bajo nivel para el juego. Provee módulos de mapa, tiles, edificios, cámara, minimapa, capas Z, caché, consola y diagnósticos; todo desacoplado del bucle del juego. Está pensado para ser consumido por `src/roguelike_game/`.

## Objetivos
- __Separación de responsabilidades__: rendering y modelos de datos reutilizables (MVC por módulo) sin lógica de gameplay.
- __Rendimiento__: vistas por chunks, caché en memoria/archivo, helpers de carga.
- __Extensibilidad__: estructura estable por carpetas, configuración centralizada y utilidades comunes.

## Arquitectura (visión general)
- __MVC por dominio__: cada dominio tiene `controller/`, `model/`, `view/` (por ejemplo `map/`, `tile/`, `buildings/`).
- __Servicios de soporte__: `cache/`, `utils/`, `input/`, `diagnostics/`, `console/`.
- __Capas Z__: composición de escenas mediante `z_layer/` para ordenar dibujo/estado con persistencia.
- __Configuración__: valores y constantes en `config/` (minimapa, tiles, mapa, capas Z, editor, etc.).

## Estructura de carpetas
- `camera/camera.py`: cámara 2D (posición, zoom, transformaciones básicas del viewport).
- `map/`
  - `controller/map_controller.py`, `model/map_model.py`, `view/map_view.py`, `view/chunked_map_view.py` (render por chunks), `utils.py`.
- `tile/`
  - `controller/tile_controller.py`, `model/tile_model.py`, `view/tile_view.py`, `utils/assets.py`, `utils/loader.py`.
- `buildings/`
  - `building_model.py`, `building_view.py`, `building_controller.py`, `building.py` (entidad/DTO de building).
- `minimap/minimap.py`: minimapa simple configurable.
- `z_layer/`
  - `state.py`, `logic.py`, `render.py`, `persistence.py` (pipeline de capas con guardado/carga).
- `world/`
  - `world.py`, `persistence.py`, `world_config.py` (mundo, slots y persistencia asociada).
- `cache/`
  - `memory_cache.py`, `file_cache.py`, `cache_manager.py`, `icache.py` (interfaces y backends de caché).
- `console/`
  - `commands.py`, `model/model.py`, `view/view.py`, `controller/controller.py`, `events/events.py`.
- `diagnostics/`
  - `debug.py`, `helpers.py`, `overlay/{model,view,controller,events}.py` (overlay de depuración).
- `input/`
  - `keyboard.py`, `mouse.py`, `events.py` (normalización de entrada cruda).
- `config/`
  - `config.py`, `config_tiles.py`, `map_config.py`, `config_minimap.py`, `config_z_layer.py`, `config_editor.py`.
- `utils/`
  - `loader.py`, `loading_screen.py`, `mouse.py`, `benchmark.py`.
- `log_config.py`: configuración de logging del motor.

## Funcionalidades clave
- __Mapa y Tiles__
  - Estructuras de `map_model` + `tile_model` y vistas con __render por chunks__ (`chunked_map_view.py`).
  - Carga/gestión de assets de tiles (`tile/utils/assets.py`, `tile/utils/loader.py`).
- __Edificios__
  - Modelado y render aislado de edificios (hitboxes, outline y datos) en `buildings/`.
- __Cámara y minimapa__
  - Cámara 2D básica y un minimapa configurable (`minimap/minimap.py`).
- __Capas Z__
  - Orden de dibujo/actualización con estado persistible (`z_layer/state.py`, `z_layer/persistence.py`).
- __Caché__
  - Caché de disco/memoria para reducir IO y recomputaciones (`cache/*`).
- __Consola y diagnósticos__
  - Consola in‑game con MVC dedicado y overlay de debug (`console/*`, `diagnostics/overlay/*`).

## Configuración y datos
- Ajustes del motor en `config/*.py`.
- Archivos externos (por ejemplo colisiones, layouts) se consumen desde `data/` cuando aplica (la lectura concreta la realiza el juego o los loaders utilitarios del engine).

## Integración típica (desde el juego)
- El juego (`src/roguelike_game/`) orquesta el bucle, pero reutiliza:
  - Vistas/Modelos de mapa/tiles/edificios.
  - Cámara y minimapa.
  - Persistencia de capas Z y mundo.
  - Utilidades de carga y caché.

## Extensión
- __Nuevo dominio__: replicar patrón `controller/`, `model/`, `view/` y exponer utilidades en `__init__.py`.
- __Nuevas capas__: añadir lógica/render en `z_layer/` y persistencia si es necesario.
- __Nuevas herramientas__: usar `console/` y `diagnostics/overlay/` como plantillas de MVC ligero.

## Dependencias
- Basado en `pygame` y utilidades de `requirements.txt` (por ejemplo `jsonschema`, `pydantic` para validaciones si se utiliza en consumidores).

## Licencia
- Ver `LICENSE` en la raíz del repositorio.
