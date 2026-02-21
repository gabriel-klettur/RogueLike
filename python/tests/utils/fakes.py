from __future__ import annotations

from typing import Any, Dict


class FakeWorld:
    """Minimal ECS World stub for systems tests.

    Provides the subset of API used by meteor_shower and meteor_fall systems.
    """

    def __init__(self) -> None:
        self.components: Dict[str, Dict[int, Any]] = {}
        self._next_eid: int = 1
        self._frame_count: int = 0

    def tick_frame(self) -> None:
        """Increment frame counter. Call before each system update in tests."""
        self._frame_count += 1

    def create_entity(self) -> int:
        eid = self._next_eid
        self._next_eid += 1
        return eid

    def remove_entity(self, eid: int) -> None:
        # Remove entity id from all component maps.
        # Some component maps are dicts (entity_id -> component), others may be lists (event queues).
        # Be tolerant and handle both without raising.
        for name, cmap in list(self.components.items()):
            try:
                if isinstance(cmap, dict):
                    # Direct entity mapping
                    cmap.pop(eid, None)
                elif isinstance(cmap, list):
                    # Remove any occurrences of eid in lists (usually no-ops)
                    try:
                        while True:
                            cmap.remove(eid)
                    except ValueError:
                        pass
            except Exception:
                # Never break tests due to test-double cleanup
                pass

    def get_entities_with(self, *component_names: str) -> list[int]:
        """Return entity ids that have ALL the requested components.

        Minimal helper to support systems that need to iterate over entities with
        certain components (e.g., Position & Health). It intersects the sets of
        eids present in each requested component map.
        """
        if not component_names:
            return []
        sets = []
        for name in component_names:
            cmap = self.components.get(name, {})
            if not isinstance(cmap, dict):
                cmap = {}
            sets.append(set(cmap.keys()))
        if not sets:
            return []
        common = sets[0]
        for s in sets[1:]:
            common = common & s
            if not common:
                break
        return list(common)


class FakeCamera:
    def __init__(self, zoom: float = 1.0):
        self.zoom = float(zoom)
        self.offset_x = 0.0
        self.offset_y = 0.0

    def apply(self, pos: tuple[float, float]) -> tuple[float, float]:
        x, y = pos
        return (x, y)

    def scale(self, size: tuple[float, float]) -> tuple[float, float]:
        w, h = size
        return (w, h)
