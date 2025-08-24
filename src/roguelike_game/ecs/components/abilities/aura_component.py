import time
from roguelike_game.config.spells_defaults import (
    DEFAULT_AURA_OFFSET_X,
    DEFAULT_AURA_PARTICLES_PER_FRAME,
    DEFAULT_AURA_PARTICLE_SPEED,
    DEFAULT_AURA_PARTICLE_MIN_SIZE,
    DEFAULT_AURA_PARTICLE_MAX_SIZE,
    DEFAULT_AURA_PARTICLE_COLORS,
    DEFAULT_AURA_PARTICLE_LIFESPAN,
)

class AuraComponent:
    """
    Componente que representa un aura activa sobre la entidad (caster).
    radius: radio de efecto en píxeles.
    buff: diccionario con detalles del buff (por ejemplo, {'heal_per_second': 5}).
    duration: duración en segundos.
    start_time: marca de tiempo de inicio.
    """
    def __init__(self, radius: float, buff: dict, duration: float, spell_key: str = "", last_refresh_version: int = -1):
        self.radius = radius
        self.buff = buff
        self.duration = duration
        self.start_time = time.time()
        # Identificación de hechizo y control de refresco
        self.spell_key = spell_key
        self.last_refresh_version = last_refresh_version
        # Parámetros VFX de aura (valores por defecto centralizados)
        self.offset_x = buff.get('offset_x', DEFAULT_AURA_OFFSET_X)
        self.particles_per_frame = buff.get('particles_per_frame', DEFAULT_AURA_PARTICLES_PER_FRAME)
        self.particle_speed      = buff.get('particle_speed', DEFAULT_AURA_PARTICLE_SPEED)
        self.particle_min_size   = buff.get('particle_min_size', DEFAULT_AURA_PARTICLE_MIN_SIZE)
        self.particle_max_size   = buff.get('particle_max_size', DEFAULT_AURA_PARTICLE_MAX_SIZE)
        self.particle_colors     = buff.get('particle_colors', DEFAULT_AURA_PARTICLE_COLORS)
        self.particle_lifespan   = buff.get('particle_lifespan', DEFAULT_AURA_PARTICLE_LIFESPAN)