import pygame
from typing import Dict, Any, Optional, Tuple
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.movement_speed import MovementSpeed
from roguelike_game.ecs.components.rendering.animator import Animator
from roguelike_game.factories.monster.cache import _load_caches_once, _SPRITE_SURFACES, _DEATH_SURFACES

# ── Per-type shared caches (built once, shared across all NPCs of same type) ──
_CROPPED_FRAME_CACHE: Dict[str, pygame.Surface] = {}  # monster_type -> cropped "down" frame
_SHARED_ANIM_CACHE: Dict[str, Dict[str, list]] = {}   # monster_type -> {dir: [frame, ...]}
_SHARED_MASK_CACHE: Dict[str, Dict[str, list]] = {}   # monster_type -> {dir: [mask, ...]}


def _make_placeholder(size: Tuple[int, int] = (16, 16)) -> pygame.Surface:
    """Return a simple placeholder surface to use when no asset exists."""
    surf = pygame.Surface(size, pygame.SRCALPHA)
    surf.fill((0, 0, 0, 255))  # solid black
    return surf


def _get_cropped_frame(monster_type: str) -> pygame.Surface:
    """Return a cropped (bounding-rect) 'down' frame, cached per type."""
    cached = _CROPPED_FRAME_CACHE.get(monster_type)
    if cached is not None:
        return cached
    base_map = _SPRITE_SURFACES.get(monster_type, {})
    raw_frame = base_map.get("down")
    if isinstance(raw_frame, pygame.Surface):
        frame = raw_frame
    elif raw_frame is not None and hasattr(raw_frame, "get_bounding_rect"):
        frame = raw_frame
    else:
        frame = _make_placeholder()
    # Crop transparent borders once
    try:
        bbox = frame.get_bounding_rect()
        if bbox.width and bbox.height:
            frame = frame.subsurface(bbox).copy()
    except Exception:
        pass
    _CROPPED_FRAME_CACHE[monster_type] = frame
    return frame


def _get_shared_anims(monster_type: str) -> Tuple[Dict[str, list], Dict[str, list]]:
    """Return shared (sprites_dict, masks_dict) for a monster type, built once."""
    if monster_type in _SHARED_ANIM_CACHE:
        return _SHARED_ANIM_CACHE[monster_type], _SHARED_MASK_CACHE.get(monster_type, {})
    base = _SPRITE_SURFACES.get(monster_type, {})
    sprites: Dict[str, list] = {}
    masks: Dict[str, list] = {}
    for d, surf in base.items():
        if isinstance(surf, pygame.Surface):
            # Share the surface directly — no copy needed (never mutated in-place)
            sprites[d] = [surf]
            try:
                masks[d] = [pygame.mask.from_surface(surf)]
            except Exception:
                masks[d] = []
        elif surf is not None and hasattr(surf, "get_size"):
            sprites[d] = [surf]
            masks[d] = []
    if not sprites:
        ph = _make_placeholder()
        sprites = {"down": [ph]}
    elif "down" not in sprites:
        any_dir = next(iter(sprites.values()))[0]
        sprites["down"] = [any_dir]
    _SHARED_ANIM_CACHE[monster_type] = sprites
    _SHARED_MASK_CACHE[monster_type] = masks
    return sprites, masks


def create_sprite_component(monster_type: str) -> Tuple[Sprite, Optional[pygame.Surface]]:
    """Create Sprite component using shared cached surfaces (no per-NPC copy)."""
    # Auto-load caches only if not already loaded for this monster_type (skip in tests)
    if monster_type not in _SPRITE_SURFACES:
        _load_caches_once()
    frame = _get_cropped_frame(monster_type)
    try:
        sprite = Sprite(frame)
    except Exception:
        sprite = Sprite.__new__(Sprite)
        sprite.image = frame
    death_image = _DEATH_SURFACES.get(monster_type)
    return sprite, death_image


def create_movement_components(px: int, py: int, monster_type: str, cfg: Dict[str, Any]) -> Tuple[MovementSpeed, Animator]:
    """Initialize MovementSpeed and Animator ECS components using shared surfaces."""
    # Auto-load caches only if not already loaded for this monster_type (skip in tests)
    if monster_type not in _SPRITE_SURFACES:
        _load_caches_once()
    sprites, masks = _get_shared_anims(monster_type)
    movement = MovementSpeed(float(cfg["speed"]))
    animator = Animator(animations=sprites, current_state="down", masks=masks)
    return movement, animator