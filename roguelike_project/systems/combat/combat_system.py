# roguelike_project/systems/combat/combat_system.py

import pygame
from roguelike_project.systems.combat.projectiles.fireball import Fireball
from roguelike_project.systems.combat.effects.laser_beam import LaserBeam
from roguelike_project.systems.combat.effects.firework_launch import FireworkLaunch  
from roguelike_project.systems.combat.explosions.firework_explosion import FireworkExplosion  
from roguelike_project.systems.combat.effects.smoke_emitter import SmokeEmitter
from roguelike_project.utils.benchmark import benchmark 

class CombatSystem:
    def __init__(self, state):
        self.state = state
        self.player = state.player

        self.projectiles = []
        self.explosions = []
        self.lasers = []
        self.smoke_emitters = []
        self.fireworks = [] 

        self.shooting_laser = False
        self.last_laser_time = 0

    def shoot_fireball(self, angle):
        center_x = self.player.x + self.player.sprite_size[0] // 2
        center_y = self.player.y + self.player.sprite_size[1] // 2
        fireball = Fireball(center_x, center_y, angle, self.explosions)
        self.projectiles.append(fireball)

    def shoot_laser(self, target_x, target_y, enemies):
        center_x = self.player.x + self.player.sprite_size[0] // 2
        center_y = self.player.y + self.player.sprite_size[1] // 2
        laser = LaserBeam(center_x, center_y, target_x, target_y, enemies=enemies)
        self.lasers.append(laser)

        if len(self.lasers) > 3:
            self.lasers.pop(0)

    def cast_firework_spell(self):
        center_x = self.player.x + self.player.sprite_size[0] // 2
        center_y = self.player.y + self.player.sprite_size[1] // 2

        # Posición del mouse en mundo
        mouse_x, mouse_y = pygame.mouse.get_pos()
        target_x = mouse_x / self.state.camera.zoom + self.state.camera.offset_x
        target_y = mouse_y / self.state.camera.zoom + self.state.camera.offset_y

        self.fireworks.append(FireworkLaunch(center_x, center_y, target_x, target_y))
    
    def place_smoke_emitter(self):
        center_x = self.player.x + self.player.sprite_size[0] // 2
        center_y = self.player.y + self.player.sprite_size[1] // 2
        emitter = SmokeEmitter(center_x, center_y)
        self.smoke_emitters.append(emitter)

    def update(self):
        solid_tiles = [tile for tile in self.state.tiles if tile.solid]
        enemies = self.state.enemies + list(self.state.remote_entities.values())

        # Actualizar proyectiles
        self.projectiles = [p for p in self.projectiles if p.alive]
        for p in self.projectiles:
            p.update(solid_tiles, enemies)

        # Actualizar explosiones
        self.explosions[:] = [e for e in self.explosions if not e.finished]
        for e in self.explosions:
            e.update()

        # 🔁 Láser continuo
        for laser in self.lasers[:]:
            if not laser.update():
                self.lasers.remove(laser)

        # 🎇 Actualizar fuegos artificiales
        for fw in self.fireworks[:]:
            fw.update()
            if fw.finished:
                self.fireworks.remove(fw)
                self.explosions.append(FireworkExplosion(fw.x, fw.y))
        
        for emitter in self.smoke_emitters:
            wind_x = (pygame.mouse.get_pos()[0] - self.state.screen.get_width() // 2) / 1000
            emitter.apply_force(pygame.math.Vector2(wind_x, 0))
            emitter.update()
        
        self.smoke_emitters = [e for e in self.smoke_emitters if len(e.particles) > 0]

    @benchmark(lambda self: self.state.perf_log, "----3.6.1 explosions")
    def _render_explosions(self, screen, camera):
        dirty = []
        for explosion in self.explosions:
            d = explosion.render(screen, camera)
            if d:
                dirty.append(d)
        return dirty

    @benchmark(lambda self: self.state.perf_log, "----3.6.2 lasers")
    def _render_lasers(self, screen, camera):
        dirty = []
        for laser in self.lasers:
            d = laser.render(screen, camera)
            if d:
                dirty.append(d)
        return dirty

    @benchmark(lambda self: self.state.perf_log, "----3.6.3 projectiles")
    def _render_projectiles(self, screen, camera):
        dirty = []
        for projectile in self.projectiles:
            d = projectile.render(screen, camera)
            if d:
                dirty.append(d)
        return dirty

    @benchmark(lambda self: self.state.perf_log, "----3.6.4 fireworks")
    def _render_fireworks(self, screen, camera):
        dirty = []
        for fw in self.fireworks:
            d = fw.render(screen, camera)
            if d:
                dirty.append(d)
        return dirty

    def render(self, screen, camera):
        dirty_rects = []
        dirty_rects += self._render_explosions(screen, camera)
        dirty_rects += self._render_lasers(screen, camera)
        dirty_rects += self._render_projectiles(screen, camera)
        dirty_rects += self._render_fireworks(screen, camera)

        for emitter in self.smoke_emitters:
            emitter.render(screen, camera)

        return dirty_rects
        return dirty_rects
