# Path: src/roguelike_game/ecs/components/abilities/explosion_component.py
class ExplosionComponent:
    """
    ECS component to hold an explosion effect model.
    """
    def __init__(self, model):
        self.model = model
