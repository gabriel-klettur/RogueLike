class TilesCollisionPanelState:
    """State for the Tiles Collision Panel"""
    def __init__(self):
        self.open = False  # whether collision panel is open
        self.choice = None  # current collision choice
        self.option_rects: dict = {}  # clickable rects for collision options
        # Add more state variables as needed
