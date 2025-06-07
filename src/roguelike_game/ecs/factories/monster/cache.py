import pygame
import logging
from typing import Dict, Optional
from roguelike_game.ecs.factories.monster.config import MONSTER_DEFS

logger = logging.getLogger(__name__)

_SPRITE_SURFACES: Dict[str, Dict[str, pygame.Surface]] = {}
_DEATH_SURFACES: Dict[str, Optional[pygame.Surface]] = {}
_caches_loaded: bool = False

def _load_caches_once() -> None:
    """Load and cache sprite and death surfaces for each monster type."""
    global _caches_loaded
    if _caches_loaded:
        return
    for mtype, cfg in MONSTER_DEFS.items():
        logger.debug(f"Loading sprites for: {mtype}")
        dir_map: Dict[str, pygame.Surface] = {}
        for direction, path in cfg["sprites"].items():
            image = pygame.image.load(path).convert_alpha()
            scale_val = cfg["scale"]
            if scale_val != 1.0:
                w, h = image.get_size()
                image = pygame.transform.scale(image, (int(w*scale_val), int(h*scale_val)))
            # Apply optional tint from config
            tint = cfg.get("tint")
            if tint:
                # Multiply color channels by tint (RGB tuple)
                image.fill(tuple(tint), special_flags=pygame.BLEND_RGB_MULT)
            dir_map[direction] = image
        _SPRITE_SURFACES[mtype] = dir_map
        death_path = cfg.get("death_sprite")
        if death_path:
            death_img = pygame.image.load(death_path).convert_alpha()
            death_scale = cfg["death_scale"]
            if death_scale != 1.0:
                w, h = death_img.get_size()
                death_img = pygame.transform.scale(death_img, (int(w*death_scale), int(h*death_scale)))
            # Apply optional tint to death image
            tint = cfg.get("tint")
            if tint and death_img:
                death_img.fill(tuple(tint), special_flags=pygame.BLEND_RGB_MULT)
            _DEATH_SURFACES[mtype] = death_img
        else:
            _DEATH_SURFACES[mtype] = None
    _caches_loaded = True