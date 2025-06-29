# Path: src/roguelike_game/utils/benchmark.py
import os
import json
import logging
import statistics
import heapq
from datetime import datetime

from collections import defaultdict


def setup_benchmark_logger(base_dir: str | None = None) -> logging.Logger:
    """
    Configura un logger especializado en benchmarks y retorna la instancia.
    """
    if base_dir is None:
        # Dos niveles arriba de este archivo
        root = os.path.abspath(
            os.path.join(os.path.dirname(__file__), "..", "..")
        )
        base_dir = os.path.join(root, "logs", "benchmarks")

    os.makedirs(base_dir, exist_ok=True)
    ts = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
    filepath = os.path.join(base_dir, f"benchmarks_run_{ts}.log")

    logger = logging.getLogger("benchmarks")
    logger.setLevel(logging.INFO)
    fh = logging.FileHandler(filepath, encoding="utf-8")
    fmt = logging.Formatter(
        "%(asctime)s %(levelname)s %(message)s", datefmt="%Y-%m-%dT%H:%M:%S"
    )
    fh.setFormatter(fmt)
    logger.addHandler(fh)
    return logger


def save_benchmarks(benchmarks: dict, base_dir: str | None = None) -> None:
    """
    Genera un JSON resumen de los benchmarks con estadísticas y top eventos.
    """
    if base_dir is None:
        root = os.path.abspath(
            os.path.join(os.path.dirname(__file__), "..", "..")
        )
        base_dir = os.path.join(root, "logs", "benchmarks")

    os.makedirs(base_dir, exist_ok=True)
    ts_iso = datetime.now().isoformat(timespec='seconds')
    ts_fn = datetime.now().strftime('%Y-%m-%d_%H-%M-%S')
    filepath = os.path.join(base_dir, f'benchmarks_run_{ts_fn}.json')

    # Estadísticas básicas
    summary: dict[str, dict] = {}
    for name, vals in benchmarks.items():
        if not vals:
            continue
        ms = [v * 1000 for v in vals]
        summary[name] = {
            'count': len(ms),
            'avg': round(statistics.mean(ms), 2),
            'min': round(min(ms), 2),
            'max': round(max(ms), 2),
            'median': round(statistics.median(ms), 2)
        }

    # Top 10 por max
    top_max = dict(
        sorted(summary.items(), key=lambda kv: kv[1]['max'], reverse=True)[:10]
    )

    # Top 10 eventos raw
    events = [(v*1000, name) for name, vals in benchmarks.items() for v in vals]
    top_raw = heapq.nlargest(10, events, key=lambda x: x[0])
    top_events = [{'system': n, 'value': round(t,2)} for t,n in top_raw]

    # Agrupar por categoría (prioriza dígito inicial)
    grouped: dict[str, dict] = defaultdict(dict)
    for name, stats in summary.items():
        cat = name.split('.')[0] if name and name[0].isdigit() else '4'
        grouped[cat][name] = stats

    data = {
        'run_timestamp': ts_iso,
        'top_max': top_max,
        'top_events': top_events,
        'benchmarks': grouped
    }

    with open(filepath, 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=2)

    logging.getLogger('benchmarks').info(
        f'Benchmarks summary written to {filepath}'
    )