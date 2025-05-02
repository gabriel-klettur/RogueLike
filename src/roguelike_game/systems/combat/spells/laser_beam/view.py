class LaserBeamView:
    """
    Vista: renderiza las partículas y explosión desde el modelo.
    """
    def __init__(self, model):
        self.model = model

    def render(self, screen, camera):
        model = self.model
        # Renderizar cada partícula
        for p in model.particles:
            p.render(screen, camera)
        # Renderizar explosión en destino
        if model.explosion:
            model.explosion.render(screen, camera)
# Path: src/roguelike_game/systems/combat/spells/laser_beam/view.py