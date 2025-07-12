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
        # Loop para refrescar bindings o salir
        while True:
            # Configuración de tema con texto más pequeño y disposición en 2 columnas
            theme = pygame_menu.themes.THEME_DARK.copy()
            theme.title_font_size = 24
            theme.widget_font_size = 18
            # Calcular filas según número de botones, guardar y margen vertical
            margin_count = 1  # vertical_margin añadido como widget extra
            total_buttons = len(self.input_config.bindings) + 1 + margin_count
            rows = math.ceil(total_buttons / 2)
            menu = pygame_menu.Menu('Configurar Botones', 600, 400, theme=theme, columns=2, rows=rows)
            # Flags para control interno
            refresh = False
            exit_menu = False
            orig_disable = menu.disable
            # Callbacks para flags
            def set_refresh():
                nonlocal refresh
                refresh = True
            def set_exit():
                nonlocal exit_menu
                exit_menu = True
            # Botones de binding
            for action, keyname in self.input_config.bindings.items():
                label = action.replace('_', ' ').title()
                def make_binding_cb(act):
                    def cb():
                        self._prompt_key(act)
                        set_refresh()
                        orig_disable()
                    return cb
                menu.add.button(f'{label}: {keyname}', make_binding_cb(action))
            menu.add.vertical_margin(30)
            # Guardar y volver
            def on_save():
                self.input_config.save()
                set_exit()
                orig_disable()
            menu.add.button('Guardar y Volver', on_save, align=ALIGN_CENTER)
            # Volver sin guardar
            def on_cancel():
                set_exit()
                orig_disable()
            menu.add.button('Volver sin Guardar', on_cancel, align=ALIGN_CENTER)
            # Ejecutar menu
            menu.mainloop(self.screen)
            if refresh:
                continue
            else:
                break
            while True:
                events = pygame.event.get()
                for e in events:
                    if e.type == pygame.KEYDOWN and e.key == pygame.K_ESCAPE:
                        menu.disable()
                # Salir si se solicitó exit (ESC o guardar)
                if getattr(self, '_exit_bind', False):
                    del self._exit_bind
                    running = False
                    break
                try:
                    menu.update(events)
                    menu.draw(self.screen)
                    pygame.display.flip()
                except RuntimeError as e:
                    if 'menu is not enabled' in str(e):
                        # Refresh o exit detectado, romper loop interno
                        break
                    else:
                        raise

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
