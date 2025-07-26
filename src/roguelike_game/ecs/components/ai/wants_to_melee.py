class WantsToMelee:
    """
    Evento: atacante quiere golpear a target.
    """
    def __init__(self, attacker: int, target: int):
        self.attacker = attacker
        self.target = target
# Path: src/roguelike_game/ecs/components/ai/wants_to_melee.py