import json
import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.transform.movement_speed import MovementSpeed
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.ai.patrol import Patrol
from roguelike_game.ecs.components.rendering.animator import Animator
from roguelike_game.ecs.components.physics.multi_collider import MultiCollider
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider
from roguelike_game.ecs.components.physics.collider import Collider
from roguelike_game.ecs.components.core.identity import Identity, Faction
from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_game.ecs.components.combat.melee_weapon import MeleeWeapon
from roguelike_game.ecs.components.ai.aggro_range import AggroRange
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_game.systems.config_z_layer import Z_LAYERS
import logging

from pathlib import Path
from typing import Any, Dict, Optional, Tuple

# Initialize logger
logger = logging.getLogger(__name__)

# Data directory and definitions
_DATA_DIR = Path(__file__).resolve().parents[4] / "data"
_DEFS: Dict[str, Any] = json.load(open(_DATA_DIR / "monsters.json", "r"))

# Caches for sprite and death surfaces
_SPRITE_SURFACES: Dict[str, Dict[str, pygame.Surface]] = {}
_DEATH_SURFACES: Dict[str, Optional[pygame.Surface]] = {}
_caches_loaded: bool = False

def _load_caches_once() -> None:
    """Load and cache sprite and death surfaces for each monster type."""
    global _caches_loaded
    if _caches_loaded:
        return
    for mtype, cfg in _DEFS.items():
        logger.debug(f"Loading sprites for: {mtype}")
        dir_map: Dict[str, pygame.Surface] = {}
        for direction, path in cfg["sprites"].items():
            image = pygame.image.load(path).convert_alpha()
            scale_val = cfg.get("scale", 1.0)
            if scale_val != 1.0:
                w, h = image.get_size()
                image = pygame.transform.scale(image, (int(w*scale_val), int(h*scale_val)))
            dir_map[direction] = image
        _SPRITE_SURFACES[mtype] = dir_map
        death_path = cfg.get("death_sprite")
        if death_path:
            death_img = pygame.image.load(death_path).convert_alpha()
            death_scale = cfg.get("death_scale", 1.0)
            if death_scale != 1.0:
                w, h = death_img.get_size()
                death_img = pygame.transform.scale(death_img, (int(w*death_scale), int(h*death_scale)))
            _DEATH_SURFACES[mtype] = death_img
        else:
            _DEATH_SURFACES[mtype] = None
    _caches_loaded = True

def _create_sprite_component(monster_type: str) -> Tuple[Sprite, Optional[pygame.Surface]]:
    """Create Sprite component and retrieve optional death image."""
    base_map = _SPRITE_SURFACES.get(monster_type, {})
    sprite = Sprite(base_map.get("down", {}).copy())
    death_image = _DEATH_SURFACES.get(monster_type)
    return sprite, death_image

def _calculate_position(tile_x: int, tile_y: int, cfg: Dict[str, Any], sprite: Sprite) -> Tuple[int, int]:
    """Compute the bottom-center pixel coordinates for the sprite on the map tile."""
    scale_val = cfg.get("scale", 1.0)
    orig_w, orig_h = sprite.image.get_size()
    width = int(orig_w * scale_val)
    height = int(orig_h * scale_val)
    px = tile_x * TILE_SIZE + (TILE_SIZE - width) // 2
    py = (tile_y + 1) * TILE_SIZE - height
    return px, py

def _create_patrol_components(px: int, py: int, monster_type: str, cfg: Dict[str, Any]) -> Tuple[Patrol, MovementSpeed, Animator]:
    """Initialize Patrol, MovementSpeed, and Animator ECS components."""
    sprites = {d: [surf.copy()] for d, surf in _SPRITE_SURFACES.get(monster_type, {}).items()}
    patrol = Patrol((px, py), sprites_by_direction=sprites)
    patrol.default_sprite = sprites.get("down", [])[0]
    movement = MovementSpeed(speed=cfg.get("speed", 0))
    animator = Animator(animations=sprites, current_state="down")
    return patrol, movement, animator

