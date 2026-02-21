from __future__ import annotations
import logging
import pygame
from roguelike_editors.entities.services.constants import ENTITIES_TOOLS, ADD_ENTITIES_ON_SYSTEM
from roguelike_editors.entities.services.entity_lookup import find_clickable_entity_at
from roguelike_editors.entities.services.camera_helpers import screen_to_tile
from roguelike_editors.entities.services.commands import (
    SpawnEntityCommand,
    DeleteEntityCommand,
    DeleteEntityDefinitionCommand,
)
from roguelike_engine.config.config_tiles import TILE_SIZE

logger = logging.getLogger(__name__)


def handle_entities_tools(editor: "EntitiesEditorController", event: pygame.event.Event) -> bool:
    """Maneja eventos cuando la herramienta activa pertenece a ENTITIES_TOOLS."""
    active = editor.model.toolbar_model.active_tool
    if active not in ENTITIES_TOOLS:
        return False

    # Add/Remove panel
    if editor.add_remove_controller.handle_event(event):
        return True

    # Picker panel (siempre procesa para hover/selección visual)
    editor.picker_controller.handle_event(event)

    # Delete definición desde picker mientras delete_mode activo
    if editor.model.delete_mode_active and event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
        panel_rect = editor.picker_controller.model.panel_rect
        if panel_rect and panel_rect.collidepoint(getattr(event, 'pos', (0, 0))):
            sel = editor.picker_controller.model.selected_id or editor.picker_controller.model.hovered_id
            if sel:
                editor.history.push(DeleteEntityDefinitionCommand(editor.properties_controller, sel))
                logger.debug(" Delete-definition command for '%s' queued via picker click", sel)
                editor.exit_delete_mode()
                return True

    # Sincronización de selección con Properties
    hovered = editor.picker_controller.model.hovered_id
    selected = editor.picker_controller.model.selected_id
    in_add_system_mode = (editor.model.add_remove_model.active_tool == ADD_ENTITIES_ON_SYSTEM)
    if not (editor.model.delete_mode_active or editor.model.spawn_mode_active):
        if not in_add_system_mode:
            editor.properties_controller.model.hovered_entity_id = hovered
            editor.properties_controller.model.selected_id = selected
        else:
            editor.properties_controller.model.hovered_entity_id = None
    else:
        editor.properties_controller.model.hovered_entity_id = None
        editor.properties_controller.model.selected_id = None

    # Selección en spawn mode a partir del picker
    if editor.model.spawn_mode_active and editor.model.spawn_entity_type is None and event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
        sel = editor.picker_controller.model.selected_id
        if sel:
            editor.model.spawn_entity_type = sel
            editor.picker_controller.model.blink = False
            editor.picker_controller.model.selection_blink = True
            pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_CROSSHAIR)
            try:
                setattr(editor.model, 'tutorial_spawn_selection_pulse', True)
            except Exception:
                pass
            return True

    # Consumir eventos dentro del rectángulo del picker
    if hasattr(event, 'pos') and editor.picker_controller.model.visible:
        panel_rect = editor.picker_controller.model.panel_rect
        if panel_rect and panel_rect.collidepoint(event.pos):
            return True

    # Properties panel (no en delete/spawn)
    if not (editor.model.delete_mode_active or editor.model.spawn_mode_active):
        if editor.properties_controller.handle_event(event):
            return True

    # Delete de entidad en el mapa en delete mode
    if editor.model.delete_mode_active and event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
        mx, my = event.pos
        eid = find_clickable_entity_at(editor.game, mx, my)
        if eid is not None:
            editor.history.push(DeleteEntityCommand(editor, eid))
            logger.debug(" Entity %s delete command queued via click at (%s,%s)", eid, mx, my)
            try:
                setattr(editor.model, 'tutorial_entity_deleted_pulse', True)
            except Exception:
                pass
            editor.exit_delete_mode()
            return True

    # Completar spawn: click en mapa
    if editor.model.spawn_mode_active and editor.model.spawn_entity_type and event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
        etype = editor.model.spawn_entity_type
        sx, sy = event.pos
        tx, ty = screen_to_tile(editor.game.camera, sx, sy, TILE_SIZE)
        # Usar TILE_SIZE importado por controlador: mantenemos compatibilidad pasando a comando la conversión ya calculada
        editor.history.push(SpawnEntityCommand(editor, etype, tx, ty))
        logger.debug(" Spawn command for '%s' at tile (%s,%s) queued", etype, tx, ty)
        editor.exit_spawn_mode()
        try:
            setattr(editor.model, 'tutorial_entity_spawned_pulse', True)
        except Exception:
            pass
        return True

    return False
