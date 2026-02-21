from __future__ import annotations
import copy
from dataclasses import dataclass
from typing import Any

from roguelike_editors.entities.services.history import Command
from roguelike_editors.entities.services import commands as cmd_mod
from roguelike_game.factories.monster import cache as monster_cache
from roguelike_editors.entities.entities_properties_panel.services.stats_templates import SCALE_FIELDS


def _is_scale_field(key: str) -> bool:
    """Check if the key is a scale_* field that belongs in assets, not stats."""
    return key in SCALE_FIELDS


@dataclass
class EditPropertyCommand(Command):
    controller: Any
    ent_id: str
    key: str
    new_text: str
    description: str = "Edit property"
    _old_value: Any = None
    _new_value: Any = None
    _is_scale: bool = False

    def _get_nested(self, root: dict, dotted: str) -> Any:
        parts = dotted.split('.')
        cur = root
        for i, p in enumerate(parts):
            if not isinstance(cur, dict):
                return None
            if i == len(parts) - 1:
                return cur.get(p)
            cur = cur.get(p, {})

    def _set_nested(self, root: dict, dotted: str, value: Any) -> None:
        parts = dotted.split('.')
        cur = root
        for i, p in enumerate(parts):
            if i == len(parts) - 1:
                cur[p] = value
            else:
                nxt = cur.get(p)
                if not isinstance(nxt, dict):
                    nxt = {}
                    cur[p] = nxt
                cur = nxt

    def _write_scale_value(self, value: Any) -> None:
        """Write scale_* field to assets and update sprites in real-time."""
        path, data, entry = cmd_mod.load_entity_data(self.ent_id, self.controller.model.player_stats, self.controller.model.monsters)
        is_player = self.ent_id in self.controller.model.player_stats
        
        # Update assets in entry
        assets = entry.setdefault('assets', {})
        active_set = assets.get('active_set', 'sets' if is_player else 'no-sets')
        
        if active_set == 'sets':
            meta = assets.setdefault('sets', {}).setdefault('sprites_data_set', {})
        else:
            meta = assets.setdefault('no-sets', {}).setdefault('sprites_data_no-set', {})
        
        meta[self.key] = value
        
        # Persist to JSON
        cmd_mod.save_entity_data(self.ent_id, entry, path, self.controller.model.player_stats, self.controller.model.monsters)
        
        # Update in-memory model
        if is_player:
            mem_assets = self.controller.model.player_assets.setdefault(self.ent_id, {})
            mem_active = mem_assets.get('active_set', 'sets')
            if mem_active == 'sets':
                mem_meta = mem_assets.setdefault('sets', {}).setdefault('sprites_data_set', {})
            else:
                mem_meta = mem_assets.setdefault('no-sets', {}).setdefault('sprites_data_no-set', {})
            mem_meta[self.key] = value
            # Reload player sprites with new scale
            ecs_world = self.controller.editor_controller.game.ecs.ecs_world
            cmd_mod.update_player_assets(ecs_world, self.ent_id)
        else:
            m = self.controller.model.monsters.setdefault(self.ent_id, {})
            mem_assets = m.setdefault('assets', {})
            mem_active = mem_assets.get('active_set', 'no-sets')
            if mem_active == 'sets':
                mem_meta = mem_assets.setdefault('sets', {}).setdefault('sprites_data_set', {})
            else:
                mem_meta = mem_assets.setdefault('no-sets', {}).setdefault('sprites_data_no-set', {})
            mem_meta[self.key] = value
            # Reload monster sprites with new scale
            try:
                from roguelike_game.factories.monster.config import reload_monster_defs as _reload_defs
                _reload_defs()
            except Exception:
                pass
            monster_cache._loaded_variants.discard(self.ent_id)
            monster_cache._SPRITE_SURFACES.pop(self.ent_id, None)
            monster_cache._DEATH_SURFACES.pop(self.ent_id, None)
            ecs_world = self.controller.editor_controller.game.ecs.ecs_world
            cmd_mod.update_monster_assets(ecs_world, self.ent_id)

    def _write_value(self, value: Any) -> None:
        # Handle scale fields separately (they go to assets, not stats)
        if _is_scale_field(self.key):
            self._write_scale_value(value)
            return
            
        path, data, entry = cmd_mod.load_entity_data(self.ent_id, self.controller.model.player_stats, self.controller.model.monsters)
        if self.ent_id in self.controller.model.player_stats:
            stats = entry.setdefault('stats', {})
            if '.' in self.key:
                self._set_nested(stats, self.key, value)
            else:
                stats[self.key] = value
            cmd_mod.save_entity_data(self.ent_id, entry, path, self.controller.model.player_stats, self.controller.model.monsters)
            dst = self.controller.model.player_stats.setdefault(self.ent_id, {})
            if '.' in self.key:
                self._set_nested(dst, self.key, value)
            else:
                dst[self.key] = value
            ecs_world = self.controller.editor_controller.game.ecs.ecs_world
            cmd_mod.update_player_stats(ecs_world, self.ent_id, self.key, value)
        else:
            stats = entry.setdefault('stats', {})
            if '.' in self.key:
                self._set_nested(stats, self.key, value)
            else:
                stats[self.key] = value
            cmd_mod.save_entity_data(self.ent_id, entry, path, self.controller.model.player_stats, self.controller.model.monsters)
            m = self.controller.model.monsters.setdefault(self.ent_id, {})
            dst = m.setdefault('stats', {})
            if '.' in self.key:
                self._set_nested(dst, self.key, value)
            else:
                dst[self.key] = value
            try:
                from roguelike_editors.entities.entities_properties_panel import entities_properties_panel_controller as epc_mod
                epc_mod.reload_monster_defs()
            except Exception:
                try:
                    from roguelike_game.factories.monster.config import reload_monster_defs as _reload_defs
                    _reload_defs()
                except Exception:
                    pass
            monster_cache._loaded_variants.discard(self.ent_id)
            monster_cache._SPRITE_SURFACES.pop(self.ent_id, None)
            monster_cache._DEATH_SURFACES.pop(self.ent_id, None)
            ecs_world = self.controller.editor_controller.game.ecs.ecs_world
            cmd_mod.update_monster_stats(ecs_world, self.ent_id, self.key, value)

    def _get_scale_old_value(self) -> Any:
        """Get the current scale value from assets."""
        path, data, entry = cmd_mod.load_entity_data(self.ent_id, self.controller.model.player_stats, self.controller.model.monsters)
        is_player = self.ent_id in self.controller.model.player_stats
        
        assets = entry.get('assets', {})
        active_set = assets.get('active_set', 'sets' if is_player else 'no-sets')
        
        if active_set == 'sets':
            meta = assets.get('sets', {}).get('sprites_data_set', {})
        else:
            meta = assets.get('no-sets', {}).get('sprites_data_no-set', {})
        
        return meta.get(self.key, 0.5)

    def apply(self) -> None:
        self._is_scale = _is_scale_field(self.key)
        
        if self._is_scale:
            self._old_value = copy.deepcopy(self._get_scale_old_value())
        else:
            path, data, entry = cmd_mod.load_entity_data(self.ent_id, self.controller.model.player_stats, self.controller.model.monsters)
            stats = entry.setdefault('stats', {})
            if '.' in self.key:
                self._old_value = copy.deepcopy(self._get_nested(stats, self.key))
            else:
                self._old_value = copy.deepcopy(stats.get(self.key))
        
        self._new_value = cmd_mod.convert_value(self.new_text, self._old_value)
        self._write_value(self._new_value)
        self.controller._reset_edit_state()

    def undo(self) -> None:
        self._write_value(self._old_value)
