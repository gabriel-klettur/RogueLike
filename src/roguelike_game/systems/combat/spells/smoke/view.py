# Path: src/roguelike_game/systems/combat/spells/smoke/view.py
from src.roguelike_game.systems.combat.spells.smoke.model import SmokeModel

class SmokeView:
    """
    Vista: renderiza las partículas de humo en pantalla.
    """
    def __init__(self, model: SmokeModel):
        self.model = model

    def render(self, screen, camera):
        for p in self.model.particles:
            p.render(screen, camera)