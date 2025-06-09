# Path: src/roguelike_game/main.py

import pygame
from collections import defaultdict
from roguelike_engine.utils.loader import load_image

from roguelike_engine.config.config import SCREEN_WIDTH, SCREEN_HEIGHT
from roguelike_game.game.game import Game
import os
import logging
from datetime import datetime
import json
import statistics
import heapq


def init_debug_log():
    """
    Activa el cursor y devuelve un diccionario para logging de performance.
    """
    pygame.mouse.set_visible(True)
    return defaultdict(list)


def setup_benchmark_logger(base_dir=None):
    if base_dir is None:
        root_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
        base_dir = os.path.join(root_dir, "logs", "benchmarks")
    os.makedirs(base_dir, exist_ok=True)
    ts = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
    filename = f"benchmarks_run_{ts}.log"
    filepath = os.path.join(base_dir, filename)
    logger = logging.getLogger("benchmarks")
    logger.setLevel(logging.INFO)
    fh = logging.FileHandler(filepath, encoding="utf-8")
    fmt = logging.Formatter("%(asctime)s %(levelname)s %(message)s", datefmt="%Y-%m-%dT%H:%M:%S")
    fh.setFormatter(fmt)
    logger.addHandler(fh)
    return logger


def save_benchmarks(benchmarks):
    # Ensure output directory exists
    root_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
    base_dir = os.path.join(root_dir, 'logs', 'benchmarks')
    os.makedirs(base_dir, exist_ok=True)
    # Prepare timestamps
    ts_iso = datetime.now().isoformat(timespec='seconds')
    ts_fn = datetime.now().strftime('%Y-%m-%d_%H-%M-%S')
    filename = f'benchmarks_run_{ts_fn}.json'
    filepath = os.path.join(base_dir, filename)

    # Compute summary metrics
    summary = {}
    for name, values in benchmarks.items():
        if not values:
            continue
        # convert raw values to milliseconds
        ms_vals = [v * 1000 for v in values]
        summary[name] = {
            'count': len(values),
            'avg': round(statistics.mean(ms_vals), 2),
            'min': round(min(ms_vals), 2),
            'max': round(max(ms_vals), 2),
            'median': round(statistics.median(ms_vals), 2)
        }

    # Compute top 10 items by max descending
    sorted_items = sorted(summary.items(), key=lambda kv: kv[1]['max'], reverse=True)
    top_max = dict(sorted_items[:10])

    # Compute top 10 raw events across all systems (milliseconds)
    events = [(v * 1000, name) for name, vals in benchmarks.items() for v in vals]
    top_events_raw = heapq.nlargest(10, events, key=lambda x: x[0])
    top_events = [{'system': name, 'value': round(val, 2)} for val, name in top_events_raw]

    # Group benchmarks by top-level numeric category
    from collections import defaultdict
    grouped = defaultdict(dict)
    for name, stats in summary.items():
        cat = name.split('.')[0] if name and name[0].isdigit() else '4'
        grouped[cat][name] = stats
    grouped_benchmarks = dict(grouped)

    # Write JSON output with grouped benchmarks
    data = {
        'run_timestamp': ts_iso,
        'top_max': top_max,
        'top_events': top_events,
        'benchmarks': grouped_benchmarks
    }
    with open(filepath, 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=2)
    logging.getLogger('benchmarks').info(f'Benchmarks summary written to {filepath}')


def main():
    pygame.init()


    screen = pygame.display.set_mode(
        (SCREEN_WIDTH, SCREEN_HEIGHT),
        pygame.HWSURFACE | pygame.DOUBLEBUF | pygame.RESIZABLE
    )

    # Cambiar icono de la ventana usando caché de imágenes
    icon = load_image("assets/ui/icon.png")

    pygame.display.set_icon(icon)
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
        save_benchmarks(performance_log)
        pygame.quit()


if __name__ == "__main__":
    main()
