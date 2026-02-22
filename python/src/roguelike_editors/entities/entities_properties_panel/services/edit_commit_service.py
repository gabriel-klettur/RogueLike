"""Service: apply commit of a property edit from the Properties panel."""
from __future__ import annotations

import logging

from roguelike_editors.entities.services.constants import ADD_ENTITIES_ON_SYSTEM
from .entity_properties_service import convert_value
from .stats_templates import SCALE_FIELDS
from typing import Any

logger = logging.getLogger(__name__)


def _is_scale_field(key: str) -> bool:
    """Check if the key is a scale_* field that belongs in assets, not stats."""
    return key in SCALE_FIELDS


def _set_nested(stats: dict, key: str, value: Any) -> None:
    if '.' not in key:
        stats[key] = value
        return
    parts = key.split('.')
    cur = stats
    for i, p in enumerate(parts):
        if i == len(parts) - 1:
            cur[p] = value
        else:
            nxt = cur.get(p)
            if not isinstance(nxt, dict):
                nxt = {}
                cur[p] = nxt
            cur = nxt


def _get_nested(stats: dict, key: str):
    if '.' not in key:
        return stats.get(key)
    parts = key.split('.')
    cur = stats
    for i, p in enumerate(parts):
        if not isinstance(cur, dict):
            return None
        if i == len(parts) - 1:
            return cur.get(p)
        cur = cur.get(p, {})
    return None


def commit_edit(controller) -> None:
    """Commit the current edit, supporting Add-Entities-On-System mode and normal mode."""
    model = controller.model
    if not model.editing_property or not model.selected_id:
        return

    ent_id = model.selected_id
    key = model.editing_property
    new_text = model.editing_text

    in_add_system_mode = False
    try:
        in_add_system_mode = (
            controller.editor_controller.model.add_remove_model.active_tool == ADD_ENTITIES_ON_SYSTEM
        )
    except Exception:
        in_add_system_mode = False

    if in_add_system_mode:
        is_selector = getattr(model, 'show_add_system_selector', False)
        sel_type = getattr(model, 'add_system_entity_type', 'Hostile')
        target_is_player = (ent_id in model.player_stats) or (is_selector and sel_type == 'Player')

        if key == 'id':
            new_id = new_text.strip() or ent_id
            if target_is_player:
                if new_id and new_id != ent_id:
                    p_stats = model.player_stats.pop(ent_id, None) or {}
                    model.player_stats[new_id] = p_stats
                    if isinstance(model.player_assets, dict) and ent_id in model.player_assets:
                        model.player_assets[new_id] = model.player_assets.pop(ent_id)
                model.selected_id = new_id
                controller._reset_edit_state()
                return
            else:
                if new_id and new_id != ent_id:
                    entry = model.monsters.pop(ent_id, None)
                    if entry is None:
                        entry = {'stats': {}, 'assets': {'active_set': 'no-sets', 'sets': {}, 'no-sets': {}}}
                else:
                    entry = model.monsters.get(ent_id)
                    if entry is None:
                        entry = {'stats': {}, 'assets': {'active_set': 'no-sets', 'sets': {}, 'no-sets': {}}}
                if isinstance(entry, dict):
                    entry['__pending__'] = True
                model.monsters[new_id] = entry
                model.selected_id = new_id
                controller._reset_edit_state()
                return

        # Handle scale fields in add-system mode (write to assets, not stats)
        if _is_scale_field(key):
            new_val = convert_value(new_text, 0.5)
            if target_is_player:
                assets = model.player_assets.setdefault(ent_id, {})
                active_set = assets.get('active_set', 'sets')
                if active_set == 'sets':
                    meta = assets.setdefault('sets', {}).setdefault('sprites_data_set', {})
                else:
                    meta = assets.setdefault('no-sets', {}).setdefault('sprites_data_no-set', {})
                meta[key] = new_val
            else:
                m_entry = model.monsters.setdefault(ent_id, {})
                if isinstance(m_entry, dict):
                    m_entry['__pending__'] = True
                assets = m_entry.setdefault('assets', {})
                active_set = assets.get('active_set', 'no-sets')
                if active_set == 'sets':
                    meta = assets.setdefault('sets', {}).setdefault('sprites_data_set', {})
                else:
                    meta = assets.setdefault('no-sets', {}).setdefault('sprites_data_no-set', {})
                meta[key] = new_val
            controller._reset_edit_state()
            return

        if target_is_player:
            stats = model.player_stats.setdefault(ent_id, {})
            old_val = _get_nested(stats, key)
            new_val = convert_value(new_text, old_val)
            _set_nested(stats, key, new_val)
            controller._reset_edit_state()
            return
        else:
            m_entry = model.monsters.setdefault(ent_id, {})
            if isinstance(m_entry, dict):
                m_entry['__pending__'] = True
            stats = m_entry.setdefault('stats', {})
            old_val = _get_nested(stats, key)
            new_val = convert_value(new_text, old_val)
            _set_nested(stats, key, new_val)
            controller._reset_edit_state()
            return

    # Normal mode: id rename is a specific command, else generic property edit
    if key == 'id':
        new_id = new_text.strip()
        if new_id and new_id != ent_id:
            from roguelike_editors.entities.services.commands import RenameEntityCommand
            controller.editor_controller.history.push(RenameEntityCommand(controller, ent_id, new_id))
        controller._reset_edit_state()
        return

    from roguelike_editors.entities.services.commands import EditPropertyCommand
    controller.editor_controller.history.push(EditPropertyCommand(controller, ent_id, key, new_text))
