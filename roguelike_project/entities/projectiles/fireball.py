import pygame
from roguelike_project.entities.projectiles.base import Projectile
from roguelike_project.entities.projectiles.explosion import Explosion
from roguelike_project.utils.loader import load_image, load_explosion_frames

class Fireball(Projectile):
    def __init__(self, x, y, angle, explosions_list=None):
        sprite = load_image("assets/projectiles/fireball.png", (64, 64))
        speed = 10
        lifespan = 60
        super().__init__(x, y, angle, speed, lifespan, sprite)
        self.damage = 10
        self.explosions_list = explosions_list

        # Cargar frames una sola vez (static)
        if not hasattr(Fireball, "explosion_frames"):
            Fireball.explosion_frames = load_explosion_frames(
                "assets/projectiles/explosion_{}.png", 4, (64, 64)
            )

        def explode(x, y):
            if self.explosions_list is not None:
                self.explosions_list.append(Explosion(x, y, Fireball.explosion_frames))

        self.on_explode = explode
