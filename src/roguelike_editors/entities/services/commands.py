"""Facade for editor commands and patch points.

- Exposes functions like ``save_entity_data`` so tests can monkeypatch
  ``roguelike_editors.entities.services.commands`` directly.
- Lazily re-exports command classes from ``commands_pkg`` via
  ``__getattr__`` to avoid circular imports and keep this file short.
"""
from __future__ import annotations

from importlib import import_module

# Re-export utility for external callers (kept stable API)
from roguelike_editors.entities.services.commands_pkg.utils import _abs_to_rel_asset_path  # noqa: F401

# Monkeypatchable wrappers (tests set these on this module)
from roguelike_editors.entities.entities_properties_panel.services.entity_properties_service import (  # noqa: E501
    load_entity_data,  # noqa: F401
    save_entity_data,  # noqa: F401
    convert_value,  # noqa: F401
)
from roguelike_editors.entities.entities_properties_panel.services.ecs_update_service import (  # noqa: E501
    update_player_assets,  # noqa: F401
    update_monster_assets,  # noqa: F401
    update_player_stats,  # noqa: F401
    update_monster_stats,  # noqa: F401
)

_CLASS_TO_MODULE = {
    "SpawnEntityCommand": "spawn",
    "MoveEntityCommand": "move",
    "DeleteEntityCommand": "delete_entity",
    "EditPropertyCommand": "edit_property",
    "SetAssetCommand": "set_asset",
    "ToggleActiveSetCommand": "toggle_active_set",
    "RenameEntityCommand": "rename_entity",
    "DeleteEntityDefinitionCommand": "delete_definition",
}

def __getattr__(name: str):
    module_name = _CLASS_TO_MODULE.get(name)
    if module_name:
        mod = import_module(
            f"roguelike_editors.entities.services.commands_pkg.{module_name}"
        )
        return getattr(mod, name)
    raise AttributeError(name)

__all__ = list(_CLASS_TO_MODULE.keys()) + [
    "_abs_to_rel_asset_path",
    "load_entity_data",
    "save_entity_data",
    "convert_value",
    "update_player_assets",
    "update_monster_assets",
    "update_player_stats",
    "update_monster_stats",
]
