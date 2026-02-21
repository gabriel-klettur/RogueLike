import time


class SummonedUnitComponent:
    def __init__(self, *, owner: int, duration: float) -> None:
        self.owner = int(owner)
        self.duration = float(duration)
        self.start_time = time.time()
