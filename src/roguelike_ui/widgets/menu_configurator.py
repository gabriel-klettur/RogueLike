import pygame
import pygame_menu
import math
from pygame_menu.locals import ALIGN_CENTER


class MenuConfigurator:
    """
    Proporciona una interfaz para reasignar bindings de teclas y guardar la configuración.
    """
    MENU_WIDTH = 600
    MENU_HEIGHT = 400

    def __init__(self, input_config, screen, font):
        self.config = input_config
        self.screen = screen
        self.font = font
        self._needs_refresh = False

    def configure(self):
        """
        Inicia el proceso de configuración de teclas. Carga la configuración existente y muestra el menú.
        """
        # Cargar configuraciones previas
        if hasattr(self.config, 'load'):
            self.config.load()
        elif hasattr(self.config, '_load'):
            self.config._load()

        # Mostrar menú hasta que no se requiera refrescar
        while True:
            self._needs_refresh = False
            self._show_menu()
            if not self._needs_refresh:
                break

    def _show_menu(self):
        """
        Construye y muestra el menú de configuración.
        """
        theme = self._configure_theme()
        rows = self._calculate_rows()
        menu = pygame_menu.Menu(
            title='Configurar Botones',
            width=self.MENU_WIDTH,
            height=self.MENU_HEIGHT,
            theme=theme,
            columns=2,
            rows=rows
        )

        # Agregar botones para cada binding
        self._add_binding_buttons(menu)
        menu.add.vertical_margin(30)

        # Botón para volver
        menu.add.button('Volver', menu.disable)

        # Ejecutar loop del menú hasta que se desactive y capturar ESC o Volver
        while True:
            events = pygame.event.get()
            for event in events:
                if event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
                    menu.disable()
            try:
                menu.update(events)
                self.screen.fill((0, 0, 0))
                menu.draw(self.screen)
                pygame.display.flip()
            except RuntimeError:
                break

    def _configure_theme(self):
        """
        Configura y retorna el tema del menú.
        """
        theme = pygame_menu.themes.THEME_DARK.copy()
        theme.title_font_size = 24
        theme.widget_font_size = 18
        return theme

    def _calculate_rows(self):
        """
        Calcula el número de filas necesario para distribuir los botones en 2 columnas,
        incluyendo espacio para el botón 'Volver' y el margen.
        """
        total_buttons = len(self.config.bindings) + 2  # +1 para 'Volver' +1 para vertical_margin
        return math.ceil(total_buttons / 2)

    def _add_binding_buttons(self, menu):
        """
        Agrega un botón por cada acción configurada en bindings.
        """
        for action, keyname in self.config.bindings.items():
            label = action.replace('_', ' ').title()
            menu.add.button(
                f'{label}: {keyname}',
                self._make_binding_callback(menu, action)
            )

    def _make_binding_callback(self, menu, action):
        """
        Genera el callback para reasignar la tecla de la acción indicada.
        """
        def callback():
            self._needs_refresh = True
            self._prompt_key(action)
            menu.disable()
        return callback

    def _prompt_key(self, action):
        """
        Muestra un prompt solicitando la nueva tecla para "action" y guarda la configuración.
        """
        prompt = f'Presione nueva tecla para {action.replace("_", " ").title()}'
        text_surface = self.font.render(prompt, True, (255, 255, 255))
        self.screen.fill((0, 0, 0))
        self.screen.blit(text_surface, (50, self.screen.get_height() // 2))
        pygame.display.flip()

        waiting = True
        while waiting:
            for event in pygame.event.get():
                if event.type == pygame.KEYDOWN:
                    if event.key == pygame.K_ESCAPE:
                        waiting = False
                        return
                    # Asignar nueva tecla
                    keyname = f'K_{pygame.key.name(event.key).upper()}'
                    self.config.set_key(action, keyname)
                    self.config.save()
                    waiting = False
                    return