def _create_physics_components(cfg: Dict[str, Any]) -> Tuple[Scale, Velocity]:
    """Create Scale and Velocity ECS components."""
    scale_cmp = Scale(scale=cfg.get("scale", 1.0))
    velocity_cmp = Velocity(0, 0)
    return scale_cmp, velocity_cmp

def _create_collider_components(sprite: Sprite, cfg: Dict[str, Any]) -> MultiCollider:
    """Construct body and feet colliders based on sprite surface."""
    mask_surf = sprite.image
    scale_val = cfg.get("scale", 1.0)
    if scale_val != 1.0:
        w, h = mask_surf.get_size()
        mask_surf = pygame.transform.scale(mask_surf, (int(w*scale_val), int(h*scale_val)))
    body = MaskCollider(pygame.mask.from_surface(mask_surf), 0, 0)
    w, h = mask_surf.get_size()
    feet = Collider(int(w*0.5), int(h*0.2), (w - int(w*0.5))//2, h - int(h*0.2))
    return MultiCollider({"body": body, "feet": feet})

def _create_zlayer_component(cfg: Dict[str, Any]) -> ZLayer:
    """Set the rendering Z-layer for the entity."""
    faction = getattr(Faction, cfg.get("faction"), None)
    return ZLayer(Z_LAYERS.get("monster", 0))

def _create_health_identity_components(eid: int, monster_type: str, cfg: Dict[str, Any]) -> Tuple[Health, Identity]:
    """Create Health and Identity ECS components."""
    health_cmp = Health(cfg.get("hp", 0), cfg.get("hp", 0))
    identity_cmp = Identity(id=eid, name=monster_type.capitalize(), title="", faction=getattr(Faction, cfg.get("faction")))
    return health_cmp, identity_cmp

def spawn_monster(world, monster_type: str, tile_x: int, tile_y: int) -> int:
    """Create a monster entity based on the provided monster type and tile coordinates."""
    _load_caches_once()
    cfg = _DEFS[monster_type]
    eid = world.create_entity()

    # Sprite & Death Image
    sprite, death_img = _create_sprite_component(monster_type)
    if death_img:
        sprite.death_image = death_img
    world.components["Sprite"][eid] = sprite

    # Record spawn tile for debugging
    if not hasattr(world, "spawn_tiles"):
        world.spawn_tiles = []
    world.spawn_tiles.append((tile_x, tile_y, eid))

    # Position
    px, py = _calculate_position(tile_x, tile_y, cfg, sprite)
    world.components["Position"][eid] = Position(px, py)

    # Patrol, MovementSpeed, Animator
    patrol, movement, animator = _create_patrol_components(px, py, monster_type, cfg)
    world.components["Patrol"][eid] = patrol
    world.components["MovementSpeed"][eid] = movement
    world.components["Animator"][eid] = animator

    # Physics: Scale & Velocity
    scale_cmp, velocity_cmp = _create_physics_components(cfg)
    world.components["Scale"][eid] = scale_cmp
    world.components["Velocity"][eid] = velocity_cmp

    # Colliders
    collider_cmp = _create_collider_components(sprite, cfg)
    world.components["MultiCollider"][eid] = collider_cmp

    # Z-Layer
    zlayer_cmp = _create_zlayer_component(cfg)
    world.components["ZLayer"][eid] = zlayer_cmp

    # Health & Identity
    health_cmp, identity_cmp = _create_health_identity_components(eid, monster_type, cfg)
    world.components["Health"][eid] = health_cmp
    world.components["Identity"][eid] = identity_cmp

    # CombatStats, MeleeWeapon, AggroRange
    world.components["CombatStats"][eid] = CombatStats(cfg.get("hp", 0), cfg.get("hp", 0), cfg.get("power", 0), cfg.get("defense", 0))
    world.components["MeleeWeapon"][eid] = MeleeWeapon(cfg.get("melee_damage", 0), cfg.get("melee_cooldown", 1.0))
    world.components["AggroRange"][eid] = AggroRange(cfg.get("aggro_range", 0))

    return eid