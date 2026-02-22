"""
Centralized defaults for spells and VFX parameters.
This module is the single source of truth for default values used by components
and resolvers when the config or buff does not specify overrides.
"""
from typing import List, Tuple

# Aura VFX defaults
DEFAULT_AURA_OFFSET_X: int = 0
DEFAULT_AURA_PARTICLES_PER_FRAME: int = 2
DEFAULT_AURA_PARTICLE_SPEED: float = 1.0
DEFAULT_AURA_PARTICLE_MIN_SIZE: int = 4
DEFAULT_AURA_PARTICLE_MAX_SIZE: int = 8
DEFAULT_AURA_PARTICLE_COLORS: List[Tuple[int, int, int]] = [
    (0, 255, 100),
    (100, 255, 150),
    (0, 200, 100),
]
DEFAULT_AURA_PARTICLE_LIFESPAN: int = 60
