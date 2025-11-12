"""Full game data hot-reload with loading screen and grouped checks.

Reloads particles, buildings, spawners, tiles, spells, entities (monsters)
and items. Shows a loading screen while processing.

Public entry point: reload_all_game_data(game, force=False) -> dict[str, int]
"""
from __future__ import annotations

from pathlib import Path
from typing import Callable, Dict, List, Tuple, Any
import logging

from .paths import DATA_DIR
from .mtimes import FILE_MTIMES, paths_changed
from .ui import draw_loader

logger = logging.getLogger(__name__)


# --- Individual reload helpers -------------------------------------------------

def _reload_items(game: Any) -> int:
    try:
        from roguelike_game.managers.items.loader import ItemsLoader
        loader = ItemsLoader()
        items, assets = loader.load()
        setattr(game, "items", items)
        setattr(game, "item_assets", assets)
        logger.info("[hot_reload] Items reloaded: %d entries", len(items or {}))
        return len(items or {})
    except Exception:
        logger.exception("[hot_reload] Failed reloading items.json")
        return 0


def _reload_tiles(game: Any) -> int:
    try:
        from roguelike_engine.tile.utils import assets as tile_assets
        tile_assets._BASE_TILE_IMAGES_CACHE = None
        try:
            tile_assets._SPRITE_CACHE.clear()
        except Exception:
            tile_assets._SPRITE_CACHE = {}
        try:
            game.map.view.invalidate_cache()
        except Exception:
            pass
        logger.info("[hot_reload] Tiles caches cleared and map view invalidated")
        return 1
    except Exception:
        logger.exception("[hot_reload] Failed to clear tiles caches")
        return 0


def _reload_buildings(game: Any) -> int:
    try:
        try:
            from roguelike_engine.config.map_config import global_map_settings
            try:
                setattr(global_map_settings, "use_zones_json", True)
            except Exception:
                pass
            try:
                global_map_settings.refresh_zone_offsets()
            except Exception:
                pass
        except Exception:
            pass
        bm = getattr(game, "buildings", None)
        if bm is None or not hasattr(bm, "init_buildings"):
            logger.warning("[hot_reload] BuildingsManager not found; skipping buildings reload")
            return 0
        buildings = bm.init_buildings() or []
        try:
            setattr(game.entities, "buildings", buildings)
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


def _reload_spawners(game: Any) -> int:
    try:
        world = getattr(getattr(game, "ecs", None), "ecs_world", None)
        if world is None:
            return 0
        comps = getattr(world, "components", {})
        sp_cfg = comps.get("SpawnerConfig", {}) or {}
        eids = list(sp_cfg.keys())
        for eid in eids:
            try:
                world.remove_entity(eid)
            except Exception:
                continue
        try:
            from roguelike_game.ecs.systems.spawner.placement.visuals import (
                preflight_validate_spawner_visuals,
            )
            _ = int(preflight_validate_spawner_visuals() or 0)
        except Exception:
            pass
        created = 0
        try:
            from roguelike_game.ecs.systems.spawner.spawner_placement_system import (
                SpawnerPlacementSystem,
            )
            sys = None
            systems = list(getattr(world, "update_systems", []) or [])
            for s in systems:
                try:
                    if isinstance(s, SpawnerPlacementSystem):
                        sys = s
                        break
                except Exception:
                    continue
            if sys is None:
                for s in systems:
                    try:
                        if type(s).__name__ == "SpawnerPlacementSystem":
                            sys = s
                            break
                    except Exception:
                        continue
            if sys is None:
                for s in systems:
                    try:
                        if (
                            hasattr(s, "update")
                            and hasattr(s, "_loaded")
                            and hasattr(s, "_templates")
                            and hasattr(s, "_waves")
                        ):
                            mod = getattr(type(s), "__module__", "") or ""
                            if "spawner_placement_system" in mod:
                                sys = s
                                break
                    except Exception:
                        continue
            if sys is not None:
                try:
                    sys._loaded = False
                    setattr(sys, "_templates", {})
                    setattr(sys, "_waves", {})
                    sys.update(world)
                    created = len(world.components.get("SpawnerConfig", {}) or {})
                except Exception:
                    pass
            else:
                try:
                    tmp = SpawnerPlacementSystem()
                    tmp.update(world)
                    created = len(world.components.get("SpawnerConfig", {}) or {})
                except Exception:
                    pass
        except Exception:
            pass
        logger.info("[hot_reload] Spawners reloaded: %d entities", int(created))
        return int(created)
    except Exception:
        logger.exception("[hot_reload] Failed to reload spawners")
        return 0


