
class GameState:
    def __init__(self):
        
        
        self.running = True

        #!------------ Entidades ------------      
        self.remote_entities = {}        
                                        
        #!------------ UI/ FLAGS ------------
        
        self.show_menu = False
        self.mode = "local"
        self.tile_editor_active = False

# Path: src/roguelike_game/game/state.py