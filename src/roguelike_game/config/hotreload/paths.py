"""Paths and package filters for hot-reload utilities.

This module centralizes project paths to avoid duplication and to
make unit testing simpler by allowing targeted patching.
"""
from __future__ import annotations

from pathlib import Path
from typing import Tuple

# Project root (same approach as other config modules)
BASE_DIR: Path = Path(__file__).resolve().parents[3]

# Data directory (JSON, sqlite, etc.)
DATA_DIR: Path = BASE_DIR / "data"

# Top-level packages we consider for Python code hot-reload
ALLOWED_PACKAGE_PREFIXES: Tuple[str, ...] = (
    "roguelike_game",
    "roguelike_engine",
    "roguelike_editors",
    "minigames",
)
