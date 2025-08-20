import pygame
from typing import Dict, Any, Tuple
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.physics.multi_collider import MultiCollider
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider
from roguelike_game.ecs.components.physics.circle_collider import CircleCollider
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_engine.config.config_z_layer import Z_LAYERS
from roguelike_game.ecs.components.core.identity import Faction


def calculate_position(tile_x: int, tile_y: int, cfg: Dict[str, Any], sprite) -> Tuple[int, int]:
    """Compute bottom-center pixel coordinates on map tile."""
    # Read asset metadata from JSON
    cfg_assets = cfg.get("assets", {})
    active_set = cfg_assets.get("active_set", "")
    active_assets = cfg_assets.get(active_set, {})
    data_block_key = f"sprites_data_{active_set}"
    data_assets = active_assets.get(data_block_key, {})
    scale_val = data_assets.get("scale", 1.0)
    img = getattr(sprite, 'image', None)
    if not isinstance(img, pygame.Surface):
        img = pygame.Surface((16, 16), pygame.SRCALPHA)
        img.fill((0, 0, 0, 255))
    orig_w, orig_h = img.get_size()
    width = int(orig_w * scale_val)
    height = int(orig_h * scale_val)
    px = tile_x * TILE_SIZE + (TILE_SIZE // 2) - (width // 2)
    py = (tile_y + 1) * TILE_SIZE - height - 1
    return px, py


def create_physics_components(cfg: Dict[str, Any]) -> Tuple[Scale, Velocity]:
    """Create Scale and Velocity ECS components."""
    # Read asset metadata from JSON
    cfg_assets = cfg.get("assets", {})
    active_set = cfg_assets.get("active_set", "")
    active_assets = cfg_assets.get(active_set, {})
    data_block_key = f"sprites_data_{active_set}"
    data_assets = active_assets.get(data_block_key, {})
    scale_val = data_assets.get("scale", 1.0)
    return Scale(scale_val), Velocity(0, 0)


def _auto_bottom_band_metrics(mask: pygame.Mask) -> Tuple[int, int]:
    """Return (auto_center_x, avg_width) on the bottom band using a weighted centroid.
    - auto_center_x: centroid of all opaque pixels in the band, rows near the bottom weigh more.
    - avg_width: weighted average span width per row (kept for radius heuristic).
    Fallbacks to image mid and width 0 if no opaque pixels.
    """
    w, h = mask.get_size()
    if w <= 0 or h <= 0:
        return w // 2, 0
    band_h = max(6, min(max(6, h // 5), 28))
    y_start = h - band_h
    total_weight = 0.0
    sum_x = 0.0
    sum_width = 0.0
    for y in range(h - 1, y_start - 1, -1):
        weight = 1.0 + (y - y_start) * 0.3
        row_count = 0
        # centroid by summing x for opaque pixels
        for x in range(w):
            if mask.get_at((x, y)):
                sum_x += x * weight
                row_count += 1
        if row_count > 0:
            total_weight += weight * row_count
            sum_width += (row_count * weight)
    if total_weight <= 0.0:
        return w // 2, 0
    cx = int(round(sum_x / total_weight))
    # translate width accumulator back to average width per row (approximate)
    # we divide by number of weighted rows; reuse band_h as approximation if needed
    denom = max(1.0, (band_h))
    avg_width = int(round((sum_width / denom)))
    return max(0, min(w - 1, cx)), max(0, avg_width)


def create_collider_components(sprite, cfg: Dict[str, Any]) -> MultiCollider:
    """Construct body and feet colliders based on sprite surface."""
    mask_surf = getattr(sprite, 'image', None)
    if not isinstance(mask_surf, pygame.Surface):
        mask_surf = pygame.Surface((16, 16), pygame.SRCALPHA)
        mask_surf.fill((0, 0, 0, 255))

    # Read asset metadata from JSON
    cfg_assets = cfg.get("assets", {})
    active_set = cfg_assets.get("active_set", "")
    active_assets = cfg_assets.get(active_set, {})
    data_block_key = f"sprites_data_{active_set}"
    data_assets = active_assets.get(data_block_key, {})
    scale_val = data_assets.get("scale", 1.0)
    if scale_val != 1.0:
        w, h = mask_surf.get_size()
        mask_surf = pygame.transform.scale(mask_surf, (int(w*scale_val), int(h*scale_val)))
    mask = pygame.mask.from_surface(mask_surf)
    body = MaskCollider(mask, 0, 0)
    w, h = mask_surf.get_size()
    # Allow config overrides; otherwise use safe defaults for a bottom band
    width_factor = cfg.get("stats", {}).get("feet_width_factor")
    height_factor = cfg.get("stats", {}).get("feet_height_factor")
    if width_factor is None:
        width_factor = 0.45  # ~45% of sprite width centered
    if height_factor is None:
        height_factor = 0.22  # ~22% of sprite height at bottom

    # Compute size with minimum pixel constraints to avoid zero-sized boxes on small sprites
    feet_w = max(8, int(w * float(width_factor)))
    feet_h = max(6, int(h * float(height_factor)))
<<<<<<< Updated upstream
    # Optional overrides from config
    stats = cfg.get("stats", {})
    cfg_radius = stats.get("feet_radius")
    cfg_dx = int(stats.get("feet_center_dx", 0) or 0)
    cfg_dy = int(stats.get("feet_center_dy", 0) or 0)
    # Auto metrics from bottom band of the sprite mask
    auto_cx, band_avg_w = _auto_bottom_band_metrics(mask)
    # Derive circle radius from band width; clamp with heuristic unless overridden
    heuristic_r = max(4, min(feet_w, feet_h) // 2)
    band_r = max(4, band_avg_w // 2) if band_avg_w > 0 else heuristic_r
    radius = int(cfg_radius) if cfg_radius is not None else max(4, min(heuristic_r, band_r))
    # Auto center to detected bottom support; apply per-variant offset from JSON if needed
    center_x = auto_cx + cfg_dx
    center_y = (h - radius - 1) + cfg_dy
=======
    # Derive circle radius from the previous rectangular heuristic
    radius = max(4, min(feet_w, feet_h) // 2)
    # Center at bottom-center of the sprite, resting on the base
    center_x = w // 2
    center_y = h - radius - 1
>>>>>>> Stashed changes
    feet = CircleCollider(radius=radius, offset_x=center_x, offset_y=center_y)
    return MultiCollider({"body": body, "feet": feet})


def create_zlayer_component(cfg: Dict[str, Any]) -> ZLayer:
    """Set the rendering Z-layer for the entity."""
    faction = getattr(Faction, cfg.get("faction"), None)
    return ZLayer(Z_LAYERS.get("monster", 0))