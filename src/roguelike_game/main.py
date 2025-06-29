# Path: src/roguelike_game/main.py
import pygame
from roguelike_engine.utils.loader import load_image
from roguelike_engine.config.config import SCREEN_WIDTH, SCREEN_HEIGHT

from roguelike_game.utils.debug import init_debug_log
from roguelike_game.utils.benchmark import setup_benchmark_logger, save_benchmarks
from roguelike_game.game.core.game import Game
from typing import Tuple, Any, Dict


def init_pygame() -> None:
    """Inicializa Pygame y hace visible el cursor."""
    pygame.init()
    pygame.mouse.set_visible(True)


def create_screen() -> pygame.Surface:
    """Configura el modo de pantalla con flags HWSURFACE, DOUBLEBUF y RESIZABLE."""
    return pygame.display.set_mode(
        (SCREEN_WIDTH, SCREEN_HEIGHT),
        pygame.HWSURFACE | pygame.DOUBLEBUF | pygame.RESIZABLE
    )


def configure_window(icon_path: str, title: str) -> None:
    """Carga y establece el icono y el título de la ventana."""
    icon = load_image(icon_path)
    pygame.display.set_icon(icon)
    pygame.display.set_caption(title)


def init_performance_tools() -> Tuple[Dict[str, list], Any]:
    """
    Inicializa y devuelve el log de performance y el logger de benchmarks.
    
    Returns:
        performance_log: diccionario para acumular tiempos
        bench_logger: logger de benchmarks para excepciones
    """
    performance_log = init_debug_log()
    bench_logger = setup_benchmark_logger()
    return performance_log, bench_logger


def create_game(screen: pygame.Surface,
                performance_log: Dict[str, list],
                map_name: str = None,
                loading_bg: str = "ui/background_ini.png") -> Game:
    """
    Construye la instancia de Game y verifica que su estado sea válido.
    
    Raises:
        RuntimeError si no se inicializa correctamente el estado.
    """
    game = Game(
        screen=screen,
        perf_log=performance_log,
        map_name=map_name,
        loading_bg=loading_bg
    )
    if not hasattr(game, "state"):
        raise RuntimeError("Game state not initialized properly!")
    return game


def run_game_loop(game: Game,
                  bench_logger: Any,
                  performance_log: Dict[str, list]) -> None:
    """
    Ejecuta el bucle principal `game.run()`, captura excepciones para el logger
    y en el `finally` cierra el juego, guarda benchmarks y sale de Pygame.
    """
    try:
        game.run()
    except Exception:
        bench_logger.exception("Uncaught exception in main loop")
        raise
    finally:
        game.shutdown()
        save_benchmarks(performance_log)
        pygame.quit()


def main() -> None:
    """Punto de entrada: orquesta todos los pasos de inicialización y ejecución."""
    init_pygame()
    screen = create_screen()
    configure_window(icon_path="assets/ui/icon.png", title="Roguelike")
    
    performance_log, bench_logger = init_performance_tools()
    
    game = create_game(
        screen=screen,
        performance_log=performance_log,
        map_name=None,
        loading_bg="ui/background_ini.png"
    )
    
    run_game_loop(game, bench_logger, performance_log)


if __name__ == "__main__":
    main()