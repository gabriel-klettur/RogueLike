
class GameState:
    def __init__(self, player, obstacles, buildings, tiles, enemies=None):
        
        
        #!------------ Entidades ------------
        self.player = player
        self.obstacles = obstacles        
        self.remote_entities = {}
        self.enemies = enemies or []
        self.buildings = buildings

        #!-------------- MAPA ---------------
    
        self.tiles = tiles        
                                        
        #!------------ UI/ FLAGS ------------
        
        self.show_menu = False
        self.mode = "local"
        self.tile_editor_active = False

# Path: src/roguelike_game/game/state.py