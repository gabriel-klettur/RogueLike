"""
Centralized hot-reload helpers for JSON-backed game data under data/.

Provides a single entry-point: reload_all_data(game=None, force=False)
that selectively reloads only the files that changed since the last call.

This is intended for developer workflows (edit JSON -> reload in-game)
without restarting the process.
"""
from __future__ import annotations

import logging
import sys
import importlib
from pathlib import Path
from typing import Callable, Dict, List, Tuple, Optional

logger = logging.getLogger(__name__)

# Project root (same approach as other config modules)
BASE_DIR = Path(__file__).resolve().parents[3]
DATA_DIR = BASE_DIR / "data"

# Cached file modification times so we reload only what changed
_FILE_MTIMES: Dict[Path, float] = {}

# Type alias for reloader entries: (file_path, callable, human_name)
Reloader = Tuple[Path, Callable[[], None], str]


def _gather_reloaders() -> List[Reloader]:
    """Return the list of known reloadable data files and their reloaders.

    Add more entries here as the project grows (e.g., drops table, items, etc.).
    """
    reloaders: List[Reloader] = []
    try:
        # Spells
        from .spells_config import reload_spells  # lazy import
        reloaders.append((DATA_DIR / "spells" / "spells.json", reload_spells, "spells.json"))
    except Exception:
        pass
    try:
        # Particles
        from .particles_config import reload_particles
        reloaders.append((DATA_DIR / "particles" / "particles.json", reload_particles, "particles.json"))
    except Exception:
        pass
    try:
        # Monsters (hostiles + optional neutrals)
        from roguelike_game.factories.monster.config import reload_monster_defs
        # We use hostiles as the sentinel, the reloader handles neutrals if present
        reloaders.append((DATA_DIR / "entities" / "new_hostiles.json", reload_monster_defs, "entities/new_hostiles.json(+neutrals)") )
    except Exception:
        pass
    # Note: Spawners/buildings editors maintain their own persistence and runtime
    # state. A safe hot-reload of those requires system-level invalidations and
    # entity rebuilds, which is beyond the scope of this lightweight utility.
    # Debug list of reload candidates (paths may not all exist in some setups)
    try:
        logger.debug("[hot_reload] Candidates: %s", [str(p) for (p, _fn, _n) in reloaders])
    except Exception:
        pass
    return reloaders


def _should_reload(path: Path, force: bool) -> bool:
    try:
        mtime = path.stat().st_mtime
    except FileNotFoundError:
        logger.debug("[hot_reload] Missing file, skipping: %s", str(path))
        return False
    except Exception:
        # If we can't stat, avoid reloading
        return False
    if force:
        prev = _FILE_MTIMES.get(path)
        _FILE_MTIMES[path] = mtime
        logger.debug("[hot_reload] Force reload: %s (prev=%s -> now=%s)", str(path), str(prev), str(mtime))
        return True
    prev = _FILE_MTIMES.get(path)
    if prev is None or mtime > prev:
        _FILE_MTIMES[path] = mtime
        if prev is None:
            logger.debug("[hot_reload] First observation -> reload: %s (mtime=%s)", str(path), str(mtime))
        else:
            logger.debug("[hot_reload] Detected change -> reload: %s (prev=%s -> now=%s)", str(path), str(prev), str(mtime))
        return True
    logger.debug("[hot_reload] No changes: %s (prev=%s == now=%s)", str(path), str(prev), str(mtime))
    return False


def reload_all_data(game=None, *, force: bool = False) -> int:
    """Reload all known data files that changed.

    Returns the number of successful reload actions performed.
    """
    reloaders = _gather_reloaders()
    try:
        logger.info("[hot_reload] Starting reload (force=%s). Candidates=%d", bool(force), len(reloaders))
    except Exception:
        pass
    done = 0
    for path, fn, name in reloaders:
        try:
            logger.debug("[hot_reload] Checking: %s", str(path))
            if _should_reload(path, force):
                fn()
                done += 1
                logger.info("[hot_reload] Reloaded %s", name)
        except Exception:
            logger.exception("[hot_reload] Failed reloading %s", name)
    if done == 0:
        logger.info("[hot_reload] No changes detected under data/ (nothing reloaded)")
    else:
        logger.info("[hot_reload] Completed reload: %d module(s)", done)
    # Optional: notify UI systems/overlays here if needed in the future
    return done


