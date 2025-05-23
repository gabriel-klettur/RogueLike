# src/roguelike_game/entities/player/controller/player_controller.py

import pygame
from src.roguelike_game.entities.player.model.player_model import PlayerModel
from src.roguelike_game.entities.player.view.assets import PlayerAssets
from src.roguelike_game.entities.player.view.player_view import PlayerView
from src.roguelike_game.entities.player.view.hud_view import HUDView
from roguelike_engine.utils.loader import load_image

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

        # Vista del sprite
        sprites, size      = PlayerAssets(self.model.character_name, self.model.sprite_size).get_sprites()
        self.player_view   = PlayerView(sprites)
        self.renderer      = self.player_view  # alias para Game._init_systems

        # Vista del HUD
        icon_paths = {
            "Restore":    "assets/ui/restore_icon.png",
            "Dash":       "assets/ui/dash_icon.png",
            "Slash":      "assets/ui/slash_icon.png",
            "Shield":     "assets/ui/shield_icon.png",
            "Firework":   "assets/ui/firework_icon.png",
            "Smoke":      "assets/ui/smoke_icon.png",
            "Lightning":  "assets/ui/lightning_icon.png",
            "Pixel Fire": "assets/ui/pixel_fire_icon.png",
            "Teleport":   "assets/ui/teleport_icon.png",
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
        sprites, size = PlayerAssets(self.model.character_name, self.model.sprite_size).get_sprites()
        self.player_view = PlayerView(sprites)
        self.renderer    = self.player_view

    # Renderizado del jugador (solo sprite)
    def render(self, screen, camera):
        self.player_view.render(self.model, screen, camera)

    # Renderizado del HUD (barras y cooldowns)
    def render_hud(self, screen, camera):
        self.hud_view.draw_status_bars(self.model, screen, camera)
        self.hud_view.render_cooldowns(self.model, screen)

    # Update frame (dash, teletransport)
    def update(self, dt):
        self.movement.update_dash(
            [t for t in self.state.tiles if t.solid],
            self.obstacles
        )

    # Placeholder de manejo de entrada si se requiere
    def handle_input(self, event):
        pass
