from roguelike_project.systems.combat.view.effects.particles.spells.firework_launch import FireworkLaunch
from roguelike_project.systems.combat.view.effects.particles.explosions.firework_explosion import FireworkExplosion
from roguelike_project.utils.benchmark import benchmark
from roguelike_project.systems.combat.view.effects.animations.fireball import Fireball
import pygame

class ProjectilesManager:
    def __init__(self, state):
        self.state = state
        self.projectiles = []
        self.fireworks = []

    def spawn_fireball(self, angle):
        px = self._player_center()
        fireball = Fireball(*px, angle, self.state.systems.explosions)
        self.projectiles.append(fireball)

    def spawn_firework(self):
        px = self._player_center()
        mouse = self._mouse_world()
        self.fireworks.append(FireworkLaunch(px[0], px[1], *mouse))

    def update(self):
        tiles = [t for t in self.state.tiles if t.solid]
        enemies = self.state.enemies + list(self.state.remote_entities.values())

        self.projectiles = [p for p in self.projectiles if p.alive]
        for p in self.projectiles:
            p.update(tiles, enemies)

        for fw in self.fireworks[:]:
            fw.update()
            if fw.finished:
                self.fireworks.remove(fw)
                self.state.systems.explosions.add_explosion(FireworkExplosion(fw.x, fw.y))

    @benchmark(lambda self: self.state.perf_log, "----3.6.3 projectiles")
    def render(self, screen, camera):
        dirty = []
        for projectile in self.projectiles + self.fireworks:
            if (d := projectile.render(screen, camera)):
                dirty.append(d)
        return dirty

    def _player_center(self):
        p = self.state.player
        return (p.x + p.sprite_size[0] // 2, p.y + p.sprite_size[1] // 2)

    def _mouse_world(self):
        mx, my = pygame.mouse.get_pos()
        return (
            mx / self.state.camera.zoom + self.state.camera.offset_x,
            my / self.state.camera.zoom + self.state.camera.offset_y
        )