# --- Full game hot-reload with loading screen ---------------------------------

def _paths_changed(paths: List[Path], *, force: bool) -> bool:
    any_existing = False
    changed = False
    for p in paths:
        try:
            if p.exists():
                any_existing = True
                if _should_reload(p, force):
                    changed = True
            else:
                logger.debug("[hot_reload] Missing candidate (ignored in group): %s", str(p))
        except Exception:
            continue
    # If forcing and there is at least one existing file, consider changed
    if force and any_existing:
        return True
    return changed


def _draw_loader(game, frac: float, msg: str) -> None:
    try:
        loader = getattr(game, 'loader', None)
        if loader is None:
            from roguelike_engine.utils.loading_screen import LoadingScreen
            loader = LoadingScreen(game.screen)
            setattr(game, 'loader', loader)
        loader.draw(max(0.0, min(1.0, float(frac))), str(msg))
    except Exception:
        # Best-effort: at least pump events to keep window responsive
        try:
            import pygame
            pygame.event.pump()
        except Exception:
            pass


def _reload_items(game) -> int:
    try:
        from roguelike_game.managers.items.loader import ItemsLoader
        loader = ItemsLoader()
        items, assets = loader.load()
        setattr(game, 'items', items)
        setattr(game, 'item_assets', assets)
        logger.info("[hot_reload] Items reloaded: %d entries", len(items or {}))
        return len(items or {})
    except Exception:
        logger.exception("[hot_reload] Failed reloading items.json")
        return 0


def _reload_tiles(game) -> int:
    # Clear tile sprite caches and invalidate current map view cache
    try:
        from roguelike_engine.tile.utils import assets as tile_assets
        tile_assets._BASE_TILE_IMAGES_CACHE = None
        try:
            tile_assets._SPRITE_CACHE.clear()
        except Exception:
            tile_assets._SPRITE_CACHE = {}
        # Invalidate current map view cache so new sprites are used
        try:
            game.map.view.invalidate_cache()
        except Exception:
            pass
        logger.info("[hot_reload] Tiles caches cleared and map view invalidated")
        return 1
    except Exception:
        logger.exception("[hot_reload] Failed to clear tiles caches")
        return 0


def _reload_buildings(game) -> int:
    # Re-run BuildingsManager pipeline and invalidate spatial index
    try:
        try:
            from roguelike_engine.config.map_config import global_map_settings
            try:
                setattr(global_map_settings, 'use_zones_json', True)
            except Exception:
                pass
            try:
                global_map_settings.refresh_zone_offsets()
            except Exception:
                pass
        except Exception:
            pass
        bm = getattr(game, 'buildings', None)
        if bm is None or not hasattr(bm, 'init_buildings'):
            logger.warning("[hot_reload] BuildingsManager not found; skipping buildings reload")
            return 0
        buildings = bm.init_buildings() or []
        # Sync entities namespace and rebuild spatial index
        try:
            setattr(game.entities, 'buildings', buildings)
        except Exception:
            pass
        try:
            game.ecs.ecs_world.invalidate_spatial_index()
        except Exception:
            pass
        logger.info("[hot_reload] Buildings reloaded: %d instances", len(buildings))
        return len(buildings)
    except Exception:
        logger.exception("[hot_reload] Failed to reload buildings")
        return 0


