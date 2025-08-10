import pygame
from roguelike_ui.widgets.title_bar import TitleBar

class InventoryTitleView:
    """
    Vista para el panel de título del Inventory Editor.
    Encapsula el render del TitlePanel y expone el rect para layout.
    """
    def __init__(self, controller, model, font: pygame.font.Font):
        self.controller = controller
        self.model = model
        # Usar la misma fuente/base visual que EntitiesTitleView
        # (alineamos estilo: Arial 24 bold)
        self.font = pygame.font.SysFont("Arial", 24, bold=True)
        # Posición estándar
        self.x = 10
        self.y = 10
        # Usar TitleBar reutilizable y exponer el TitlePanel interno como widget
        self.title_bar = TitleBar(text=self.model.title, x=self.x, y=self.y, font=self.font)
        self.widget = self.title_bar.panel

    def render(self, screen: pygame.Surface) -> pygame.Rect:
        """
        Renderiza el título y devuelve el rect (x, y, w, h) del panel de título
        para que el layout de los paneles quede perfectamente alineado debajo.
        """
        # Actualizar texto y renderizar mediante TitleBar
        self.title_bar.update_text(self.model.title)
        return self.title_bar.render(screen)
