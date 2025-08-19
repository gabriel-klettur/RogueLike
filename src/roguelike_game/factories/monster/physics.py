import pygame
from typing import Dict, Any, Tuple
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.physics.multi_collider import MultiCollider
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider
from roguelike_game.ecs.components.physics.collider import Collider
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
    body = MaskCollider(pygame.mask.from_surface(mask_surf), 0, 0)
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

    offset_x = (w - feet_w) // 2
    offset_y = h - feet_h  # anchor to bottom

    feet = Collider(feet_w, feet_h, offset_x, offset_y)
    return MultiCollider({"body": body, "feet": feet})


def create_zlayer_component(cfg: Dict[str, Any]) -> ZLayer:
    """Set the rendering Z-layer for the entity."""
    faction = getattr(Faction, cfg.get("faction"), None)
    return ZLayer(Z_LAYERS.get("monster", 0))