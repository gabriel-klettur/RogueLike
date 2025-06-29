# Path: src/roguelike_game/ecs/factories/monster/sprite_loader.py
import pygame
from typing import Dict, Any, Optional, Tuple
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.ai.patrol import Patrol
from roguelike_game.ecs.components.transform.movement_speed import MovementSpeed
from roguelike_game.ecs.components.rendering.animator import Animator
from roguelike_game.ecs.factories.monster.cache import _load_caches_once, _SPRITE_SURFACES, _DEATH_SURFACES


def create_sprite_component(monster_type: str) -> Tuple[Sprite, Optional[pygame.Surface]]:
    """Create Sprite component and retrieve optional death image."""
    _load_caches_once()
    base_map = _SPRITE_SURFACES.get(monster_type, {})
    sprite = Sprite(base_map.get("down", {}).copy())
    death_image = _DEATH_SURFACES.get(monster_type)
    return sprite, death_image


def create_patrol_components(px: int, py: int, monster_type: str, cfg: Dict[str, Any]) -> Tuple[Patrol, MovementSpeed, Animator]:
    """Initialize Patrol, MovementSpeed, and Animator ECS components."""
    _load_caches_once()
    sprites = {d: [surf.copy()] for d, surf in _SPRITE_SURFACES.get(monster_type, {}).items()}
    patrol = Patrol((px, py), sprites_by_direction=sprites)
    patrol.default_sprite = sprites.get("down", [])[0]
    movement = MovementSpeed(cfg["speed"])
    animator = Animator(animations=sprites, current_state="down")
    return patrol, movement, animator