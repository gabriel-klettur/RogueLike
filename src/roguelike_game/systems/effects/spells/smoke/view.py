# Path: src/roguelike_game/systems/effects/spells/smoke/view.py
from roguelike_game.systems.effects.spells.smoke.model import SmokeModel

class SmokeView:
    """
    Vista: renderiza las partículas de humo en pantalla.
    """
    def __init__(self, model: SmokeModel):
        self.model = model

    def render(self, screen, camera):
        for p in self.model.particles:
            p.render(screen, camera)