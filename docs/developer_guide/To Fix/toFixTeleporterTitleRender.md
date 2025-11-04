# Bug: Teleport + Tile Editor Render (fallback a mundo base)

Este documento lista posibilidades (≥20) que pueden explicar por qué, al teletransportarse a un mundo vacío y pintar un tile con el Tile Editor, se renderizan tiles del mundo base. Para cada causa se incluye una breve descripción y cómo diagnosticarla.

- **[OverlayStore apunta al mundo equivocado]**
  El `JsonOverlayStore` mantiene `zones_dir` capturado del mundo anterior. Si no se reinicializa tras `world_service.activate()`, carga/guarda overlays del mundo base.
  Diagnóstico: loggear `JsonOverlayStore.zones_dir` antes/después de `swap_world_and_spawn` y al guardar/cargar overlays.

- **[Normalización inconsistente de zona sentinela]**
  Se usan variantes `'no zone'`, `'no-zone'`, `'no_zone'` mezcladas, generando archivos duplicados y lecturas vacías.
  Diagnóstico: inspeccionar `worlds/<id>/zones/overlays/` y unificar nombres; trazar `overlay_manager.load_layers('no zone|no-zone|no_zone')`.

- **[Política de render con fallback activo]**
  La vista (`ChunkedMapView`) permite fallback a `Layer.Ground` cuando `use_zones_json` está activo y no detecta correctamente “mundo en blanco”.
  Diagnóstico: forzar logs y verificar que cuando `zones.json` está vacío (o mundo ≠ `base`), `overlay_no_fallback=True`.

- **[Variable mal referenciada en paths parciales]**
  `update_cells_all_zooms` podría usar un flag distinto al de `_build_chunk_surfaces`/`update_chunks`, reintroduciendo fallback solo en algunos zooms.
  Diagnóstico: asegurar que todos usan el mismo flag (`overlay_no_fallback`) y cover con pruebas de zoom.

- **[Loader no fusiona overlay global]**
  En mundos en blanco, el editor guarda overlay global; si el loader solo fusiona por zonas, parece “vacío” y la vista hace fallback al base.
  Diagnóstico: verificar que `text_loader_strategy` carga y fusiona `'no zone'` a (0,0) a tamaño de mapa completo.

- **[Guardado recortado a tamaño de zona]**
  El editor podría guardar `'no zone'` recortando al tamaño de una zona (50x50) en lugar de todo el mapa, dejando áreas sin overlay que caen en fallback visual.
  Diagnóstico: al guardar, validar dimensiones del grid serializado: deben ser `map.height x map.width`.

- **[Cálculo de offsets de zonas contaminado]**
  `zone_offsets` envuelto por `_OffsetsDict` puede inyectar claves base al acceder, simulando que hay zonas definidas.
  Diagnóstico: decidir “mundo en blanco” leyendo el archivo `ZONES_INDEX` directamente (no usar `zone_offsets`).

- **[Reutilización de caché de mapa cruzando mundos]**
  Cache `map_<world>_<name>.pkl` no invalidada al editar; tras salvar overlay, el loader podría devolver el cache del mundo anterior.
  Diagnóstico: invalidar cache cuando `overlays_dir/*.overlay.json` o `zones.json` cambian (mtime), y borrar cache en `swap_world_and_spawn`.

- **[Caché de sprites escalados no limpiada]**
  `_SCALED_CACHE` persiste entre mundos/zooms y puede mostrar artefactos del mundo base.
  Diagnóstico: limpiar `_SCALED_CACHE` en `invalidate_cache()` y al hacer swap de mundo.

- **[Renderer alternativo con fallback]**
  Un camino de renderizado fuera de `ChunkedMapView` (p. ej., capa del editor, colisiones) podría dibujar usando fallback.
  Diagnóstico: revisar `map_renderer.py`/`pipeline_runner.py` y confirmar que toda composición de tiles pasa por la vista chunked.

- **[Sprite loader con fallback agresivo]**
  `get_sprite_for_tile` cae a `DEFAULT_TILE_MAP` si no hay overlay, dibujando suelos/muros base sobre un mundo en blanco.
  Diagnóstico: cuando `overlay_no_fallback=True`, asegurar que tiles sin overlay no dibujan nada.

