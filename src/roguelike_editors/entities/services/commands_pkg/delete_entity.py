from __future__ import annotations
from dataclasses import dataclass
from typing import Any

from roguelike_editors.entities.services.history import Command
from roguelike_editors.entities.services.ecs_snapshot import snapshot_entity, restore_entity


@dataclass
class DeleteEntityCommand(Command):
    controller: Any
    eid: int
    description: str = "Delete entity"
    _snapshot: Any = None

    def apply(self) -> None:
        world = self.controller.game.ecs.ecs_world
        self._snapshot = snapshot_entity(world, self.eid)
        world.remove_entity(self.eid)
        if hasattr(world, 'invalidate_spatial_index'):
            world.invalidate_spatial_index()

    def undo(self) -> None:
        world = self.controller.game.ecs.ecs_world
        if self._snapshot is not None:
            restore_entity(world, self._snapshot)
            if hasattr(world, 'invalidate_spatial_index'):
                world.invalidate_spatial_index()
