# core/game/state.py

class GameState:
    def __init__(self, screen, background, player, obstacles, camera, clock, font, menu, tiles):
        self.screen = screen
        self.background = background        
        self.player = player
        self.obstacles = obstacles
        self.camera = camera
        self.clock = clock
        self.font = font
        self.menu = menu        
        self.tiles = tiles  # ✅ Aquí se guarda
        self.remote_entities = {}

        self.running = True
        self.show_menu = False
        self.mode = "local"
