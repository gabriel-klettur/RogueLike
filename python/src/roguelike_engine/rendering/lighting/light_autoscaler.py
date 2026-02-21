from __future__ import annotations

"""Frame-time based autoscaling helper for the lighting system."""

from collections import deque
from enum import Enum, auto
from typing import Deque, Optional


class AutoScaleTrend(Enum):
    """Possible performance feedback emitted by :class:`LightAutoscaler`."""

    TOO_SLOW = auto()
    TOO_FAST = auto()


class LightAutoscaler:
    """Track composition timings and suggest quality adjustments.

    The autoscaler records the composition time of recent frames and compares the
    median against a configurable budget. When the rendering time drifts above
    or below the thresholds the caller receives a :class:`AutoScaleTrend`
    indicating whether to decrease or increase quality.
    """

    def __init__(self, budget_ms: float = 2.0, history: int = 120) -> None:
        self.enabled: bool = True
        self.budget_ms: float = budget_ms
        self._history: Deque[float] = deque(maxlen=history)
        self._cooldown_frames: int = 0

    def reset(self) -> None:
        """Clear the sampling history and cooldown."""

        self._history.clear()
        self._cooldown_frames = 0

    def record(self, duration_ms: float) -> Optional[AutoScaleTrend]:
        """Register the latest composition cost and return trend feedback.

        Parameters
        ----------
        duration_ms:
            Composition time for the current frame in milliseconds.
        """

        if not self.enabled:
            return None

        self._history.append(duration_ms)

        if self._cooldown_frames > 0:
            self._cooldown_frames -= 1
            return None

        if len(self._history) < 30:
            return None

        sorted_times = sorted(self._history)
        median = sorted_times[len(sorted_times) // 2]
        upper = self.budget_ms * 1.1
        lower = self.budget_ms * 0.6

        if median > upper:
            self._history.clear()
            self._cooldown_frames = 60
            return AutoScaleTrend.TOO_SLOW
        if median < lower:
            self._history.clear()
            self._cooldown_frames = 120
            return AutoScaleTrend.TOO_FAST
        return None
