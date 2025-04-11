import sys
import os
import time
import pygame
from collections import defaultdict

# Agregar el path del proyecto
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from roguelike_project.engine.game.game import Game
from roguelike_project.config import DEBUG, FPS, SCREEN_WIDTH, SCREEN_HEIGHT
from roguelike_project.utils.debug_overlay import render_debug_overlay
from roguelike_project.utils.benchmark import benchmark  # ✅ Decorador profesional

# --- Debug Tools ---
def init_debug():
    pygame.mouse.set_visible(True)
    return defaultdict(list)

# --- Main Loop ---
def main():
    pygame.init()
    screen = pygame.display.set_mode((SCREEN_WIDTH, SCREEN_HEIGHT), pygame.HWSURFACE | pygame.DOUBLEBUF)
    pygame.display.set_caption("Roguelike")

    performance_log = init_debug() if DEBUG else None

    game = Game(screen, perf_log=performance_log)

    if not hasattr(game, 'state'):
        raise RuntimeError("Game state not initialized properly!")

    if DEBUG:
        game.handle_events = benchmark(performance_log, '1. handle_events')(game.handle_events)
        game.update = benchmark(performance_log, '2. update')(game.update)
        game.render = benchmark(performance_log, '3. **TOTAL: RENDER')(game.render)

    #! --------------------------------------------------------------------------------------- !#
    #! -------------------------------  Main game engine loop -------------------------------- !#
    #! --------------------------------------------------------------------------------------- !#
    while game.state.running:        
        game.handle_events()
        game.update()
        game.render(performance_log)    

        pygame.display.flip()
        game.state.clock.tick(FPS)

    pygame.quit()

if __name__ == "__main__":
    main()