import time


class TimedDespawn:
    """Component that schedules an entity for removal after a given TTL.

    Attributes
    ----------
    start_time:
        Absolute timestamp (time.time()) when the timer starts.
    ttl:
        Time to live in seconds from ``start_time``.
    """

    def __init__(self, start_time: float | None = None, ttl: float = 60.0):
        try:
            base = float(start_time) if start_time is not None else time.time()
        except Exception:
            base = time.time()
        self.start_time: float = base
        try:
            self.ttl: float = float(ttl)
        except Exception:
            self.ttl = 60.0
