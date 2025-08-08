import pygame
from roguelike_ui.widgets.title_panel import TitlePanel

class InventoryTitleView:
    """
    Vista para el panel de título del Inventory Editor.
    Encapsula el render del TitlePanel y expone el rect para layout.
    """
    def __init__(self, controller, model, font: pygame.font.Font):
        self.controller = controller
        self.model = model
        # Usar Arial 24 bold como en otros editores
        self.font = pygame.font.SysFont("Arial", 24, bold=True)
        # Posición estándar
        self.x = 10
        self.y = 10
        # Widget reutilizable
        self.widget = TitlePanel(
            text=self.model.title,
            font=self.font,
            x=self.x,
            y=self.y
        )

    def render(self, screen: pygame.Surface) -> pygame.Rect:
        """
        Renderiza el título y devuelve el rect (x, y, w, h) del panel de título
        para que el layout de los paneles quede perfectamente alineado debajo.
        """
        # Actualizar texto dinámicamente
        self.widget.text = self.model.title
        # Pintar
        self.widget.render(screen)
        # Calcular rect del fondo del título para layout
        text_surf = self.font.render(self.widget.text or "", True, (255, 255, 255))
        bg_w = text_surf.get_width() + self.widget.padding_x * 2
        bg_h = text_surf.get_height() + self.widget.padding_y * 2
        return pygame.Rect(self.widget.x, self.widget.y, bg_w, bg_h)
