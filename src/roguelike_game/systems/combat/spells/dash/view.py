# Path: src/roguelike_game/systems/combat/spells/dash/view.py
from roguelike_game.systems.combat.spells.dash.model import DashModel

class DashView:
    """
    Vista MVC para el dash: renderiza las partículas.
    """
    def __init__(self, model: DashModel):
        self.model = model

    def render(self, screen, camera):
        dirty_rect = None
        for p in self.model.particles:
            rect = p.render(screen, camera)
            if rect:
                dirty_rect = rect if dirty_rect is None else dirty_rect.union(rect)
        return dirty_rect