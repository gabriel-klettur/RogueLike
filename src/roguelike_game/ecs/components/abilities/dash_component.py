import time

class DashComponent:
    """
    ECS component representing a dash: stores direction, speed, and timing.
    """
    def __init__(self, dir_x: float, dir_y: float, speed: float, duration: float):
        self.dir_x = dir_x
        self.dir_y = dir_y
        self.speed = speed
        self.duration = duration
        now = time.time()
        self.start_time = now
        self.last_update = now
