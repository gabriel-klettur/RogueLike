"""
Modelo para la toolbar de Items.
"""


class ItemsToolBarPanelModel:
    """
    Modelo de datos para la toolbar de Items.
    """
    def __init__(self):
        # Claves de botones disponibles en la toolbar
        self.tools = [
            'items_on_map',  # botón principal que abre el sub-toolbar
            'undo',            
            'redo',
            'tutorial_items',
        ]
        # Herramienta activa
        self.active_tool = None

