import pygame

import logging
logger = logging.getLogger(__name__)

class MenuHandler:
    """
    Gestiona las opciones del menú, la navegación y la ejecución de acciones.
    """
    def __init__(self, state, input_config, configurator):
        self.state = state
        self.input_config = input_config
        self.configurator = configurator
        self.selected = 0

    def get_options(self):
        """
        Genera la lista de opciones según el estado actual.
        """
        mode_option = "Modo local" if self.state.mode == "online" else "Modo multijugador"
        return ["Continuar", mode_option, "Configurar Botones", "Salir"]

    def handle_input(self, event):
        """
        Procesa teclas de navegación y selección.
        """
        options = self.get_options()
        if event.type == pygame.KEYDOWN:
            if event.key == pygame.K_UP:
                self.selected = (self.selected - 1) % len(options)
            elif event.key == pygame.K_DOWN:
                self.selected = (self.selected + 1) % len(options)
            elif event.key == pygame.K_RETURN:
                return options[self.selected]
        return None

    def execute_option(self, selected):
        """
        Ejecuta la opción del menú seleccionada.
        """
        if selected == "Salir":
            self.state.running = False
        elif selected == "Configurar Botones":
            self.configurator.configure()
            # Borrar eventos pendientes (p.ej. ESC) para no alternar menú principal
            pygame.event.clear(pygame.KEYDOWN)
        elif selected in ("Modo multijugador", "Modo local"):
            self._toggle_mode()

    def _toggle_mode(self):
        """
        Alterna entre modo local y online.
        """
        if self.state.mode == "local":
            self.state.mode = "online"
            logger.info("Conectando al servidor...")
            # self.state.network.connect()
        else:
            self.state.mode = "local"
            logger.info("Cambiando a modo local...")
            # self.state.network.disconnect()
