# File: roguelike_project/systems/combat/spells/laser_beam/view.py
class LaserBeamView:
    """
    Vista: renderiza las partículas y explosión desde el modelo.
    """
    def __init__(self, model):
        self.model = model

    def render(self, screen, camera):
        m = self.model
        # Renderizar cada partícula
        for p in m.particles:
            p.render(screen, camera)
        # Renderizar explosión en destino
        if m.explosion:
            m.explosion.render(screen, camera)
