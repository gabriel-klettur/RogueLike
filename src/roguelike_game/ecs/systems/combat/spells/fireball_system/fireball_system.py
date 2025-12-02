"""Fireball projectile system with modular collision handling."""
from __future__ import annotations

import logging
from typing import Iterable

from roguelike_engine.utils.benchmark.benchmark import benchmark

from .collisions.buildings import handle_building_collision
from .collisions.tiles import handle_tile_collisions
from .collisions.units import apply_combat_effects, handle_unit_collisions
from .collisions.walls import handle_wall_collision, precompute_wall_cache, WallCacheEntry
from .mask_cache import CircleMaskCache
from .runtime import advance, build_runtime, compute_sampling, exceeds_range

logger = logging.getLogger(__name__)


class FireballSystem:
    """Updates fireball projectiles: movement, collisions, and removal."""

    def __init__(self, perf_log) -> None:
        self.perf_log = perf_log
        self._mask_cache = CircleMaskCache()
        self._debug_count_logged = False

    @benchmark(lambda self: self.perf_log, "fireball_update")
    def update(self, world, camera=None) -> None:  # pragma: no cover - benchmark wrapper
        """Advance all fireball projectiles for one tick."""

        fireballs = world.components.get("FireballComponent", {})
        if not fireballs:
            return

        self._log_projectile_count_once(len(fireballs))
        walls_cache = precompute_wall_cache(world)

        for entity_id in list(fireballs.keys()):
            runtime = build_runtime(world, entity_id)
            if runtime is None:
                continue

            if not advance(runtime):
                continue

            if exceeds_range(runtime):
                continue

            compute_sampling(runtime)
            sample_points = runtime.sample_points
            path_aabb = runtime.path_aabb

            if handle_building_collision(runtime, sample_points, path_aabb, self._mask_cache):
                continue

            if handle_wall_collision(runtime, sample_points, path_aabb, walls_cache, self._mask_cache):
                continue

            collision = handle_unit_collisions(runtime, sample_points, self._mask_cache)
            if collision is not None:
                apply_combat_effects(runtime, collision)
                continue

            handle_tile_collisions(runtime, sample_points, path_aabb)

    def _log_projectile_count_once(self, count: int) -> None:
        if self._debug_count_logged:
            return
        self._debug_count_logged = True
        try:
            logger.debug("[FireballSystem] start update: fireballs=%d", count)
        except Exception:  # pragma: no cover - safety net for logging failures
            pass


__all__ = ["FireballSystem", "WallCacheEntry"]
