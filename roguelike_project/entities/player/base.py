from .stats import PlayerStats
from .movement import PlayerMovement
from .renderer import PlayerRenderer
from .assets import load_character_assets
from roguelike_project.entities.projectiles.fireball import Fireball 
from roguelike_project.entities.projectiles.laser_shot import LaserShot
import pygame

class Player:
    def __init__(self, x, y, character_name="first_hero"):
        self.x = x
        self.y = y
        self.character_name = character_name
        self.is_walking = False

        self.stats = PlayerStats(character_name)
        self.sprites, self.sprite_size = load_character_assets(character_name)
        self.direction = "down"
        self.sprite = self.sprites[self.direction]

        self.rect = None
        self.hitbox = None

        self.projectiles = []
        self.explosions = []
        self.lasers = []

        self.movement = PlayerMovement(self)
        self.renderer = PlayerRenderer(self)

    def change_character(self, new_character_name):
        self.__init__(self.x, self.y, new_character_name)
    
    def shoot(self, angle):
        center_x = self.x + self.sprite_size[0] // 2
        center_y = self.y + self.sprite_size[1] // 2
        fireball = Fireball(center_x, center_y, angle, self.explosions)
        self.projectiles.append(fireball)

    def move(self, dx, dy, collision_mask, obstacles):
        self.movement.move(dx, dy, collision_mask, obstacles)

    def take_damage(self):
        self.stats.take_damage()

    def restore_all(self):
        self.stats.restore_all()

    def update(self, solid_tiles, enemies):
        # Actualizar proyectiles
        self.projectiles = [p for p in self.projectiles if p.alive]
        for projectile in self.projectiles:
            projectile.update(solid_tiles=solid_tiles, enemies=enemies)

        # Actualizar explosiones
        self.explosions = [e for e in self.explosions if not e.finished]
        for explosion in self.explosions:
            explosion.update()
        
        if self.shooting_laser:
            mouse_x, mouse_y = pygame.mouse.get_pos()
            world_mouse_x = mouse_x / self.renderer.camera.zoom + self.renderer.camera.offset_x
            world_mouse_y = mouse_y / self.renderer.camera.zoom + self.renderer.camera.offset_y
            self.fire_laser(world_mouse_x, world_mouse_y)

        # Actualizar láseres (eliminar los expirados)
        for laser in self.lasers[:]:
            if not laser.update():
                self.lasers.remove(laser)

    def render(self, screen, camera):        
        self.renderer.render(screen, camera)

    def render_hud(self, screen, camera):
        self.renderer.render_hud(screen, camera)

    def fire_laser(self, target_x, target_y):
        center_x = self.x + self.sprite_size[0] // 2
        center_y = self.y + self.sprite_size[1] // 2
        new_laser = LaserShot(center_x, center_y, target_x, target_y)
        
        # 🚀 Agregar nueva instancia
        self.lasers.append(new_laser)

        # 🧹 Limitar a 3 láseres activos (efecto continuo)
        if len(self.lasers) > 3:
            self.lasers.pop(0)
