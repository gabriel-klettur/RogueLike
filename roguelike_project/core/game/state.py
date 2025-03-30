class GameState:
    def __init__(self, screen, background, collision_mask, player, obstacles, camera, clock, font, menu):
        self.screen = screen
        self.background = background
        self.collision_mask = collision_mask
        self.player = player
        self.obstacles = obstacles
        self.camera = camera
        self.clock = clock
        self.font = font
        self.menu = menu        
        self.remote_entities = {}  # ✅ Se añade al estado

        self.running = True
        self.show_menu = False

        self.mode = "local"  # o "online"
