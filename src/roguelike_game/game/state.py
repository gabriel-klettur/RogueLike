# Path: src/roguelike_game/game/state.py

class GameState:
    def __init__(self, screen, background, player, obstacles, buildings, camera, clock, font, menu, tiles, enemies=None):
        self.screen = screen
        self.background = background        
        self.player = player
        self.obstacles = obstacles
        self.camera = camera
        self.clock = clock
        self.font = font
        self.menu = menu                

        self.tiles = tiles
        self.remote_entities = {}
        self.enemies = enemies or []
        self.buildings = buildings

        self.running = True
        self.show_menu = False
        self.mode = "local"

        #!------------ Deberia ir en otro lugar ------------
        self.tile_editor_active = False
