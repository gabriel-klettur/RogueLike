"""Audit helpers reporting changes between old and new instances."""
from __future__ import annotations

import logging
from typing import Dict, Iterable, List

logger = logging.getLogger(__name__)

__all__ = ["audit_changes"]


def audit_changes(old_instances: Iterable[dict], new_instances: Iterable[dict]) -> None:
    """Log ID-level differences between old and new instance payloads."""

    old_map = _as_map(old_instances)
    new_map = _as_map(new_instances)

    old_ids = set(old_map.keys())
    new_ids = set(new_map.keys())

    added = sorted(new_ids - old_ids)
    removed = sorted(old_ids - new_ids)

    if added:
        logger.info("[Buildings][SaveSplit][Audit] Added IDs: %s", added)
    if removed:
        logger.info("[Buildings][SaveSplit][Audit] Removed IDs: %s", removed)

    for iid in sorted(new_ids & old_ids):
        _log_field_diffs(iid, old_map[iid], new_map[iid])


def _as_map(instances: Iterable[dict]) -> Dict[int, dict]:
    result: Dict[int, dict] = {}
    for entry in instances or []:
        try:
            iid_raw = entry.get("id") if isinstance(entry, dict) else None
            iid = int(iid_raw) if iid_raw is not None and str(iid_raw).isdigit() else None
        except Exception:
            iid = None
        if iid is None:
            continue
        result[iid] = entry
    return result


def _log_field_diffs(instance_id: int, old: dict, new: dict) -> None:
    diffs = {}
    for key in ("template_id", "zone", "rel_x", "rel_y"):
        if old.get(key) != new.get(key):
            diffs[key] = {"old": old.get(key), "new": new.get(key)}
    if diffs:
        logger.info("[Buildings][SaveSplit][Audit] Modified ID %s: %s", instance_id, diffs)
