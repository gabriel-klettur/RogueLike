class MeleeWeapon:
    """
    (Opcional) daño extra y cooldown de arma cuerpo a cuerpo.
    """
    def __init__(self, damage: int, cooldown: float):
        self.damage = damage
        self.cooldown = cooldown
# Path: src/roguelike_game/ecs/components/combat/melee_weapon.py