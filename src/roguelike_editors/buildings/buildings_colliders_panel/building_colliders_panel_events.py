import os
import json
import logging
import pygame

try:
    from roguelike_engine.config.config_tiles import TILE_SIZE
except Exception:
    TILE_SIZE = 32

try:
    from roguelike_engine.config.config import BUILDINGS_COLLISIONS_DATA_PATH, BUILDINGS_TEMPLATES_PATH, BUILDINGS_INSTANCES_PATH
except Exception:
    BUILDINGS_COLLISIONS_DATA_PATH = "data/buildings/buildings_collisions_data.json"

from roguelike_editors.buildings.utils.save_buildings_to_json import save_buildings_to_json, save_buildings_split

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
                self.model.active_building = b
                col = int((world_x - x_b) // TILE_SIZE)
                row = int((world_y - y_b) // TILE_SIZE)
                if 0 <= row < len(b.collision_map) and 0 <= col < len(b.collision_map[0]):
                    # Pinta en el edificio activo
                    b.collision_map[row][col] = self.model.choice
                    # Invalida caches
                    try:
                        b.model._collision_tiles_cache = None
                        b.model._collision_tile_objs = None
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
                                try:
                                    other.model._collision_tiles_cache = None
                                    other.model._collision_tile_objs = None
                                except Exception:
                                    pass
                            except Exception:
                                # Si algún edificio no tiene mapa válido, lo omitimos
                                continue
                return True
        return False

    def _save_collisions(self, buildings, force: bool = False):
        # Persiste colisiones en un esquema robusto y backward-compatible:
        # Nuevo esquema (preferido):
        # {
        #   "by_image_path": { image_path: {width,height,collision} },    # CG
        #   "by_building_instance_id": { "<id>": {width,height,collision} }  # CU
        #   "by_spawn_id": { spawn_id: {width,height,collision} }  # legacy/soportado si existe
        # }
        # Esquema legacy soportado en lectura:
        # {
        #   "global": { ... }, "instances": { ... }, "by_building_id": { ... }
        # }
        active = getattr(self.model, 'active_building', None)
        # Usar SIEMPRE la selección de la UI para decidir cómo persistir (CG/CU)
        eff_scope = getattr(self.editor_state, 'collider_scope', 'CG')

        # Cargar existente (aceptando nuevo y legacy)
        try:
            with open(BUILDINGS_COLLISIONS_DATA_PATH, 'r', encoding='utf-8') as cf:
                raw = json.load(cf) or {}
        except Exception:
            raw = {}
        data = {"by_image_path": {}, "by_spawn_id": {}, "by_building_instance_id": {}}
        if isinstance(raw, dict):
            # Preferir nuevas claves si existen
            if any(k in raw for k in ("by_image_path", "by_spawn_id", "by_building_instance_id")):
                data["by_image_path"] = raw.get("by_image_path", {}) or {}
                data["by_spawn_id"] = raw.get("by_spawn_id", {}) or {}
                data["by_building_instance_id"] = raw.get("by_building_instance_id", {}) or {}
            elif any(k in raw for k in ("global", "instances", "by_building_id")):
                data["by_image_path"] = raw.get("global", {}) or {}
                data["by_spawn_id"] = raw.get("instances", {}) or {}
                data["by_building_instance_id"] = raw.get("by_building_id", {}) or {}
            elif raw:
                # Plano legacy (todo por image_path)
                data["by_image_path"] = raw

        updated_by_img = []
        updated_by_inst = []

        # Guardar CG globales SOLO por image_path (no por instancia)
        if force or eff_scope == 'CG':
            target_img = getattr(active, 'image_path', None)
            for b in buildings:
                if getattr(b, 'collision_map', None) is None:
                    continue
                # No persistir CG global desde visuales de spawner; sus colisiones deben ir por instancia
                if getattr(b, '_is_spawner_visual', False) or getattr(b, 'spawner_instance_id', None):
                    continue
                # Restringir a la misma imagen que el activo para evitar guardar duplicados
                if target_img and getattr(b, 'image_path', None) != target_img:
                    continue
                key = getattr(b, 'image_path', '')
                if not key:
                    continue
                data['by_image_path'][key] = {
                    'width': len(b.collision_map[0]) if b.collision_map else 0,
                    'height': len(b.collision_map),
                    'collision': b.collision_map,
                }
                updated_by_img.append(key)

        # Guardar CU por instancia en by_building_instance_id
        if eff_scope == 'CU' and active is not None and getattr(active, 'collision_map', None) is not None:
            try:
                bid = getattr(active, 'id', None)
                if bid is not None:
                    bid_str = str(bid)
                    data['by_building_instance_id'][bid_str] = {
                        'width': len(active.collision_map[0]) if active.collision_map else 0,
                        'height': len(active.collision_map),
                        'collision': active.collision_map,
                    }
                    updated_by_inst.append(bid_str)
            except Exception:
                pass

        # Escribir archivo
        os.makedirs(os.path.dirname(BUILDINGS_COLLISIONS_DATA_PATH), exist_ok=True)
        with open(BUILDINGS_COLLISIONS_DATA_PATH, 'w', encoding='utf-8') as cf:
            json.dump(data, cf, indent=4)

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
                        # Botón 'Save CU' (guardar overrides por instancia en buildings_data.json)
                        try:
                            save_rect = self.model.picker_rects.get('save_cu')
                            if save_rect and save_rect.collidepoint((mx, my)):
                                if os.path.exists(BUILDINGS_TEMPLATES_PATH) and os.path.exists(BUILDINGS_INSTANCES_PATH):
                                    save_buildings_split(buildings)
                                    logger.info("[Colliders][CU] Overrides guardados (si existen) en split files")
                                else:
                                    save_buildings_to_json(buildings)
                                    logger.info("[Colliders][CU] Overrides guardados (si existen) en buildings_data.json")
                                return True
                        except Exception:
                            pass
                        for ch, rect in self.model.picker_rects.items():
                            if rect.collidepoint((mx, my)):
                                self.model.choice = ch
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
                return True
            if self.model.brush_dragging and self.model.choice:
                self._paint_at_mouse(camera, buildings)
                return True
        return False
