from dataclasses import dataclass

@dataclass
class ExperienceComponent:
    """
    Componente que define la experiencia (XP) y nivel de una entidad.
    xp: puntos de experiencia actuales.
    level: nivel actual.
    xp_to_next_level: experiencia requerida para subir de nivel.
    """
    xp: int = 0
    level: int = 1
    xp_to_next_level: int = 100
