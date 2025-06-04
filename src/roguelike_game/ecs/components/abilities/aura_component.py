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
        # Parámetros VFX de aura
        self.offset_x = getattr(buff, 'offset_x', 0)  # fallback si no existe
        self.particles_per_frame = getattr(buff, 'particles_per_frame', 2)
        self.particle_speed      = getattr(buff, 'particle_speed', 1.0)
        self.particle_min_size   = getattr(buff, 'particle_min_size', 4)
        self.particle_max_size   = getattr(buff, 'particle_max_size', 8)
        self.particle_colors     = getattr(buff, 'particle_colors', [(0,255,100),(100,255,150),(0,200,100)])
        self.particle_lifespan   = getattr(buff, 'particle_lifespan', 60)
