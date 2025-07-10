"""
Package de gestión de menú refactorizado.
"""
import logging

from .handler import MenuHandler
from roguelike_ui.widgets.menu_renderer import MenuRenderer
from roguelike_ui.widgets.menu_configurator import MenuConfigurator

logger = logging.getLogger(__name__)
logger.setLevel(logging.INFO)

class MenuManager:
    """
    Orquesta la lógica, entrada y renderizado del menú.
    """
    def __init__(self, state, screen, input_config, font_size=36):
        self.state = state
        self.screen = screen
        self.input_config = input_config

        # Componentes del menú
        self.renderer = MenuRenderer(font_size)
        self.configurator = MenuConfigurator(input_config, screen, self.renderer.font)
        self.handler = MenuHandler(state, input_config, self.configurator)

        # Flag para mostrar/ocultar menú
        self.show_menu = False

    def handle_input(self, event):
        """
        Procesa la entrada del menú y devuelve la opción seleccionada o None.
        """
        return self.handler.handle_input(event)

    def draw(self, screen):
        """
        Dibuja el menú y devuelve el rect para dirty rects.
        """
        options = self.handler.get_options()
        selected = self.handler.selected
        return self.renderer.draw(screen, selected, options)

    def execute_menu_option(self, selected, state):
        """
        Ejecuta la acción seleccionada en el menú.
        """
        self.handler.execute_option(selected)
