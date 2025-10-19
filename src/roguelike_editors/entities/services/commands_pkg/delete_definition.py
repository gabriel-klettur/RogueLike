from __future__ import annotations
import copy
import json
import os
from dataclasses import dataclass
from typing import Any, Optional

from roguelike_editors.entities.services.history import Command
from roguelike_ui.services.json_persistence import load_from_json
from roguelike_editors.entities.services import commands as cmd_mod
from roguelike_engine.utils.loader import load_image, load_sprite_sheet
from roguelike_game.factories.monster.sprite_loader import create_sprite_component
from roguelike_game.config.players_config import PLAYER_ASSETS
from roguelike_game.factories.monster.config import reload_monster_defs
from roguelike_game.factories.monster import cache as monster_cache


@dataclass
class DeleteEntityDefinitionCommand(Command):
    controller: Any
    ent_id: str
    description: str = "Delete entity definition"
    _saved_entry: Any = None
    _saved_index: Optional[int] = None
    _section: Optional[str] = None
    _path: Optional[str] = None
    _saved_default: Any = None

    def _persist_delete(self) -> None:
        path, _, entry = cmd_mod.load_entity_data(self.ent_id, self.controller.model.player_stats, self.controller.model.monsters)
        self._path = path
        root = load_from_json(path)
        base = os.path.basename(path).lower()
        if base == 'new_players.json':
            self._section = 'players'
        elif base == 'new_hostiles.json':
            self._section = 'hostiles'
        elif base == 'new_neutrals.json':
            self._section = 'neutrals'
        else:
            if self.ent_id in self.controller.model.player_stats:
                self._section = 'players'
            else:
                try:
                    faction = (entry or {}).get('stats', {}).get('faction')
                except Exception:
                    faction = None
                self._section = 'neutrals' if faction == 'NEUTRAL' else 'hostiles'
        classes = root.setdefault(self._section, {}).setdefault('classes', {})
        self._saved_entry = copy.deepcopy(entry)
        keys = list(classes.keys())
        try:
            self._saved_index = keys.index(self.ent_id)
        except ValueError:
            self._saved_index = None
        if self._section == 'players':
            self._saved_default = root.get('DEFAULT_CLASS')
        new_classes = {}
        for k, v in classes.items():
            if k != self.ent_id:
                new_classes[k] = v
        root.setdefault(self._section, {})['classes'] = new_classes
        if self._section == 'players':
            try:
                if root.get('DEFAULT_CLASS') == self.ent_id:
                    first = next(iter(new_classes.keys()), None)
                    if first is None:
                        root.pop('DEFAULT_CLASS', None)
                    else:
                        root['DEFAULT_CLASS'] = first
            except Exception:
                pass
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(root, f, ensure_ascii=False, indent=2)

    def _remove_in_memory(self) -> None:
        is_player = self.ent_id in self.controller.model.player_stats
        if is_player:
            self.controller.model.player_stats.pop(self.ent_id, None)
            try:
                self.controller.model.player_assets.pop(self.ent_id, None)
            except Exception:
                pass
            try:
                classes = self.controller.editor_controller.model.classes
                classes.pop(self.ent_id, None)
            except Exception:
                pass
        else:
            self.controller.model.monsters.pop(self.ent_id, None)
            try:
                editor_model = self.controller.editor_controller.model
                if self._section == 'neutrals':
                    editor_model.neutrals.pop(self.ent_id, None)
                else:
                    editor_model.hostiles.pop(self.ent_id, None)
            except Exception:
                pass
        try:
            assets = self.controller.editor_controller.model.assets
            assets.pop(self.ent_id, None)
        except Exception:
            pass
        try:
            picker_model = self.controller.editor_controller.picker_controller.model
            if picker_model.selected_id == self.ent_id:
                picker_model.selected_id = None
            if picker_model.hovered_id == self.ent_id:
                picker_model.hovered_id = None
        except Exception:
            pass
        if not is_player:
            try:
                reload_monster_defs()
                monster_cache._loaded_variants.discard(self.ent_id)
                monster_cache._SPRITE_SURFACES.pop(self.ent_id, None)
                monster_cache._DEATH_SURFACES.pop(self.ent_id, None)
            except Exception:
                pass

    def apply(self) -> None:
        if not (self.ent_id in self.controller.model.player_stats or self.ent_id in self.controller.model.monsters):
            return
        self._persist_delete()
        self._remove_in_memory()
        try:
            self.controller.model.hovered_entity_id = None
            if self.controller.model.selected_id == self.ent_id:
                self.controller.model.selected_id = None
        except Exception:
            pass
        try:
            self.controller.editor_controller.render(self.controller.editor_controller.game.screen)
        except Exception:
            pass

    def _persist_restore(self) -> None:
        if not (self._path and self._section and self._saved_entry is not None):
            return
        root = load_from_json(self._path)
        classes = root.setdefault(self._section, {}).setdefault('classes', {})
        new_classes = {}
        inserted = False
        if self._saved_index is None:
            new_classes.update(classes)
            new_classes[self.ent_id] = self._saved_entry
        else:
            for i, (k, v) in enumerate(classes.items()):
                if i == self._saved_index:
                    new_classes[self.ent_id] = self._saved_entry
                    inserted = True
                new_classes[k] = v
            if not inserted:
                new_classes[self.ent_id] = self._saved_entry
        root.setdefault(self._section, {})['classes'] = new_classes
        if self._section == 'players':
            try:
                if self._saved_default is None:
                    root.pop('DEFAULT_CLASS', None)
                else:
                    root['DEFAULT_CLASS'] = self._saved_default
            except Exception:
                pass
        with open(self._path, 'w', encoding='utf-8') as f:
            json.dump(root, f, ensure_ascii=False, indent=2)

    def _restore_in_memory(self) -> None:
        is_player = (self._section == 'players')
        if is_player:
            stats = copy.deepcopy(self._saved_entry.get('stats', {}))
            target = self.controller.model.player_stats
        else:
            target = self.controller.model.monsters
        new_order = {}
        items = list(target.items())
        if self._saved_index is None:
            new_order.update(target)
            if is_player:
                new_order[self.ent_id] = stats
            else:
                new_order[self.ent_id] = copy.deepcopy(self._saved_entry)
        else:
            inserted = False
            for i, (k, v) in enumerate(items):
                if i == self._saved_index:
                    if is_player:
                        new_order[self.ent_id] = stats
                    else:
                        new_order[self.ent_id] = copy.deepcopy(self._saved_entry)
                    inserted = True
                new_order[k] = v
            if not inserted:
                if is_player:
                    new_order[self.ent_id] = stats
                else:
                    new_order[self.ent_id] = copy.deepcopy(self._saved_entry)
        target.clear()
        target.update(new_order)
        try:
            editor_model = self.controller.editor_controller.model
            if is_player:
                pass
            else:
                if self._section == 'neutrals':
                    editor_model.neutrals[self.ent_id] = copy.deepcopy(self._saved_entry)
                else:
                    editor_model.hostiles[self.ent_id] = copy.deepcopy(self._saved_entry)
        except Exception:
            pass
        if is_player:
            try:
                self.controller.model.player_assets[self.ent_id] = copy.deepcopy(self._saved_entry.get('assets', {}))
            except Exception:
                pass
            try:
                classes = self.controller.editor_controller.model.classes
                classes[self.ent_id] = copy.deepcopy(self._saved_entry)
            except Exception:
                pass
        try:
            assets_map = self.controller.editor_controller.model.assets
            if is_player:
                try:
                    cfg = self.controller.editor_controller.model.classes.get(self.ent_id, {})
                    idle_list = cfg.get('assets', {}).get('sets', {}).get('sprites_set', {}).get('idle', [])
                    if idle_list:
                        path = idle_list[0]
                        orig_size = tuple(self.controller.editor_controller.model.orig_size)
                        frames = load_sprite_sheet(path, orig_size, columns=1)
                        assets_map[self.ent_id] = frames[0]
                    else:
                        asset_info = PLAYER_ASSETS.get(self.ent_id)
                        path = None
                        if isinstance(asset_info, str):
                            path = asset_info
                        elif isinstance(asset_info, dict):
                            path = next(iter(asset_info.values()), None)
                        if path:
                            frames = load_sprite_sheet(path, tuple(self.controller.editor_controller.model.orig_size), columns=1)
                            assets_map[self.ent_id] = frames[0]
                        else:
                            try:
                                assets_map[self.ent_id] = load_image(f"assets/npc/player/{self.ent_id}/{self.ent_id}_1_down.png")
                            except Exception:
                                pass
                except Exception:
                    pass
            else:
                try:
                    sprite, _ = create_sprite_component(self.ent_id)
                    assets_map[self.ent_id] = sprite.image
                except Exception:
                    pass
        except Exception:
            pass
        if not is_player:
            try:
                reload_monster_defs()
                monster_cache.load_caches_for([self.ent_id])
            except Exception:
                pass
        try:
            self.controller.editor_controller.render(self.controller.editor_controller.game.screen)
        except Exception:
            pass

    def undo(self) -> None:
        if self._saved_entry is None:
            return
        self._persist_restore()
        self._restore_in_memory()
