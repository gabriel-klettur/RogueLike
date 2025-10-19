from __future__ import annotations
from dataclasses import dataclass
from typing import Any, Optional

from roguelike_editors.entities.services.history import Command
from roguelike_editors.entities.services.spawn_services import spawn_entity


@dataclass
class SpawnEntityCommand(Command):
    controller: Any
    etype: str
    tx: int
    ty: int
    description: str = "Spawn entity"
    eid: Optional[int] = None

    def apply(self) -> None:
        game = self.controller.game
        eid = spawn_entity(game, self.etype, self.tx, self.ty, self.controller.model.player_stats)
        self.eid = eid
        world = game.ecs.ecs_world
        if hasattr(world, 'invalidate_spatial_index'):
            world.invalidate_spatial_index()

    def undo(self) -> None:
        if self.eid is None:
            return
        world = self.controller.game.ecs.ecs_world
        world.remove_entity(self.eid)
        if hasattr(world, 'invalidate_spatial_index'):
            world.invalidate_spatial_index()
