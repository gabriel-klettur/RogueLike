# Path: src/roguelike_game/game/menu_manager.py
import pygame
import pygame_menu

class MenuManager:
    def __init__(self, state, screen, input_config, font_size=36):
        self.state = state
        self.screen = screen
        self.input_config = input_config
        self.selected = 0
        self.font = pygame.font.SysFont("Arial", font_size)
        self.surface = pygame.Surface((400, 250))
        self.surface.set_alpha(240)
        self.bg_color = (30, 30, 30)
        self.default_color = (255, 255, 255)
        self.selected_color = (255, 200, 0)
        self.show_menu = False

    def get_options(self):
        # Cambia el texto según el modo actual
        mode_option = "Modo local" if self.state.mode == "online" else "Modo multijugador"
        return [ mode_option, "Configurar Botones", "Salir"]

    def handle_input(self, event):
        options = self.get_options()
        if event.type == pygame.KEYDOWN:
            if event.key == pygame.K_UP:
                self.selected = (self.selected - 1) % len(options)
            elif event.key == pygame.K_DOWN:
                self.selected = (self.selected + 1) % len(options)
            elif event.key == pygame.K_RETURN:
                return options[self.selected]
        return None

    def draw(self, screen):
        options = self.get_options()
        self.surface.fill(self.bg_color)
        for i, option in enumerate(options):
            color = self.selected_color if i == self.selected else self.default_color
            text = self.font.render(option, True, color)
            self.surface.blit(text, (50, 40 + i * 50))
        screen.blit(self.surface, (400, 300))

    def execute_menu_option(self, selected, state):   
        if selected == "Salir":
            state.running = False
        elif selected == "Configurar Botones":
            self.configure_bindings()
        elif selected in ("Modo multijugador","Modo local"):
            self.toggle_mode(state)

    def toggle_mode(self, state):
        """
        Cambia entre local y online usando NetworkManager.
        """
        if state.mode == "local":
            state.mode = "online"
            print(" Conectando al servidor...")
            #state.network.connect()
        else:
            state.mode = "local"
            print(" Cambiando a modo local...")
            #state.network.disconnect()

    def configure_bindings(self):
        """Muestra menú de reasignación con botones y prompt de tecla."""
        # Recargar bindings desde JSON para reflejar cambios sin reiniciar
        self.input_config._load()
        running = True
        while running:
            menu = pygame_menu.Menu('Configurar Botones', 600, 400, theme=pygame_menu.themes.THEME_DARK)
            # Botones para cada acción
            for action, keyname in self.input_config.bindings.items():
                label = action.replace('_', ' ').title()
                # Al pulsar, esperar nueva tecla
                menu.add.button(f'{label}: {keyname}', lambda act=action: self._prompt_key(act) or menu.disable())
            # Botón de guardar y salir
            menu.add.button('Guardar y Volver', lambda: (self.input_config.save(), setattr(self, '_exit_bind', True), menu.disable()))
            # Flag de salida
            self._exit_bind = False
            menu.mainloop(self.screen)
            if getattr(self, '_exit_bind', False):
                del self._exit_bind
                running = False

    def _prompt_key(self, action):
        """Muestra mensaje y captura siguiente KEYDOWN para reasignar la acción."""
        prompt = f'Presione nueva tecla para {action.replace("_"," ").title()}'
        # Mostrar prompt
        text = self.font.render(prompt, True, (255, 255, 255))
        self.screen.fill((0, 0, 0))
        self.screen.blit(text, (50, self.screen.get_height() // 2))
        pygame.display.flip()
        # Capturar tecla
        waiting = True
        while waiting:
            for e in pygame.event.get():
                if e.type == pygame.KEYDOWN:
                    keyname = pygame.key.name(e.key)
                    self.input_config.set_key(action, keyname)
                    waiting = False