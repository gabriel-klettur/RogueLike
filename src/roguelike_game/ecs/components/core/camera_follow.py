"""
Module: camera_follow.py
Componente que identifica la entidad que la cámara debe seguir.
"""


class CameraFollowComponent:
    """
    Marks the entity the camera should track and stores runtime follow state.

    Attributes:
        enabled:              Master toggle — when False the camera ignores this entity.
        defer_follow_frames:  Countdown of frames to skip follow (e.g. after MMB pan).
    """

    def __init__(self, enabled: bool = True, defer_follow_frames: int = 0):
        self.enabled = enabled
        self.defer_follow_frames = defer_follow_frames
# Path: src/roguelike_game/ecs/components/core/camera_follow.py