from __future__ import annotations
from dataclasses import dataclass
from typing import Any, Tuple

from roguelike_game.ecs.utils.position_utils import compute_foot_tile
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_editors.entities.services.history import Command


@dataclass
class MoveEntityCommand(Command):
    controller: Any
    eid: int
    start_pos: Tuple[int, int]
    end_pos: Tuple[int, int]
    description: str = "Move entity"

    def _apply_runtime(self, pos_xy: Tuple[int, int]) -> None:
        world = self.controller.game.ecs.ecs_world
        pos_store = world.components.get('Position', {})
        pos = pos_store.get(self.eid)
        if pos is None:
            return
        pos.x, pos.y = int(pos_xy[0]), int(pos_xy[1])
        if hasattr(world, 'invalidate_spatial_index'):
            world.invalidate_spatial_index()

    def _persist_npc_tile(self) -> None:
        g = self.controller.game
        world = g.ecs.ecs_world
        comps = world.components
        npc_tags = comps.get('NPCTagComponent', {}) or {}
        if self.eid not in npc_tags:
            return
        inst_store = comps.get('MonsterInstanceComponent', {}) or {}
        inst = inst_store.get(self.eid)
        if inst is None:
            return
        instance_id = getattr(inst, 'instance_id', None)
        if not instance_id:
            return
        try:
            tile = compute_foot_tile(world, self.eid, TILE_SIZE)
        except Exception:
            tile = None
        level = getattr(g.world, 'current_level', None) or getattr(g.map, 'name', None)
        if not level:
            return
        if getattr(g.world, 'npc_memory', None) is None:
            g.world.npc_memory = {}
        entry = dict(getattr(g.world, 'npc_memory', {}).get(str(instance_id), {}) or {})
        entry.update({
            'level': level,
            'tile': [int(tile[0]), int(tile[1])] if tile is not None else None,
        })
        g.world.npc_memory[str(instance_id)] = entry
        try:
            mgr = getattr(g, 'map', None)
            ls = getattr(mgr, '_local_state', None)
            if isinstance(ls, dict):
                states = dict(ls.get('npc_states', {}) or {})
                states[str(instance_id)] = {
                    'level': level,
                    'tile': entry.get('tile'),
                    'hp': (states.get(str(instance_id), {}) or {}).get('hp'),
                    'dead': (states.get(str(instance_id), {}) or {}).get('dead'),
                    'prototype': (states.get(str(instance_id), {}) or {}).get('prototype'),
                }
                ls['npc_states'] = states
        except Exception:
            pass

    def apply(self) -> None:
        self._apply_runtime(self.end_pos)
        self._persist_npc_tile()

    def undo(self) -> None:
        self._apply_runtime(self.start_pos)
        self._persist_npc_tile()
