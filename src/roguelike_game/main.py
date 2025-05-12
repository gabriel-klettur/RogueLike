# Path: src/roguelike_game/main.py

import pygame
from collections import defaultdict

from roguelike_game.game.game import Game
from roguelike_engine.config import FPS, SCREEN_WIDTH, SCREEN_HEIGHT
from roguelike_engine.utils.benchmark import benchmark

def init_debug():
    pygame.mouse.set_visible(True)
    return defaultdict(list)

def main():
    pygame.init()
    screen = pygame.display.set_mode(
        (SCREEN_WIDTH, SCREEN_HEIGHT),
        pygame.HWSURFACE | pygame.DOUBLEBUF
    )
    pygame.display.set_caption("Roguelike")

    # -------- Inicializar performance_log --------
    performance_log = init_debug()

    # Creamos el juego pasándole el log
    game = Game(
        screen,
        perf_log        = performance_log,        
        map_name        = None
    )
    if not hasattr(game, 'state'):
        raise RuntimeError("Game state not initialized properly!")

    try:
        game.run()  
    except Exception as e:
        print(f"An error occurred: {e}")
        raise
    finally:
        pygame.quit()
    


if __name__ == "__main__":
    main()
