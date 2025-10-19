"""Inventory I/O utilities.

Small helpers to keep JSON file handling and UUID normalization in one place.
"""
from __future__ import annotations

import json
import os
import uuid
from typing import Any, Dict, Optional


def make_dirs_for(path: str) -> None:
    """Ensure parent directory exists for the target path."""
    directory = os.path.dirname(path)
    if directory:
        os.makedirs(directory, exist_ok=True)


def ensure_active_file(path: str, default_obj: Optional[Dict[str, Any]] = None) -> None:
    """Ensure an active JSON file exists with a valid JSON payload.

    If file is missing or empty/invalid, writes `default_obj` (defaults to `{}`).
    """
    if default_obj is None:
        default_obj = {}
    make_dirs_for(path)
    if not os.path.exists(path):
        with open(path, "w", encoding="utf-8") as f:
            json.dump(default_obj, f, indent=2)
        return
    # If exists but invalid JSON, rewrite with default
    try:
        with open(path, "r", encoding="utf-8") as f:
            json.load(f)
    except Exception:
        with open(path, "w", encoding="utf-8") as f:
            json.dump(default_obj, f, indent=2)


def read_json_or(path: str, default_obj: Any) -> Any:
    """Read JSON file or return `default_obj` if missing/invalid."""
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return default_obj


def write_json(path: str, payload: Any) -> None:
    """Write JSON with UTF-8 and indentation."""
    make_dirs_for(path)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2)


def safe_uuid_str(value: Optional[str], fallback: Optional[str] = None) -> str:
    """Return a valid UUID string.

    - If `value` is a valid UUID string, return it.
    - Else if `fallback` provided and valid, return fallback.
    - Else generate a new UUID4.
    """
    candidates = [value, fallback]
    for cand in candidates:
        if not cand:
            continue
        try:
            uuid.UUID(str(cand))
            return str(cand)
        except Exception:
            pass
    return str(uuid.uuid4())
