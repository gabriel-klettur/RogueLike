import logging
from roguelike_engine.utils.benchmark import benchmark

logger = logging.getLogger(__name__)


class BuildingDamageSystem:
    """
    Consumes BuildingDamageEvents published by HitboxSystem and applies damage to
    world buildings. Maintains a per-building health state in world.components['BuildingHealth']
    and updates building visual state based on BuildingModel.state_thresholds.

    An event has the shape: { 'building_key': str, 'damage': int|float }
    The key is matched against Building.spawn_id (preferred) or Building.id.
    """

    DEFAULT_MAX_HP = 100

    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._index: dict[str, object] = {}
        self._indexed_len: int = -1

    def _ensure_index(self, world):
        buildings = getattr(world, 'buildings', []) or []
        if self._indexed_len == len(buildings) and self._index:
            return
        self._index = {}
        for b in buildings:
            try:
                if getattr(b, '_is_spawner_visual', False):
                    continue
                key = getattr(b, 'spawn_id', None) or getattr(b, 'id', None)
                if key is not None:
                    self._index[str(key)] = b
            except Exception:
                continue
        self._indexed_len = len(buildings)

    def _state_from_ratio(self, building, ratio: float) -> str | None:
        """
        Given a ratio in [0..1], choose a state using BuildingModel.state_thresholds.
        thresholds format: [{"state": str, "min_ratio": float}] expected sorted desc.
        We sort defensively.
        """
        try:
            thresholds = getattr(getattr(building, 'model', None), 'state_thresholds', None)
            if not thresholds:
                return None
            # Defensive sort (descending by min_ratio)
            ts = sorted(thresholds, key=lambda t: float(t.get('min_ratio', 0.0)), reverse=True)
            for t in ts:
                st = t.get('state')
                mr = float(t.get('min_ratio', 0.0))
                if ratio >= mr:
                    return st
        except Exception:
            return None
        return None

    @benchmark(lambda self: self.perf_log, "4.7b. BuildingDamageSystem.update")
    def update(self, world, camera=None):
        events = world.components.get('BuildingDamageEvents')
        if not events:
            return
        self._ensure_index(world)
        bh_map = world.components.setdefault('BuildingHealth', {})

        # Process events and then clear
        for evt in list(events):
            try:
                key = str(evt.get('building_key'))
                dmg = float(evt.get('damage', 0))
            except Exception:
                continue
            if not key:
                continue
            building = self._index.get(key)
            if building is None:
                # Unknown key; skip silently
                continue
            # Initialize health if first time
            state = bh_map.get(key)
            if state is None:
                max_hp = getattr(building, 'max_hp', None)
                try:
                    max_hp = int(max_hp) if max_hp is not None else self.DEFAULT_MAX_HP
                except Exception:
                    max_hp = self.DEFAULT_MAX_HP
                state = {'current_hp': max_hp, 'max_hp': max_hp}
                bh_map[key] = state
            # Apply damage
            cur = float(state.get('current_hp', state.get('max_hp', self.DEFAULT_MAX_HP)))
            max_hp = float(state.get('max_hp', self.DEFAULT_MAX_HP))
            cur = max(0.0, cur - dmg)
            state['current_hp'] = cur

            # Update visual state by thresholds if configured
            ratio = (cur / max_hp) if max_hp > 0 else 0.0
            new_state = self._state_from_ratio(building, ratio)
            if isinstance(new_state, str) and new_state:
                try:
                    changed = building.set_visual_state(new_state)
                    if changed:
                        # If geometry changed due to a different image/scale, spatial index may need rebuild
                        # We conservatively invalidate the spatial index so collisions update next frame
                        try:
                            world.invalidate_spatial_index()
                        except Exception:
                            pass
                except Exception:
                    # Never break the loop on visual errors
                    pass

            # Optional: building destruction hook (no physics changes by default)
            if cur <= 0:
                # Could add additional effects here (particles, sound) via a future event queue
                pass

        # Clear processed events
        try:
            events.clear()
        except Exception:
            world.components['BuildingDamageEvents'] = []
