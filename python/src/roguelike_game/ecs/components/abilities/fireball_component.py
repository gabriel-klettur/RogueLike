"""
Componente ECS para fireball: velocidad dx, dy, daño, vida útil y edad.
"""

class FireballComponent:
    """
    Almacena la velocidad, daño y duración (frames) de la fireball.
    """
    def __init__(self, dx: float, dy: float, damage: float = 10, lifespan: int = 60, caster: int = None, spell_key: str = None, spawn_pos: tuple[float, float] = None, vfx_scale_multiplier: float = 1.0, hit_radius: float = 2.0):
        self.dx = dx
        self.dy = dy
        self.damage = damage
        self.lifespan = lifespan
        self.caster = caster
        self.age = 0
        self.spell_key = spell_key
        # Posición inicial de spawn para cálculo de rango
        self.spawn_pos = spawn_pos
        # Multiplicador visual para VFX asociados (trail/impact), propagado al impacto
        try:
            self.vfx_scale_multiplier = float(vfx_scale_multiplier)
        except Exception:
            self.vfx_scale_multiplier = 1.0
        # Radio de colisión en píxeles (aproximación circular)
        try:
            self.hit_radius = float(hit_radius)
        except Exception:
            self.hit_radius = 2.0

# Path: src/roguelike_game/ecs/components/abilities/fireball_component.py