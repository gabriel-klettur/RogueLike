import os
import json
import logging
import statistics
import heapq
from datetime import datetime
from roguelike_engine.log_config import build_log_filepath

from collections import defaultdict


def setup_benchmark_logger(base_dir: str | None = None) -> logging.Logger:
    """
    Configura un logger especializado en benchmarks y retorna la instancia.
    """
    if base_dir is None:
        # Tres niveles arriba (desde src/roguelike_game/utils -> raíz del proyecto)
        root = os.path.abspath(
            os.path.join(os.path.dirname(__file__), "..", "..", "..")
        )
        base_dir = os.path.join(root, "logs", "benchmarks")

    os.makedirs(base_dir, exist_ok=True)
    _dt = datetime.now()
    filepath = str(build_log_filepath("benchmarks_run", directory=base_dir, extension="log", now_dt=_dt))

    logger = logging.getLogger("benchmarks")
    logger.setLevel(logging.INFO)
    fh = logging.FileHandler(filepath, encoding="utf-8")
    fmt = logging.Formatter(
        "%(asctime)s %(levelname)s %(message)s", datefmt="%Y-%m-%dT%H:%M:%S"
    )
    fh.setFormatter(fmt)
    logger.addHandler(fh)
    return logger


def save_benchmarks(benchmarks: dict, base_dir: str | None = None) -> tuple[str, str]:
    """
    Genera un JSON resumen de los benchmarks con estadísticas y top eventos.
    """
    if base_dir is None:
        root = os.path.abspath(
            os.path.join(os.path.dirname(__file__), "..", "..", "..")
        )
        base_dir = os.path.join(root, "logs", "benchmarks")

    os.makedirs(base_dir, exist_ok=True)
    _dt = datetime.now()
    ts_iso = _dt.isoformat(timespec='seconds')
    json_path = str(build_log_filepath('benchmarks_run', directory=base_dir, extension='json', now_dt=_dt))

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

    with open(json_path, 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=2)

    logging.getLogger('benchmarks').info(
        f'Benchmarks summary written to {json_path}'
    )

    # Additionally, emit a human-friendly .log table summary without colliding with engine logger .log
    log_path = str(build_log_filepath('benchmarks_summary', directory=base_dir, extension='log', now_dt=_dt))
    try:
        lines: list[str] = []
        lines.append(f"Run: {ts_iso}")
        # Flatten grouped metrics for table rows (name -> stats)
        rows: list[tuple[str, float, float, float, int]] = []
        for group_name, group in (data.get('benchmarks') or {}).items():
            for name, stats in (group or {}).items():
                rows.append((
                    name,
                    stats.get('avg'),
                    stats.get('min'),
                    stats.get('max'),
                    stats.get('count'),
                ))
        # Sort rows by avg desc
        rows.sort(key=lambda r: (r[1] is None, r[1] if r[1] is not None else 0.0), reverse=True)
        # Compute column widths
        header = ["Metric Key", "Avg(ms)", "Min(ms)", "Max(ms)", "Samples"]
        col_w = [len(h) for h in header]
        for r in rows:
            col_w[0] = max(col_w[0], len(str(r[0])))
            col_w[1] = max(col_w[1], len(f"{r[1]}"))
            col_w[2] = max(col_w[2], len(f"{r[2]}"))
            col_w[3] = max(col_w[3], len(f"{r[3]}"))
            col_w[4] = max(col_w[4], len(f"{r[4]}"))
        fmt = f"{{:<{col_w[0]}}}  {{:>{col_w[1]}}}  {{:>{col_w[2]}}}  {{:>{col_w[3]}}}  {{:>{col_w[4]}}}"
        lines.append("")
        lines.append(fmt.format(*header))
        lines.append("-" * (sum(col_w) + 2 * (len(header) - 1) + 2))
        for r in rows:
            a = "{:.3f}".format(r[1]) if isinstance(r[1], (int, float)) else str(r[1])
            mi = "{:.3f}".format(r[2]) if isinstance(r[2], (int, float)) else str(r[2])
            ma = "{:.3f}".format(r[3]) if isinstance(r[3], (int, float)) else str(r[3])
            lines.append(fmt.format(str(r[0]), a, mi, ma, str(r[4])))
        with open(log_path, 'w', encoding='utf-8') as lf:
            lf.write("\n".join(lines) + "\n")
        logging.getLogger('benchmarks').info(
            f'Benchmarks table summary written to {log_path}'
        )
    except Exception:
        logging.getLogger('benchmarks').exception('Failed to write benchmarks table summary')
        log_path = ""

    return json_path, log_path