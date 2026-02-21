from __future__ import annotations
from pathlib import Path

from roguelike_engine.config.config import ASSETS_DIR


def _abs_to_rel_asset_path(path: str) -> str:
    abs_path = Path(path).resolve()
    assets_root = Path(ASSETS_DIR).resolve()
    try:
        rel = abs_path.relative_to(assets_root)
        return f"assets/{rel.as_posix()}"
    except ValueError:
        return str(path).replace("\\", "/")
