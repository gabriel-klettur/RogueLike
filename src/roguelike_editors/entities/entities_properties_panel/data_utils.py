from __future__ import annotations
import pygame
from roguelike_editors.entities.entities_properties_panel.services.entity_flatten import (
    flatten_entity_data,
)
from roguelike_editors.entities.entities_properties_panel.services.stats_templates import (
    PLAYER_STATS_TEMPLATE,
    MONSTER_STATS_TEMPLATE,
)


def get_entity_data(model) -> dict:
    """Return flattened entity data depending on current add-system mode.

    When add-system selector is visible, return only stats for selected type.
    Otherwise, return full flattened data (stats + assets with 'asset_*' keys).
    """
    ent_id = model.hovered_entity_id or model.selected_id
    if getattr(model, "show_add_system_selector", False):
        sel_type = getattr(model, "add_system_entity_type", "Hostile")
        if sel_type == "Player":
            return dict(model.player_stats.get(ent_id, {}))
        monster = model.monsters.get(ent_id, {}) if model.monsters else {}
        return dict(monster.get("stats", {}))
    return flatten_entity_data(model.player_stats, model.player_assets, model.monsters, ent_id)


def _flatten_once(d: dict) -> dict:
    flat: dict = {}
    for k, v in d.items():
        if isinstance(v, dict):
            for sk, sv in v.items():
                flat[f"{k}.{sk}"] = sv
        else:
            flat[k] = v
    return flat


def get_entity_stats_data(model) -> dict:
    """Merge template + source stats and return a 1-level flattened dict.

    - Player uses PLAYER_STATS_TEMPLATE and player stats
    - Hostile uses MONSTER_STATS_TEMPLATE and monster['stats']
    """
    ent_id = model.hovered_entity_id or model.selected_id
    if getattr(model, "show_add_system_selector", False):
        sel_type = getattr(model, "add_system_entity_type", "Hostile")
    else:
        sel_type = "Player" if ent_id in model.player_stats else "Hostile"

    if sel_type == "Player":
        tmpl = PLAYER_STATS_TEMPLATE
        src = model.player_stats.get(ent_id, {})
    else:
        tmpl = MONSTER_STATS_TEMPLATE
        src = (model.monsters.get(ent_id, {}) or {}).get("stats", {})

    merged: dict = {}
    for k, v in tmpl.items():
        merged[k] = dict(v) if isinstance(v, dict) else v
    for k, v in src.items():
        if isinstance(v, dict) and isinstance(merged.get(k), dict):
            merged[k].update(v)
        else:
            merged[k] = v

    return _flatten_once(merged)
