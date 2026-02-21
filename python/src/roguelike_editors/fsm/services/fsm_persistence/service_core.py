from __future__ import annotations
from typing import Any, Dict, Tuple, List
from pathlib import Path
import json
import logging

from .paths import (
    default_sets_path,
    default_assignments_path,
    default_schema_path,
    default_ids_path,
)
from .validate_json import validate
from .normalize import _ensure_ids_and_defaults
from .linting import _lint_sets_both
from .exports import _export_ids_json
from .loaders import load_sets as _load_sets_impl, load_assignments as _load_assignments_impl

logger = logging.getLogger(__name__)

# Cached lint items for editor UI access
_LAST_LINT: Tuple[List[str], List[str]] = ([], [])
_LAST_LINT_ENRICHED: List[Dict[str, Any]] = []


def get_last_lint() -> Tuple[List[str], List[str]]:
    """Return the last (warnings, errors) produced by save_sets or lint during load."""
    return _LAST_LINT


def get_last_lint_enriched() -> List[Dict[str, Any]]:
    """Return the last enriched lint items.
    Each item: {severity, scope, set_id, state_id?, transition_id?, from?, to?, event?, message}
    """
    return list(_LAST_LINT_ENRICHED)


def save_sets(data: Dict[str, Any], path: str | Path) -> Tuple[List[str], List[str]]:
    """Save FSM sets to JSON file (pretty, deterministic).
    Flow: (1) normalize/migrate, (2) validate (optional), (3) static lint, (4) save, (5) export ids index JSON.
    Returns (warnings, errors) from linting for caller UI.
    """
    # 1) Normalize/migrate in-memory (ids/defaults)
    try:
        _ensure_ids_and_defaults(data)
    except Exception:
        # Keep going even if normalization fails
        pass
    # 2) Validate (optional if schema missing)
    try:
        validate(data, default_schema_path())
    except Exception:
        # Do not block save during authoring
        pass
    # 2b) Lint cross-field rules (non-blocking)
    warns: List[str] = []
    errs: List[str] = []
    enriched: List[Dict[str, Any]] = []
    try:
        warns, errs, enriched = _lint_sets_both(data)
        global _LAST_LINT, _LAST_LINT_ENRICHED
        _LAST_LINT = (list(warns), list(errs))
        _LAST_LINT_ENRICHED = list(enriched)
        for msg in warns:
            try:
                logger.warning("[FSMSets][lint][warning] %s", msg)
            except Exception:
                pass
        if errs:
            # Raise to allow interested callers to catch and surface; we still swallow below
            raise ValueError("; ".join(errs))
    except Exception:
        # Do not block authoring; callers may surface errors
        pass
    # 3) Save pretty, deterministic
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    with open(str(p), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2, sort_keys=True)
    # 4) Export ids index JSON (non-fatal on failure)
    try:
        _export_ids_json(data, default_ids_path())
    except Exception:
        pass
    # Return lint results so callers can surface UI feedback
    try:
        return _LAST_LINT
    except Exception:
        return (warns, errs)


def load_all(
    sets_path: Path | None = None,
    assignments_path: Path | None = None,
    schema_path: Path | None = None,
) -> Tuple[Dict[str, Any], Dict[str, Any]]:
    """Load sets and assignments with optional validation. Returns (sets, assignments)."""
    sets_path = sets_path or default_sets_path()
    assignments_path = assignments_path or default_assignments_path()
    schema_path = schema_path or default_schema_path()

    sets = _load_sets_impl(sets_path)
    try:
        validate(sets, schema_path)
    except Exception:
        # Keep running even if schema invalid or not present
        pass
    try:
        assignments = _load_assignments_impl(assignments_path)
    except FileNotFoundError:
        assignments = {"by_archetype": {}, "by_eid": {}}
    # Compute and cache initial lint for editor badges so they appear on first render
    try:
        warns, errs, enriched = _lint_sets_both(sets)
        global _LAST_LINT, _LAST_LINT_ENRICHED
        _LAST_LINT = (list(warns), list(errs))
        _LAST_LINT_ENRICHED = list(enriched)
    except Exception:
        # Don't block load flow if linting fails
        pass
    return sets, assignments
