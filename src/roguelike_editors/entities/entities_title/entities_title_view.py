import pygame
from roguelike_ui.widgets.title_panel import TitlePanel

class EntitiesTitleView:
    """
    Vista para el panel de título de entidades.
    """
    def __init__(self, controller, model, font):
        """
        Args:
            controller: Controlador del title panel.
            model: Modelo con atributo title.
            font: Instancia pygame.font.Font para renderizar el texto.
        """
        self.controller = controller
        self.model = model
        self.font = pygame.font.SysFont("Arial", 24, bold=True)
        # Configuración de posición
        self.x = 10
        self.y = 10
        # Crear widget genérico TitlePanel
        self.widget = TitlePanel(
            text=self.model.title,
            font=self.font,
            x=self.x,
            y=self.y
        )

    def render(self, screen):
        """
        Renderiza el panel de título.
        """
        # Actualizar texto dinámicamente
        self.widget.text = self.model.title
        self.widget.render(screen)