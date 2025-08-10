import pygame
import importlib
import roguelike_game.config.players_config as players_config


class ClassSelectorManager:
    """
    Displays a class selection menu toggled by F2.
    """
    def __init__(self, state, input_config, screen, font_size=36):
        self.state = state
        self.input_config = input_config
        self.screen = screen
        # Options from configuration keys (refreshed from JSON)
        self.options = []
        self.refresh_options()
        self.selected = 0
        self.show = False
        self.font = pygame.font.SysFont("Arial", font_size)
        self.padding = 10

    def refresh_options(self):
        """Reload players_config and refresh available class options."""
        try:
            importlib.reload(players_config)
            opts = list(players_config.PLAYER_ASSETS.keys())
            self.options = opts
            # Clamp selected index
            if self.options:
                self.selected %= len(self.options)
            else:
                self.selected = 0
        except Exception:
            # In case of transient JSON edits, keep previous options
            pass

    def handle_input(self, event):
        # Handle mouse click on class options
        if event.type == pygame.MOUSEBUTTONDOWN:
            mx, my = event.pos
            # Panel dimensions
            width = 300
            line_height = self.font.get_height() + self.padding * 2
            height = line_height * len(self.options)
            x = (self.screen.get_width() - width) // 2
            y = (self.screen.get_height() - height) // 2
            # Check click within panel
            if x <= mx <= x + width and y <= my <= y + height:
                rel_y = my - y
                idx = rel_y // line_height
                if 0 <= idx < len(self.options):
                    chosen = self.options[idx]
                    self.state.current_player_class = chosen
                    self.show = False
                    return chosen

        if event.type == pygame.KEYDOWN:
            key = event.key
            up_key = self.input_config.get_key("move_up")
            down_key = self.input_config.get_key("move_down")
            if key == up_key:
                self.selected = (self.selected - 1) % len(self.options)
                return None
            elif key == down_key:
                self.selected = (self.selected + 1) % len(self.options)
                return None
            elif key == pygame.K_RETURN:
                chosen = self.options[self.selected]
                self.state.current_player_class = chosen
                self.show = False
                return chosen
        return None

    def draw(self):
        # Ensure options reflect latest JSON when the selector is visible
        self.refresh_options()
        # Semi-transparent overlay
        overlay = pygame.Surface(self.screen.get_size(), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 128))
        # Panel
        width = 300
        line_height = self.font.get_height() + self.padding * 2
        height = line_height * len(self.options)
        x = (self.screen.get_width() - width) // 2
        y = (self.screen.get_height() - height) // 2
        panel = pygame.Surface((width, height))
        panel.fill((50, 50, 50))
        # Draw options
        for i, opt in enumerate(self.options):
            color = (255, 255, 0) if i == self.selected else (255, 255, 255)
            text = self.font.render(opt, True, color)
            tx = self.padding
            ty = i * line_height + self.padding
            panel.blit(text, (tx, ty))
        overlay.blit(panel, (x, y))
        self.screen.blit(overlay, (0, 0))
