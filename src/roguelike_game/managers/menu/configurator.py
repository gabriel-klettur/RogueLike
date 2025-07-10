"""
Configurador de botones del menú usando pygame_menu.
"""
import pygame
import pygame_menu

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
            menu = pygame_menu.Menu('Configurar Botones', 600, 400, theme=pygame_menu.themes.THEME_DARK)
            for action, keyname in self.input_config.bindings.items():
                label = action.replace('_', ' ').title()
                menu.add.button(
                    f'{label}: {keyname}',
                    lambda act=action: self._prompt_key(act) or menu.disable()
                )
            menu.add.button(
                'Guardar y Volver',
                lambda: (
                    self.input_config.save(),
                    setattr(self, '_exit_bind', True),
                    menu.disable()
                )
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
                    keyname = f'K_{pygame.key.name(e.key).upper()}'
                    self.input_config.set_key(action, keyname)
                    waiting = False
