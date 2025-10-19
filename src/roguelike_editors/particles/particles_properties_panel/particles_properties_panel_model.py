class ParticlesPropertiesPanelModel:
    """Model for the particles properties panel.

    Holds UI visibility and the currently selected instance entry loaded from
    particles_instances.json.
    """

    def __init__(self) -> None:
        self.visible: bool = False
        self.selected_id: int | None = None
        self.entry: dict | None = None
        # Anchor position (screen coords)
        self.x: int = 0
        self.y: int = 0
        # Panel size
        self.width: int = 260
        self.padding: int = 8
