from __future__ import annotations

from typing import Any
import pygame

from roguelike_engine.rendering.lighting import get_global_lighting
from roguelike_engine.rendering.lighting.light_types import Light


class LightingSyncSystem:
    """ECS update system that mirrors LightComponent entities into the LightingManager.

    Notes:
    - Runs in update phase; effect is visible next frame (pipeline composes lightmap earlier).
    - Uses world.get_entities_in_camera to avoid pushing far lights.
    - LightingManager enforces limits/quality and composes a low-res lightmap.
    """

    def __init__(self, perf_log: dict | None = None) -> None:
        self.perf_log = perf_log
        # Track last-synced ECS light ids to remove them without touching debug lights
        self._last_ids: set[str] = set()
        # Stagger state for ECS lights
        self._stagger_targets: list[int] = []  # entity ids ordered
        self._stagger_cursor: int = 0
        self._stagger_next_ms: int = 0
        self._stagger_interval_ms: int = 3000

    def update(self, world: Any, camera: Any) -> None:
        comps = world.components
        lights = comps.get('LightComponent', {})
        lm = get_global_lighting()
        # Remove previously synced ECS lights (preserve debug lights spawned manually)
        if self._last_ids:
            try:
                for lid in list(self._last_ids):
                    lm.remove_by_id(lid)
            except Exception:
                pass
            self._last_ids.clear()
        # Daytime optimization: skip syncing during configured disable window to save CPU
        try:
            from roguelike_engine.rendering.lighting.daynight import get_global_daynight
            dn = get_global_daynight()
            if dn.is_lights_disabled_now():
                # Reset stagger so it restarts cleanly when night returns
                self._stagger_targets = []
                self._stagger_cursor = 0
                self._stagger_next_ms = pygame.time.get_ticks()
                return
        except Exception:
            pass
        if not lights:
            # Nothing to sync this frame; keep any debug lights intact
            return
        pos_store = comps.get('Position', {})
        # Determine visible ECS light entities (ids)
        try:
            visible_iter = world.get_entities_in_camera(camera, 'LightComponent')
        except Exception:
            visible_iter = lights.keys()
        visible_eids = [int(eid) for eid in visible_iter if eid in lights]
        # Rebuild stagger targets if empty or composition changed
        need_rebuild = False
        if not self._stagger_targets:
            need_rebuild = True
        else:
            try:
                if set(self._stagger_targets) != set(visible_eids):
                    need_rebuild = True
            except Exception:
                need_rebuild = True
        # Pull config for order and interval
        order_desc = False
        try:
            from roguelike_engine.rendering.lighting.daynight import get_global_daynight
            dn = get_global_daynight()
            order_desc = (dn.get_lights_stagger_order() == 'desc')
            self._stagger_interval_ms = int(dn.get_lights_stagger_interval_ms())
        except Exception:
            pass
        if need_rebuild:
            try:
                visible_eids.sort(reverse=order_desc)
            except Exception:
                pass
            self._stagger_targets = visible_eids
            self._stagger_cursor = 0
            self._stagger_next_ms = pygame.time.get_ticks()
        # Advance cursor over time
        now = pygame.time.get_ticks()
        if self._stagger_interval_ms <= 0:
            self._stagger_cursor = len(self._stagger_targets)
        else:
            while self._stagger_cursor < len(self._stagger_targets) and now >= self._stagger_next_ms:
                self._stagger_cursor += 1
                self._stagger_next_ms += self._stagger_interval_ms
        # Add up to current cursor lights
        limit = min(self._stagger_cursor, len(self._stagger_targets))
        for i in range(limit):
            eid = self._stagger_targets[i]
            lc = lights.get(eid)
            if lc is None or not getattr(lc, 'enabled', True):
                continue
            p = pos_store.get(eid)
            if p is None:
                continue
            try:
                lid = f"ecs:{eid}"
                lm.add(
                    Light(
                        x=float(getattr(p, 'x', 0.0)),
                        y=float(getattr(p, 'y', 0.0)),
                        radius=int(getattr(lc, 'radius', 160)),
                        color=tuple(getattr(lc, 'color', (255, 200, 140))),
                        intensity=float(getattr(lc, 'intensity', 1.0)),
                        falloff=float(getattr(lc, 'falloff', 2.0)),
                        enabled=bool(getattr(lc, 'enabled', True)),
                        flicker_amp=float(getattr(lc, 'flicker_amp', 0.0)),
                        flicker_speed=float(getattr(lc, 'flicker_speed', 2.3)),
                        center_scale=float(getattr(lc, 'center_scale', 1.0)),
                        id=lid,
                    )
                )
                self._last_ids.add(lid)
            except Exception:
                # Be robust against malformed component data
                continue
