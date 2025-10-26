class LightingEditorState:
    def __init__(self) -> None:
        self.visible: bool = False
        self.spawn_mode: bool = False  # when True, clicking on map spawns debug lights
        # UI layout config
        self.panel_x: int = 16
        self.panel_y: int = 80
        self.panel_w: int = 260
        self.row_h: int = 34
        # Cached button rects (screen-space)
        self._btn_ambient = None
        self._btn_lights = None
        self._btn_spawn = None
        self._btn_clear = None
        self._panel_rect = None  # full panel rect for hit-testing
