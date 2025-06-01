# Path: src/roguelike_game/main.py

import pygame
from collections import defaultdict

from roguelike_engine.config.config import SCREEN_WIDTH, SCREEN_HEIGHT
from roguelike_game.game.game import Game


def init_debug_log():
    """
    Activa el cursor y devuelve un diccionario para logging de performance.
    """
    pygame.mouse.set_visible(True)
    return defaultdict(list)


def main():
    pygame.init()
    screen = pygame.display.set_mode(
        (SCREEN_WIDTH, SCREEN_HEIGHT),
        pygame.HWSURFACE | pygame.DOUBLEBUF | pygame.RESIZABLE
    )
    pygame.display.set_caption("Roguelike")

    # Inicializamos el registro de rendimiento (performance_log)
    performance_log = init_debug_log()

    # Creamos la instancia de Game
    game = Game(
        screen,
        perf_log=performance_log,
        map_name=None,
        loading_bg="ui/background_ini.png"
    )

    # Asegurarnos de que el estado se inicializó correctamente
    if not hasattr(game, "state"):
        raise RuntimeError("Game state not initialized properly!")

    try:
        # Arrancamos el bucle principal
        game.run()
    except Exception as e:
        # Podrías aquí registrar el error en un log si lo deseas
        print(f"[ERROR] Uncaught exception en main(): {e}")
        # Propagar la excepción para que Pygame se cierre correctamente
        raise
    finally:
        # Llamamos a un método de Game para que se encargue de
        # guardar TODO lo necesario antes de hacer pygame.quit()
        game.shutdown()
        pygame.quit()


if __name__ == "__main__":
    main()
