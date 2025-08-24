import pygame
from roguelike_ui.widgets.title_bar import TitleBar

class FsmTitleView:
    """
    Vista para el panel de título del editor FSM.
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
        # Crear TitleBar reutilizable y exponer widget (TitlePanel) para compatibilidad
        self.title_bar = TitleBar(text=self.model.title, x=self.x, y=self.y, font=self.font)
        self.widget = self.title_bar.panel

    def render(self, screen) -> pygame.Rect:
        """
        Renderiza el panel de título y devuelve su rect para layout.
        """
        # Actualizar texto dinámicamente y renderizar mediante TitleBar
        self.title_bar.update_text(self.model.title)
        return self.title_bar.render(screen)
