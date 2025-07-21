class LayersPanelState:
    """State for the Layers Panel"""
    def __init__(self):
        self.visible_layers = {}  # layer visibility dict
        self.option_rects: dict = {}  # clickable rects per layer
        # Initialize default visibility in controller based on editor state
