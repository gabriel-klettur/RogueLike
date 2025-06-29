class FacingCooldown:
    """
    Componente para controlar la frecuencia de cambio de dirección de sprite.
    next_allowed: timestamp (segundos) a partir del cual se permite nuevo cambio.
    """
    def __init__(self, next_allowed: float = 0.0):
        self.next_allowed = next_allowed
# Path: src/roguelike_game/ecs/components/combat/facing_cooldown.py