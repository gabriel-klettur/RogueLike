"""
SpawnerRuntimeSystem: schedules spawn requests based on SpawnerConfig/State.
MVP supports:
- trigger: proximity (driven by SpawnerTriggerSystem setting state.started)
- policy: periodic with cooldown_s -> cooldown_frames
- waves[0].spawns: list of entries { kind: "monster", id: str, count: int, spread_radius: int }
"""
from __future__ import annotations

from .spawner_runtime import SpawnerRuntimeSystem

__all__ = ["SpawnerRuntimeSystem"]
