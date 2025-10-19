"""
SpawnerDebugRenderSystem: draws spawner anchors and proximity radii for debugging.
"""
from __future__ import annotations
from roguelike_game.ecs.systems.rendering.spawner.spawner_anchor_debug_system import (
    SpawnerAnchorDebugRenderSystem,
)
from roguelike_game.ecs.systems.rendering.spawner.spawner_info_overlay_system import (
    SpawnerInfoOverlaySystem,
)
from roguelike_game.ecs.systems.rendering.spawner.collider_velocity_debug_system import (
    ColliderAndVelocityDebugSystem,
)


class SpawnerDebugRenderSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Compose with split systems; instantiate eagerly to avoid per-frame checks
        self._anchor_sys = SpawnerAnchorDebugRenderSystem(perf_log=self.perf_log)
        self._collider_sys = ColliderAndVelocityDebugSystem(perf_log=self.perf_log)
        self._overlay_sys = SpawnerInfoOverlaySystem(perf_log=self.perf_log)

    def update(self, world, screen, camera):
        """Delegate rendering to sub-systems in a stable overlay order.
        Anchor → colliders/velocity → info overlay.
        """
        try:
            self._anchor_sys.update(world, screen, camera)
        except Exception:
            # Never break the frame on debug overlay failures
            pass
        try:
            self._collider_sys.update(world, screen, camera)
        except Exception:
            pass
        try:
            self._overlay_sys.update(world, screen, camera)
        except Exception:
            pass
