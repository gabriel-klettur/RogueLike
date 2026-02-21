import math
from typing import Optional


def primary_direction_from_vector(dx: float, dy: float) -> str:
    """Return one of 'left','right','up','down' based on dominant axis."""
    if abs(dx) > abs(dy):
        return 'left' if dx < 0 else 'right'
    else:
        return 'down' if dy > 0 else 'up'


def _resolve_anim_base(entity, state_class_name: str) -> Optional[str]:
    # Read base from FSM context anim_map
    try:
        fsm = entity.world.components.get('NPCState', {}).get(entity.id).fsm
        amap = (getattr(fsm, 'context', {}) or {}).get('anim_map') or {}
        return amap.get(state_class_name)
    except Exception:
        return None


def set_mapped_anim(entity, state_class_name: str, direction: Optional[str] = None, reset_frame: bool = True) -> None:
    """Apply animation based on animation_map.json for given state class.
    If direction is provided, tries '<base>_<direction>' then falls back to just
    'direction' (for idle directional sheets) then '<base>'.
    """
    world = entity.world
    eid = entity.id
    anim = world.components.get('Animator', {}).get(eid)
    if not anim:
        return
    base = _resolve_anim_base(entity, state_class_name)
    if not base:
        return
    # Prefer base+direction key; also support direction+base naming
    keys_to_try = []
    if direction:
        keys_to_try.append(f"{base}_{direction}")
        keys_to_try.append(f"{direction}_{base}")
        keys_to_try.append(direction)  # e.g., idle uses plain directions
    keys_to_try.append(base)
    for key in keys_to_try:
        if key in getattr(anim, 'animations', {}):
            if anim.current_state != key:
                anim.current_state = key
                if reset_frame and hasattr(anim, 'frame_idx'):
                    anim.frame_idx = 0
            return
    # No available key -> leave current state unchanged
    return


def set_mapped_anim_for(world, eid: int, state_class_name: str, direction: Optional[str] = None, reset_frame: bool = True) -> None:
    """Same as set_mapped_anim, but takes (world, eid) directly.
    Useful for systems that don't have an entity object.
    """
    anim = world.components.get('Animator', {}).get(eid)
    if not anim:
        return
    # Resolve base from FSM context
    try:
        fsm = world.components.get('NPCState', {}).get(eid).fsm
        amap = (getattr(fsm, 'context', {}) or {}).get('anim_map') or {}
        base = amap.get(state_class_name)
    except Exception:
        base = None
    if not base:
        return
    keys_to_try = []
    if direction:
        keys_to_try.append(f"{base}_{direction}")
        keys_to_try.append(f"{direction}_{base}")
        keys_to_try.append(direction)
    keys_to_try.append(base)
    for key in keys_to_try:
        if key in getattr(anim, 'animations', {}):
            if anim.current_state != key:
                anim.current_state = key
                if reset_frame and hasattr(anim, 'frame_idx'):
                    anim.frame_idx = 0
            return
    return
