"""Reload changed Python modules under src/ using importlib.reload.

Note:
- Bound names imported via `from mod import func` won't update. Prefer module
  access patterns.
- We reload deeper modules first to reduce dependency churn.
"""
from __future__ import annotations

import sys
import importlib
from pathlib import Path
from typing import Optional
import logging

from .paths import BASE_DIR, ALLOWED_PACKAGE_PREFIXES
from .mtimes import PY_FILE_MTIMES, should_reload

logger = logging.getLogger(__name__)


def _module_in_project(mod_name: str, mod_obj) -> Optional[Path]:
    """Return the module file path if it is a project module under src/, else None."""
    try:
        f = getattr(mod_obj, "__file__", None)
        if not f:
            return None
        p = Path(f).resolve()
        if p.suffix != ".py":
            return None
        # Must be within the repository root and specifically inside src/
        if not str(p).startswith(str(BASE_DIR / "src")):
            return None
        if not any(mod_name.startswith(pref + ".") or mod_name == pref for pref in ALLOWED_PACKAGE_PREFIXES):
            return None
        return p
    except Exception:
        return None


def reload_changed_python_modules(*, force: bool = False) -> int:
    """Reload changed Python modules under src/ using importlib.reload."""
    candidates: list[tuple[str, object, Path]] = []
    for name, mod in list(sys.modules.items()):
        if not mod:
            continue
        p = _module_in_project(name, mod)
        if p is None:
            continue
        if should_reload(p, force=force, cache=PY_FILE_MTIMES):
            candidates.append((name, mod, p))

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
        # Candidates exist but mtimes did not surpass cache (same-second writes?). Sync cache.
        for _n, _m, path in candidates:
            try:
                PY_FILE_MTIMES[path] = path.stat().st_mtime
            except Exception:
                pass
    return reloaded
