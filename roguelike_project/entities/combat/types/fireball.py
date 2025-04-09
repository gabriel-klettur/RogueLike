import pygame
from roguelike_project.entities.combat.base.projectile import Projectile
from roguelike_project.utils.loader import load_image, load_explosion_frames
from roguelike_project.entities.combat.explosions import EXPLOSION_TYPES

class Fireball(Projectile):
    # Lista cíclica de tipos
    explosion_types = list(EXPLOSION_TYPES.values())
    _explosion_index = 0

    def __init__(self, x, y, angle, explosions_list=None):
        sprite = load_image("assets/projectiles/fireball.png", (64, 64))
        speed = 10
        lifespan = 60
        super().__init__(x, y, angle, speed, lifespan, sprite)
        self.damage = 10
        self.explosions_list = explosions_list

        if not hasattr(Fireball, "explosion_frames"):
            Fireball.explosion_frames = load_explosion_frames(
                "assets/projectiles/explosion_{}.png", 4, (64, 64)
            )

        def explode(x, y):
            print(f"🔥 ¡Explosión ejecutada en ({x}, {y})!")
            if self.explosions_list is not None:
                explosion_cls = Fireball.explosion_types[Fireball._explosion_index]
                print(f"🧨 Clase de explosión usada: {explosion_cls}")
                self.explosions_list.append(explosion_cls(x, y))
                Fireball._explosion_index = (Fireball._explosion_index + 1) % len(Fireball.explosion_types)

        self.on_explode = explode
