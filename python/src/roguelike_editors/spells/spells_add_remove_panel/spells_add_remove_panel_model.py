"""
Spells Add/Remove panel model.
"""


class SpellsAddRemovePanelModel:
    def __init__(self):
        self.tools = ['add_spell', 'remove_spell']
        self.active_tool: str | None = None
        self.visible: bool = False

