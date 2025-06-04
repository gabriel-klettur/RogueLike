class SmokeEmitterView:
    """
    Vista: renderiza las partículas del emisor.
    """
    def __init__(self, model):
        self.model = model

    def render(self, screen, camera):
        for p in self.model.particles:
            p.render(screen, camera)
# Path: src/roguelike_game/systems/combat/spells/smoke_emitter/view.py