from __future__ import annotations

import logging

from ..types import InitContext

logger = logging.getLogger(__name__)


def preflight_spawner_visuals(ctx: InitContext) -> None:
    """Ensure spawner visuals are materialized before buildings load.

    - Validates all spawner instance "visuals" entries.
    - Creates missing building instances and tags them as spawner visuals.
    - Persists updates so Buildings loader will include them at startup.
    """
    try:
        from roguelike_game.ecs.systems.spawner.placement.visuals import (
            preflight_validate_spawner_visuals as _preflight,
        )
    except Exception:
        return
    try:
        cnt = int(_preflight() or 0)
        if cnt:
            logger.info("[Init] Preflight spawner visuals updated %d instance(s)", cnt)
    except Exception:
        # Never block initialization if preflight fails
        pass
