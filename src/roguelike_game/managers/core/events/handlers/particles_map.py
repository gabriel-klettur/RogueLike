import pygame
from roguelike_editors.particles.services.instances_service import (
    append_instance as _particles_append_instance,
    remove_nearest_instance as _particles_remove_nearest,
    find_nearest_instance as _particles_find_nearest,
    update_instance_position as _particles_update_pos,
)
from roguelike_game.ecs.components.transform.position import Position as _EcsPosition
from roguelike_game.ecs.components.particles.particle_preset_component import ParticlePresetComponent as _EcsParticlePresetComp


def process_particles_map_input(game, remaining_events, overlay):
    pass_events = []
    try:
        pe = getattr(game, 'particles_editor', None)
        particles_editor_visible = bool(getattr(getattr(pe, 'model', None), 'visible', False))
        add_active = False
        remove_active = False
        selected_pid = None
        if particles_editor_visible and getattr(pe, 'controller', None):
            try:
                ar_model = getattr(pe.controller, 'particles_add_remove_model', None)
                add_active = add_active or (ar_model is not None and getattr(ar_model, 'active_tool', None) == 'particles_add')
                remove_active = remove_active or (ar_model is not None and getattr(ar_model, 'active_tool', None) == 'particles_remove')
            except Exception:
                pass
            try:
                picker = getattr(pe.controller, 'particles_picker_controller', None)
                if picker is not None:
                    selected_pid = getattr(picker.model, 'selected_id', None)
                    add_active = add_active or bool(getattr(picker.model, 'add_mode_active', False))
                    remove_active = remove_active or bool(getattr(picker.model, 'delete_mode_active', False))
            except Exception:
                selected_pid = None
        _to_world = lambda mx, my: (
            mx / float(getattr(game.camera, 'zoom', 1.0) or 1.0) + float(getattr(game.camera, 'offset_x', 0.0) or 0.0),
            my / float(getattr(game.camera, 'zoom', 1.0) or 1.0) + float(getattr(game.camera, 'offset_y', 0.0) or 0.0),
        )
        _model = getattr(pe, 'model', None) if pe else None
        _world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
        for ev in remaining_events:
            if particles_editor_visible and add_active and selected_pid and ev.type == pygame.MOUSEBUTTONDOWN and getattr(ev, 'button', None) == 3:
                mx, my = getattr(ev, 'pos', (None, None))
                if mx is not None and not getattr(__import__('roguelike_ui.ui_blocker', fromlist=['is_blocked']), 'is_blocked')(mx, my):
                    if _model is not None:
                        _model.drag_place_active = True
                        _model.drag_pid = str(selected_pid)
                        _model.drag_entity_eid = None
                    continue
            if particles_editor_visible and _model is not None and getattr(_model, 'drag_place_active', False) and ev.type == pygame.MOUSEMOTION:
                pass
            if particles_editor_visible and _model is not None and getattr(_model, 'drag_place_active', False) and ev.type == pygame.MOUSEBUTTONUP and getattr(ev, 'button', None) == 3:
                mx, my = getattr(ev, 'pos', (None, None))
                if mx is not None and not getattr(__import__('roguelike_ui.ui_blocker', fromlist=['is_blocked']), 'is_blocked')(mx, my):
                    try:
                        wx, wy = _to_world(mx, my)
                    except Exception:
                        wx, wy = None, None
                    if wx is not None and wy is not None:
                        new_entry = None
                        try:
                            new_entry = _particles_append_instance(str(getattr(_model, 'drag_pid', selected_pid or '')), float(wx), float(wy))
                        except Exception:
                            new_entry = None
                        try:
                            if _world is not None and new_entry is not None:
                                eid = _world.create_entity()
                                _world.components.setdefault('Position', {})[eid] = _EcsPosition(float(wx), float(wy))
                                _world.components.setdefault('ParticlePresetComponent', {})[eid] = _EcsParticlePresetComp(str(getattr(_model, 'drag_pid', selected_pid or '')), int(new_entry.get('id')) if new_entry.get('id') is not None else None)
                        except Exception:
                            pass
                        try:
                            if new_entry is not None and _model is not None:
                                _model.selected_instance_id = int(new_entry.get('id')) if new_entry.get('id') is not None else None
                        except Exception:
                            pass
                try:
                    _model.drag_place_active = False
                    _model.drag_pid = None
                    _model.drag_entity_eid = None
                    if pe and getattr(pe, 'controller', None):
                        try:
                            ar_model = getattr(pe.controller, 'particles_add_remove_model', None)
                            if ar_model is not None:
                                ar_model.active_tool = None
                        except Exception:
                            pass
                        try:
                            picker = getattr(pe.controller, 'particles_picker_controller', None)
                            if picker is not None:
                                picker.model.add_mode_active = False
                        except Exception:
                            pass
                except Exception:
                    pass
                continue
            if add_active and selected_pid and ev.type == pygame.MOUSEBUTTONDOWN and getattr(ev, 'button', None) == 1:
                mx, my = getattr(ev, 'pos', (None, None))
                if mx is not None and not getattr(__import__('roguelike_ui.ui_blocker', fromlist=['is_blocked']), 'is_blocked')(mx, my):
                    try:
                        wx = mx / float(getattr(game.camera, 'zoom', 1.0) or 1.0) + float(getattr(game.camera, 'offset_x', 0.0) or 0.0)
                        wy = my / float(getattr(game.camera, 'zoom', 1.0) or 1.0) + float(getattr(game.camera, 'offset_y', 0.0) or 0.0)
                    except Exception:
                        wx, wy = None, None
                    if wx is not None and wy is not None:
                        new_entry = None
                        try:
                            new_entry = _particles_append_instance(str(selected_pid), float(wx), float(wy))
                        except Exception:
                            new_entry = None
                        try:
                            world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
                            if world is not None and new_entry is not None:
                                eid = world.create_entity()
                                world.components.setdefault('Position', {})[eid] = _EcsPosition(float(wx), float(wy))
                                world.components.setdefault('ParticlePresetComponent', {})[eid] = _EcsParticlePresetComp(str(selected_pid), int(new_entry.get('id')) if new_entry.get('id') is not None else None)
                        except Exception:
                            pass
                        try:
                            if new_entry is not None and _model is not None:
                                _model.selected_instance_id = int(new_entry.get('id')) if new_entry.get('id') is not None else None
                        except Exception:
                            pass
                        try:
                            if pe and getattr(pe, 'controller', None):
                                try:
                                    ar_model = getattr(pe.controller, 'particles_add_remove_model', None)
                                    if ar_model is not None:
                                        ar_model.active_tool = None
                                except Exception:
                                    pass
                                try:
                                    picker = getattr(pe.controller, 'particles_picker_controller', None)
                                    if picker is not None:
                                        picker.model.add_mode_active = False
                                except Exception:
                                    pass
                        except Exception:
                            pass
                        continue
            elif remove_active and ev.type == pygame.MOUSEBUTTONDOWN and getattr(ev, 'button', None) == 1:
                mx, my = getattr(ev, 'pos', (None, None))
                if mx is not None and not getattr(__import__('roguelike_ui.ui_blocker', fromlist=['is_blocked']), 'is_blocked')(mx, my):
                    try:
                        wx = mx / float(getattr(game.camera, 'zoom', 1.0) or 1.0) + float(getattr(game.camera, 'offset_x', 0.0) or 0.0)
                        wy = my / float(getattr(game.camera, 'zoom', 1.0) or 1.0) + float(getattr(game.camera, 'offset_y', 0.0) or 0.0)
                    except Exception:
                        wx, wy = None, None
                    if wx is not None and wy is not None:
                        removed = None
                        try:
                            removed = _particles_remove_nearest(float(wx), float(wy))
                        except Exception:
                            removed = None
                        if removed is not None:
                            try:
                                world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
                                if world is not None:
                                    presets = world.components.get('ParticlePresetComponent', {})
                                    target_e = None
                                    try:
                                        rid = int(removed.get('id')) if removed.get('id') is not None else None
                                    except Exception:
                                        rid = None
                                    if rid is not None:
                                        for eid, comp in list(presets.items()):
                                            try:
                                                if int(getattr(comp, 'entry_id', -1)) == rid:
                                                    target_e = eid
                                                    break
                                            except Exception:
                                                continue
                                    if target_e is None:
                                        pos_map = world.components.get('Position', {})
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
                        try:
                            if pe and getattr(pe, 'controller', None):
                                try:
                                    ar_model = getattr(pe.controller, 'particles_add_remove_model', None)
                                    if ar_model is not None:
                                        ar_model.active_tool = None
                                except Exception:
                                    pass
                                try:
                                    picker = getattr(pe.controller, 'particles_picker_controller', None)
                                    if picker is not None:
                                        picker.model.delete_mode_active = False
                                except Exception:
                                    pass
                        except Exception:
                            pass
                        continue
            elif particles_editor_visible and not add_active and not remove_active and ev.type == pygame.MOUSEBUTTONDOWN and getattr(ev, 'button', None) == 1:
                mx, my = getattr(ev, 'pos', (None, None))
                if mx is not None and not getattr(__import__('roguelike_ui.ui_blocker', fromlist=['is_blocked']), 'is_blocked')(mx, my):
                    try:
                        wx, wy = _to_world(mx, my)
                    except Exception:
                        wx, wy = None, None
                    if wx is not None and wy is not None and _model is not None:
                        entry = None
                        try:
                            entry = _particles_find_nearest(float(wx), float(wy), max_dist_px=48)
                        except Exception:
                            entry = None
                        if isinstance(entry, dict):
                            try:
                                _model.selected_instance_id = int(entry.get('id'))
                            except Exception:
                                _model.selected_instance_id = None
                            continue
            elif particles_editor_visible and _model is not None and _model.selected_instance_id is not None and ev.type == pygame.MOUSEBUTTONDOWN and getattr(ev, 'button', None) == 3:
                mx, my = getattr(ev, 'pos', (None, None))
                if mx is not None and not getattr(__import__('roguelike_ui.ui_blocker', fromlist=['is_blocked']), 'is_blocked')(mx, my):
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
                        try:
                            if isinstance(entry, dict) and int(entry.get('id')) == int(_model.selected_instance_id):
                                _model.drag_move_active = True
                                if _world is not None:
                                    pos_map = _world.components.get('Position', {})
                                    particles = _world.components.get('ParticlePresetComponent', {})
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
                                        _model.selected_entity_eid = best_e
                                continue
                        except Exception:
                            pass
            elif particles_editor_visible and _model is not None and getattr(_model, 'drag_move_active', False) and ev.type == pygame.MOUSEMOTION:
                mx, my = getattr(ev, 'pos', (None, None))
                if mx is not None and not getattr(__import__('roguelike_ui.ui_blocker', fromlist=['is_blocked']), 'is_blocked')(mx, my):
                    try:
                        wx, wy = _to_world(mx, my)
                    except Exception:
                        wx, wy = None, None
                    if wx is not None and wy is not None and _world is not None and _model.selected_entity_eid is not None:
                        try:
                            pos_map = _world.components.setdefault('Position', {})
                            if _model.selected_entity_eid in pos_map:
                                pos_map[_model.selected_entity_eid] = _EcsPosition(float(wx), float(wy))
                        except Exception:
                            pass
                pass_events.append(ev)
                continue
            elif particles_editor_visible and _model is not None and getattr(_model, 'drag_move_active', False) and ev.type == pygame.MOUSEBUTTONUP and getattr(ev, 'button', None) == 3:
                mx, my = getattr(ev, 'pos', (None, None))
                if mx is not None and not getattr(__import__('roguelike_ui.ui_blocker', fromlist=['is_blocked']), 'is_blocked')(mx, my):
                    try:
                        wx, wy = _to_world(mx, my)
                    except Exception:
                        wx, wy = None, None
                    if wx is not None and wy is not None:
                        try:
                            _particles_update_pos(int(_model.selected_instance_id), float(wx), float(wy))
                        except Exception:
                            pass
                        if _world is not None and _model.selected_entity_eid is None:
                            try:
                                pos_map = _world.components.get('Position', {})
                                particles = _world.components.get('ParticlePresetComponent', {})
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
                                    _world.components.setdefault('Position', {})[best_e] = _EcsPosition(float(wx), float(wy))
                            except Exception:
                                pass
                try:
                    _model.drag_move_active = False
                    _model.selected_entity_eid = None
                except Exception:
                    pass
                continue
            pass_events.append(ev)
    except Exception:
        pass_events = list(remaining_events)
    return pass_events
