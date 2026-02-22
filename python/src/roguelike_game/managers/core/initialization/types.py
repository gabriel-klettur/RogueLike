from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from typing import Callable, Iterable, Optional


@dataclass
class InitContext:
    """Carries all inputs needed by initialization stages.

    Attributes
    ----------
    game: The main game object used across managers.
    screen: The pygame screen surface.
    perf_log: Optional path or object for performance logging.
    map_name: Optional map to force-load when there is no current level.
    loading_bg: Optional background path for the loading screen.
    stage_log_path: Log file path for stage timings (created by bootstrap).
    ts_dt: Timestamp of startup for consistent profile/log filenames.
    """

    game: object
    screen: object
    perf_log: Optional[object]
    map_name: Optional[str]
    loading_bg: Optional[str]
    stage_log_path: Optional[str]
    ts_dt: Optional[datetime]


# A Stage is (message, callable(ctx))
Stage = tuple[str, Callable[[InitContext], None]]

# For convenience types
Stages = Iterable[Stage]
