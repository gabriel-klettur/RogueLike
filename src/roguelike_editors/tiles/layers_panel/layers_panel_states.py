from typing import Optional, Tuple

class LayersPanelState:
    """State for the Layers Panel"""
    def __init__(self):
        self.visible_layers = {}  # layer visibility dict
        self.option_rects: dict = {}  # clickable rects per layer
        # Panel drag state
        self.pos: Optional[Tuple[int, int]] = None
        self.dragging: bool = False
        self.drag_offset: Tuple[int, int] = (0, 0)
        # Initialize default visibility in controller based on editor state
