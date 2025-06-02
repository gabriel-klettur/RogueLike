import time

class AuraComponent:
    """
    Componente que representa un aura activa sobre la entidad (caster).
    radius: radio de efecto en píxeles.
    buff: diccionario con detalles del buff (por ejemplo, {'heal_per_second': 5}).
    duration: duración en segundos.
    start_time: marca de tiempo de inicio.
    """
    def __init__(self, radius: float, buff: dict, duration: float):
        self.radius = radius
        self.buff = buff
        self.duration = duration
        self.start_time = time.time()
