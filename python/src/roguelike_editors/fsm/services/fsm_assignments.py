"""Assignment helpers for mapping archetypes/eids to FSM set IDs."""
from __future__ import annotations
from typing import Optional

from .fsm_cache import ensure_cache


def assignment_for(archetype: str, eid: Optional[int] = None) -> Optional[str]:
    """Return the set_id assigned to an archetype or entity id, if any."""
    c = ensure_cache()
    by_eid = c.assignments.get("by_eid", {})
    if eid is not None and str(eid) in by_eid:
        return by_eid[str(eid)]
    return c.assignments.get("by_archetype", {}).get(archetype)
