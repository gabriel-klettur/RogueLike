import time


class TotemComponent:
    def __init__(self, *, radius: float, duration: float, tick_period: float, kind: str, value: float, owner: int | None = None) -> None:
        self.radius = float(radius)
        self.duration = float(duration)
        self.tick_period = max(0.05, float(tick_period))
        self.kind = str(kind)
        self.value = float(value)
        self.owner = owner
        self.start_time = time.time()
        self.last_tick_time = 0.0
