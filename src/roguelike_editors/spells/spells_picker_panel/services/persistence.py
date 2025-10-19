from __future__ import annotations

import os
from typing import Any

import pygame

from roguelike_engine.utils.loader import load_image, _IMAGE_CACHE
from roguelike_game.config.spells_config import reload_spells
from roguelike_ui.services.json_persistence import load_from_json, save_to_json


def commit_edit(controller: Any) -> None:
    """Commit the currently edited field to the spells JSON and refresh state.

    Expects a controller with fields:
    - model (with editing_property/text/cursor, selected_id, hovered_id, spells, assets)
    - view (with assets dict)
    - preview_manager (with rebuild method)
    """
    model = controller.model
    if not getattr(model, "editing_property", None):
        return

    sid = model.selected_id or model.hovered_id
    if not sid:
        return

    key = model.editing_property
    new_text = model.editing_text

    path = os.path.join(os.getcwd(), "data", "spells", "spells.json")
    root = load_from_json(path)
    entry = root.get(sid, {})
    old_val = entry.get(key)

    # Convert type preserving existing schema types
    try:
        if isinstance(old_val, bool):
            converted = str(new_text).lower() in ("true", "1", "yes")
        elif isinstance(old_val, int):
            converted = int(new_text)
        elif isinstance(old_val, float):
            converted = float(new_text)
        else:
            converted = new_text
    except (ValueError, TypeError):
        converted = new_text

    entry[key] = converted

    # Persist changes
    save_to_json(path, sid, entry)

    # Hot-reload runtime spells config so new casts reflect changes
    try:
        reload_spells()
    except Exception:
        pass

    # Rebuild previews in case vfx.preview or particle params changed
    try:
        controller.preview_manager.rebuild(model.spells)
    except Exception:
        pass

    # Update model entry and reset editing state
    model.spells[sid] = entry
    model.editing_property = None
    model.editing_text = ""
    model.editing_cursor = 0


def reload_sprites_from_spells(controller: Any) -> None:
    """Clear cache and reload sprite surfaces for all spells in the picker.

    Synchronizes both model.assets and view.assets.
    """
    model = controller.model
    view = controller.view

    try:
        _IMAGE_CACHE.clear()
    except Exception:
        pass

    for sid, sdef in list(model.spells.items()):
        try:
            path: str | None = None
            v = sdef.get("sprite")
            if isinstance(v, str) and v:
                path = v
            else:
                vfx = sdef.get("vfx") if isinstance(sdef.get("vfx"), dict) else None
                if isinstance(vfx, dict):
                    spr = vfx.get("sprite") if isinstance(vfx.get("sprite"), dict) else None
                    if isinstance(spr, dict):
                        p = spr.get("path")
                        if isinstance(p, str) and p:
                            path = p
            if not path:
                continue

            img = load_image(path)
            model.assets[sid] = img
            try:
                view.assets[sid] = img
            except Exception:
                pass
        except Exception:
            # Continue best-effort reload for remaining spells
            pass
