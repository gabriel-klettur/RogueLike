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
        # Local imports cached to avoid per-frame import overhead and to clarify deps
        from .markers import MarkerRenderer
        from .sections import (
            debug_fireballs as _dfb,
            debug_hitboxes as _dhb,
            debug_lasers as _dlz,
            debug_auras as _dar,
            consume_debug_events as _cde,
        )
        self._markers = MarkerRenderer()
        # Cache section fns
        self._sec_fireballs = _dfb
        self._sec_hitboxes = _dhb
        self._sec_lasers = _dlz
        self._sec_auras = _dar
        self._sec_consume_events = _cde
        # Dedupe trackers
        self._seen_hitbox_pairs: set[tuple[int, int]] = set()  # (hb_eid, target_eid)
        self._laser_prev_pairs: set[tuple[int, int]] = set()   # (caster, target)

    def update(self, world, screen, camera):
        if not getattr(config, 'DEBUG', False):
            return
        # 1) Fireballs
        self._sec_fireballs(world, screen, camera, self._markers)

        # 2) Hitboxes
        self._seen_hitbox_pairs = self._sec_hitboxes(
            world, screen, camera, self._markers, self._seen_hitbox_pairs
        )

        # 3) Laser beams
        laser_curr_pairs = self._sec_lasers(
            world, screen, camera, self._markers, self._laser_prev_pairs
        )
        self._laser_prev_pairs = laser_curr_pairs

        # 4) Auras
        self._sec_auras(world, screen, camera)

        # 5) Consume queued gameplay debug events
        self._sec_consume_events(world, screen, camera, self._markers)

        # Render persistent markers last
        self._markers.render(screen, camera)
