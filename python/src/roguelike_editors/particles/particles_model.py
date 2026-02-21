class ParticlesEditorModel:
    """Minimal model for the Particles Editor."""
    def __init__(self):
        self.visible: bool = False
        self.title: str = "PARTICLES EDITOR"
        self.title_rect = None
        # UI/mode flags used by add/remove panel and picker
        self.picker_visible: bool = False
        self.delete_mode_active: bool = False
        # Selection and drag state for instances on the map
        self.selected_instance_id: int | None = None
        self.selected_entity_eid: int | None = None
        # Right-click place-drag (creating an instance)
        self.drag_place_active: bool = False
        self.drag_pid: str | None = None
        self.drag_entity_eid: int | None = None
        # Right-click move-drag (moving an existing instance)
        self.drag_move_active: bool = False
