import os
import json
import logging
import pygame
try:
    # Used to trigger the same reload that F1 performs
    from roguelike_game.config.hot_reload import reload_all_game_data
except Exception:  # pragma: no cover
    reload_all_game_data = None

try:
    from roguelike_engine.config.config_tiles import TILE_SIZE
except Exception:
    TILE_SIZE = 32

try:
    from roguelike_engine.config.config import (
        BUILDINGS_TEMPLATES_PATH,
        BUILDINGS_INSTANCES_PATH,
        BUILDINGS_COLLISIONS_DATA_PATH,
        BUILDINGS_COLLISIONS_BY_IMAGE_PATH,
        BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH,
        BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH,
    )
except Exception:
    BUILDINGS_COLLISIONS_DATA_PATH = "data/buildings/buildings_collisions_data.json"
    BUILDINGS_COLLISIONS_BY_IMAGE_PATH = "data/buildings/buildings_collisions_by_image.json"
    BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH = "data/buildings/buildings_collisions_by_spawn_id.json"
    BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH = "data/buildings/buildings_collisions_by_building_instance_id.json"

from roguelike_editors.buildings.utils.save_buildings_to_json import save_buildings_split
from roguelike_editors.buildings.utils.collisions_apply import apply_collisions_to_loaded_buildings

logger = logging.getLogger(__name__)


