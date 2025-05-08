# Path: src/roguelike_game/game/state.py
class GameState:
    def __init__(self):
        
        #!------------ Core State ------------
        self.running = True  

        #!------------ Entidades ------------      
        self.remote_entities = {}        
                                                
        #!------------- Editors -------------
        self.tile_editor_active = False

