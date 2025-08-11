"""
Modelo para la toolbar de Spells.
"""


class SpellsToolBarPanelModel:
    """Modelo de datos para la toolbar de Spells."""
    def __init__(self):
        # Centrar el toggle principal 'spells_on_map' entre undo/redo
        self.tools = ['undo', 'spells_on_map', 'redo']
        self.active_tool: str | None = None

