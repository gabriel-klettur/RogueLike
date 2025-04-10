# roguelike_project/entities/combat/combat_system.py

import pygame
import time
from roguelike_project.entities.combat.types.fireball import Fireball
from roguelike_project.entities.combat.visual_effects.laser_beam import LaserShot


class CombatSystem:
    def __init__(self, owner):
        self.owner = owner  # normalmente será el player
        self.projectiles = []
        self.explosions = []
        self.lasers = []

        self.shooting_laser = False
        self.last_laser_time = 0

    def shoot_fireball(self, angle):
        x = self.owner.x + self.owner.sprite_size[0] // 2
        y = self.owner.y + self.owner.sprite_size[1] // 2
        fireball = Fireball(x, y, angle, self.explosions)
        self.projectiles.append(fireball)

    def shoot_laser(self, target_x, target_y, enemies):
        x = self.owner.x + self.owner.sprite_size[0] // 2
        y = self.owner.y + self.owner.sprite_size[1] // 2
        laser = LaserShot(x, y, target_x, target_y, enemies=enemies)
        self.lasers.append(laser)

        if len(self.lasers) > 3:
            self.lasers.pop(0)

    def update(self, state):
        solid_tiles = [tile for tile in state.tiles if tile.solid]
        enemies = state.enemies + list(state.remote_entities.values())

        # Actualizar proyectiles
        for p in self.projectiles:
            p.update(solid_tiles, enemies)
        self.projectiles = [p for p in self.projectiles if p.alive]

        # Actualizar explosiones
        for e in self.explosions:
            e.update()
        self.explosions[:] = [e for e in self.explosions if not e.finished]

        # Fuego continuo de láser
        if self.shooting_laser:
            mouse_x, mouse_y = pygame.mouse.get_pos()
            world_x = mouse_x / state.camera.zoom + state.camera.offset_x
            world_y = mouse_y / state.camera.zoom + state.camera.offset_y
            self.shoot_laser(world_x, world_y, enemies)

        # Actualizar láseres
        for laser in self.lasers[:]:
            if not laser.update():
                self.lasers.remove(laser)

    def render(self, screen, camera):
        for explosion in self.explosions:
            explosion.render(screen, camera)
        for laser in self.lasers:
            laser.render(screen, camera)
        for projectile in self.projectiles:
            projectile.render(screen, camera)
