"""
Modelo para la toolbar de Buildings.
"""


class BuildingsToolBarPanelModel:
    """
    Modelo de datos para la toolbar de Buildings.
    """
    def __init__(self):
        # Claves de botones disponibles en la toolbar
        self.tools = [
            'buildings_manager',   # Toggle del picker de edificios
            'buildings_colliders', # Toggle del modo de colisiones
            'undo',
            'redo',
        ]
        # Herramienta activa (una a la vez)
        self.active_tool: str | None = None

