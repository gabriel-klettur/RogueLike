from __future__ import annotations
from dataclasses import dataclass
from typing import Any, Optional

from roguelike_editors.entities.services.history import Command
from roguelike_editors.entities.services import commands as cmd_mod


@dataclass
class ToggleActiveSetCommand(Command):
    controller: Any
    ent_id: str
    description: str = "Toggle active asset set"
    _old_active: Optional[str] = None

    def _set_active(self, target: str) -> None:
        path, data, entry = cmd_mod.load_entity_data(self.ent_id, self.controller.model.player_stats, self.controller.model.monsters)
        assets = entry.setdefault('assets', {})
        assets['active_set'] = target
        cmd_mod.save_entity_data(self.ent_id, entry, path, self.controller.model.player_stats, self.controller.model.monsters)
        if self.ent_id in self.controller.model.player_stats:
            self.controller.model.player_assets.setdefault(self.ent_id, {})['active_set'] = target
        else:
            self.controller.model.monsters.setdefault(self.ent_id, {}).setdefault('assets', {})['active_set'] = target
        self.controller._on_active_set_toggled(self.ent_id)

    def apply(self) -> None:
        path, data, entry = cmd_mod.load_entity_data(self.ent_id, self.controller.model.player_stats, self.controller.model.monsters)
        assets = entry.setdefault('assets', {})
        default_active = 'sets' if self.ent_id in self.controller.model.player_stats else 'no-sets'
        curr = assets.get('active_set', default_active)
        self._old_active = curr
        new_val = 'no-sets' if curr == 'sets' else 'sets'
        self._set_active(new_val)

    def undo(self) -> None:
        if self._old_active is not None:
            self._set_active(self._old_active)
