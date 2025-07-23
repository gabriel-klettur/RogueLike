from typing import Optional, Tuple

class TilesViewPanelState:
    """State for the Tiles View Panel"""
    def __init__(self):
        self.active = False  # whether the view panel is active
        self.pos: Optional[Tuple[int, int]] = None
        self.dragging: bool = False
        self.drag_offset: Tuple[int, int] = (0, 0)
        self.size = None
        # Add more state variables as needed
