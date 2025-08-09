import pygame
import logging
from typing import Dict, Optional, Iterable
from roguelike_game.factories.monster.config import MONSTER_DEFS
from roguelike_engine.utils.loader import load_image


logger = logging.getLogger(__name__)

_SPRITE_SURFACES: Dict[str, Dict[str, pygame.Surface]] = {}
_DEATH_SURFACES: Dict[str, Optional[pygame.Surface]] = {}
_loaded_variants: set[str] = set()

def _norm_scale(value, fallback: float) -> float:
    """Return a numeric scale. If value is None or invalid, use fallback; if fallback is None, use 1.0."""
    if fallback is None:
        fallback = 1.0
    if value is None:
        return float(fallback)
    try:
        return float(value)
    except Exception:
        return float(fallback)


def load_caches_for(variants: Iterable[str]) -> None:
    """Load and cache sprite and death surfaces for specified monster types."""

    for mtype in variants:
        if mtype in _loaded_variants:
            continue
        cfg = MONSTER_DEFS[mtype]
        # Determine active set (default to no-sets)
        cfg_assets = cfg.get("assets", {})
        active_set = cfg_assets.get("active_set", "no-sets")
        # Select assets and metadata groups
        if active_set == "sets":
            group = cfg_assets.get("sets", {})
            assets_group = group.get("sprites_set", {})
            data_assets = group.get("sprites_data_set", {})
        else:
            group = cfg_assets.get("no-sets", {})
            assets_group = {k: v for k, v in group.items() if k != "sprites_data_no-set"}
            data_assets = group.get("sprites_data_no-set", {})
        # Fallback scales and tint (normalize Nones and invalids)
        default_scale = _norm_scale(data_assets.get("scale", 1.0), 1.0)
        default_death_scale = _norm_scale(data_assets.get("death_scale", default_scale), default_scale)
        tint = data_assets.get("tint")
        dir_map: Dict[str, pygame.Surface] = {}
        # Mapping directions
        dir_names = {
            "s": "down", "e": "right", "n": "up", "w": "left",
            "se": "down_right", "ne": "up_right", "sw": "down_left", "nw": "up_left"
        }
        # Flatten assets
        for state, entry in assets_group.items():
            # 'sets' provides list of sheet paths
            if active_set == "sets":
                if not isinstance(entry, list) or not entry:
                    continue
                sheet = entry[0]
                for dkey, name in dir_names.items():
                    raw = load_image(sheet)
                    scale_key = f"scale_{state}"
                    use_scale = _norm_scale(data_assets.get(scale_key, default_scale), default_scale)
                    if use_scale != 1.0:
                        w0, h0 = raw.get_size()
                        image = pygame.transform.scale(raw, (int(w0 * use_scale), int(h0 * use_scale)))
                    else:
                        image = raw
                    if tint:
                        image.fill(tuple(tint), special_flags=pygame.BLEND_RGB_MULT)
                    dir_map[f"{state}_{name}"] = image
            # 'no-sets' provides dict of directions
            else:
                if not isinstance(entry, dict):
                    continue
                for dkey, path in entry.items():
                    if not path:
                        continue
                    raw = load_image(path)
                    if state == "idle":
                        key = dir_names.get(dkey, dkey)
                        use_scale = _norm_scale(data_assets.get("scale_idle", default_scale), default_scale)
                    elif state == "death":
                        key = "death"
                        use_scale = _norm_scale(data_assets.get("scale_death", default_death_scale), default_death_scale)
                    else:
                        key = f"{state}_{dir_names.get(dkey, dkey)}"
                        use_scale = _norm_scale(data_assets.get(f"scale_{state}", default_scale), default_scale)
                    if use_scale != 1.0:
                        w0, h0 = raw.get_size()
                        image = pygame.transform.scale(raw, (int(w0 * use_scale), int(h0 * use_scale)))
                    else:
                        image = raw
                    if tint:
                        image.fill(tuple(tint), special_flags=pygame.BLEND_RGB_MULT)
                    dir_map[key] = image
        _SPRITE_SURFACES[mtype] = dir_map
        _DEATH_SURFACES[mtype] = dir_map.get("death")
        _loaded_variants.add(mtype)



def _load_caches_once() -> None:
    """Load all monster caches."""
    load_caches_for(MONSTER_DEFS.keys())