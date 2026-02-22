from __future__ import annotations
import pygame
from roguelike_editors.entities.services.entity_lookup import find_clickable_entity_at
from roguelike_editors.entities.services.camera_helpers import screen_to_world
from roguelike_editors.entities.services.commands import MoveEntityCommand


def handle_map_interactions(editor: "EntitiesEditorController", event: pygame.event.Event) -> bool:
    """Interacciones de mapa: hover, selección, drag y persistencia de movimiento."""
    if not (editor.model.active and not (editor.model.delete_mode_active or editor.model.spawn_mode_active)):
        return False

    try:
        if event.type == pygame.MOUSEMOTION:
            mx, my = getattr(event, 'pos', pygame.mouse.get_pos())
            eid = find_clickable_entity_at(editor.game, mx, my)
            editor.model.hovered_entity_eid = eid
            if editor.model.is_right_dragging and editor.model.selected_entity_eid is not None:
                try:
                    wx, wy = screen_to_world(editor.game.camera, mx, my)
                    world = editor.game.ecs.ecs_world
                    pos_store = world.components.get('Position', {})
                    pos = pos_store.get(editor.model.selected_entity_eid)
                    if pos is not None:
                        dx, dy = editor.model.drag_offset_world
                        pos.x = int(wx - dx)
                        pos.y = int(wy - dy)
                        if hasattr(world, 'invalidate_spatial_index'):
                            world.invalidate_spatial_index()
                    return True
                except Exception:
                    pass
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            mx, my = event.pos
            eid = find_clickable_entity_at(editor.game, mx, my)
            editor.model.selected_entity_eid = eid
            return eid is not None
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 3:
            mx, my = event.pos
            eid = find_clickable_entity_at(editor.game, mx, my)
            if eid is not None and eid == editor.model.selected_entity_eid:
                try:
                    wx, wy = screen_to_world(editor.game.camera, mx, my)
                    world = editor.game.ecs.ecs_world
                    pos = world.components.get('Position', {}).get(eid)
                    if pos is not None:
                        editor.model.drag_offset_world = (float(wx - pos.x), float(wy - pos.y))
                        editor.model.drag_start_world_pos = (int(pos.x), int(pos.y))
                        editor.model.is_right_dragging = True
                        return True
                except Exception:
                    pass
        if event.type == pygame.MOUSEBUTTONUP and getattr(event, 'button', None) == 3:
            if editor.model.is_right_dragging and editor.model.selected_entity_eid is not None:
                try:
                    world = editor.game.ecs.ecs_world
                    pos = world.components.get('Position', {}).get(editor.model.selected_entity_eid)
                    if pos is not None and editor.model.drag_start_world_pos is not None:
                        end_pos = (int(pos.x), int(pos.y))
                        start_pos = tuple(editor.model.drag_start_world_pos)
                        editor.history.push(MoveEntityCommand(editor, editor.model.selected_entity_eid, start_pos, end_pos))
                except Exception:
                    pass
                finally:
                    editor.model.is_right_dragging = False
                    editor.model.drag_start_world_pos = None
                    return True
    except Exception:
        pass

    return False
