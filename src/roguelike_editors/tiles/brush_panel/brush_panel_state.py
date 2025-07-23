import pygame

class BrushPanelState:
    """
    State for the Brush Size Panel.
    """
    def __init__(self):
        # Initializes all brush sizes (1x1 to 10x10)
        self.sizes = [(i, i) for i in range(1, 11)]
        # Currently selected index in sizes
        self.selected_index = 0
        # Panel visibility flag
        self.visible = False
        # Clickable rects for each size option (index -> pygame.Rect)
        self.option_rects: dict[int, pygame.Rect] = {}

    def select(self, index: int):
        """
        Update selected index to the given valid index.
        """
        if 0 <= index < len(self.sizes):
            self.selected_index = index

    @property
    def selected_size(self):
        """
        Returns the current brush size (width, height).
        """
        return self.sizes[self.selected_index]