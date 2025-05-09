# Path: src/roguelike_game/main.py
import pygame
from collections import defaultdict

from roguelike_game.game.game import Game
from roguelike_engine.config import FPS, SCREEN_WIDTH, SCREEN_HEIGHT, DEBUG
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

    # -------- Always initialize performance_log --------
    performance_log = init_debug()

    # Pass performance_log even if DEBUG is False at startup,
    # so overlay can be drawn when toggling DEBUG at runtime
    game = Game(screen, perf_log=performance_log)

    if not hasattr(game, 'state'):
        raise RuntimeError("Game state not initialized properly!")

    # Wrap methods only if starting in DEBUG mode
    if DEBUG:
        game.handle_events = benchmark(performance_log, '1. handle_events')(game.handle_events)
        game.update        = benchmark(performance_log, '2. update')(game.update)
        game.render        = benchmark(performance_log, '3. **TOTAL: RENDER')(game.render)

    # Main loop
    while game.state.running:
        game.handle_events()
        game.update()
        # Always pass performance_log; render_manager checks config.DEBUG internally
        game.render(performance_log)

        pygame.display.flip()
        game.clock.tick(FPS)

    pygame.quit()


if __name__ == "__main__":
    main()
