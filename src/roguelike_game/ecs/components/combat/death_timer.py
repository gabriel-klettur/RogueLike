import time

class DeathTimer:
    """
    Componente ECS que almacena el tiempo de inicio de muerte y la duración antes de eliminación.
    """
    def __init__(self, start_time: float, duration: float = 60.0):
        self.start_time = start_time
        self.duration = duration
