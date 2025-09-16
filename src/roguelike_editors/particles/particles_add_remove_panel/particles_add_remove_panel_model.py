"""
Particles Add/Remove panel model.
"""


class ParticlesAddRemovePanelModel:
    def __init__(self):
        # Tools order: add to system, add to map, remove from map/picker
        self.tools = ['particles_add_system', 'particles_add', 'particles_remove']
        self.active_tool: str | None = None
        self.visible: bool = False
