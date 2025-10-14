import roguelike_engine.config.config as config
 


class SpellCollisionDebugSystem:
    """
    Draws visual debug overlays for spell collisions when DEBUG is enabled (F9).
    Responsibilities orchestrated here, logic delegated to submodules:
      - Fireballs: projectile point, colliding NPC collider highlight, solid tile hit.
      - Hitboxes: highlight already-hit targets.
      - Laser beams: show beam line with thickness and mark intersecting targets.
      - Auras: draw caster radius.
      - Gameplay debug events: persist outlines/markers even if sources were removed.
      - Persistent markers: fade-out overlays rendered last.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        from .markers import MarkerRenderer
        self._markers = MarkerRenderer()
        # Dedupe trackers
        self._seen_hitbox_pairs: set[tuple[int, int]] = set()  # (hb_eid, target_eid)
        self._laser_prev_pairs: set[tuple[int, int]] = set()   # (caster, target)

    def update(self, world, screen, camera):
        if not getattr(config, 'DEBUG', False):
            return
        # Lazy imports to avoid import cycles at startup
        from .sections import (
            debug_fireballs,
            debug_hitboxes,
            debug_lasers,
            debug_auras,
            consume_debug_events,
        )

        # 1) Fireballs
        debug_fireballs(world, screen, camera, self._markers)

        # 2) Hitboxes
        self._seen_hitbox_pairs = debug_hitboxes(
            world, screen, camera, self._markers, self._seen_hitbox_pairs
        )

        # 3) Laser beams
        laser_curr_pairs = debug_lasers(
            world, screen, camera, self._markers, self._laser_prev_pairs
        )
        self._laser_prev_pairs = laser_curr_pairs

        # 4) Auras
        debug_auras(world, screen, camera)

        # 5) Consume queued gameplay debug events
        consume_debug_events(world, screen, camera, self._markers)

        # Render persistent markers last
        self._markers.render(screen, camera)