# --- Full grouped reload -------------------------------------------------------

def reload_all_game_data(game: Any, *, force: bool = False) -> Dict[str, int]:
    """Reload particles, buildings, spawners, tiles, spells, entities and items."""
    base = DATA_DIR
    groups: List[Tuple[str, List[Path], Callable[[], int]]] = []

    # 1) Spells
    try:
        from roguelike_game.config.spells_config import reload_spells
        groups.append(
            (
                "Spells",
                [base / "spells" / "spells.json"],
                lambda: (reload_spells() or 0)
                and len(
                    __import__(
                        "roguelike_game.config.spells_config", fromlist=["SPELLS"]
                    ).SPELLS
                ),
            )
        )
    except Exception:
        pass

    # 2) Particles
    try:
        from roguelike_game.config.particles_config import reload_particles
        groups.append(
            (
                "Particles",
                [base / "particles" / "particles.json"],
                lambda: (reload_particles() or 0)
                and len(
                    __import__(
                        "roguelike_game.config.particles_config", fromlist=["PARTICLES"]
                    ).PARTICLES
                ),
            )
        )
    except Exception:
        pass

    # 3) Entities (monsters)
    try:
        from roguelike_game.factories.monster.config import (
            reload_monster_defs,
            MONSTER_DEFS,
        )
        groups.append(
            (
                "Entities",
                [base / "entities" / "new_hostiles.json", base / "entities" / "new_neutrals.json"],
                lambda: (reload_monster_defs() or 0) or len(MONSTER_DEFS),
            )
        )
    except Exception:
        pass

    # 4) Items (DB-backed)
    groups.append(("Items", [base / "roguelike.sqlite3"], lambda: _reload_items(game)))

    # 5) Tiles (assets-based; safe to clear caches when anything else changed)
    groups.append(("Tiles", [], lambda: _reload_tiles(game)))

    # 6) Buildings (templates, instances, collisions split)
    try:
        from roguelike_engine.config.config import (
            BUILDINGS_TEMPLATES_PATH,
            BUILDINGS_INSTANCES_PATH,
            BUILDINGS_COLLISIONS_BY_IMAGE_PATH,
            BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH,
            BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH,
        )
        groups.append(
            (
                "Buildings",
                [
                    Path(BUILDINGS_TEMPLATES_PATH),
                    Path(BUILDINGS_INSTANCES_PATH),
                    Path(BUILDINGS_COLLISIONS_BY_IMAGE_PATH),
                    Path(BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH),
                    Path(BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH),
                ],
                lambda: _reload_buildings(game),
            )
        )
    except Exception:
        pass

    # 7) Spawners (templates, waves, instances)
    groups.append(
        (
            "Spawners",
            [
                base / "spawners" / "spawners_templates.json",
                base / "spawners" / "spawners_waves.json",
                base / "spawners" / "spawners_instances.json",
            ],
            lambda: _reload_spawners(game),
        )
    )

    total = len(groups)
    results: Dict[str, int] = {}
    changed_any = False

    # Initial feedback
    draw_loader(game, 0.0, "Comprobando cambios…")

    for idx, (name, paths, fn) in enumerate(groups, start=1):
        try:
            grp_changed = paths_changed(paths, force=force, cache=FILE_MTIMES)
        except Exception:
            grp_changed = True if force else False
        if grp_changed or force or name in ("Tiles",):
            changed_any = True
            draw_loader(game, idx / total, f"Recargando {name}…")
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
        draw_loader(game, 1.0, "Sin cambios detectados")
    else:
        logger.info("[hot_reload] Reload summary: %s", results)
        draw_loader(game, 1.0, "Recarga completa")

    return results
