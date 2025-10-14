from __future__ import annotations

from typing import Any, Dict


def model_getstate(self: Any) -> Dict[str, Any]:
    """Serialize `BuildingModel` fields, excluding volatile pygame surfaces.

    Mirrors the legacy behavior so pickles remain compatible.
    """
    return {
        'rel_x': self.rel_x,
        'rel_y': self.rel_y,
        'zone': self.zone,
        'solid': self.solid,
        'image_path': self.image_path,
        'split_ratio': self.split_ratio,
        'z_bottom': self.z_bottom,
        'z_top': self.z_top,
        'collision_map': self._collision_map,
        'original_scale': self.original_scale,
        'collider_scope': self.collider_scope,
        'images_by_state': getattr(self, 'images_by_state', {}) or {},
        'state_thresholds': getattr(self, 'state_thresholds', None),
        'current_visual_state': getattr(self, 'current_visual_state', None),
    }


def model_setstate(self: Any, state: Dict[str, Any]) -> None:
    """Restore fields into an existing `BuildingModel` instance.

    Post-condition: image surfaces must be restored by the caller.
    """
    self.rel_x = state['rel_x']
    self.rel_y = state['rel_y']
    self.zone = state.get('zone', None)
    self.solid = state['solid']
    self.image_path = state['image_path']
    self.split_ratio = state['split_ratio']
    self.z_bottom = state['z_bottom']
    self.z_top = state['z_top']
    self.z = self.z_bottom
    self._collision_map = state['collision_map']
    self._collision_tiles_cache = None
    self._collision_tile_objs = None
    self.original_scale = state.get('original_scale')
    self.collider_scope = state.get('collider_scope', 'CG')
    self.images_by_state = state.get('images_by_state', {}) or {}
    self.state_thresholds = state.get('state_thresholds')
    self.current_visual_state = state.get('current_visual_state')
