import pygame
from roguelike_project.systems.combat.view.effects.animations.projectiles import Projectile
from roguelike_project.utils.loader import load_image
from roguelike_project.systems.combat.view.effects.particles.explosions.fire import FireExplosion

class Fireball(Projectile):
    def __init__(self, x, y, angle, explosions_list=None):
        sprite = load_image("assets/projectiles/fireball.png", (64, 64))
        speed = 15
        lifespan = 60
        super().__init__(x, y, angle, speed, lifespan, sprite)
        self.damage = 10
        self.explosions_list = explosions_list

        def explode(x, y):            
            if self.explosions_list and hasattr(self.explosions_list, "add_explosion"):
                self.explosions_list.add_explosion(FireExplosion(x, y))

        self.on_explode = explode