class CombatStats:
    """
    HP, ataque y defensa de la entidad.
    """
    def __init__(self, current_hp: int, max_hp: int, power: int, defense: int):
        self.current_hp = current_hp
        self.max_hp = max_hp
        self.power = power
        self.defense = defense
# Path: src/roguelike_game/ecs/components/combat/combat_stats.py