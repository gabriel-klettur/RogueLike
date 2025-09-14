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
        # Hover/selection (future use)
        self.hovered_id: Optional[str] = None
        self.selected_id: Optional[str] = None