- **[Generador de mapa inyecta base]**
  `MapService.build_map` puede generar `lobby`/`dungeon` si cree que hay zonas; en mundo vacío debe construir un grid vacío.
  Diagnóstico: en el early-path “mundo en blanco” construir `rows` totalmente vacías y no colocar zonas base.

- **[Reinicio parcial del render tras teleport]**
  Solo se invalida parte del estado (p. ej., cámara o visibilidad de capas), dejando chunks de zoom antiguo con fallback.
  Diagnóstico: reinstanciar `MapRenderer` y limpiar caches tras `swap_world_and_spawn`.

- **[Hilos de guardado vs recarga inmediata]**
  El hilo asíncrono guarda overlay mientras el loader recarga; una carrera puede cargar un archivo a medio escribir o anterior.
  Diagnóstico: tras finalizar pincel, esperar/sincronizar antes de recargar, o versionar el overlay y confirmar mtime.

- **[Directorio de overlays inexistente]**
  Si `overlays_dir` no existe en el mundo destino, `load_layers` devuelve vacío y la vista interpreta “no hay overlays” → fallback.
  Diagnóstico: scaffolding del mundo debe crear `zones/overlays/` y `json_store` debe hacer `os.makedirs`.

- **[MapManager.tiles_by_layer/layers filtrados]**
  Un filtro temporal de capas (p. ej., por UI del editor) podría ocultar overlays y provocar fallback aparente.
  Diagnóstico: en `map_renderer.render_map`, revisar que el filtrado respete visibilidad y se restaure al salir.

- **[Fusión de overlays por zona con offsets incorrectos]**
  `text_loader_strategy` aplica `zone_layers` con offsets; si `off_x/off_y` erróneos, overlay se aplica fuera de límites y parece “no aplicado”.
  Diagnóstico: log de offsets y bounds-check riguroso al fusionar.

- **[Discrepancia entre nombres de zonas y offsets]**
  Si `zones.json` usa nombres con mayúsculas/minúsculas, pero se resuelven con variantes distintas, la fusión puede fallar.
  Diagnóstico: normalizar nombres (case-insensitive) al mapear `Layer -> grid`.

- **[Invalidez de dimensiones de grillas]**
  Si `raw_layers` y `matrix` difieren en tamaño y no se adaptan, render por chunk se descuadra y aparecen tiles “restantes”.
  Diagnóstico: adaptar grillas a `width x height` y loggear ajustes.

- **[Sentinelas inyectadas por el envoltorio]**
  `_OffsetsDict.__missing__` retorna offsets para `lobby`/`dungeon` aunque no existan en JSON, induciendo a creer que hay zonas.
  Diagnóstico: cuando `use_zones_json=True`, no derivar zonas base si el JSON está vacío.

- **[Regeneración de overlay_map en caliente]**
  Ejecutar `generate_overlay_map()` en caliente puede cambiar mappings (`OVERLAY_CODE_MAP`) y producir códigos no reconocidos.
  Diagnóstico: llamar una vez al inicio o proteger con flag estable.

- **[Cámara/viewport fuera del área con overlay]**
  Pintar fuera del (0,0) y no mover la cámara puede dar la impresión de fallback cuando en realidad se dibuja fuera de vista.
  Diagnóstico: registrar `camera.offset` y tile pintada; centrar vista para validar.

- **[Cache de disco con reloj/mtime inexacto]**
  Comparaciones de mtime entre cache y overlays pueden fallar por reloj del sistema, dejando cache obsoleto.
  Diagnóstico: invalidación optimista siempre que ocurra guardado de overlay o `zones.json` vacío.

- **[Capa Ground mezclando overlay vacío y char base]**
  Si `Layer.Ground` se usa tanto para overlay como para fallback char, su fusión puede sobrescribir vacíos con base.
  Diagnóstico: política clara: si `overlay_no_fallback`, código vacío no dibuja nada.

