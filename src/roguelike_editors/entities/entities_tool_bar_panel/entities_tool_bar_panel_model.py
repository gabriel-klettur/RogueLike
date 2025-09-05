"""
Modelo para la toolbar de entidades (stub).
"""

from roguelike_editors.entities.services.constants import ENTITIES_TOOL_ON_MAP

class EntitiesToolBarPanelModel:
    """
    Modelo de datos para la toolbar de entidades.
    """
    def __init__(self):
        # Claves de botones disponibles en la toolbar
        self.tools = [
            'tutorial_entities',
            ENTITIES_TOOL_ON_MAP,
            'undo',
            'redo',
        ]
        # Herramienta activa (por implementar)
        self.active_tool = None