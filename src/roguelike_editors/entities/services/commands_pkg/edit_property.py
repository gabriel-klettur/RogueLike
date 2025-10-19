from __future__ import annotations
import copy
from dataclasses import dataclass
from typing import Any

from roguelike_editors.entities.services.history import Command
from roguelike_editors.entities.services import commands as cmd_mod
from roguelike_game.factories.monster import cache as monster_cache


@dataclass
class EditPropertyCommand(Command):
    controller: Any
    ent_id: str
    key: str
    new_text: str
    description: str = "Edit property"
    _old_value: Any = None
    _new_value: Any = None

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

    def _write_value(self, value: Any) -> None:
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

    def apply(self) -> None:
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