class BuildingCollidersPanelEventHandler:
    def __init__(self, state, editor_state, model):
        self.state = state
        self.editor_state = editor_state
        self.model = model

    def _paint_at_mouse(self, camera, buildings):
        if not self.model.choice:
            return True
        mx, my = pygame.mouse.get_pos()
        world_x = mx / camera.zoom + camera.offset_x
        world_y = my / camera.zoom + camera.offset_y
        for b in reversed(buildings):
            x_b, y_b = b.x, b.y
            w_img, h_img = b.image.get_size()
            rect = pygame.Rect(x_b, y_b, w_img, h_img)
            if rect.collidepoint(world_x, world_y):
                # Asegurar que el edificio bajo cursor sea el activo para pintar
                try:
                    active = getattr(self.editor_state, 'active_building', None)
                except Exception:
                    active = None
                if active is None or active is not b:
                    try:
                        self.editor_state.active_building = b
                        logger.info("[Colliders] Seleccionado edificio ID %s para pintar", getattr(b, 'id', None))
                    except Exception:
                        pass
                self.model.active_building = b
                # Asegurar grilla por defecto 15x15 si no existe o es 1x1/invalid
                try:
                    cmap = getattr(b, 'collision_map', None)
                    need_init = False
                    if not cmap or not isinstance(cmap, list) or not cmap or not isinstance(cmap[0], list):
                        need_init = True
                    else:
                        r0 = len(cmap)
                        c0 = len(cmap[0]) if r0 > 0 else 0
                        if r0 <= 1 or c0 <= 1:
                            need_init = True
                    if need_init:
                        b.collision_map = [["." for _ in range(15)] for _ in range(15)]
                except Exception:
                    try:
                        b.collision_map = [["." for _ in range(15)] for _ in range(15)]
                    except Exception:
                        pass
                # Calcular índice de celda proporcional a imagen y grilla actual
                try:
                    rows = len(b.collision_map)
                    cols = len(b.collision_map[0]) if rows > 0 else 0
                    if rows > 0 and cols > 0 and w_img > 0 and h_img > 0:
                        cw = max(1.0, w_img / float(cols))
                        ch = max(1.0, h_img / float(rows))
                        col = int((world_x - x_b) / cw)
                        row = int((world_y - y_b) / ch)
                    else:
                        col = int((world_x - x_b) // TILE_SIZE)
                        row = int((world_y - y_b) // TILE_SIZE)
                except Exception:
                    col = int((world_x - x_b) // TILE_SIZE)
                    row = int((world_y - y_b) // TILE_SIZE)
                if 0 <= row < len(b.collision_map) and 0 <= col < len(b.collision_map[0]):
                    # Pinta en el edificio activo
                    try:
                        prev = b.collision_map[row][col]
                    except Exception:
                        prev = None
                    b.collision_map[row][col] = self.model.choice
                    # Aggregate stroke stats instead of per-cell INFO logs
                    try:
                        if not getattr(self.editor_state, '_colliders_stroke_started', False):
                            self.editor_state._colliders_stroke_started = True
                            self.editor_state._colliders_stroke_cells = 0
                            self.editor_state._colliders_stroke_buildings = set()
                            self.editor_state._colliders_stroke_scope = getattr(self.editor_state, 'collider_scope', getattr(b, 'collider_scope', 'CG'))
                        self.editor_state._colliders_stroke_cells += 1
                        bid = getattr(b, 'id', None)
                        if bid is not None:
                            self.editor_state._colliders_stroke_buildings.add(bid)
                    except Exception:
                        pass
                    # Tutorial: marcar pulso de pintado
                    try:
                        setattr(self.editor_state, 'tutorial_colliders_painted_pulse', True)
                        setattr(self.editor_state, 'tutorial_colliders_painted_on_selected_pulse', True)
                    except Exception:
                        pass
                    # Invalida caches de colisión del edificio editado (en el modelo)
                    try:
                        if hasattr(b, 'model'):
                            b.model.invalidate_collision_caches()
                    except Exception:
                        pass
                    try:
                        setattr(self.editor_state, 'colliders_dirty', True)
                    except Exception:
                        pass
                    # Según alcance seleccionado en la UI (editor_state), propagar a todos los que comparten image_path
                    scope_b = getattr(self.editor_state, 'collider_scope', getattr(b, 'collider_scope', 'CG'))
                    if scope_b == 'CG':
                        rows_ref = len(b.collision_map)
                        cols_ref = len(b.collision_map[0]) if rows_ref > 0 else 0
                        for other in buildings:
                            if other is b:
                                continue
                            if getattr(other, 'image_path', None) != getattr(b, 'image_path', None):
                                continue
                            # No sobrescribir instancias marcadas como CU
                            if getattr(other, 'collider_scope', 'CG') == 'CU':
                                continue
                            # Mapear índice (row,col) proporcionalmente si tamaños difieren
                            try:
                                rows2 = len(other.collision_map)
                                cols2 = len(other.collision_map[0]) if rows2 > 0 else 0
                                if rows2 <= 0 or cols2 <= 0:
                                    continue
                                r2 = int(row * rows2 / max(1, rows_ref))
                                c2 = int(col * cols2 / max(1, cols_ref))
                                if r2 >= rows2: r2 = rows2 - 1
                                if c2 >= cols2: c2 = cols2 - 1
                                other.collision_map[r2][c2] = self.model.choice
                                # Do not spam per-cell propagate logs; count affected buildings only
                                try:
                                    if not getattr(self.editor_state, '_colliders_stroke_started', False):
                                        self.editor_state._colliders_stroke_started = True
                                        self.editor_state._colliders_stroke_cells = 0
                                        self.editor_state._colliders_stroke_buildings = set()
                                        self.editor_state._colliders_stroke_scope = getattr(self.editor_state, 'collider_scope', getattr(b, 'collider_scope', 'CG'))
                                    obid = getattr(other, 'id', None)
                                    if obid is not None:
                                        self.editor_state._colliders_stroke_buildings.add(obid)
                                except Exception:
                                    pass
                                try:
                                    if hasattr(other, 'model'):
                                        other.model.invalidate_collision_caches()
                                except Exception:
                                    pass
                            except Exception:
                                # Si algún edificio no tiene mapa válido, lo omitimos
                                continue
                return True
        return False

    def _save_collisions(self, buildings, force: bool = False):
        # Persistir ahora en archivos divididos.
        active = getattr(self.model, 'active_building', None)
        eff_scope = getattr(self.editor_state, 'collider_scope', 'CG')
        # Single concise summary for the stroke
        try:
            cells = int(getattr(self.editor_state, '_colliders_stroke_cells', 0) or 0)
            bset = getattr(self.editor_state, '_colliders_stroke_buildings', set()) or set()
            bcount = len(bset)
            logger.info(f"[Colliders][SAVE] scope={eff_scope} active_id={getattr(active,'id',None)} cells={cells} buildings_affected={bcount} force={force}")
        except Exception:
            pass

        # Cargar existentes desde archivos split (si no existen, dict vacío)
        def _read_dict(path):
            try:
                if os.path.exists(path):
                    with open(path, 'r', encoding='utf-8') as f:
                        d = json.load(f) or {}
                        return d if isinstance(d, dict) else {}
            except Exception:
                return {}
            return {}

        by_image = _read_dict(BUILDINGS_COLLISIONS_BY_IMAGE_PATH)
        by_spawn = _read_dict(BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH)
        by_binst = _read_dict(BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH)

        updated_by_img = []
        updated_by_inst = []

        # Guardar CG globales SOLO por image_path (no por instancia)
        if force or eff_scope == 'CG':
            target_img = getattr(active, 'image_path', None)
            if target_img and getattr(active, 'collision_map', None) is not None:
                # Escribir a partir del edificio activo únicamente (fuente de verdad del CG)
                key = target_img
                try:
                    giw, gih = active.image.get_size()
                except Exception:
                    giw, gih = (0, 0)
                by_image[key] = {
                    'width': len(active.collision_map[0]) if active.collision_map else 0,
                    'height': len(active.collision_map),
                    'collision': active.collision_map,
                    'grid_ref_size': [int(giw), int(gih)],
                }
                updated_by_img.append(key)
            else:
                # Fallback: si no hay activo, mantener el comportamiento previo (merge por image_path)
                for b in buildings:
                    if getattr(b, 'collision_map', None) is None:
                        continue
                    if getattr(b, '_is_spawner_visual', False) or getattr(b, 'spawner_instance_id', None):
                        continue
                    if target_img and getattr(b, 'image_path', None) != target_img:
                        continue
                    key = getattr(b, 'image_path', '')
                    if not key:
                        continue
                    try:
                        giw, gih = b.image.get_size()
                    except Exception:
                        giw, gih = (0, 0)
                    by_image[key] = {
                        'width': len(b.collision_map[0]) if b.collision_map else 0,
                        'height': len(b.collision_map),
                        'collision': b.collision_map,
                        'grid_ref_size': [int(giw), int(gih)],
                    }
                    updated_by_img.append(key)

        # Guardar CU por instancia en by_building_instance_id
        if eff_scope == 'CU' and active is not None and getattr(active, 'collision_map', None) is not None:
            try:
                bid = getattr(active, 'id', None)
                if bid is not None:
                    bid_str = str(bid)
                    try:
                        giw, gih = active.image.get_size()
                    except Exception:
                        giw, gih = (0, 0)
                    by_binst[bid_str] = {
                        'width': len(active.collision_map[0]) if active.collision_map else 0,
                        'height': len(active.collision_map),
                        'collision': active.collision_map,
                        'grid_ref_size': [int(giw), int(gih)],
                    }
                    updated_by_inst.append(bid_str)
            except Exception:
                pass

        # Crear carpeta destino
        out_dir = os.path.dirname(BUILDINGS_COLLISIONS_BY_IMAGE_PATH) or os.path.dirname(BUILDINGS_COLLISIONS_DATA_PATH)
        os.makedirs(out_dir, exist_ok=True)

        # Escribir archivos split
        try:
            with open(BUILDINGS_COLLISIONS_BY_IMAGE_PATH, 'w', encoding='utf-8') as f:
                json.dump(by_image, f, indent=4)
            with open(BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH, 'w', encoding='utf-8') as f:
                json.dump(by_spawn, f, indent=4)
            with open(BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH, 'w', encoding='utf-8') as f:
                json.dump(by_binst, f, indent=4)
        except Exception as e:
            logger.error(f"[Colliders] Error escribiendo archivos de colisiones: {e}")
        # Aplicar cambios persistidos a los edificios cargados actualmente (sin F1)
        try:
            _applied = apply_collisions_to_loaded_buildings(
                buildings,
                by_image=by_image,
                by_binst=by_binst,
                updated_by_img=updated_by_img,
                updated_by_inst=updated_by_inst,
            )
            try:
                if _applied:
                    logger.info(f"[Colliders][APPLY] Updated in-memory buildings: {int(_applied)}")
            except Exception:
                pass
        except Exception:
            pass
        try:
            setattr(self.editor_state, 'colliders_dirty', True)
        except Exception:
            pass

        # Aplicar en runtime inmediatamente (como en Tiles Editor: flush_brush)
        # Si tenemos referencia al ECS, forzar rebuild del índice espacial usando
        # la misma lista de edificios que está editando el panel.
        try:
            w = getattr(self, 'ecs_world', None)
            if w is not None:
                try:
                    # Asegurar que ECSWorld usa la lista de edificios actual
                    w.buildings = buildings
                except Exception:
                    pass
                # Reconstruir índice espacial ya en este frame
                try:
                    logger.info("[Colliders][SAVE] Rebuilding SpatialIndex immediately via ecs_world in panel")
                except Exception:
                    pass
                # Emit INFO for this rebuild only
                try:
                    setattr(w, '_log_rebuild_info', True)
                except Exception:
                    pass
                w.rebuild_spatial_index()
                # Clear dirty flag and stamp time to avoid duplicate BE interval rebuild/log right after
                try:
                    self.editor_state.colliders_dirty = False
                    self.editor_state.last_colliders_rebuild_ms = pygame.time.get_ticks()
                except Exception:
                    pass
        except Exception:
            # Fallback: si no hay ECSWorld disponible, nada; update_manager cubrirá por dirty flag
            pass

        # Keep entities namespace in sync for systems that read from game.entities.buildings
        try:
            g = getattr(self, 'game', None)
            if g is not None and hasattr(g, 'entities'):
                setattr(g.entities, 'buildings', buildings)
        except Exception:
            pass
        # Optional: developer fallback to full hot-reload (F1-equivalent) when explicitly requested
        try:
            import os as _os
            if (_os.environ.get('RL_FORCE_RELOAD_ON_COLLIDER_SAVE') == '1') and callable(reload_all_game_data) and g is not None:
                reload_all_game_data(g, force=True)
        except Exception:
            pass

        # Reset stroke debug counters to avoid accumulation across strokes
        try:
            self.editor_state._colliders_stroke_started = False
            self.editor_state._colliders_stroke_cells = 0
            self.editor_state._colliders_stroke_buildings = set()
        except Exception:
            pass

        # Logs
        try:
            if updated_by_img:
                sample = ", ".join(updated_by_img[:5])
                more = "" if len(updated_by_img) <= 5 else f" (+{len(updated_by_img)-5} más)"
                logger.info(f"[Colliders][CG] Guardadas/mezcladas {len(updated_by_img)} entradas por image_path: {sample}{more}")
            if updated_by_inst:
                sample = ", ".join(updated_by_inst[:5])
                more = "" if len(updated_by_inst) <= 5 else f" (+{len(updated_by_inst)-5} más)"
                logger.info(f"[Colliders][CU] Guardadas/mezcladas {len(updated_by_inst)} entradas por building_instance_id: {sample}{more}")
        except Exception:
            pass

    def handle(self, event, camera, buildings) -> bool:
        if not self.model.active:
            return False
        if event.type == pygame.MOUSEBUTTONDOWN:
            mx, my = event.pos
            # Picker interactions
            if self.model.picker_open:
                x0, y0 = self.model.picker_pos or (0, 0)
                w, h = self.model.picker_panel_size
                if x0 <= mx <= x0 + w and y0 <= my <= y0 + h:
                    if event.button == 1:
                        # Botón 'Save CU' (persistir solo colisiones CU por instancia)
                        try:
                            save_rect = self.model.picker_rects.get('save_cu')
                            if save_rect and save_rect.collidepoint((mx, my)):
                                # Forzar alcance CU temporalmente y persistir solo en by_building_instance_id
                                prev_scope = getattr(self.editor_state, 'collider_scope', 'CG')
                                try:
                                    self.editor_state.collider_scope = 'CU'
                                    self._save_collisions(buildings)
                                finally:
                                    try:
                                        self.editor_state.collider_scope = prev_scope
                                    except Exception:
                                        pass
                                logger.info("[Colliders][CU] Guardado per-instance en buildings_collisions_by_building_instance_id.json")
                                # Tutorial: pulso de guardado por botón
                                try:
                                    setattr(self.editor_state, 'tutorial_colliders_saved_button_pulse', True)
                                except Exception:
                                    pass
                                return True
                        except Exception:
                            pass
                        for ch, rect in self.model.picker_rects.items():
                            if rect.collidepoint((mx, my)):
                                self.model.choice = ch
                                # Tutorial: selección de brocha
                                try:
                                    setattr(self.editor_state, 'tutorial_colliders_choice_pulse', True)
                                except Exception:
                                    pass
                                try:
                                    logger.info(f"[Colliders] Seleccionado tipo '{ch}' en el picker")
                                except Exception:
                                    pass
                                return True
                    elif event.button == 3:
                        self.model.picker_dragging = True
                        dx = mx - x0; dy = my - y0
                        self.model.picker_drag_offset = (dx, dy)
                        return True
            # Brush start
            if event.button == 1 and self.model.choice:
                self.model.brush_dragging = True
                self._paint_at_mouse(camera, buildings)
                return True
        elif event.type == pygame.MOUSEBUTTONUP:
            if event.button == 3 and self.model.picker_dragging:
                self.model.picker_dragging = False
                return True
            if event.button == 1 and self.model.brush_dragging:
                self.model.brush_dragging = False
                # persist
                self._save_collisions(buildings)
                return True
        elif event.type == pygame.MOUSEMOTION:
            mx, my = event.pos
            if self.model.picker_dragging:
                dx, dy = self.model.picker_drag_offset
                self.model.picker_pos = (mx - dx, my - dy)
                # Tutorial: movimiento del picker
                try:
                    setattr(self.editor_state, 'tutorial_colliders_picker_moved_pulse', True)
                except Exception:
                    pass
                return True
            if self.model.brush_dragging and self.model.choice:
                self._paint_at_mouse(camera, buildings)
                return True
        return False
