import pygame
import logging
from typing import Dict, Optional
from roguelike_game.ecs.factories.monster.config import MONSTER_DEFS
from roguelike_engine.utils.loader import load_image

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
        scale_val = cfg["scale"]
        dir_map: Dict[str, pygame.Surface] = {}
        for direction, path in cfg["sprites"].items():
            # Load raw image with caching
            raw = load_image(path)
            # Scale if needed with caching
            if scale_val != 1.0:
                w0, h0 = raw.get_size()
                image = load_image(path, (int(w0*scale_val), int(h0*scale_val)))
            else:
                image = raw
            # Apply optional tint
            tint = cfg.get("tint")
            if tint:
                image.fill(tuple(tint), special_flags=pygame.BLEND_RGB_MULT)
            dir_map[direction] = image
        _SPRITE_SURFACES[mtype] = dir_map
        death_path = cfg.get("death_sprite")
        if death_path:
            raw_death = load_image(death_path)
            death_scale = cfg["death_scale"]
            if death_scale != 1.0:
                w0, h0 = raw_death.get_size()
                death_img = load_image(death_path, (int(w0*death_scale), int(h0*death_scale)))
            else:
                death_img = raw_death
            tint = cfg.get("tint")
            if tint and death_img:
                death_img.fill(tuple(tint), special_flags=pygame.BLEND_RGB_MULT)
            _DEATH_SURFACES[mtype] = death_img
        else:
            _DEATH_SURFACES[mtype] = None
    _caches_loaded = True