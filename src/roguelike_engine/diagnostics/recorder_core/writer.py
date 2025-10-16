from __future__ import annotations

import json
import os
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Tuple

from roguelike_engine.log_config import build_log_filepath
from .aggregator import MetricsAggregator
from .context import now_iso


def _session_datetime(data: Dict[str, Any]) -> datetime:
    try:
        ts_val = float(data.get("_started_ts", 0.0) or 0.0)
        return datetime.fromtimestamp(ts_val, timezone.utc) if ts_val > 0 else datetime.now(timezone.utc)
    except Exception:
        return datetime.now(timezone.utc)


def write_session(data: Dict[str, Any]) -> str:
    """Write a single diagnostics session JSON file under logs/diagnostics.

    Returns the output path.
    """
    out_dir = os.path.join(os.getcwd(), "logs", "diagnostics")
    os.makedirs(out_dir, exist_ok=True)
    dt = _session_datetime(data)
    out_path = str(
        build_log_filepath(
            "diagnostics_session", directory=out_dir, extension="json", now_dt=dt
        )
    )
    payload = dict(data)
    payload.pop("_started_ts", None)
    tmp_path = out_path + ".tmp"
    with open(tmp_path, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=2)
    os.replace(tmp_path, out_path)
    return out_path


def _summary_from(data: Dict[str, Any], agg: Optional[MetricsAggregator]) -> Dict[str, Any]:
    started_at = data.get("started_at")
    ended_at = data.get("ended_at")
    duration = data.get("duration_seconds")
    game_context = data.get("game_context", {})
    if agg is None:
        agg = MetricsAggregator()
    return {
        "session_id": data.get("session_id"),
        "started_at": started_at,
        "ended_at": ended_at,
        "duration_seconds": duration,
        "game_context": game_context,
        "fps": agg.fps_summary(),
        "frame_time_ms": agg.frame_time_summary(),
        "metrics": agg.metrics_summary(),
    }


def write_summary(data: Dict[str, Any], agg: Optional[MetricsAggregator]) -> Tuple[str, str]:
    """Write a JSON summary and a human-friendly .log table under logs/benchmarks.

    Returns (json_path, log_path).
    """
    out_dir = os.path.join(os.getcwd(), "logs", "benchmarks")
    os.makedirs(out_dir, exist_ok=True)
    dt = _session_datetime(data)
    summary = _summary_from(data, agg)

    # JSON summary
    json_path = str(
        build_log_filepath(
            "diagnostics_session_summary", directory=out_dir, extension="json", now_dt=dt
        )
    )
    tmp_json = json_path + ".tmp"
    with open(tmp_json, "w", encoding="utf-8") as f:
        json.dump(summary, f, ensure_ascii=False, indent=2)
    os.replace(tmp_json, json_path)

    # LOG table
    log_path = str(
        build_log_filepath(
            "diagnostics_session_summary", directory=out_dir, extension="log", now_dt=dt
        )
    )
    lines: List[str] = []
    lines.append(
        f"Session: {summary.get('session_id')}  Started: {summary.get('started_at')}  Ended: {summary.get('ended_at')}  Duration: {summary.get('duration_seconds'):.2f}s"
    )
    gc = summary.get("game_context") or {}
    lines.append(
        f"Context: map={gc.get('map_name')} world_level={gc.get('world_level')} camera={gc.get('camera')}"
    )
    fps = summary.get("fps") or {}
    ft = summary.get("frame_time_ms") or {}
    lines.append(
        f"FPS: avg={fps.get('avg')} min={fps.get('min')} max={fps.get('max')} samples={fps.get('samples')}"
    )
    lines.append(
        f"FrameTime(ms): avg={ft.get('avg')} min={ft.get('min')} max={ft.get('max')} samples={ft.get('samples')}"
    )
    lines.append("")

    header = ["Metric Key", "Avg(ms)", "Min(ms)", "Max(ms)", "Samples"]
    rows: List[tuple] = []
    metrics: Dict[str, Any] = summary.get("metrics", {})
    for k, rec in metrics.items():
        rows.append((k, rec.get("avg_ms"), rec.get("min_ms"), rec.get("max_ms"), rec.get("samples")))
    rows.sort(key=lambda r: (r[1] is None, r[1] if r[1] is not None else 0.0), reverse=True)

    col_w = [len(h) for h in header]
    for r in rows:
        col_w[0] = max(col_w[0], len(str(r[0])))
        col_w[1] = max(col_w[1], len(f"{r[1]}"))
        col_w[2] = max(col_w[2], len(f"{r[2]}"))
        col_w[3] = max(col_w[3], len(f"{r[3]}"))
        col_w[4] = max(col_w[4], len(f"{r[4]}"))

    fmt = f"{{:<{col_w[0]}}}  {{:>{col_w[1]}}}  {{:>{col_w[2]}}}  {{:>{col_w[3]}}}  {{:>{col_w[4]}}}"
    lines.append(fmt.format(*header))
    lines.append("-" * (sum(col_w) + 2 * (len(header) - 1) + 2))
    for r in rows:
        a = "{:.3f}".format(r[1]) if isinstance(r[1], (int, float)) else str(r[1])
        mi = "{:.3f}".format(r[2]) if isinstance(r[2], (int, float)) else str(r[2])
        ma = "{:.3f}".format(r[3]) if isinstance(r[3], (int, float)) else str(r[3])
        lines.append(fmt.format(str(r[0]), a, mi, ma, str(r[4])))

    with open(log_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")

    return json_path, log_path
