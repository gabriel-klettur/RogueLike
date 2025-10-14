from __future__ import annotations

import logging

import pygame
import roguelike_engine.config.config as config
from roguelike_engine.camera.camera import Camera
from roguelike_engine.z_layer.state import ZState

from ..types import InitContext

logger = logging.getLogger(__name__)


def setup_display(ctx: InitContext) -> None:
    g = ctx.game
    g.clock = pygame.time.Clock()
    g.font = pygame.font.SysFont(config.FONT_NAME, config.FONT_SIZE)
    g.camera = Camera(config.SCREEN_WIDTH, config.SCREEN_HEIGHT)
    g.z_state = ZState()
    g.perf_log = ctx.perf_log
