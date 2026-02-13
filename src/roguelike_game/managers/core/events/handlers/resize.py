"""
Handler for window resize (VIDEORESIZE) events.

Updates the display surface, camera dimensions and global config so that
the game stays centred on the player and all UI elements adapt to the new
window size.
"""

import pygame
import roguelike_engine.config.config as config

import logging
logger = logging.getLogger(__name__)


def handle_resize(game, events):
    """Scan *events* for VIDEORESIZE; apply the first one found.

    Returns the (possibly replaced) event list with the resize event removed
    so downstream handlers don't see a stale event.
    """
    resize_event = None
    for ev in events:
        if ev.type == pygame.VIDEORESIZE:
            resize_event = ev

    if resize_event is None:
        return events

    new_w, new_h = resize_event.w, resize_event.h
    # Enforce a reasonable minimum so the window stays usable
    new_w = max(640, new_w)
    new_h = max(480, new_h)

    logger.info("Window resized to %dx%d", new_w, new_h)

    # 1) Re-create the display surface with the same flags
    new_screen = pygame.display.set_mode(
        (new_w, new_h),
        pygame.HWSURFACE | pygame.DOUBLEBUF | pygame.RESIZABLE,
    )

    # 2) Update the Game's screen reference (used everywhere via game.screen)
    game.screen = new_screen

    # 3) Update camera so centring maths use the new dimensions
    if hasattr(game, 'camera'):
        game.camera.resize(new_w, new_h)

    # 4) Keep the global config constants in sync for any code that reads them
    config.SCREEN_WIDTH = new_w
    config.SCREEN_HEIGHT = new_h

    # 5) Propagate new screen to all subsystems that cache their own reference
    _propagate_screen(game, new_screen)

    # Strip all VIDEORESIZE events so they aren't processed again downstream
    return [ev for ev in events if ev.type != pygame.VIDEORESIZE]


def _propagate_screen(game, new_screen):
    """Update cached screen references across subsystems."""
    # Renderer
    try:
        if hasattr(game, 'renderer'):
            game.renderer.screen = new_screen
    except Exception:
        pass
    # Menu manager
    try:
        if hasattr(game, 'menu'):
            game.menu.screen = new_screen
    except Exception:
        pass
    # Class selector
    try:
        if hasattr(game, 'class_selector'):
            game.class_selector.screen = new_screen
    except Exception:
        pass
    # ECS manager (roguelike_game.managers.ecs)
    try:
        if hasattr(game, 'ecs'):
            game.ecs.screen = new_screen
    except Exception:
        pass
    # ECS world manager (roguelike_game.ecs.core.manager)
    try:
        if hasattr(game, 'ecs') and hasattr(game.ecs, 'ecs_world'):
            game.ecs.ecs_world.screen = new_screen
    except Exception:
        pass
    # RenderSystem (stores its own screen ref)
    try:
        if hasattr(game, 'ecs') and hasattr(game.ecs, 'ecs_world'):
            for sys in getattr(game.ecs.ecs_world, 'render_systems', []):
                if hasattr(sys, 'screen'):
                    sys.screen = new_screen
    except Exception:
        pass
    # Save subsystem (menu.saves)
    try:
        if hasattr(game, 'menu') and hasattr(game.menu, 'saves'):
            game.menu.saves.screen = new_screen
    except Exception:
        pass
    # Console view rect (anchored to bottom of screen)
    try:
        if hasattr(game, 'console_view'):
            sw, sh = new_screen.get_size()
            console_h = sh // 3
            game.console_view.rect = pygame.Rect(0, sh - console_h, sw, console_h)
    except Exception:
        pass
