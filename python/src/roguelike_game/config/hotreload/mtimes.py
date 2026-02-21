"""MTime caches and helpers for hot-reload decisions.

Provides tiny, side-effect-free helpers to decide whether a path changed
since last observation. We keep separate caches for data files and python
modules to let callers reason independently.
"""
from __future__ import annotations

from pathlib import Path
from typing import Dict, Iterable
import logging

logger = logging.getLogger(__name__)

# Public caches (module-level, simple and testable)
FILE_MTIMES: Dict[Path, float] = {}
PY_FILE_MTIMES: Dict[Path, float] = {}


def should_reload(path: Path, *, force: bool, cache: Dict[Path, float]) -> bool:
    """Return True if the file should be reloaded.

    - If force is True, we always return True (and update cache if stat ok).
    - If the file does not exist, return False.
    - If mtime increased or is first time, cache it and return True.
    """
    try:
        mtime = path.stat().st_mtime
    except FileNotFoundError:
        logger.debug("[hot_reload] Missing file, skipping: %s", str(path))
        return False
    except Exception:
        return False

    if force:
        prev = cache.get(path)
        cache[path] = mtime
        logger.debug("[hot_reload] Force reload: %s (prev=%s -> now=%s)", str(path), str(prev), str(mtime))
        return True

    prev = cache.get(path)
    if prev is None or mtime > prev:
        cache[path] = mtime
        if prev is None:
            logger.debug("[hot_reload] First observation -> reload: %s (mtime=%s)", str(path), str(mtime))
        else:
            logger.debug(
                "[hot_reload] Detected change -> reload: %s (prev=%s -> now=%s)",
                str(path), str(prev), str(mtime)
            )
        return True
    logger.debug("[hot_reload] No changes: %s (prev=%s == now=%s)", str(path), str(prev), str(mtime))
    return False


def paths_changed(paths: Iterable[Path], *, force: bool, cache: Dict[Path, float]) -> bool:
    """Return True if any of the given paths changed according to cache.

    If force is True and at least one file exists, returns True.
    """
    any_existing = False
    changed = False
    for p in paths:
        try:
            if p.exists():
                any_existing = True
                if should_reload(p, force=force, cache=cache):
                    changed = True
            else:
                logger.debug("[hot_reload] Missing candidate (ignored in group): %s", str(p))
        except Exception:
            continue
    if force and any_existing:
        return True
    return changed
