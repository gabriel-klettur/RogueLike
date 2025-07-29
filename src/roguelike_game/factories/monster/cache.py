import pygame
import logging
from typing import Dict, Optional, Iterable
from roguelike_game.factories.monster.config import MONSTER_DEFS
from roguelike_engine.utils.loader import load_image

logger = logging.getLogger(__name__)

_SPRITE_SURFACES: Dict[str, Dict[str, pygame.Surface]] = {}
_DEATH_SURFACES: Dict[str, Optional[pygame.Surface]] = {}
_loaded_variants: set[str] = set()

def load_caches_for(variants: Iterable[str]) -> None:
    """Load and cache sprite and death surfaces for specified monster types."""
    global _loaded_variants
    for mtype in variants:
        if mtype in _loaded_variants:
            continue
        cfg = MONSTER_DEFS[mtype]
        logger.debug(f"Loading sprites for: {mtype}")
        scale_val = cfg["scale"]
        dir_map: Dict[str, pygame.Surface] = {}
        for direction, path in cfg["sprites"].items():
            raw = load_image(path)
            # Scale raw surface in memory instead of reloading
            if scale_val != 1.0:
                w0, h0 = raw.get_size()
                image = pygame.transform.scale(raw, (int(w0*scale_val), int(h0*scale_val)))
            else:
                image = raw
            tint = cfg.get("tint")
            if tint:
                image.fill(tuple(tint), special_flags=pygame.BLEND_RGB_MULT)
            dir_map[direction] = image
        _SPRITE_SURFACES[mtype] = dir_map
        death_path = cfg.get("sprites", {}).get("death")
        if death_path:
            raw_death = load_image(death_path)
            death_scale = cfg["death_scale"]
            if death_scale != 1.0:
                w0, h0 = raw_death.get_size()
                death_img = pygame.transform.scale(raw_death, (int(w0*death_scale), int(h0*death_scale)))
            else:
                death_img = raw_death
            tint = cfg.get("tint")
            if tint and death_img:
                death_img.fill(tuple(tint), special_flags=pygame.BLEND_RGB_MULT)
            _DEATH_SURFACES[mtype] = death_img
        else:
            _DEATH_SURFACES[mtype] = None
        _loaded_variants.add(mtype)

def _load_caches_once() -> None:
    """Load all monster caches."""
    load_caches_for(MONSTER_DEFS.keys())