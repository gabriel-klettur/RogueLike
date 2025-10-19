from __future__ import annotations

from typing import Dict, Tuple
import logging
import time as _time

# Module logger (respects global configuration)
logger = logging.getLogger(__name__)

# Generic de-duplication for noisy logs (keyed windows)
_DEDUP_TIMERS: Dict[str, Tuple[int, int]] = {}


def _now_ms() -> int:
    """Return current monotonic time in milliseconds."""
    return int(_time.monotonic() * 1000)


def dedup_should_log(key: str, window_ms: int = 2000) -> Tuple[bool, int]:
    """Return (allow, suppressed_count) for a log key over a time window.

    If called repeatedly within window_ms for the same key, suppress logs and
    accumulate a counter. On the first call after the window elapses, allow the
    log and return how many duplicates were suppressed in that period.
    """
    now = _now_ms()
    last, count = _DEDUP_TIMERS.get(key, (-10_000_000, 0))
    if now - last >= window_ms:
        _DEDUP_TIMERS[key] = (now, 0)
        return True, count
    else:
        _DEDUP_TIMERS[key] = (last, count + 1)
        return False, 0
