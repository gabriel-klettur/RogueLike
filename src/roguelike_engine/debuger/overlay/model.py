import pygame
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Set, Tuple


Color = Tuple[int, int, int]
ColorA = Tuple[int, int, int, int]


@dataclass
class DebugOverlayModel:
    perf_log: Dict[str, List[float]]
    font_name: str = "Consolas"
    font_size: int = 12
    bg_color: ColorA = (0, 0, 0, 180)
    text_color: Color = (255, 255, 255)
    value_color: Color = (200, 255, 200)
    padding_x: int = 10
    padding_y: int = 4
    spacing: int = 4
    border_colors: Dict[str, Color] = field(default_factory=lambda: {
        "lobby":    (255, 255, 255),
        "dungeon":  (0, 255,   0),
        "global":   (128,   0, 128),
    })
    border_width: int = 5
    update_interval: float = 0.2
    scroll_speed: int = 20

    # Runtime state
    scroll_offset: int = 0
    collapsed_groups: Set[str] = field(default_factory=set)
    initially_collapsed: bool = True
    last_update_time: float = 0.0

    # Panel surf/cache owned by the model so event handler can see sizes/rects
    panel_surf: Optional[pygame.Surface] = None
    panel_rect: Optional[pygame.Rect] = None
    line_keys: List[str] = field(default_factory=list)

    # Cached max widths from last rebuild (optional)
    label_w: int = 0
    value_w: int = 0

    def reset_panel(self):
        self.panel_surf = None
        self.panel_rect = None
        self.line_keys.clear()
