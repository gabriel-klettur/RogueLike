import pygame
from pathlib import Path
from roguelike_engine.world.world_config import WORLD_CONFIG

import logging
logger = logging.getLogger(__name__)

class MenuHandler:
    """
    Gestiona las opciones del menú, la navegación y la ejecución de acciones.
    """
    def __init__(self, state, input_config, configurator, options_configurator=None):
        self.state = state
        self.input_config = input_config
        self.configurator = configurator
        self.options_configurator = options_configurator
        self.selected = 0
        self.mode = "pause"  # 'start' | 'pause'

    def get_options(self):
        """
        Genera la lista de opciones según el estado actual.
        """
        if self.mode == "start":
            opts = ["Nuevo juego", "Opciones", "Salir"]
            if self._has_saves():
                # Insertar Cargar juego después de Nuevo juego
                opts.insert(1, "Cargar juego")
            return opts
        # pause menu
        mode_option = "Modo local" if self.state.mode == "online" else "Modo multijugador"
        # Incluir 'Nueva Partida' también en el menú de pausa
        opts = ["Continuar", "Nueva Partida", "Guardar partida", "Opciones", mode_option, "Salir"]
        if self._has_saves():
            # Insertar Cargar juego después de Guardar partida
            try:
                gi = opts.index("Guardar partida")
                opts.insert(gi + 1, "Cargar juego")
            except ValueError:
                opts.insert(2, "Cargar juego")
        return opts

    def handle_input(self, event):
        """
        Procesa teclas de navegación y selección.
        """
        options = self.get_options()
        if event.type == pygame.KEYDOWN:
            if event.key in (pygame.K_UP, pygame.K_w, pygame.K_a):
                self.selected = (self.selected - 1) % len(options)
            elif event.key in (pygame.K_DOWN, pygame.K_s, pygame.K_d):
                self.selected = (self.selected + 1) % len(options)
            elif event.key in (pygame.K_RETURN, pygame.K_SPACE):
                return options[self.selected]
        return None

    def execute_option(self, selected):
        """
        Ejecuta la opción del menú seleccionada.
        """
        if selected == "Salir":
            self.state.running = False
        elif selected in ("Configurar Botones", "Opciones"):
            # Limpiar eventos que dispararon la entrada (ENTER/click) para evitar
            # que el primer frame del submenú consuma esa misma tecla/click.
            try:
                pygame.event.clear([pygame.KEYDOWN, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP])
            except Exception:
                pygame.event.clear()
            # Si existe un configurador de opciones (Inputs/Sounds), úsalo.
            if self.options_configurator is not None:
                self.options_configurator.configure()
            else:
                # Fallback: configurador clásico de inputs
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
            net = getattr(self.state, "network", None)
            if net is not None and hasattr(net, "connect"):
                self.state.mode = "online"
                logger.info("Conectando al servidor...")
                # self.state.network.connect()
            else:
                self.state.mode = "local"
                logger.info("Modo multijugador no disponible; permanece en modo local.")
        else:
            self.state.mode = "local"
            logger.info("Cambiando a modo local...")
            # self.state.network.disconnect()

    def _has_saves(self) -> bool:
        """Devuelve True si hay partidas guardadas disponibles para cargar."""
        try:
            save_dir: Path = WORLD_CONFIG.save_dir
            if not save_dir.exists():
                return False
            # Partidas multi-slot
            if any(save_dir.glob('partida_*.json')):
                return True
        except Exception:
            pass
        return False
