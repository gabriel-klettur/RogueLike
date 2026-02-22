from __future__ import annotations

from roguelike_engine.config.config_tiles import TILE_SIZE
from typing import Any, Iterable


def get_policy_flags(cfg: Any) -> tuple[bool, int, bool, bool, bool]:
    policy = getattr(cfg, 'policy', {}) or {}
    looping = bool(policy.get('loop') or policy.get('repeat') or policy.get('restart_on_done'))
    max_active = int(policy.get('max_active', 0) or 0)
    advance_on = str(policy.get('advance_on', 'clear') or 'clear').lower()
    advance_on_cooldown = (advance_on == 'cooldown')
    proximity_initial_only = bool(policy.get('proximity_initial_only'))
    # Whether KO/dead entities are excluded from active/clear checks
    count_ko_as_clear = bool(policy.get('count_ko_as_clear', True))
    return looping, max_active, advance_on_cooldown, proximity_initial_only, count_ko_as_clear


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


def _is_active_for_spawner(world: Any, eid: int) -> bool:
    """Return False if entity is KO (Unconscious) or in Death state.

    If NPCState is missing, default to active to avoid false positives.
    """
    try:
        ns = world.components.get('NPCState', {}).get(eid)
        if ns is None:
            return True
        fsm = getattr(ns, 'fsm', None)
        cur = getattr(fsm, 'current_state', None)
        if cur is None:
            return True
        try:
            # Import locally to avoid module-level cycles
            from roguelike_game.ecs.systems.fsm.states.unconscious_state import UnconsciousState  # type: ignore
            from roguelike_game.ecs.systems.fsm.states.death_state import DeathState  # type: ignore
            if isinstance(cur, (UnconsciousState, DeathState)):
                return False
        except Exception:
            pass
        # Fallback by class name
        try:
            name = cur.__class__.__name__.lower()
            if name in {'unconsciousstate', 'deathstate'}:
                return False
        except Exception:
            pass
        return True
    except Exception:
        return True


def prune_tracking_sets_ko(world: Any, st: Any, ents_set: Iterable[int]) -> None:
    """KO-aware variant: removes entities that are either gone or KO/dead.

    - Keeps only entities that still exist AND are active for the spawner.
    - Applies to both current_wave_entities and active_entities if present.
    """
    try:
        if getattr(st, 'current_wave_entities', None) is not None:
            alive = set()
            for ent_id in list(st.current_wave_entities):
                try:
                    if (ent_id in ents_set) and _is_active_for_spawner(world, ent_id):
                        alive.add(ent_id)
                except Exception:
                    continue
            st.current_wave_entities = alive
    except Exception:
        pass
    try:
        if getattr(st, 'active_entities', None) is not None:
            active_alive = set()
            for ent_id in list(st.active_entities):
                try:
                    if (ent_id in ents_set) and _is_active_for_spawner(world, ent_id):
                        active_alive.add(ent_id)
                except Exception:
                    continue
            st.active_entities = active_alive
    except Exception:
        pass


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
