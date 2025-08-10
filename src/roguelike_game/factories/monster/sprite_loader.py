import pygame
from typing import Dict, Any, Optional, Tuple
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.ai.patrol import Patrol
from roguelike_game.ecs.components.transform.movement_speed import MovementSpeed
from roguelike_game.ecs.components.rendering.animator import Animator
from roguelike_game.factories.monster.cache import _load_caches_once, _SPRITE_SURFACES, _DEATH_SURFACES


def _make_placeholder(size: Tuple[int, int] = (16, 16)) -> pygame.Surface:
    """Return a simple placeholder surface to use when no asset exists."""
    surf = pygame.Surface(size, pygame.SRCALPHA)
    surf.fill((0, 0, 0, 255))  # solid black
    return surf


def create_sprite_component(monster_type: str) -> Tuple[Sprite, Optional[pygame.Surface]]:
    """Create Sprite component and retrieve optional death image."""
    # Auto-load caches only if not already loaded for this monster_type (skip in tests)
    if monster_type not in _SPRITE_SURFACES:
        _load_caches_once()
    base_map = _SPRITE_SURFACES.get(monster_type, {})
    # Initialize Sprite component, bypass loader for surface-like objects
    raw_frame = base_map.get("down")
    if isinstance(raw_frame, pygame.Surface):
        frame = raw_frame.copy()
    elif raw_frame is not None and hasattr(raw_frame, "copy"):
        frame = raw_frame.copy()
    else:
        frame = _make_placeholder()
    # Crop transparent borders to tight sprite frame (guarded)
    try:
        bbox = frame.get_bounding_rect()
        if bbox.width and bbox.height:
            frame = frame.subsurface(bbox).copy()
    except Exception:
        pass
    try:
        sprite = Sprite(frame)
    except Exception:
        sprite = Sprite.__new__(Sprite)
        sprite.image = frame
    death_image = _DEATH_SURFACES.get(monster_type)
    return sprite, death_image


def create_patrol_components(px: int, py: int, monster_type: str, cfg: Dict[str, Any]) -> Tuple[Patrol, MovementSpeed, Animator]:
    """Initialize Patrol, MovementSpeed, and Animator ECS components."""
    # Auto-load caches only if not already loaded for this monster_type (skip in tests)
    if monster_type not in _SPRITE_SURFACES:
        _load_caches_once()
    base = _SPRITE_SURFACES.get(monster_type, {})
    sprites: Dict[str, list] = {}
    for d, surf in base.items():
        if isinstance(surf, pygame.Surface):
            sprites[d] = [surf.copy()]
        elif surf is not None and hasattr(surf, "copy"):
            sprites[d] = [surf.copy()]
    if not sprites:
        ph = _make_placeholder()
        sprites = {"down": [ph]}
    else:
        # Ensure there is at least a 'down' direction for defaults
        if "down" not in sprites:
            any_dir = next(iter(sprites.values()))[0]
            sprites["down"] = [any_dir]
    patrol = Patrol((px, py), sprites_by_direction=sprites)
    patrol.default_sprite = sprites["down"][0]
    movement = MovementSpeed(float(cfg["speed"]))
    animator = Animator(animations=sprites, current_state="down")
    return patrol, movement, animator