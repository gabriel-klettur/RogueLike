from roguelike_project.systems.combat.projectiles.fireball import Fireball
from roguelike_project.utils.benchmark import benchmark

class ProjectilesManager:
    def __init__(self, state):
        self.state = state
        self.projectiles = []

    def spawn_fireball(self, angle):
        px = self.state.player.x + self.state.player.sprite_size[0] // 2
        py = self.state.player.y + self.state.player.sprite_size[1] // 2
        fireball = Fireball(px, py, angle, self.state.combat.explosions.explosions)
        self.projectiles.append(fireball)
    
    def update(self):
        tiles = [t for t in self.state.tiles if t.solid]
        enemies = self.state.enemies + list(self.state.remote_entities.values())

        self.projectiles = [p for p in self.projectiles if p.alive]
        for p in self.projectiles:
            p.update(tiles, enemies)

    @benchmark(lambda self: self.state.perf_log, "----3.6.1 projectiles_render")
    def render(self, screen, camera):
        dirty_rects = []
        for projectile in self.projectiles:
            d = projectile.render(screen, camera)
            if d:
                dirty_rects.append(d)
        return dirty_rects
