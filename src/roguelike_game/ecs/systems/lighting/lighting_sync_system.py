from __future__ import annotations

from typing import Any

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
        if not lights:
            # Nothing to sync this frame; keep any debug lights intact
            return
        pos_store = comps.get('Position', {})
        # Iterate only visible entities to keep per-frame cost low
        try:
            it = world.get_entities_in_camera(camera, 'LightComponent')
        except Exception:
            it = lights.keys()
        for eid in it:
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
                        id=lid,
                    )
                )
                self._last_ids.add(lid)
            except Exception:
                # Be robust against malformed component data
                continue
