# Path: src/roguelike_game/ecs/systems/particles/laser_beam_emitter_system.py
import random
import time
import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.laser_beam_component import LaserBeamComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent

class LaserBeamEmitterSystem:
    """
    Sistema ECS que emite partículas y aplica daño para cada entidad con LaserBeamComponent.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "LaserBeamEmitterSystem.update")
    def update(self, world, camera=None):
        now = time.time()
        # Remove beam when middle mouse is released
        if not pygame.mouse.get_pressed()[1]:
            world.components.get('LaserBeamComponent', {}).clear()
            return
        # Debug beam presence
        beam_count = len(world.components.get('LaserBeamComponent', {}))
        if beam_count:
            print(f"[DEBUG][LaserBeamEmitter] frame={now:.3f} beams={beam_count}")
        to_remove = []
        for caster, beam in list(world.components.get('LaserBeamComponent', {}).items()):
            # dynamic thickness from beam.scale
            thickness_px = max(2, int(beam.scale * 20))
            half_thickness = thickness_px / 2
            # Recalculate beam origin/target to follow caster and cursor
            pos_cmp = world.components['Position'].get(caster)
            if pos_cmp:
                cx, cy = pos_cmp.x, pos_cmp.y
                sprite_cmp = world.components.get('Sprite', {}).get(caster)
                if sprite_cmp:
                    w, h = sprite_cmp.image.get_size()
                    cx += w/2; cy += h/2
                mx, my = pygame.mouse.get_pos()
                wx = mx / camera.zoom + camera.offset_x
                wy = my / camera.zoom + camera.offset_y
                beam.origin = (cx, cy)
                beam.target = (wx, wy)
            x1, y1 = beam.origin
            x2, y2 = beam.target
            dx = x2 - x1
            dy = y2 - y1
            length = (dx*dx + dy*dy) ** 0.5 or 1
            # 1. Generar partículas a lo largo de la línea
            for i in range(beam.particle_count):
                t = i / beam.particle_count
                px = x1 + t * dx + random.uniform(-beam.dispersion, beam.dispersion)
                py = y1 + t * dy + random.uniform(-beam.dispersion, beam.dispersion)
                pid = world.create_entity()
                world.components['Position'][pid] = Position(px, py)
                color = random.choice(beam.colors)
                size = thickness_px
                # beam particles live only one frame to avoid trails
                lifespan_frames = 1
                world.components['ParticleComponent'][pid] = ParticleComponent(0, 0, color, size, lifespan_frames)
            # 2. Aplicar daño a entidades en el haz (una vez por caster)
            for target in world.get_entities_with('Position', 'Health'):
                pos_t = world.components['Position'][target]
                sprite_t = world.components.get('Sprite', {}).get(target)
                if sprite_t:
                    tw, th = sprite_t.image.get_size()
                    tx = pos_t.x + tw / 2
                    ty = pos_t.y + th / 2
                    br = max(tw, th) / 2
                else:
                    tx = pos_t.x
                    ty = pos_t.y
                    br = 0
                tdx = tx - x1
                tdy = ty - y1
                proj = (tdx * dx + tdy * dy) / length
                # skip if outside extended segment
                if proj + br < 0 or proj - br > length:
                    continue
                pdist = abs(tdx * dy - tdy * dx) / length
                if pdist <= half_thickness + br:
                    hp = world.components['Health'][target]
                    hp.current_hp = max(0, hp.current_hp - beam.damage)
            # 3. Quitar componente si expiró la duración
            if beam.duration is not None and now >= beam.start_time + beam.duration:
                to_remove.append(caster)
        for caster in to_remove:
            world.components['LaserBeamComponent'].pop(caster, None)