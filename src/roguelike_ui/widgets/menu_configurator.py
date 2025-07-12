import pygame
import pygame_menu
import math
from pygame_menu.locals import ALIGN_CENTER

class MenuConfigurator:
    """
    Muestra interfaz para reasignar bindings y guardar.
    """
    def __init__(self, input_config, screen, font):
        self.input_config = input_config
        self.screen = screen
        self.font = font

    def configure(self):
        self.input_config._load()
        running = True
        while running:
            # Configuración de tema con texto más pequeño y disposición en 2 columnas
            theme = pygame_menu.themes.THEME_DARK.copy()
            theme.title_font_size = 24
            theme.widget_font_size = 18
            # Calcular filas según número de botones, guardar y margen vertical
            margin_count = 1  # vertical_margin añadido como widget extra
            total_buttons = len(self.input_config.bindings) + 1 + margin_count  # bindings + guardar + margen
            rows = math.ceil(total_buttons / 2)
            menu = pygame_menu.Menu('Configurar Botones', 600, 400, theme=theme, columns=2, rows=rows)
            # Override disable para capturar cerrar con ESC o botón
            orig_disable = menu.disable
            def disable_and_set_bind():
                self._exit_bind = True
                orig_disable()
            menu.disable = disable_and_set_bind
            for action, keyname in self.input_config.bindings.items():
                label = action.replace('_', ' ').title()
                menu.add.button(
                    f'{label}: {keyname}',
                    lambda act=action: self._prompt_key(act) or menu.disable()
                )
            menu.add.vertical_margin(30)
            menu.add.button(
                'Guardar y Volver',
                lambda: (
                    self.input_config.save(),
                    setattr(self, '_exit_bind', True),
                    menu.disable()
                ),
                align=ALIGN_CENTER,

            )
            self._exit_bind = False
            menu.mainloop(self.screen)
            if getattr(self, '_exit_bind', False):
                del self._exit_bind
                running = False

    def _prompt_key(self, action):
        prompt = f'Presione nueva tecla para {action.replace("_"," ").title()}'
        text = self.font.render(prompt, True, (255, 255, 255))
        self.screen.fill((0, 0, 0))
        self.screen.blit(text, (50, self.screen.get_height() // 2))
        pygame.display.flip()
        waiting = True
        while waiting:
            for e in pygame.event.get():
                if e.type == pygame.KEYDOWN:
                    if e.key == pygame.K_ESCAPE:
                        waiting = False
                        return
                    keyname = f'K_{pygame.key.name(e.key).upper()}'
                    self.input_config.set_key(action, keyname)
                    waiting = False
