from __future__ import annotations

from typing import Any, Dict, List, Optional, Tuple

from ..overlay.services import perf_tree as _perf


def build_flat_metrics(perf_log: Dict[str, List[float]] | Any) -> Dict[str, float]:
    """Return a flat mapping key->avg_ms computed over last 60 samples.

    Silent on errors and non-conforming inputs.
    """
    flat: Dict[str, float] = {}
    try:
        items = getattr(perf_log, "items", None)
        it = perf_log.items() if callable(items) else []
        for key, samples in it:
            if not samples:
                continue
            recent = samples[-60:]
            if not recent:
                continue
            avg_ms = (sum(recent) / len(recent)) * 1000.0
            flat[str(key)] = round(float(avg_ms), 3)
    except Exception:
        pass
    return flat


def build_perf_tree(perf_log: Dict[str, List[float]] | Any) -> Optional[Dict[str, Any]]:
    try:
        return _perf.build_perf_tree(perf_log)
    except Exception:
        return None


def compute_fps_ft(state: Optional[Any]) -> Tuple[Optional[float], Optional[float]]:
    """Compute FPS and frame-time (ms) from a state-like object with a pygame clock."""
    fps: Optional[float] = None
    ft_ms: Optional[float] = None
    try:
        if state is not None and hasattr(state, "clock"):
            get_fps = getattr(state.clock, "get_fps", None)
            if callable(get_fps):
                fps_v = float(get_fps())
                if fps_v > 0:
                    fps = round(fps_v, 2)
                    ft_ms = round(1000.0 / fps_v, 2)
                else:
                    fps = 0.0
                    ft_ms = 0.0
    except Exception:
        pass
    return fps, ft_ms


def sample_from_perf_log(perf_log: Dict[str, List[float]] | Any, state: Optional[Any], started_ts: float) -> Dict[str, Any]:
    """Build a single sample dict given a raw perf_log mapping and state."""
    from .context import now_iso  # local import to avoid cycles

    flat = build_flat_metrics(perf_log)
    fps, ft_ms = compute_fps_ft(state)
    import time

    sample = {
        "t_offset_s": round(max(0.0, time.time() - float(started_ts or 0.0)), 3),
        "timestamp": now_iso(),
        "fps": fps,
        "frame_time_ms": ft_ms,
        "metrics_ms": flat,
        "metrics_tree": None,
    }
    return sample
