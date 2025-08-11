from dataclasses import dataclass, field
from typing import Dict, List, Optional, Set, Tuple


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

    # Runtime state
    panel_surf: Optional[object] = None
    panel_rect: Optional[object] = None
    last_update_time: float = 0.0
    scroll_offset: int = 0
    label_w: int = 0
    value_w: int = 0
    line_keys: List[str] = field(default_factory=list)
    collapsed_groups: Set[str] = field(default_factory=set)
    initially_collapsed: bool = False

    def reset_panel(self):
        self.panel_surf = None
        self.panel_rect = None
        self.line_keys.clear()
