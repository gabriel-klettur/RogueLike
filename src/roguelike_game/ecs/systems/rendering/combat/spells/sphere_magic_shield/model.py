import time

class SphereMagicShieldModel:
    """
    ECS model for a pulsing magic shield: origin, base_radius, duration, and elapsed time.
    """
    def __init__(self, x: float, y: float, radius: int = 80, duration: float = 5.0):
        self.x = x
        self.y = y
        self.base_radius = radius
        self.radius = radius
        self.duration = duration
        self.start_time = time.time()
        # color pulsante
        self.color = (150, 200, 255)

    def elapsed(self) -> float:
        return time.time() - self.start_time

    def is_finished(self) -> bool:
        return self.elapsed() > self.duration