def _reload_spawners(game) -> int:
    # Remove current spawner entities and force re-placement
    try:
        world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
        if world is None:
            return 0
        comps = getattr(world, 'components', {})
        sp_cfg = comps.get('SpawnerConfig', {}) or {}
        eids = list(sp_cfg.keys())
        for eid in eids:
            try:
                world.remove_entity(eid)
            except Exception:
                continue
        # Find placement system and reset its state
        created = 0
        try:
            from roguelike_game.ecs.systems.spawner.spawner_placement_system import SpawnerPlacementSystem
            sys = None
            for s in getattr(world, 'update_systems', []) or []:
                if isinstance(s, SpawnerPlacementSystem):
                    sys = s
                    break
            if sys is not None:
                try:
                    sys._loaded = False
                    # Clear internal caches to be safe
                    setattr(sys, '_templates', {})
                    setattr(sys, '_waves', {})
                    # Place immediately so user sees results right after F1
                    sys.update(world)
                    # After update, count placed spawners
                    created = len(world.components.get('SpawnerConfig', {}) or {})
                except Exception:
                    pass
        except Exception:
            pass
        logger.info("[hot_reload] Spawners reloaded: %d entities", int(created))
        return int(created)
    except Exception:
        logger.exception("[hot_reload] Failed to reload spawners")
        return 0


def reload_all_game_data(game, *, force: bool = False) -> Dict[str, int]:
    """Reload particles, buildings, spawners, tiles, spells, entities (monsters) and items.

    Draws a loading screen with a progress bar while reloading. Returns a dict
    with counts per category for logging/telemetry.
    """
    base = DATA_DIR
    groups: List[Tuple[str, List[Path], Callable[[], int]]] = []

    # 1) Spells
    try:
        from .spells_config import reload_spells
        groups.append((
            'Spells', [base / 'spells' / 'spells.json'],
            lambda: (reload_spells() or 0) and len(__import__('roguelike_game.config.spells_config', fromlist=['SPELLS']).SPELLS)
        ))
    except Exception:
        pass
    # 2) Particles
    try:
        from .particles_config import reload_particles
        groups.append((
            'Particles', [base / 'particles' / 'particles.json'],
            lambda: (reload_particles() or 0) and len(__import__('roguelike_game.config.particles_config', fromlist=['PARTICLES']).PARTICLES)
        ))
    except Exception:
        pass
    # 3) Entities (monsters)
    try:
        from roguelike_game.factories.monster.config import reload_monster_defs, MONSTER_DEFS
        groups.append((
            'Entities', [base / 'entities' / 'new_hostiles.json', base / 'entities' / 'new_neutrals.json'],
            lambda: (reload_monster_defs() or 0) or len(MONSTER_DEFS)
        ))
    except Exception:
        pass
    # 4) Items (catalog + prices)
    groups.append((
        'Items', [base / 'items' / 'items.json', base / 'items' / 'items_price.json'],
        lambda: _reload_items(game)
    ))
    # 5) Tiles (assets-based; no JSON dependency -> always safe to clear caches when anything else changed)
    groups.append((
        'Tiles', [],
        lambda: _reload_tiles(game)
    ))
    # 6) Buildings (templates, instances, collisions split)
    try:
        from roguelike_engine.config.config import (
            BUILDINGS_TEMPLATES_PATH, BUILDINGS_INSTANCES_PATH,
            BUILDINGS_COLLISIONS_BY_IMAGE_PATH, BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH,
            BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH,
        )
        groups.append((
            'Buildings', [
                Path(BUILDINGS_TEMPLATES_PATH), Path(BUILDINGS_INSTANCES_PATH),
                Path(BUILDINGS_COLLISIONS_BY_IMAGE_PATH), Path(BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH),
                Path(BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH),
            ],
            lambda: _reload_buildings(game)
        ))
    except Exception:
        pass
    # 7) Spawners (templates, waves, instances)
    groups.append((
        'Spawners', [base / 'spawners' / 'spawners_templates.json', base / 'spawners' / 'spawners_waves.json', base / 'spawners' / 'spawners_instances.json'],
        lambda: _reload_spawners(game)
    ))

    total = len(groups)
    results: Dict[str, int] = {}
    changed_any = False
    # Mostrar feedback inicial siempre
    _draw_loader(game, 0.0, "Comprobando cambios…")
    for idx, (name, paths, fn) in enumerate(groups, start=1):
        try:
            grp_changed = _paths_changed(paths, force=force)
        except Exception:
            grp_changed = True if force else False
        if grp_changed or force or name in ('Tiles',):
            changed_any = True
            _draw_loader(game, idx / total, f"Recargando {name}…")
            logger.info("[hot_reload] Reloading group '%s' (force=%s)", name, bool(force))
            try:
                cnt = int(fn() or 0)
            except Exception:
                logger.exception("[hot_reload] Group '%s' failed", name)
                cnt = 0
            results[name] = cnt
        else:
            logger.info("[hot_reload] Skipping group '%s' (no changes)", name)
            results[name] = 0
    if not changed_any:
        logger.info("[hot_reload] No groups changed (nothing to do)")
        # Feedback visual de 'sin cambios'
        _draw_loader(game, 1.0, "Sin cambios detectados")
    else:
        logger.info("[hot_reload] Reload summary: %s", results)
        _draw_loader(game, 1.0, "Recarga completa")
    return results


