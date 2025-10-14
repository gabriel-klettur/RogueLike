from __future__ import annotations
import copy
import json
import os
from dataclasses import dataclass
from typing import Any

from roguelike_editors.entities.services.history import Command
from roguelike_editors.entities.services import commands as cmd_mod
from roguelike_ui.services.json_persistence import load_from_json
from roguelike_game.factories.monster.config import reload_monster_defs
from roguelike_game.factories.monster import cache as monster_cache


@dataclass
class RenameEntityCommand(Command):
    controller: Any
    old_id: str
    new_id: str
    description: str = "Rename entity id"
    _saved_entry: Any = None

    def _persist_rename(self, src_id: str, dst_id: str) -> None:
        path, _, entry = self._load_entity_data(src_id)
        root = load_from_json(path)
        base = os.path.basename(path).lower()
        if base == 'new_players.json':
            section = 'players'
        elif base == 'new_hostiles.json':
            section = 'hostiles'
        elif base == 'new_neutrals.json':
            section = 'neutrals'
        else:
            if src_id in self.controller.model.player_stats:
                section = 'players'
            else:
                try:
                    faction = (entry or {}).get('stats', {}).get('faction')
                except Exception:
                    faction = None
                section = 'neutrals' if faction == 'NEUTRAL' else 'hostiles'
        classes = root.setdefault(section, {}).setdefault('classes', {})
        new_classes = {}
        for k, v in classes.items():
            if k == src_id:
                new_classes[dst_id] = entry
            else:
                new_classes[k] = v
        root.setdefault(section, {})['classes'] = new_classes
        if section == 'players':
            try:
                if root.get('DEFAULT_CLASS') == src_id:
                    root['DEFAULT_CLASS'] = dst_id
            except Exception:
                pass
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(root, f, ensure_ascii=False, indent=2)

    def _load_entity_data(self, ent_id: str):
        return cmd_mod.load_entity_data(ent_id, self.controller.model.player_stats, self.controller.model.monsters)

    def apply(self) -> None:
        if not self.old_id or not self.new_id or self.old_id == self.new_id:
            return
        if self.new_id in self.controller.model.player_stats or self.new_id in self.controller.model.monsters:
            return
        _, _, entry = self._load_entity_data(self.old_id)
        self._saved_entry = copy.deepcopy(entry)
        self._persist_rename(self.old_id, self.new_id)
        is_player = self.old_id in self.controller.model.player_stats
        target = self.controller.model.player_stats if is_player else self.controller.model.monsters
        new_order = {}
        for k, v in target.items():
            if k == self.old_id:
                new_order[self.new_id] = v
            else:
                new_order[k] = v
        target.clear()
        target.update(new_order)
        try:
            if not is_player:
                path2, _, entry2 = self._load_entity_data(self.new_id)
                base2 = os.path.basename(path2).lower()
                editor_model = self.controller.editor_controller.model
                try:
                    editor_model.hostiles.pop(self.old_id, None)
                except Exception:
                    pass
                try:
                    editor_model.neutrals.pop(self.old_id, None)
                except Exception:
                    pass
                if base2 == 'new_neutrals.json':
                    editor_model.neutrals[self.new_id] = copy.deepcopy(entry2)
                else:
                    editor_model.hostiles[self.new_id] = copy.deepcopy(entry2)
        except Exception:
            pass
        try:
            if is_player:
                classes = self.controller.editor_controller.model.classes
                if self.old_id in classes:
                    classes[self.new_id] = classes.pop(self.old_id)
        except Exception:
            pass
        try:
            assets = self.controller.editor_controller.model.assets
            if self.old_id in assets:
                assets[self.new_id] = assets.pop(self.old_id)
        except Exception:
            pass
        try:
            if is_player:
                p_assets = self.controller.model.player_assets
                if self.old_id in p_assets:
                    p_assets[self.new_id] = p_assets.pop(self.old_id)
        except Exception:
            pass
        try:
            picker_model = self.controller.editor_controller.picker_controller.model
            if picker_model.selected_id == self.old_id:
                picker_model.selected_id = self.new_id
            if picker_model.hovered_id == self.old_id:
                picker_model.hovered_id = self.new_id
        except Exception:
            pass
        self.controller.model.selected_id = self.new_id
        if not is_player:
            try:
                reload_monster_defs()
                monster_cache.load_caches_for([self.new_id])
            except Exception:
                pass
        try:
            self.controller.grid_controller.model.last_entity_id = None
            self.controller.grid_controller.model.last_state_tab = None
            self.controller.editor_controller.render(self.controller.editor_controller.game.screen)
        except Exception:
            pass
