"""Service: handle choosing an asset from the grid or picker.

Moves complex path normalization and in-memory update logic out of the controller.
"""
from __future__ import annotations

import os
import logging

from roguelike_editors.entities.services.constants import ADD_ENTITIES_ON_SYSTEM
from roguelike_editors.entities.services.commands import SetAssetCommand
from .panel_ui_utils import reset_grid_cache, clear_thumbnail_cache, request_render

logger = logging.getLogger(__name__)


def handle_asset_chosen(controller, cell_key: str, path) -> None:
    """Apply an asset choice to the currently selected entity.

    If the editor is in Add-Entities-On-System mode, perform an in-memory update only.
    Otherwise, push an undoable command into the editor history.
    """
    ent_id = controller.model.selected_id
    if not ent_id:
        return

    in_add_system_mode = False
    try:
        in_add_system_mode = (
            controller.editor_controller.model.add_remove_model.active_tool == ADD_ENTITIES_ON_SYSTEM
        )
    except Exception:
        in_add_system_mode = False

    # In-memory update branch for add-system mode
    if in_add_system_mode:
        try:
            path_str = os.fspath(path)
        except Exception:
            path_str = path
        if isinstance(path_str, str):
            path_str = path_str.replace('\\\\', '/').replace('\\', '/')

        if ent_id in controller.model.player_stats:
            assets = controller.model.player_assets.setdefault(ent_id, {})
            default_active = assets.get('active_set', 'sets')
            active = assets.get('active_set', default_active)
        else:
            m_entry = controller.model.monsters.setdefault(ent_id, {})
            if isinstance(m_entry, dict):
                m_entry['__pending__'] = True
            assets = m_entry.setdefault('assets', {})
            default_active = 'no-sets'
            active = assets.get('active_set', default_active)

        parts = cell_key.split('_')
        if len(parts) == 3 and parts[0] == 'asset':
            _, state, direction = parts
            if active == 'sets':
                sprites_set = assets.setdefault('sets', {}).setdefault('sprites_set', {})
                sprites_set[state] = [path_str]
            else:
                no_sets = assets.setdefault('no-sets', {})
                no_sets.setdefault(state, {})[direction] = path_str
            assets.pop('sprites', None)

        controller.assets_picker_controller.hide()
        reset_grid_cache(controller.grid_controller)
        clear_thumbnail_cache(controller.grid_controller)
        request_render(controller.editor_controller)
        return

    # Default: push command for persistence and undo/redo
    controller.editor_controller.history.push(SetAssetCommand(controller, ent_id, cell_key, path))
