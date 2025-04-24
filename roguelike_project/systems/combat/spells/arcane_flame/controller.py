from roguelike_project.systems.combat.spells.arcane_flame.model import ArcaneFlameModel

class ArcaneFlameController:
    """
    Controlador del fuego arcano.
    """
    def __init__(self, model: ArcaneFlameModel):
        self.m = model

    def update(self):
        self.m.update()

    def is_finished(self) -> bool:
        return self.m.is_finished()
