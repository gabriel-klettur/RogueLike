"""
Modelo para la toolbar de Spells.
"""


class SpellsToolBarPanelModel:
    """Modelo de datos para la toolbar de Spells."""
    def __init__(self):
        # Orden solicitado: primero 'spells_on_map', debajo 'undo' y luego 'redo'
        self.tools = ['spells_on_map', 'undo', 'redo']
        self.active_tool: str | None = None

