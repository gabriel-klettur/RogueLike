class LightingEditorState:
    def __init__(self) -> None:
        self.visible: bool = False
        self.spawn_mode: bool = False  # when True, clicking on map spawns debug lights
        self.shadows_on: bool = False  # legacy local flag (now driven by manager)
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
        self._btn_occlusion = None
        self._btn_shadows = None
        self._panel_rect = None  # full panel rect for hit-testing
        # Scrolling
        self.scroll_offset: int = 0
        self._content_height: int = 0
        self._viewport_rect = None
        self._scrollbar_track = None
        self._scrollbar_thumb = None
        self._dragging_scroll: bool = False
        self._drag_start_y: int | None = None
        self._drag_start_offset: int | None = None
        # Tooltips
        self._tooltips: list = []
        self._current_tooltip: str | None = None
        # UI rects for manager quality/limits steppers
        self._btn_lrs_minus = None  # low-res scale -
        self._btn_lrs_plus = None   # low-res scale +
        self._btn_ml_minus = None   # max lights -
        self._btn_ml_plus = None    # max lights +
        self._btn_mr_minus = None   # max radius -
        self._btn_mr_plus = None    # max radius +
        self._btn_sh_hero_minus = None  # shadow hero count -
        self._btn_sh_hero_plus = None   # shadow hero count +
        self._btn_sh_rays_minus = None  # shadow rays -
        self._btn_sh_rays_plus = None   # shadow rays +
        # Time scale moved to DayTimePanel
