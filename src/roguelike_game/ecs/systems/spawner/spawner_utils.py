from __future__ import annotations

from roguelike_engine.config.config_tiles import TILE_SIZE
from typing import Any, Iterable


def get_policy_flags(cfg: Any) -> tuple[bool, int, bool, bool]:
    policy = getattr(cfg, 'policy', {}) or {}
    looping = bool(policy.get('loop') or policy.get('repeat') or policy.get('restart_on_done'))
    max_active = int(policy.get('max_active', 0) or 0)
    advance_on = str(policy.get('advance_on', 'clear') or 'clear').lower()
    advance_on_cooldown = (advance_on == 'cooldown')
    proximity_initial_only = bool(policy.get('proximity_initial_only'))
    return looping, max_active, advance_on_cooldown, proximity_initial_only


def prune_tracking_sets(st: Any, ents_set: Iterable[int]) -> None:
    if getattr(st, 'current_wave_entities', None) is not None:
        alive = set()
        for ent_id in list(st.current_wave_entities):
            if ent_id in ents_set:
                alive.add(ent_id)
        st.current_wave_entities = alive

    if getattr(st, 'active_entities', None) is not None:
        active_alive = set()
        for ent_id in list(st.active_entities):
            if ent_id in ents_set:
                active_alive.add(ent_id)
        st.active_entities = active_alive


def compute_defend_metadata(cfg: Any, sr: Any, fallback_max: int, shape: str):
    defend_center = None
    defend_radius_px = None
    defend_leash = None
    defend_shape = None
    try:
        if getattr(cfg, 'defend_spawn', False):
            defend_tiles = 0
            if isinstance(sr, (int, float)) and int(sr) > 0:
                defend_tiles = int(sr)
            elif isinstance(sr, str) and str(sr).strip().lower() in {"random", "aleatorio", "aleatoreo"}:
                defend_tiles = max(1, int(fallback_max))
            if defend_tiles > 0:
                ax, ay = cfg.anchor_tile
                cx = ax * TILE_SIZE + TILE_SIZE // 2
                cy = ay * TILE_SIZE + TILE_SIZE // 2
                defend_center = (float(cx), float(cy))
                defend_radius_px = float(defend_tiles * TILE_SIZE)
                defend_leash = bool(getattr(cfg, 'defend_leash', True))
                defend_shape = str(shape)
    except Exception:
        defend_center = None
        defend_radius_px = None
        defend_leash = None
        defend_shape = None
    return defend_center, defend_radius_px, defend_leash, defend_shape
