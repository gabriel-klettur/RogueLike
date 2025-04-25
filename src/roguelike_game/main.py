import sys
import os
import pygame
from collections import defaultdict

sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from src.roguelike_game.game.game import Game
from src.roguelike_engine.config import FPS, SCREEN_WIDTH, SCREEN_HEIGHT
import src.roguelike_engine.config as config

from src.roguelike_engine.utils.benchmark import benchmark 

# --- Debug Tools ---
def init_debug():
    pygame.mouse.set_visible(True)
    return defaultdict(list)

# --- Main Loop ---
def main():
    pygame.init()
    screen = pygame.display.set_mode((SCREEN_WIDTH, SCREEN_HEIGHT), pygame.HWSURFACE | pygame.DOUBLEBUF)
    pygame.display.set_caption("Roguelike")

    performance_log = init_debug() if config.DEBUG else None

    game = Game(screen, perf_log=performance_log)

    if not hasattr(game, 'state'):
        raise RuntimeError("Game state not initialized properly!")

    if config.DEBUG:
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