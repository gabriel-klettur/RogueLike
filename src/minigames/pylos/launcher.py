"""Launcher for the Pylos mini-game.

This module makes it easy to start the mini-game either as:
  - `python -m src_pylos.launcher` (preferred)
  - `python src_pylos/launcher.py` (fallback)
"""

from __future__ import annotations

import os
import sys


def _ensure_project_root_on_syspath() -> None:
    """Ensure the project root (parent of this package) is on sys.path.

    This allows running the launcher directly as a script without breaking
    package imports like `import src_pylos.main`.
    """
    pkg_dir = os.path.dirname(os.path.abspath(__file__))
    project_root = os.path.dirname(pkg_dir)
    if project_root not in sys.path:
        sys.path.insert(0, project_root)


def main() -> int:
    """Import and run the Pylos mini-game."""
    try:
        # Preferred when executed as a module
        from .main import run  # type: ignore
    except Exception:
        # Fallback when executed as a script
        _ensure_project_root_on_syspath()
        from pylos.main import run  # type: ignore
    return run()


if __name__ == "__main__":
    raise SystemExit(main())

