"""
Modelo para el panel de añadir/eliminar Items.
"""


class ItemsAddRemovePanelModel:
    """
    Modelo de datos para el sub-toolbar de Items.
    """
    def __init__(self):
        self.tools = [
            'add_item',
            'remove_item',
            'add_item_on_system',
        ]
        self.active_tool = None
        self.visible = False

