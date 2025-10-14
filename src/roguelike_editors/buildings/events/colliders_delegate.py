def handle_colliders(owner, ev, camera, buildings) -> bool:
    """Delegate to colliders panel if active. Returns True if consumed."""
    try:
        colliders = getattr(owner, "colliders", None)
        if colliders and colliders.is_active() and colliders.handle_event(ev, camera, buildings):
            return True
    except Exception:
        pass
    return False
