from __future__ import annotations

from typing import Dict, Any, Optional


class RunningStat:
    """Simple running statistics aggregator for a numeric stream.

    Tracks count, sum, min, and max. Provides average on demand.
    """

    __slots__ = ("count", "sum", "min", "max")

    def __init__(self) -> None:
        self.count: int = 0
        self.sum: float = 0.0
        self.min: Optional[float] = None
        self.max: Optional[float] = None

    def update(self, value: float) -> None:
        v = float(value)
        self.count += 1
        self.sum += v
        self.min = v if self.min is None else min(self.min, v)
        self.max = v if self.max is None else max(self.max, v)

    def to_summary(self, avg_key: str = "avg", min_key: str = "min", max_key: str = "max", samples_key: str = "samples") -> Dict[str, Any]:
        if self.count <= 0:
            return {avg_key: None, min_key: None, max_key: None, samples_key: 0}
        return {
            avg_key: round(self.sum / self.count, 3),
            min_key: round(float(self.min), 3) if self.min is not None else None,
            max_key: round(float(self.max), 3) if self.max is not None else None,
            samples_key: self.count,
        }


class MetricsAggregator:
    """Aggregates metrics, FPS, and frame-times over time.

    Exposes helpers to build a summary payload with averages and ranges.
    """

    def __init__(self) -> None:
        self.metrics: Dict[str, RunningStat] = {}
        self.fps: RunningStat = RunningStat()
        self.frame_time_ms: RunningStat = RunningStat()

    def update_metric(self, key: str, value_ms: float) -> None:
        rec = self.metrics.get(key)
        if rec is None:
            rec = RunningStat()
            self.metrics[key] = rec
        rec.update(value_ms)

    def update_fps(self, fps_value: float) -> None:
        self.fps.update(fps_value)

    def update_frame_time(self, ft_ms: float) -> None:
        self.frame_time_ms.update(ft_ms)

    def metrics_summary(self) -> Dict[str, Any]:
        out: Dict[str, Any] = {}
        for k in sorted(self.metrics.keys()):
            stat = self.metrics[k]
            sm = stat.to_summary(avg_key="avg_ms", min_key="min_ms", max_key="max_ms")
            out[k] = sm
        return out

    def fps_summary(self) -> Dict[str, Any]:
        return self.fps.to_summary(avg_key="avg", min_key="min", max_key="max")

    def frame_time_summary(self) -> Dict[str, Any]:
        return self.frame_time_ms.to_summary(avg_key="avg", min_key="min", max_key="max")

    def to_dict(self) -> Dict[str, Any]:
        return {
            "fps": self.fps_summary(),
            "frame_time_ms": self.frame_time_summary(),
            "metrics": self.metrics_summary(),
        }
