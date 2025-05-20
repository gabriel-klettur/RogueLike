# Path: src/roguelike_game/entities/player/controller/player_controller.py
import pygame
from roguelike_game.entities.player.model.player_model import PlayerModel
from roguelike_game.entities.player.view.assets import PlayerAssets
from roguelike_game.entities.player.view.player_view import PlayerView
from roguelike_game.entities.player.view.hud_view import HUDView
from roguelike_engine.utils.loader import load_image
from roguelike_game.entities.player.config_player import ORIGINAL_SPRITE_SIZE, HUD_RESTORE, HUD_DASH, HUD_SLASH, HUD_SHIELD, HUD_FIREWORK, HUD_SMOKE, HUD_LIGHTNING, HUD_ARCANE_FIRE, HUD_TELEPORT

class PlayerController:
    def __init__(self, x, y, z_state=None, obstacles=None):
        # Estado inyectado desde Game
        self.state = None

        # Modelo interno
        self.model     = PlayerModel(x, y)
        self.stats     = self.model.stats
        self.movement  = self.model.movement
        self.attack    = self.model.attack
        self.obstacles = obstacles or []

        # Vista del sprite: usamos siempre el tamaño original para recorte
        sprites, _      = PlayerAssets(self.model.character_name, ORIGINAL_SPRITE_SIZE).get_sprites()
        self.player_view   = PlayerView(sprites)
        self.renderer      = self.player_view  # alias para Game._init_systems

        # Vista del HUD
        icon_paths = {
            "Restore":    HUD_RESTORE,
            "Dash":       HUD_DASH,
            "Slash":      HUD_SLASH,
            "Shield":     HUD_SHIELD,
            "Firework":   HUD_FIREWORK,
            "Smoke":      HUD_SMOKE,
            "Lightning":  HUD_LIGHTNING,
            "Pixel Fire": HUD_ARCANE_FIRE,
            "Teleport":   HUD_TELEPORT,
        }
        icons = {n: load_image(p, (48,48)) for n,p in icon_paths.items()}
        self.hud_view = HUDView(icons)

        # Registrar capa Z
        if z_state:
            from roguelike_game.systems.config_z_layer import Z_LAYERS
            z_state.set(self.model, Z_LAYERS["player"])

    # Delegación de atributos para compatibilidad con Game
    @property
    def x(self): return self.model.x
    @x.setter
    def x(self, v): self.model.x = v

    @property
    def y(self): return self.model.y
    @y.setter
    def y(self, v): self.model.y = v

    @property
    def sprite_size(self): return self.model.sprite_size

    def center(self): return (self.model.x + self.model.sprite_size[0]//2,
                               self.model.y + self.model.sprite_size[1]//2)
    def hitbox(self): return self.model.hitbox()

    # Métodos imitando Player original
    def move(self, dx, dy, collision_tiles, obstacles):
        self.movement.move(dx, dy, collision_tiles, obstacles)

    def take_damage(self):
        self.stats.take_damage()

    def restore_all(self):
        return self.stats.restore_all()

    def change_character(self, new_name):
        # Re-inicializar modelo y vistas
        self.model.change_character(new_name)
        sprites, _ = PlayerAssets(self.model.character_name, ORIGINAL_SPRITE_SIZE).get_sprites()
        self.player_view = PlayerView(sprites)
        self.renderer    = self.player_view

    # Renderizado del jugador (solo sprite)
    def render(self, screen, camera):
        self.player_view.render(self.model, screen, camera)

    # Renderizado del HUD (barras y cooldowns)
    def render_hud(self, screen, camera):
        self.hud_view.draw_status_bars(self.model, screen, camera)
        self.hud_view.render_cooldowns(self.model, screen)

    # Update frame (dash, teletransport) - usar lista precomputada de tiles sólidos
    def update(self, map):
        self.movement.update_dash(
            map.solid_tiles,
            self.obstacles
        )

    # Placeholder de manejo de entrada si se requiere
    def handle_input(self, event):
        pass