- **[Editor no agrega zona a set de pendientes]**
  Si al pintar en mundo vacío no se añade `'no zone'` a `tile_zones`, no se persiste; al recargar parece base.
  Diagnóstico: log de `self._pending_tile_zones` y de `get_zone_for` en celdas editadas.

- **[Persistencia de colisiones interfiere con render]**
  Capas de colisión pueden forzar modos de render que oculten overlays.
  Diagnóstico: toggles de colisión y trazas en `map_renderer` para confirmar rutas.

- **[Activos por defecto demasiado “expresivos”]**
  Los sprites base (p. ej., muros/suelos) hacen muy visible el fallback; en modo overlay-only conviene no dibujarlos.
  Diagnóstico: en overlay-only, suprimir cualquier dibujado sin overlay.

## Hallazgos y cambios ya implementados (sesión actual)
- **[JsonOverlayStore dinámico por mundo]**
  `overlays_dir` se resuelve en cada `load/save` según el mundo activo (evita capturar rutas del mundo base antes del teleport). Además, se crean directorios según sea necesario.

- **[Guardado sentinela: replace, not merge]**
  En mundos en blanco y para la zona sentinela, el editor no mezcla con contenido anterior: crea una grilla vacía tamaño mapa y escribe solo celdas cambiadas.

- **[Renderer: overlay-only sin fallback ni códigos inválidos]**
  En `overlay_no_fallback=True`, el render ignora códigos que no estén en `OVERLAY_CODE_MAP` y no cae al sprite base.

- **[Diagnóstico profundo añadido]**
  - `JsonOverlayStore` (load/save): loguea `world, zone, file, counts por capa`.
  - `OverlayManager.load_layers`: log INFO con `counts` por capa cargada.
  - `TextMapLoader`: `initial counts`, `zone counts`, `sentinel 'no_zone' counts`, `final counts`.
  - `ChunkedMapView`: `ground_counts empty / nonempty / valid / invalid` cuando cambia `overlay_no_fallback`.
  - `TileEditor.flush_brush`: zonas, nº de celdas, capa, `unique_codes` y `zones_empty` por zona.

- **[Cache de render]**
  Limpieza de caché de sprites escalados al invalidar la vista; invalidación forzada tras `flush_brush` para evitar artefactos.

## 10 nuevas posibilidades (no exploradas aún)
- **[Inicialización de filas con multiplicación]**
  Alguna grilla podría haberse creado con `[[""] * W] * H`, compartiendo filas y expandiendo cambios verticalmente.
  Diagnóstico: verificar identidad (`id(row)`) de filas tras cada construcción de grilla.

- **[Código de overlay a partir de char base]**
  El editor podría mapear `'.'`/`'#'` a un “código” no válido (p. ej., el propio char) cuando el asset no corresponde, inflando `invalid`.
  Diagnóstico: revisar `unique_codes` en `flush_brush` y validar contra `OVERLAY_CODE_MAP` antes de guardar.

- **[cells inflado por evento repetido]**
  `apply_brush` podría dispararse múltiples veces por frame o por “hover+click”, agregando cientos de celdas.
  Diagnóstico: comparar `cells` y `unique_cells` registrados y frecuencia de `flush`.

- **[map.layers compartido entre mundos]**
  Referencias compartidas por copia superficial al hacer swap (p. ej., `existing = previous.layers`), propagando contenido del base.
  Diagnóstico: comparar `id(map.layers[Layer.Ground])` antes y después del teleport.

- **[Carga de overlay de base por colisión de nombre de zona]**
  Un alias de zona (p. ej., `'Forest'` vs `'forest'`) podría resolver al archivo del mundo base por un normalizador de rutas.
  Diagnóstico: log del `Path` final en `JsonOverlayStore` y comprobar existencia real de archivos por mundo.

- **[Rehidratado desde cache con capas completas]**
  `save_cache()` podría persistir `layers` completos y ser priorizado al recargar, sobrescribiendo overlays “ligeros”.
  Diagnóstico: validar precedencia entre cache y overlays (y mtime) y desactivar cache para pruebas A/B.

- **[Doble fusión del sentinela]**
  Algún pipeline alternativo podría fusionar `'no zone'` y `'no_zone'` secuencialmente en otro punto del render.
  Diagnóstico: grep de llamadas a `load_layers('no zone'|'no-zone'|'no_zone')` fuera de `TextMapLoader`.

