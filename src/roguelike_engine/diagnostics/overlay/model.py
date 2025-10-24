from dataclasses import dataclass, field
from typing import Dict, List, Optional, Set, Tuple

from .services import persistence as persist


@dataclass
class DiagnosticsOverlayModel:
    perf_log: Dict[str, List[float]]
    font_name: str = "Consolas"
    font_size: int = 12
    bg_color: Tuple[int, int, int, int] = (0, 0, 0, 180)
    text_color: Tuple[int, int, int] = (255, 255, 255)
    value_color: Tuple[int, int, int] = (200, 255, 200)
    padding_x: int = 10
    padding_y: int = 4
    spacing: int = 4
    border_colors: Dict[str, Tuple[int, int, int]] = field(default_factory=lambda: {
        'lobby': (255, 255, 255),
        'dungeon': (0, 255, 0),
        'global': (128, 0, 128),
    })
    border_width: int = 5
    update_interval: float = 0.2
    scroll_speed: int = 20
    # Anchor config: default overlay appears at top-right with a margin
    anchor_top_right: bool = True
    anchor_margin: int = 8

    # Runtime state
    panel_surf: Optional[object] = None
    panel_rect: Optional[object] = None
    # Current topleft position for the overlay panel (persisted during runtime)
    panel_pos: Optional[Tuple[int, int]] = None
    # Dragging state (RMB drag)
    dragging: bool = False
    drag_offset: Tuple[int, int] = (0, 0)
    last_update_time: float = 0.0
    scroll_offset: int = 0
    label_w: int = 0
    value_w: int = 0
    line_keys: List[str] = field(default_factory=list)
    # Parallel to line_keys; indentation level (0+). None for lines where level is unknown.
    line_levels: List[Optional[int]] = field(default_factory=list)
    # Per-line override colors for right-side values; None means use default value_color.
    # This list must be kept aligned with the currently rendered lines (after paging).
    value_colors: List[Optional[Tuple[int, int, int]]] = field(default_factory=list)
    collapsed_groups: Set[str] = field(default_factory=set)
    initially_collapsed: bool = True

    # Minimize/restore UI state
    is_minimized: bool = False
    header_rect: Optional[object] = None
    btn_min_rect: Optional[object] = None
    btn_restore_rect: Optional[object] = None
    minimized_height: int = 0

    # Animation state
    animating: bool = False
    anim_mode: str = ""  # "minimize" or "restore"
    anim_start_time: float = 0.0
    anim_duration: float = 0.15

    # Safety/config limits
    # Máximo de líneas a renderizar en el panel para evitar superficies gigantes
    max_lines: int = 400
    # Máximo de caracteres por campo (izquierda/derecha) para evitar text surfaces descomunales
    max_chars_per_field: int = 256
    # Límites duros del tamaño del surface (se combinarán con el tamaño de pantalla si existe)
    max_surface_width: int = 2000
    max_surface_height: int = 8000

    # Paging (para virtualizar contenido grande sin crear superficies enormes)
    paging_enabled: bool = True
    page_index: int = 0
    total_lines: int = 0
    lines_per_page: int = 0
    total_pages: int = 1

    # Toolbar (bottom-right) state
    toolbar_enabled: bool = True
    toolbar_minimized: bool = False
    toolbar_rect: Optional[object] = None
    toolbar_header_rect: Optional[object] = None
    toolbar_btn_min_rect: Optional[object] = None
    # Map button key -> rect
    toolbar_buttons: Dict[str, object] = field(default_factory=dict)
    # Anchor bottom-right with margin
    toolbar_anchor_bottom_right: bool = True
    toolbar_margin: int = 8
    # Per-system debug toggles (default all enabled)
    toolbar_toggles: Dict[str, bool] = field(
        default_factory=lambda: {
            "spell_collision": True,
            "npc_attack": True,
            "hitbox": True,
            "patrol": True,
            "defend_area": True,
            "telegraph": True,
            "trail": True,
            "building_collision": True,
        }
    )

    def reset_panel(self):
        self.panel_surf = None
        self.panel_rect = None
        self.line_keys.clear()
        self.line_levels.clear()
        self.value_colors.clear()
        self.header_rect = None
        self.btn_min_rect = None
        self.btn_restore_rect = None

    def load_persisted_state(self) -> None:
        try:
            cols = persist.load_overlay_state()
            if isinstance(cols, list) and cols:
                self.collapsed_groups = set(cols)
                # Disable auto-collapse if we have a persisted state
                self.initially_collapsed = False
            ui = persist.load_overlay_ui_state()
            if isinstance(ui, dict):
                self.is_minimized = bool(ui.get("minimized", False))
                # Toolbar UI
                tb = ui.get("toolbar", {}) if isinstance(ui.get("toolbar", {}), dict) else {}
                self.toolbar_minimized = bool(tb.get("minimized", False))
                tgl = tb.get("toggles", {}) if isinstance(tb.get("toggles", {}), dict) else {}
                # Merge persisted toggles with defaults; unknown keys are ignored
                for k, v in tgl.items():
                    if k in self.toolbar_toggles and isinstance(v, bool):
                        self.toolbar_toggles[k] = v
        except Exception:
            # Fail silently; diagnostics overlay should not crash the game
            pass

    def save_persisted_state(self) -> None:
        try:
            persist.save_overlay_state(
                self.collapsed_groups,
                ui={
                    "minimized": self.is_minimized,
                    "toolbar": {
                        "minimized": self.toolbar_minimized,
                        "toggles": dict(self.toolbar_toggles),
                    },
                },
            )
        except Exception:
            # Fail silently to avoid impacting runtime
            pass
