class ChaseTarget:
    """
    Componente que indica el entity_id del objetivo a perseguir.
    """
    def __init__(self, target: int):
        self.target = target
# Path: src/roguelike_game/ecs/components/ai/chase_target.py