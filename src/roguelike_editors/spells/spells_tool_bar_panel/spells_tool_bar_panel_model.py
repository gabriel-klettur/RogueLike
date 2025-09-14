"""
Modelo para la toolbar de Spells.
"""


class SpellsToolBarPanelModel:
    """Modelo de datos para la toolbar de Spells."""
    def __init__(self):
        # Orden: tutorial, luego toggle principal del picker, y finalmente undo/redo
        self.tools = ['tutorial_spells', 'spells_on_map', 'spells_reload', 'undo', 'redo']
        self.active_tool: str | None = None

