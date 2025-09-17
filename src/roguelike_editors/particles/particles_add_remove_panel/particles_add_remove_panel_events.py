"""
Particles Add/Remove panel events.
Responsible for both toolbar icon clicks and map interactions when the
Particles Editor is active: selection, add (RMB drag), move (RMB drag), remove.
"""

import pygame
from roguelike_ui.ui_blocker import is_blocked
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_editors.particles.services.instances_service import (
    append_instance as _particles_append_instance,
    remove_nearest_instance as _particles_remove_nearest,
    find_nearest_instance as _particles_find_nearest,
    update_instance_position as _particles_update_pos,
)
from roguelike_game.ecs.components.transform.position import Position as _EcsPosition
from roguelike_game.ecs.components.particles.particle_preset_component import (
    ParticlePresetComponent as _EcsParticlePresetComp,
)


class ParticlesAddRemovePanelEventHandler:
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

    def handle_event(self, event):
        # Handle toolbar icon clicks ONLY when the panel is visible
        if getattr(self.model, 'visible', False):
            if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
                pos = event.pos
                view = getattr(self.controller, 'particles_add_remove_view', None)
                widget = getattr(view, 'widget', None)
                icon_rects = getattr(widget, 'icon_rects', {}) if widget else {}

                # Add to system
                rect_sys = icon_rects.get('particles_add_system')
                if rect_sys and rect_sys.collidepoint(pos):
                    self.model.active_tool = (
                        None if self.model.active_tool == 'particles_add_system' else 'particles_add_system'
                    )
                    # Future hook: open creation dialog or capture current selection to create system entry
                    try:
                        self.controller.model.delete_mode_active = False
                        # Also reflect on picker's model if available
                        try:
                            picker = getattr(self.controller, 'particles_picker_controller', None)
                            if picker is not None:
                                picker.model.delete_mode_active = False
                        except Exception:
                            pass
                    except Exception:
                        pass
                    return True

                # Note: 'particles_add' button removed; placement now begins with RMB on picker

                # Remove from map and picker
                rect_del = icon_rects.get('particles_remove')
                if rect_del and rect_del.collidepoint(pos):
                    if self.model.active_tool == 'particles_remove':
                        self.model.active_tool = None
                        try:
                            self.controller.model.delete_mode_active = False
                        except Exception:
                            pass
                    else:
                        self.model.active_tool = 'particles_remove'
                        try:
                            self.controller.model.delete_mode_active = True
                            # Reflect on picker's model so it handles deletions
                            try:
                                picker = getattr(self.controller, 'particles_picker_controller', None)
                                if picker is not None:
                                    picker.model.delete_mode_active = True
                                    picker.model.add_mode_active = False
                            except Exception:
                                pass
                        except Exception:
                            pass
                    return True
        # If no toolbar icon handled (or panel hidden), continue with map interactions below
        
        # --- Map interactions below (selection/add/move/remove) ---
        # Only handle when the particles list tool is active in the toolbar
        # so we don't interfere with other editor tools.
        
        # Note: The code above returns early on toolbar icon clicks.
        
        # Helper: screen->world using camera
        def _to_world(sx: int, sy: int) -> tuple[float, float]:
            game = getattr(self.controller, 'game', None)
            cam = getattr(game, 'camera', None) if game else None
            zoom = float(getattr(cam, 'zoom', 1.0) or 1.0)
            ox = float(getattr(cam, 'offset_x', 0.0) or 0.0)
            oy = float(getattr(cam, 'offset_y', 0.0) or 0.0)
            return (
                float(sx) / (zoom if zoom != 0 else 1.0) + ox,
                float(sy) / (zoom if zoom != 0 else 1.0) + oy,
            )

        # Resolve state & collaborators
        game = getattr(self.controller, 'game', None)
        world = getattr(getattr(game, 'ecs', None), 'ecs_world', None) if game else None
        picker = getattr(self.controller, 'particles_picker_controller', None)
        model = getattr(self.controller, 'model', None)
        if model is None:
            return False
        add_active = getattr(self.model, 'active_tool', None) == 'particles_add'
        remove_active = getattr(self.model, 'active_tool', None) == 'particles_remove'
        selected_pid = None
        try:
            selected_pid = getattr(getattr(picker, 'model', None), 'selected_id', None)
        except Exception:
            selected_pid = None

        # --- Right-click drag: place new instance ---
        if add_active and isinstance(selected_pid, str) and event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 3:
            mx, my = getattr(event, 'pos', (None, None))
            if mx is not None and not is_blocked(mx, my):
                # Start drag placement; overlay will render preview at cursor
                model.drag_place_active = True
                model.drag_pid = str(selected_pid)
                model.drag_entity_eid = None
                return True

        if getattr(model, 'drag_place_active', False) and event.type == pygame.MOUSEBUTTONUP and getattr(event, 'button', None) == 3:
            mx, my = getattr(event, 'pos', (None, None))
            if mx is not None and not is_blocked(mx, my) and isinstance(model.drag_pid, str):
                try:
                    wx, wy = _to_world(mx, my)
                except Exception:
                    wx, wy = None, None
                if wx is not None and wy is not None:
                    # Persist JSON entry
                    entry = _particles_append_instance(model.drag_pid, float(wx), float(wy))
                    # Spawn runtime ECS entity
                    try:
                        if world is not None and isinstance(entry, dict):
                            eid = world.create_entity()
                            world.components.setdefault('Position', {})[eid] = _EcsPosition(float(wx), float(wy))
                            world.components.setdefault('ParticlePresetComponent', {})[eid] = _EcsParticlePresetComp(str(entry.get('preset_id')), int(entry.get('id')))
                    except Exception:
                        pass
                    # Select new instance
                    try:
                        model.selected_instance_id = int(entry.get('id')) if isinstance(entry, dict) else None
                    except Exception:
                        model.selected_instance_id = None
                # Clear drag state and exit add mode blinking
                model.drag_place_active = False
                model.drag_pid = None
                model.drag_entity_eid = None
                # Exit add mode in panel & picker
                try:
                    self.model.active_tool = None
                    if picker is not None:
                        picker.model.add_mode_active = False
                except Exception:
                    pass
                return True

        # --- Selection with LMB when not in add/remove modes ---
        if not add_active and not remove_active and event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            mx, my = getattr(event, 'pos', (None, None))
            if mx is not None and not is_blocked(mx, my):
                try:
                    wx, wy = _to_world(mx, my)
                except Exception:
                    wx, wy = None, None
                if wx is not None and wy is not None:
                    entry = None
                    try:
                        entry = _particles_find_nearest(float(wx), float(wy), max_dist_px=48)
                    except Exception:
                        entry = None
                    if isinstance(entry, dict):
                        try:
                            model.selected_instance_id = int(entry.get('id'))
                        except Exception:
                            model.selected_instance_id = None
                        return True

        # --- Right-click drag to move selected instance ---
        if model.selected_instance_id is not None and event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 3:
            mx, my = getattr(event, 'pos', (None, None))
            if mx is not None and not is_blocked(mx, my):
                try:
                    wx, wy = _to_world(mx, my)
                except Exception:
                    wx, wy = None, None
                if wx is not None and wy is not None:
                    # Ensure the right-click is close enough to the selected instance
                    entry = None
                    try:
                        entry = _particles_find_nearest(float(wx), float(wy), max_dist_px=48)
                    except Exception:
                        entry = None
                    try:
                        if isinstance(entry, dict) and int(entry.get('id')) == int(model.selected_instance_id):
                            model.drag_move_active = True
                            # Try to find nearest runtime particle entity to move for immediate feedback
                            if world is not None:
                                pos_map = world.components.get('Position', {})
                                particles = world.components.get('ParticlePresetComponent', {})
                                best_e = None
                                best_d2 = None
                                for eid in list(particles.keys()):
                                    pos = pos_map.get(eid)
                                    if pos is None:
                                        continue
                                    dx = float(wx) - float(getattr(pos, 'x', 0.0))
                                    dy = float(wy) - float(getattr(pos, 'y', 0.0))
                                    d2 = dx*dx + dy*dy
                                    if best_d2 is None or d2 < best_d2:
                                        best_d2 = d2
                                        best_e = eid
                                if best_e is not None and (best_d2 is None or best_d2 <= 48.0*48.0):
                                    model.selected_entity_eid = best_e
                            return True
                    except Exception:
                        pass

        if getattr(model, 'drag_move_active', False) and event.type == pygame.MOUSEMOTION:
            mx, my = getattr(event, 'pos', (None, None))
            if mx is not None and not is_blocked(mx, my):
                try:
                    wx, wy = _to_world(mx, my)
                except Exception:
                    wx, wy = None, None
                if wx is not None and wy is not None and world is not None and model.selected_entity_eid is not None:
                    try:
                        pos_map = world.components.setdefault('Position', {})
                        if model.selected_entity_eid in pos_map:
                            pos_map[model.selected_entity_eid] = _EcsPosition(float(wx), float(wy))
                    except Exception:
                        pass
            return True

        if getattr(model, 'drag_move_active', False) and event.type == pygame.MOUSEBUTTONUP and getattr(event, 'button', None) == 3:
            mx, my = getattr(event, 'pos', (None, None))
            if mx is not None and not is_blocked(mx, my):
                try:
                    wx, wy = _to_world(mx, my)
                except Exception:
                    wx, wy = None, None
                if wx is not None and wy is not None:
                    try:
                        _particles_update_pos(int(model.selected_instance_id), float(wx), float(wy))
                    except Exception:
                        pass
                    # Also move nearest runtime particle to this final position if entity not captured
                    if world is not None and model.selected_entity_eid is None:
                        try:
                            pos_map = world.components.get('Position', {})
                            particles = world.components.get('ParticlePresetComponent', {})
                            best_e = None
                            best_d2 = None
                            for eid in list(particles.keys()):
                                pos = pos_map.get(eid)
                                if pos is None:
                                    continue
                                dx = float(wx) - float(getattr(pos, 'x', 0.0))
                                dy = float(wy) - float(getattr(pos, 'y', 0.0))
                                d2 = dx*dx + dy*dy
                                if best_d2 is None or d2 < best_d2:
                                    best_d2 = d2
                                    best_e = eid
                            if best_e is not None and (best_d2 is None or best_d2 <= 48.0*48.0):
                                world.components.setdefault('Position', {})[best_e] = _EcsPosition(float(wx), float(wy))
                        except Exception:
                            pass
            # Clear drag state
            model.drag_move_active = False
            model.selected_entity_eid = None
            return True

        # --- Remove mode: remove nearest on LMB ---
        if remove_active and event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            mx, my = getattr(event, 'pos', (None, None))
            if mx is not None and not is_blocked(mx, my):
                try:
                    wx, wy = _to_world(mx, my)
                except Exception:
                    wx, wy = None, None
                if wx is not None and wy is not None:
                    removed = None
                    try:
                        removed = _particles_remove_nearest(float(wx), float(wy), max_dist_px=48)
                    except Exception:
                        removed = None
                    # Remove closest runtime entity
                    if world is not None:
                        try:
                            pos_map = world.components.get('Position', {})
                            presets = world.components.get('ParticlePresetComponent', {})
                            target_e = None
                            if isinstance(removed, dict) and 'id' in removed:
                                # Prefer by entry id match
                                for eid, comp in list(presets.items()):
                                    try:
                                        if int(getattr(comp, 'entry_id', -1)) == int(removed.get('id')):
                                            target_e = eid
                                            break
                                    except Exception:
                                        continue
                            if target_e is None:
                                # Fallback: nearest by position
                                best_e = None
                                best_d2 = None
                                for eid in list(presets.keys()):
                                    pos = pos_map.get(eid)
                                    if pos is None:
                                        continue
                                    dx = float(wx) - float(getattr(pos, 'x', 0.0))
                                    dy = float(wy) - float(getattr(pos, 'y', 0.0))
                                    d2 = dx*dx + dy*dy
                                    if best_d2 is None or d2 < best_d2:
                                        best_d2 = d2
                                        best_e = eid
                                if best_e is not None and (best_d2 is None or best_d2 <= 48.0*48.0):
                                    target_e = best_e
                            if target_e is not None:
                                world.remove_entity(target_e)
                        except Exception:
                            pass
                    # Exit remove mode and clear picker flag
                    try:
                        self.model.active_tool = None
                        if picker is not None:
                            picker.model.delete_mode_active = False
                    except Exception:
                        pass
                    return True

        return False