- **[Superficie de fondo persistente]**
  Una Surface base no limpiada podría “mantener” el último frame del mundo base bajo los overlays.
  Diagnóstico: asegurar `fill((0,0,0,255))` previo a cada blit y validar alpha/blend.

- **[Auto-tiling lateral]**
  Algún “auto-tiler” para transición de suelos podría propagar el código pintado a vecinos (p. ej., en un sistema de decoración).
  Diagnóstico: desactivar decoradores/auto-tilers y observar `unique_codes` sin vecinos.

- **[Cambio de mundo tardío en hilos]**
  Un hilo (auto-import, audio, spawners) podría reestablecer `current_world=base` brevemente durante el guardado.
  Diagnóstico: assert invariante de `current_world` al entrar/salir de `save_layers` y capturar “flips” de valor.

## Acciones recomendadas (resumen)
- Reinstanciar `JsonOverlayStore` en `world_service.activate()` y normalizar `'no zone'`.
- En `ChunkedMapView`, unificar `overlay_no_fallback` en todos los métodos y zooms.
- `text_loader_strategy`: fusionar `'no zone'` a tamaño de mapa completo.
- `tile_editor_controller`: guardar sentinelas como overlay de mapa completo (0,0)-(W,H).
- Invalidar caches (mapa/vista/sprites) al teleportar y al guardar overlay.

## Checklist de ejecución (estado actual)
- [x] OverlayStore apunta al mundo correcto (worlds/service.py, json_store.py)
- [x] Normalización sentinela ('no zone'/'no-zone'/'no_zone') (json_store.py)
- [x] Política overlay_no_fallback unificada (roguelike_engine/map/view/chunked_map_view.py)
- [x] Variable mal referenciada corregida en update_cells_all_zooms (chunked_map_view.py)
- [x] Loader fusiona overlay global 'no zone' a tamaño completo (text_loader_strategy.py)
- [x] Guardado sentinela a tamaño mapa completo (tile_editor_controller.py)
- [x] Detección de mundo en blanco usando ZONES_INDEX (map_renderer.py)
- [x] Invalidación de cache de mapa por overlays/zones.json (roguelike_game/managers/map/loader.py)
- [x] Limpieza de caché de sprites escalados al invalidar vista (chunked_map_view.py)
- [x] Sin rutas de render alternas con fallback fuera de ChunkedMapView (pipeline_runner.py, map_renderer.py)
- [x] Política en get_sprite_for_tile cubierta por la vista (tile/utils/assets.py + overlay_no_fallback)
- [x] Generador no inyecta contenido base en mundos vacíos (map_service.py)
- [x] Reinit de renderer y caches al teleport (MapManager.swap_world_and_spawn)
- [x] Carrera de guardado eliminada (flush_brush guarda overlays síncronamente)
- [x] overlays_dir asegurado por mundo (worlds/service.scaffold_world_if_missing, json_store.py)
- [x] Filtrado de capas restaura estado tras render (map_renderer.py)
- [x] Merge por zona respeta offsets y límites (text_loader_strategy.py)
- [x] Case-insensitive para nombres de zona (map_config.MapSettings._OffsetsDict)
- [x] Adaptación de tamaños de grillas al fusionar (text_loader_strategy.py)
- [x] Sin auto-inyección lobby/dungeon con zones.json vacío (map_config.MapSettings.zone_offsets)
- [x] overlay_map generado una sola vez por sesión (text_loader_strategy.py)
- [x] Validación de cámara/viewport documentada (sin cambio de código)
- [x] Invalidación de cache por mtime de overlays y zones.json (map/loader.py)
- [x] Política Ground en overlay-only: códigos vacíos no dibujan (chunked_map_view.py)
- [x] Editor añade zona sentinela a pendientes en mundo vacío (tile_editor_controller.py + get_zone_for)
- [x] Capas de colisión no interfieren con tiles (map_renderer.py)
- [x] Activos por defecto invisibles bajo overlay_no_fallback (chunked_map_view.py)
