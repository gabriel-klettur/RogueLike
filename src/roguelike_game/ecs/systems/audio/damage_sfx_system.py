"""ECS system that emits damage-received SFX whenever an entity's HP drops.

Design rationale — *Observer via polling* (*delta detection*):
  Instead of patching every damage-dealing system (hitbox, fireball, burn,
  poison, chain lightning, meteor, dash, mine, puddle, boomerang, totem,
  melee …), this system snapshots HP each frame and detects decreases.
  Any source of damage — present or future — automatically triggers the
  correct SFX without a single line of code in the damage dealer.

  - *Separation of concerns*: damage systems stay focused on damage math;
    audio logic lives here exclusively.
  - *Open/Closed*: new damage sources require zero changes.
  - *Single Responsibility*: one system, one job — react to HP drops with
    audio events.

Runs once per frame, right before AudioSystem, so queued events are
consumed in the same tick.
"""

from __future__ import annotations

import logging
from typing import Any, Dict

from roguelike_game.ecs.systems.combat.combat_sfx import (
    PLAYER_DAMAGE_CHOICES,
    resolve_npc_damage_choices,
)

logger = logging.getLogger(__name__)


class DamageSfxSystem:
    """Emit ``play_sfx`` audio events whenever an entity's HP decreases."""

    def __init__(self, perf_log: Any = None) -> None:
        self.perf_log = perf_log
        # entity_id → last known current_hp
        self._prev_hp: Dict[int, int] = {}

    def update(self, world: Any, **kwargs: Any) -> None:
        hp_map = world.components.get("Health", {})
        player_tags = world.components.get("PlayerTagComponent", {})
        archetype_map = world.components.get("MonsterArchetype", {})

        # Collect entities whose HP dropped this frame
        for eid, health in hp_map.items():
            cur = getattr(health, "current_hp", None)
            if cur is None:
                continue
            prev = self._prev_hp.get(eid)
            # First frame we see this entity — just record, no sound
            if prev is None:
                self._prev_hp[eid] = int(cur)
                continue
            # HP did not decrease — update snapshot and skip
            if int(cur) >= prev:
                self._prev_hp[eid] = int(cur)
                continue
            # HP decreased — determine which SFX to play
            self._prev_hp[eid] = int(cur)
            try:
                if eid in player_tags:
                    self._enqueue(world, PLAYER_DAMAGE_CHOICES)
                else:
                    archetype = archetype_map.get(eid)
                    npc_type = getattr(archetype, "type", None)
                    choices = resolve_npc_damage_choices(npc_type)
                    if choices:
                        self._enqueue(world, choices)
            except Exception:
                pass

        # Prune removed entities to avoid unbounded memory growth
        live = set(hp_map.keys())
        stale = [k for k in self._prev_hp if k not in live]
        for k in stale:
            del self._prev_hp[k]

    @staticmethod
    def _enqueue(world: Any, choices: list[str]) -> None:
        aq = world.components.setdefault("AudioEventQueue", [])
        aq.append({"type": "play_sfx", "choices": choices, "group": "sfx"})
