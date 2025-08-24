from typing import Optional
import pygame


class SpellsEditorView:
    """Container view for the Spells Editor.

    Currently the picker/properties/toolbar subviews render themselves. This
    view exists to mirror the Items editor layout point and to expose rects if
    we centralize rendering in the future.
    """

    def __init__(self) -> None:
        self.picker_rect: Optional[pygame.Rect] = None
        self.properties_rect: Optional[pygame.Rect] = None
        self.title_rect: Optional[pygame.Rect] = None

    def sync_from_subviews(self, picker_title_rect: Optional[pygame.Rect], props_rect: Optional[pygame.Rect]) -> None:
        self.title_rect = picker_title_rect
        self.properties_rect = props_rect

