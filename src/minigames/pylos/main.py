"""Pylos mini-game entry point.

Provides a `run()` function that initializes pygame, wires MVC components,
and executes the main loop. Designed for future integration with the larger
project while remaining runnable as a standalone mini-game.
"""

from __future__ import annotations

from typing import Final

import pygame

from pylos.controller import PylosController
from pylos.model import GameState
from pylos.view import PylosView
from pylos.view.view import ViewConfig
from pylos.view.menu import ConfigMenu
from pylos.data.config_store import load_view_prefs, save_view_prefs


FPS: Final[int] = 60


def _init_pygame(cfg: ViewConfig) -> pygame.Surface:
    """Initialize pygame and create the main screen surface."""
    pygame.init()
    screen = pygame.display.set_mode((cfg.width, cfg.height))
    pygame.display.set_caption("Pylos Mini-game")
    return screen


def run() -> int:
    """Run the Pylos mini-game loop.

    Returns:
        Exit code (0 for normal quit).
    """
    cfg = ViewConfig()
    # Enable assets: use board.png and marble sprites
    cfg.use_assets = True
    # Initial calibration for board.png: normalized rect spanning 4x4 hole centers
    # Tweak these if alignment looks off
    cfg.grid_norm_left = 0.185
    cfg.grid_norm_top = 0.185
    cfg.grid_norm_width = 0.63
    cfg.grid_norm_height = 0.63
    screen = _init_pygame(cfg)
    clock = pygame.time.Clock()

    view = PylosView(screen, cfg)
    # Initial load of persisted calibration (grid, board manual, marble size) and UI flags
    loaded = load_view_prefs(cfg, view)

    while True:
        # 1) Show configuration menu
        menu = ConfigMenu(screen, cfg)
        game_cfg = menu.run()

        # 2) Create game state and controller based on menu
        # Reload persisted values in case user returned to menu previously
        loaded = load_view_prefs(cfg, view)
        state = GameState()
        state.current_player = game_cfg.starting_player
        state.allow_square_removal = (game_cfg.game_mode == "Experto")
        controller = PylosController(
            state,
            view,
            ai_enabled=game_cfg.ai_enabled,
            ai_player=game_cfg.ai_player,
        )
        controller.ai.set_depth(game_cfg.ai_depth)
        # Apply persisted UI flags (if any)
        try:
            if isinstance(loaded.get("show_labels"), bool):
                controller.input.show_labels = bool(loaded["show_labels"])  # type: ignore[attr-defined]
            if isinstance(loaded.get("show_holes"), bool):
                controller.input.show_holes = bool(loaded["show_holes"])  # type: ignore[attr-defined]
            if isinstance(loaded.get("show_indices"), bool):
                controller.input.show_indices = bool(loaded["show_indices"])  # type: ignore[attr-defined]
        except Exception:
            pass

        # 3) Game loop
        running = True
        while running:
            # Handle input/events
            for event in pygame.event.get():
                controller.handle_event(event)

            if not controller.is_running():
                running = False
                break

            if controller.request_menu():
                # Break to outer loop to show menu again
                break

            # Update
            dt_ms = clock.tick(FPS)  # caps FPS and returns elapsed milliseconds
            _dt = dt_ms / 1000.0
            controller.update(_dt)

            # Render
            view.draw(
                state,
                controller.hovered_cell(),
                controller.info_text(),
                controller.is_ai_p2(),
                controller.selected_src(),
                controller.cursor_player_id(),
                controller.removal_cursor_player_id(),
                controller.show_holes(),
                controller.show_indices(),
                controller.show_labels(),
                controller.calibration_mode(),
            )
            pygame.display.flip()

        # Persist current calibration and UI flags before returning to menu or quitting
        save_view_prefs(
            cfg,
            view,
            show_labels=controller.show_labels(),
            show_holes=controller.show_holes(),
            show_indices=controller.show_indices(),
        )
        # If controller requested menu, continue outer loop; else exit
        if not controller.is_running():
            break
        # else: loop continues and menu will be shown again

    # Final save on exit (persist with last known controller state if available)
    try:
        save_view_prefs(
            cfg,
            view,
            show_labels=controller.show_labels(),  # type: ignore[name-defined]
            show_holes=controller.show_holes(),    # type: ignore[name-defined]
            show_indices=controller.show_indices(),# type: ignore[name-defined]
        )
    except Exception:
        save_view_prefs(cfg, view)
    pygame.quit()
    return 0


if __name__ == "__main__":
    raise SystemExit(run())

