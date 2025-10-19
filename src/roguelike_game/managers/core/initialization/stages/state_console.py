from __future__ import annotations

import logging

import pygame
from roguelike_engine.console import register_commands
from roguelike_engine.console.console_controller import ConsoleController
from roguelike_engine.console.console_events import ConsoleEvents
from roguelike_engine.console.console_model import CommandRegistry, ConsoleState
from roguelike_engine.console.console_view import ConsoleView

from roguelike_game.managers.core.state import GameState

from ..types import InitContext

logger = logging.getLogger(__name__)


def init_state(ctx: InitContext) -> None:
    g = ctx.game
    g.state = GameState()
    g.console_state = ConsoleState()
    g.command_registry = CommandRegistry()
    register_commands(g.command_registry, g)
    g.console_controller = ConsoleController(g.console_state, g.command_registry)
    g.console_events = ConsoleEvents(g.console_controller)
    screen_w, screen_h = g.screen.get_size()
    console_h = screen_h // 3
    console_rect = pygame.Rect(0, screen_h - console_h, screen_w, console_h)
    g.console_view = ConsoleView(g.console_state, console_rect)
