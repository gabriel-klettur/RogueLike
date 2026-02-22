from typing import Optional
import pygame


class ItemsEditorView:
    """Vista del Editor de Ítems.

    Hoy no dibuja directamente; el controller invoca a los subpaneles. Este
    contenedor existe para futuras responsabilidades de layout y para exponer
    rects de interés si se centraliza el render.
    """

    def __init__(self) -> None:
        self.picker_rect: Optional[pygame.Rect] = None
        self.properties_rect: Optional[pygame.Rect] = None
        self.title_rect: Optional[pygame.Rect] = None

    def sync_from_subviews(self, picker_title_rect: Optional[pygame.Rect], props_rect: Optional[pygame.Rect]) -> None:
        self.title_rect = picker_title_rect
        self.properties_rect = props_rect

