import sys
import os
import time
import pygame

# Agregar el path del proyecto
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from roguelike_project.engine.game.game import Game
from roguelike_project.config import DEBUG, FPS
from roguelike_project.utils.debug_overlay import render_debug_overlay
from roguelike_project.utils.benchmark import benchmark  # ✅ Decorador profesional

# --- Debug Tools ---
def init_debug():
    pygame.mouse.set_visible(True)
    return {
        'handle_events': [],
        'update': [],
        'render': [],
        'frame_times': []
    }

# --- Main Loop ---
def main():
    pygame.init()
    screen = pygame.display.set_mode((1200, 800), pygame.HWSURFACE | pygame.DOUBLEBUF)
    pygame.display.set_caption("Roguelike")

    performance_log = init_debug() if DEBUG else None

    game = Game(screen)

    if not hasattr(game, 'state'):
        raise RuntimeError("Game state not initialized properly!")

    # ✅ Decorar dinámicamente funciones si estamos en modo DEBUG
    if DEBUG:
        game.handle_events = benchmark(performance_log, 'handle_events')(game.handle_events)
        game.update = benchmark(performance_log, 'update')(game.update)
        game.render = benchmark(performance_log, 'render')(game.render)

    while game.state.running:
        frame_start = time.perf_counter()

        game.handle_events()
        game.update()
        game.render()

        if DEBUG:
            performance_log["frame_times"].append(time.perf_counter() - frame_start)
            render_debug_overlay(screen, performance_log)

        pygame.display.flip()
        game.state.clock.tick(FPS)

    pygame.quit()

if __name__ == "__main__":
    main()
