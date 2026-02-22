import time

class TeleportModel:
    """
    ECS model for teleport effect: start_pos, end_pos, lifespan, and phase.
    """
    def __init__(self, start_pos, end_pos, lifespan=0.5):
        self.start_pos = start_pos
        self.end_pos = end_pos
        self.lifespan = lifespan
        self.start_time = time.time()
        self.phase = 'out'  # 'out' until halfway, then 'in'

    def elapsed(self) -> float:
        return time.time() - self.start_time

    def is_finished(self) -> bool:
        return self.elapsed() > self.lifespan

    def should_switch_phase(self) -> bool:
        return self.phase == 'out' and self.elapsed() >= self.lifespan / 2
