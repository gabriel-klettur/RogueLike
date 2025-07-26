class GameState:
    def __init__(self):
        self.running = True
        self.mode = "local"

        # Player class selection state
        self.current_player_class = None

        # Editor states (models assigned during initialization)
        self.item_editor_state = None
        self.inventory_editor_state = None
        self.entities_editor_state = None
        self.spell_editor_state = None

        # Building editor state alias
        self.editor = None

