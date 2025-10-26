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
        # Spawn configuration (defaults: Torch-like)
        self.spawn_preset: str = "Torch"
        self.spawn_types: list[str] = ["Torch", "Lamp", "Magic", "Custom"]
        self.spawn_combo_open: bool = False
        self._combo_spawn_type = None
        self._combo_spawn_items: list[tuple] | None = None
        self.spawn_radius: int = 160
        self.spawn_intensity: float = 1.0
        self.spawn_falloff: float = 2.0
        self.spawn_color: tuple[int, int, int] = (255, 200, 140)
        self.spawn_flicker_amp: float = 0.15
        self.spawn_flicker_speed: float = 2.5
        self.spawn_single_shot: bool = False
        # UI rects for presets
        self._btn_preset_torch = None
        self._btn_preset_lamp = None
        self._btn_preset_magic = None
        # UI rects for spawn steppers
        self._btn_sr_minus = None  # radius -
        self._btn_sr_plus = None   # radius +
        self._btn_si_minus = None  # intensity -
        self._btn_si_plus = None   # intensity +
        self._btn_sf_minus = None  # falloff -
        self._btn_sf_plus = None   # falloff +
        self._btn_fa_minus = None  # flicker amp -
        self._btn_fa_plus = None   # flicker amp +
        self._btn_fs_minus = None  # flicker speed -
        self._btn_fs_plus = None   # flicker speed +
        self._btn_single_shot = None
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
        # UI rects for time scale (game minutes per real second)
        self._btn_ts_minus = None
        self._btn_ts_plus = None
        # UI rects for color steppers
        self._btn_r_minus = None
        self._btn_r_plus = None
        self._btn_g_minus = None
        self._btn_g_plus = None
        self._btn_b_minus = None
        self._btn_b_plus = None
        # --- Daytime panel (right side): buttons and info rects ---------------
        self._panel_time_rect = None
        self._btn_time_m5 = None
        self._btn_time_p5 = None
        self._btn_time_m30 = None
        self._btn_time_p30 = None
        self._btn_time_05 = None
        self._btn_time_07 = None
        self._btn_time_12 = None
        self._btn_time_19 = None
        self._btn_time_21 = None
        self._btn_time_00 = None
        self._lbl_time_now = None
        self._lbl_time_phase = None
        self._lbl_time_intensity = None
        # Daytime intensity controls
        self._btn_minI_minus = None
        self._btn_minI_plus = None
        # Keyframe intensity steppers for common minutes: 00:00, 05:00, 07:00, 12:00, 19:00, 21:00
        self._btn_i_0000_minus = None
        self._btn_i_0000_plus = None
        self._btn_i_0500_minus = None
        self._btn_i_0500_plus = None
        self._btn_i_0700_minus = None
        self._btn_i_0700_plus = None
        self._btn_i_1200_minus = None
        self._btn_i_1200_plus = None
        self._btn_i_1900_minus = None
        self._btn_i_1900_plus = None
        self._btn_i_2100_minus = None
        self._btn_i_2100_plus = None
        # Save button
        self._btn_time_save = None
