"""
Modelo para la toolbar de Partículas.
"""

class ParticlesToolBarPanelModel:
    """Modelo de datos para la toolbar de Partículas."""
    def __init__(self):
        # Orden: tutorial, luego listado principal, y finalmente undo/redo
        self.tools = ['tutorial_particles', 'particles_list', 'undo', 'redo']
        self.active_tool: str | None = None
