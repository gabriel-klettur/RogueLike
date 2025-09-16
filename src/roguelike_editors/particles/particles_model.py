class ParticlesEditorModel:
    """Minimal model for the Particles Editor."""
    def __init__(self):
        self.visible: bool = False
        self.title: str = "PARTICLES EDITOR"
        self.title_rect = None
        # UI/mode flags used by add/remove panel and picker
        self.picker_visible: bool = False
        self.delete_mode_active: bool = False
