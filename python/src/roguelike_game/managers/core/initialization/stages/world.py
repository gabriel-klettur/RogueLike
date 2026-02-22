from __future__ import annotations

import logging
import time
from types import SimpleNamespace

from roguelike_game.managers.map import MapManager
from roguelike_engine.worlds.service import world_service
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.world.world import WorldManager
from roguelike_engine.world.world_config import WORLD_CONFIG

from ..types import InitContext

logger = logging.getLogger(__name__)


def setup_world(ctx: InitContext) -> None:
    g = ctx.game
    # Activar mundo actual para redirigir rutas (incluye buildings en fase transición)
    try:
        world_service.activate(getattr(global_map_settings, 'current_world', 'base'))
    except Exception:
        pass
    g.world = WorldManager(WORLD_CONFIG, load_state_on_init=False)
    g._last_autosave_time = time.time()


def load_world_state(ctx: InitContext) -> None:
    try:
        ctx.game.world.load_world()
    except Exception as e:
        logger.error(f"Error al cargar mundo: {e}")


def handle_deferred_levels(ctx: InitContext) -> None:
    g = ctx.game
    for lvl in list(getattr(g.world, "_pending_levels", [])):
        state = g.world._pending_levels.pop(lvl)
        mgr = MapManager(lvl)
        mgr.deserialize_state(state)
        g.world.maps[lvl] = mgr


def init_map(ctx: InitContext) -> None:
    g = ctx.game
    if g.world.current_level:
        g.world.load_level(g.world.current_level)
        g.map = g.world.maps[g.world.current_level]
    else:
        g.map = MapManager(ctx.map_name)
        g.world.maps[g.map.name] = g.map
        g.world.current_level = g.map.name
