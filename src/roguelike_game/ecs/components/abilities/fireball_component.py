"""
Componente ECS para fireball: velocidad dx, dy, daño, vida útil y edad.
"""

class FireballComponent:
    """
    Almacena la velocidad, daño y duración (frames) de la fireball.
    """
    def __init__(self, dx: float, dy: float, damage: float = 10, lifespan: int = 60, caster: int = None):
        self.dx = dx
        self.dy = dy
        self.damage = damage
        self.lifespan = lifespan
        self.caster = caster
        self.age = 0
