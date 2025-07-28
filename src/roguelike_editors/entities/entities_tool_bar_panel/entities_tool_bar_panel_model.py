"""
Modelo para la toolbar de entidades (stub).
"""

class EntitiesToolBarPanelModel:
    """
    Modelo de datos para la toolbar de entidades.
    """
    def __init__(self):
        # Claves de botones disponibles en la toolbar
        self.tools = [
            'entities_on_map',
            'respawns',
            'undo',
            'redo',
        ]
        # Herramienta activa (por implementar)
        self.active_tool = None