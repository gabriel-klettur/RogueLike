from __future__ import annotations

import sys
import os


def main() -> int:
    # Ensure src is on path
    here = os.path.dirname(os.path.abspath(__file__))
    repo_root = os.path.abspath(os.path.join(here, os.pardir))
    src_dir = os.path.join(repo_root, "src")
    if src_dir not in sys.path:
        sys.path.insert(0, src_dir)

    try:
        from roguelike_game.ecs.systems.spawner.placement.visuals import (
            preflight_validate_spawner_visuals,
        )
    except Exception as e:
        print(f"[preflight_cli] Error importing preflight: {e}")
        return 2

    try:
        updated = int(preflight_validate_spawner_visuals() or 0)
        print(f"[preflight_cli] Spawner visuals updated: {updated}")
        return 0
    except Exception as e:
        print(f"[preflight_cli] Preflight failed: {e}")
        return 3


if __name__ == "__main__":
    raise SystemExit(main())
