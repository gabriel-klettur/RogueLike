import pygame

class MapEditorState:
    """
    Estado para el Map Editor.
    """
    def __init__(self):
        self.active = False
        self.selected_zone = None
        self.hidden_zones: set[str] = set()
        self.dragging: str | None = None
        self.drag_offset: tuple[int,int] = (0, 0)