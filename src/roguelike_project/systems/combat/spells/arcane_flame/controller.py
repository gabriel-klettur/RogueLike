from src.roguelike_project.systems.combat.spells.arcane_flame.model import ArcaneFlameModel

class ArcaneFlameController:
    """
    Controlador del fuego arcano.
    """
    def __init__(self, model: ArcaneFlameModel):
        self.model = model

    def update(self):
        self.model.update()

    def is_finished(self) -> bool:
        return self.model.is_finished()
