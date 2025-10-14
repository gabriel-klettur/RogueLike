from __future__ import annotations
import copy
from dataclasses import dataclass
from typing import Any

from roguelike_editors.entities.services.history import Command
from roguelike_editors.entities.services import commands as cmd_mod
from .utils import _abs_to_rel_asset_path


@dataclass
class SetAssetCommand(Command):
    controller: Any
    ent_id: str
    cell_key: str
    new_path: str
    description: str = "Set asset"
    _old_value: Any = None

    def _get_current_value(self, entry: dict) -> Any:
        parts = self.cell_key.split("_")
        if len(parts) != 3 or parts[0] != 'asset':
            return None
        _, state, direction = parts
        assets = entry.setdefault('assets', {})
        default_active = 'sets' if self.ent_id in self.controller.model.player_stats else 'no-sets'
        active = assets.get('active_set', default_active)
        if active == 'sets':
            sprites_set = assets.setdefault('sets', {}).setdefault('sprites_set', {})
            return copy.deepcopy(sprites_set.get(state))
        else:
            no_sets = assets.setdefault('no-sets', {})
            return copy.deepcopy(no_sets.setdefault(state, {}).get(direction))

    def _write_value(self, entry: dict, rel_path: str) -> None:
        parts = self.cell_key.split("_")
        _, state, direction = parts
        assets = entry.setdefault('assets', {})
        default_active = 'sets' if self.ent_id in self.controller.model.player_stats else 'no-sets'
        active = assets.get('active_set', default_active)
        if active == 'sets':
            sprites_set = assets.setdefault('sets', {}).setdefault('sprites_set', {})
            sprites_set[state] = [rel_path]
        else:
            no_sets = assets.setdefault('no-sets', {})
            state_no_set = no_sets.setdefault(state, {})
            state_no_set[direction] = rel_path
        entry.pop('sprites', None)

    def _persist_and_update(self, entry: dict, path: str) -> None:
        cmd_mod.save_entity_data(self.ent_id, entry, path, self.controller.model.player_stats, self.controller.model.monsters)
        ecs_world = self.controller.editor_controller.game.ecs.ecs_world
        if self.ent_id in self.controller.model.player_stats:
            self.controller.model.player_assets[self.ent_id] = entry.get('assets', {})
            cmd_mod.update_player_assets(ecs_world, self.ent_id)
        else:
            cmd_mod.update_monster_assets(ecs_world, self.ent_id)
            try:
                self.controller.model.monsters[self.ent_id] = entry
            except Exception:
                pass
        self.controller.assets_picker_controller.hide()
        self.controller.grid_controller.model.last_entity_id = None
        self.controller.grid_controller.model.last_state_tab = None
        try:
            self.controller.grid_controller.view.thumbnail_cache.clear()
        except Exception:
            pass
        try:
            self.controller.editor_controller.render(self.controller.editor_controller.game.screen)
        except Exception:
            pass

    def apply(self) -> None:
        path, data, entry = cmd_mod.load_entity_data(self.ent_id, self.controller.model.player_stats, self.controller.model.monsters)
        self._old_value = self._get_current_value(entry)
        rel = _abs_to_rel_asset_path(self.new_path)
        self._write_value(entry, rel)
        self._persist_and_update(entry, path)

    def undo(self) -> None:
        path, data, entry = cmd_mod.load_entity_data(self.ent_id, self.controller.model.player_stats, self.controller.model.monsters)
        parts = self.cell_key.split("_")
        if len(parts) != 3 or parts[0] != 'asset':
            return
        _, state, direction = parts
        assets = entry.setdefault('assets', {})
        default_active = 'sets' if self.ent_id in self.controller.model.player_stats else 'no-sets'
        active = assets.get('active_set', default_active)
        if active == 'sets':
            sprites_set = assets.setdefault('sets', {}).setdefault('sprites_set', {})
            sprites_set[state] = copy.deepcopy(self._old_value) if self._old_value is not None else []
        else:
            no_sets = assets.setdefault('no-sets', {})
            state_no_set = no_sets.setdefault(state, {})
            if self._old_value is None:
                state_no_set.pop(direction, None)
            else:
                state_no_set[direction] = self._old_value
        cmd_mod.save_entity_data(self.ent_id, entry, path, self.controller.model.player_stats, self.controller.model.monsters)
        ecs_world = self.controller.editor_controller.game.ecs.ecs_world
        if self.ent_id in self.controller.model.player_stats:
            self.controller.model.player_assets[self.ent_id] = entry.get('assets', {})
            cmd_mod.update_player_assets(ecs_world, self.ent_id)
        else:
            cmd_mod.update_monster_assets(ecs_world, self.ent_id)
            try:
                self.controller.model.monsters[self.ent_id] = entry
            except Exception:
                pass
        try:
            self.controller.grid_controller.view.thumbnail_cache.clear()
        except Exception:
            pass
