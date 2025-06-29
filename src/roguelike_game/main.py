import pygame
from roguelike_engine.utils.loader import load_image
from roguelike_engine.config.config import SCREEN_WIDTH, SCREEN_HEIGHT

from roguelike_game.utils.debug import init_debug_log
from roguelike_game.utils.benchmark import setup_benchmark_logger, save_benchmarks
from roguelike_game.game.game import Game


def main() -> None:
    # Inicialización de Pygame y pantalla
    pygame.init()
    screen = pygame.display.set_mode(
        (SCREEN_WIDTH, SCREEN_HEIGHT),
        pygame.HWSURFACE | pygame.DOUBLEBUF | pygame.RESIZABLE
    )

    # Icono y título
    icon = load_image("assets/ui/icon.png")
    pygame.display.set_icon(icon)
    pygame.display.set_caption("Roguelike")

    # Registros de performance y benchmarks
    performance_log = init_debug_log()
    bench_logger = setup_benchmark_logger()

    # Crear y validar juego
    game = Game(
        screen,
        perf_log=performance_log,
        map_name=None,
        loading_bg="ui/background_ini.png"
    )
    if not hasattr(game, 'state'):
        raise RuntimeError("Game state not initialized properly!")

    try:
        game.run()
    except Exception:
        bench_logger.exception("Uncaught exception in main loop")
        raise
    finally:
        game.shutdown()
        save_benchmarks(performance_log)
        pygame.quit()


if __name__ == '__main__':
    main()