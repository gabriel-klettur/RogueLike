import pygame
from typing import Dict, Optional, Tuple


class ParticlesPickerModel:
    """Model for the Particles Picker grid."""

    def __init__(self):
        # id -> definition (source dict used to build preview)
        self.items: Dict[str, dict] = {}
        # id -> preview provider (object with render((w,h), dt_ms) -> Surface)
        self.preview_providers: Dict[str, object] = {}
        # Layout
        self.cell_size: int = 64
        self.cell_margin: int = 8
        self.columns: int = 8
        self.grid_origin: Tuple[int, int] = (16, 16)
        self.grid_rect: Optional[pygame.Rect] = None
        # Grouping and hit testing
        self.group_by_kind: bool = True
        self.cell_rects: Dict[str, pygame.Rect] = {}
        self.toggle_rect: Optional[pygame.Rect] = None
        # Scrolling
        self.scroll_y: int = 0
        self.content_height: int = 0
        self.viewport_height: int = 0
        # Hover/selection (future use)
        self.hovered_id: Optional[str] = None
        self.selected_id: Optional[str] = None
        # Modes
        self.delete_mode_active: bool = False
        self.add_mode_active: bool = False
