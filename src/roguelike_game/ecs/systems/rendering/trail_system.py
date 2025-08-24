import time
from roguelike_game.ecs.components.rendering.trail_component import TrailComponent, TrailSnapshot
from roguelike_engine.utils.benchmark import benchmark

class TrailSystem:
    """
    Sistema que genera y actualiza snapshots de rastro de sombra.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        now = time.time()
        trail_map = world.components.get('TrailComponent', {})
        sprite_map = world.components.get('Sprite', {})
        pos_map = world.components.get('Position', {})
        # Generar snapshots periódicos
        for eid, trail in trail_map.items():
            if now - trail.last_gen >= trail.config.interval:
                trail.last_gen = now
                sprite = sprite_map.get(eid)
                pos = pos_map.get(eid)
                if sprite and pos:
                    img = sprite.image.copy()
                    img.set_alpha(255)
                    trail.snapshots.append(TrailSnapshot(img, (pos.x, pos.y), now))
                    if len(trail.snapshots) > trail.config.max_trails:
                        trail.snapshots.pop(0)
        # Actualizar fade y limpiar expirados
        for eid, trail in trail_map.items():
            new_list = []
            for snap in trail.snapshots:
                age = now - snap.spawn_time
                if age < trail.config.life_time:
                    alpha = int(255 * (1 - age / trail.config.life_time))
                    snap.image.set_alpha(alpha)
                    new_list.append(snap)
            trail.snapshots = new_list