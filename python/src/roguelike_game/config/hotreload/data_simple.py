"""Lightweight data hot-reload (JSON/DB under data/).

Entry point: reload_all_data(game=None, force=False)

Only checks a curated list of files and calls their associated reloaders
when the file mtime changes (or when forced). Designed for quick, targeted
reloads bound to a key (e.g., F1) during development.
"""
from __future__ import annotations

from pathlib import Path
from typing import Callable, Dict, List, Tuple
import logging

from .paths import DATA_DIR
from .mtimes import FILE_MTIMES, should_reload

logger = logging.getLogger(__name__)

Reloader = Tuple[Path, Callable[[], None], str]


def _gather_reloaders() -> List[Reloader]:
    """Return the list of known reloadable data files and their reloaders.

    Extend this list as the project grows.
    """
    reloaders: List[Reloader] = []
    try:
        # Spells
        from roguelike_game.config.spells_config import reload_spells  # lazy import
        reloaders.append((DATA_DIR / "spells" / "spells.json", reload_spells, "spells.json"))
    except Exception:
        pass
    try:
        # Particles
        from roguelike_game.config.particles_config import reload_particles
        reloaders.append((DATA_DIR / "particles" / "particles.json", reload_particles, "particles.json"))
    except Exception:
        pass
    try:
        # Monsters (hostiles + optional neutrals)
        from roguelike_game.factories.monster.config import reload_monster_defs
        # We use hostiles as the sentinel, the reloader handles neutrals if present
        reloaders.append(
            (
                DATA_DIR / "entities" / "new_hostiles.json",
                reload_monster_defs,
                "entities/new_hostiles.json(+neutrals)",
            )
        )
    except Exception:
        pass
    try:
        logger.debug("[hot_reload] Candidates: %s", [str(p) for (p, _fn, _n) in reloaders])
    except Exception:
        pass
    return reloaders


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
            if should_reload(path, force=force, cache=FILE_MTIMES):
                fn()
                done += 1
                logger.info("[hot_reload] Reloaded %s", name)
        except Exception:
            logger.exception("[hot_reload] Failed reloading %s", name)
    if done == 0:
        logger.info("[hot_reload] No changes detected under data/ (nothing reloaded)")
    else:
        logger.info("[hot_reload] Completed reload: %d module(s)", done)
    return done
