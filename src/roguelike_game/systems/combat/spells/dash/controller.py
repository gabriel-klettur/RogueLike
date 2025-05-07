# Path: src/roguelike_game/systems/combat/spells/dash/controller.py
from roguelike_game.systems.combat.spells.dash.model import DashModel

class DashController:
    """
    Controlador MVC para el dash.
    """
    def __init__(self, model: DashModel):
        self.model = model

    def update(self):
        self.model.update()

    def is_finished(self) -> bool:
        return self.model.is_finished()