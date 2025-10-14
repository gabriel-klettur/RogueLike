from __future__ import annotations

import cProfile
import logging
import pstats

from roguelike_engine.log_config import build_log_filepath
from roguelike_game.managers.ecs import ECSManager

from ..types import InitContext

logger = logging.getLogger(__name__)


def init_ecs(ctx: InitContext) -> None:
    g = ctx.game
    pr = cProfile.Profile()
    pr.enable()
    g.ecs = ECSManager(ctx.screen, g.map, g.buildings, ctx.perf_log)
    g.ecs.ecs_world.state = g.state
    pr.disable()
    logf = build_log_filepath(
        "ecs_init_profile", directory="logs/profile", extension="log", now_dt=ctx.ts_dt
    )
    with open(logf, "w") as pf:
        p = pstats.Stats(pr, stream=pf)
        p.sort_stats("tottime").print_stats(30)
    try:
        snap = getattr(g.world, "npc_inventories", None) or {}
        if snap:
            g.ecs.ecs_world.components["NPCInventorySnapshot"] = dict(snap)
    except Exception:
        pass