# --- Python modules hot-reload (code under src/) --------------------------------

# Separate cache for .py module mtimes
_PY_FILE_MTIMES: Dict[Path, float] = {}

def _module_in_project(mod_name: str, mod_obj) -> Optional[Path]:
    """Return the module file path if it is a project module under src/, else None."""
    try:
        f = getattr(mod_obj, "__file__", None)
        if not f:
            return None
        p = Path(f).resolve()
        # Only reload plain .py files that live inside the project repo under src/
        if p.suffix != ".py":
            return None
        # Must be within the repository root and specifically inside src/
        if not str(p).startswith(str(BASE_DIR / "src")):
            return None
        # Limit to our top-level packages to avoid editor helpers or tests if desired
        allowed_prefixes = (
            "roguelike_game",
            "roguelike_engine",
            "roguelike_editors",
            "minigames",
        )
        if not any(mod_name.startswith(pref + ".") or mod_name == pref for pref in allowed_prefixes):
            return None
        return p
    except Exception:
        return None


def _should_reload_py(path: Path, *, force: bool) -> bool:
    try:
        mtime = path.stat().st_mtime
    except Exception:
        return False
    if force:
        prev = _PY_FILE_MTIMES.get(path)
        _PY_FILE_MTIMES[path] = mtime
        return True
    prev = _PY_FILE_MTIMES.get(path)
    if prev is None or mtime > prev:
        _PY_FILE_MTIMES[path] = mtime
        return True
    return False


def reload_changed_python_modules(*, force: bool = False) -> int:
    """Reload changed Python modules under src/ using importlib.reload.

    Notes:
    - Python hot-reload updates module objects, but existing bound names (e.g.,
      `from mod import func`) will NOT update automatically. Prefer module-level
      accesses (`import mod; mod.func()`).
    - We reload deeper modules first to reduce dependency churn.
    """
    # Collect candidate modules with their paths
    candidates: list[tuple[str, object, Path]] = []
    for name, mod in list(sys.modules.items()):
        if not mod:
            continue
        p = _module_in_project(name, mod)
        if p is None:
            continue
        if _should_reload_py(p, force=force):
            candidates.append((name, mod, p))

    # Sort by package depth (deeper first)
    candidates.sort(key=lambda t: t[0].count("."), reverse=True)

    reloaded = 0
    for name, mod, p in candidates:
        try:
            importlib.reload(mod)
            reloaded += 1
            logger.info("[hot_reload] Code reloaded: %s (%s)", name, p.name)
        except Exception:
            logger.exception("[hot_reload] Failed reloading module: %s", name)
    if reloaded == 0 and candidates:
        # Candidates exist but mtimes did not surpass cache (could be same-second). Force update cache.
        for _n, _m, path in candidates:
            try:
                _PY_FILE_MTIMES[path] = path.stat().st_mtime
            except Exception:
                pass
    return reloaded


def reload_all_game_data_and_code(game, *, force: bool = False) -> Dict[str, int]:
    """Reload Python code (under src/) and all JSON-backed game data.

    Returns a dict summary that includes 'Code' key for modules reloaded.
    """
    code_cnt = reload_changed_python_modules(force=force)
    data_summary = reload_all_game_data(game, force=force)
    try:
        summary: Dict[str, int] = {"Code": int(code_cnt)}
        summary.update({k: int(v or 0) for k, v in (data_summary or {}).items()})
    except Exception:
        summary = {"Code": int(code_cnt)}
    return summary
