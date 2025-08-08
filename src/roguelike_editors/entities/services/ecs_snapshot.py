from __future__ import annotations
from typing import Any, Dict, Tuple
import copy

class EntitySnapshot:
    def __init__(self, eid: int, components: Dict[str, Any]):
        self.eid = eid
        self.components = components  # component_name -> component_instance (deepcopied)


def snapshot_entity(world, eid: int) -> EntitySnapshot:
    """
    Take a deep snapshot of the given entity's components.
    """
    comps = world.components
    captured: Dict[str, Any] = {}
    for cname, store in comps.items():
        if eid in store:
            captured[cname] = copy.deepcopy(store[eid])
    return EntitySnapshot(eid, captured)


def restore_entity(world, snap: EntitySnapshot) -> int:
    """
    Restore an entity from snapshot, trying to preserve the original ID.
    If the ID is free, reuse it. Otherwise, allocate a new ID via create_entity().
    Returns the entity id used.
    """
    eid = snap.eid
    if eid not in world.entities:
        # Ensure next_entity_id is above eid so future create_entity() won't collide
        if eid >= world.next_entity_id:
            world.next_entity_id = eid + 1
        world.entities.append(eid)
    # Write components back
    for cname, cmp in snap.components.items():
        store = world.components.get(cname)
        if store is None:
            continue
        store[eid] = copy.deepcopy(cmp)
    # Spatial index may need rebuild
    if hasattr(world, 'invalidate_spatial_index'):
        world.invalidate_spatial_index()
    return eid
