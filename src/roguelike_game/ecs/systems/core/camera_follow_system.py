"""
Module: camera_follow_system.py
ECS system that makes the camera follow the entity tagged with CameraFollowComponent.
Replaces the procedural _step_camera logic formerly in update_manager.py.
"""
import types
import logging

logger = logging.getLogger(__name__)


class CameraFollowSystem:
    """
    Reads CameraFollowComponent + Position on the player entity and centres
    the camera, respecting editor suppression flags stored on world.state.
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    # ------------------------------------------------------------------
    # Public API (called by ECSWorld.update)
    # ------------------------------------------------------------------
    def update(self, world, camera):
        comps = world.components
        cfg_map = comps.get('CameraFollowComponent', {})
        pos_map = comps.get('Position', {})

        for eid in list(cfg_map):
            cfg = cfg_map.get(eid)
            if cfg is None or not cfg.enabled:
                continue

            # --- Defer countdown (e.g. after MMB pan) ---
            if cfg.defer_follow_frames > 0:
                cfg.defer_follow_frames -= 1
                continue

            # --- Editor / overlay suppression checks ---
            if self._is_suppressed(world):
                continue

            # --- Follow the entity ---
            pos = pos_map.get(eid)
            if pos is not None:
                camera.update(types.SimpleNamespace(x=pos.x, y=pos.y))

    # ------------------------------------------------------------------
    # Suppression helpers — read-only checks against world.state / config
    # ------------------------------------------------------------------
    @staticmethod
    def _is_suppressed(world) -> bool:
        """Return True when the camera should NOT follow the player."""
        state = getattr(world, 'state', None)
        if state is None:
            return False

        # Global defer_follow_frames on state (set by mouse.py MMB release)
        try:
            defer = int(getattr(state, 'defer_follow_frames', 0) or 0)
            if defer > 0:
                state.defer_follow_frames = defer - 1
                return True
        except Exception:
            pass

        # Particles Editor visible → keep camera where MMB released
        try:
            if bool(getattr(state, 'particles_editor_visible', False)):
                return True
        except Exception:
            pass

        # Map Editor defer frames
        try:
            me_state = getattr(state, '_map_editor_state', None)
            if me_state is None:
                # Fallback: check via world reference if available
                me_state = getattr(getattr(world, '_map_editor', None), 'editor_state', None)
            if me_state is not None and getattr(me_state, 'defer_follow_frames', 0) > 0:
                me_state.defer_follow_frames -= 1
                return True
        except Exception:
            pass

        # Item Editor hold-focus or visible
        try:
            ie_model = getattr(state, '_item_editor_model', None)
            if ie_model is not None:
                if getattr(ie_model, 'holding_pos_focus', False):
                    return True
                if getattr(ie_model, 'visible', False):
                    return True
        except Exception:
            pass

        # Debug overlays that freeze camera
        try:
            import roguelike_engine.config.config as cfg
            if bool(getattr(cfg, 'DEBUG_SPAWNER', False)):
                return True
            if bool(getattr(cfg, 'DEBUG_ENTITIES', False)):
                return True
        except Exception:
            pass

        # MMB panning in progress
        try:
            if getattr(state, 'mmb_panning', False):
                return True
        except Exception:
            pass

        # Spawner Editor hold-focus
        try:
            if getattr(state, 'spawner_hold_focus', False):
                return True
        except Exception:
            pass

        return False
