# Path: src/roguelike_game/systems/combat/spells/healing_aura/healing_aura_emitter_system.py

import random
import time

from pygame.math import Vector2
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.aura_component import AuraComponent
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent

class HealingAuraEmitterSystem:
    """
    Sistema ECS que emite partículas visuales para cada entidad que posea un AuraComponent.
    Cada partícula nace en un punto aleatorio dentro de un óvalo que cubre la altura y anchura del sprite,
    y asciende de manera vertical hasta la altura de la cabeza, donde desaparece.
    Esto crea la ilusión de un flujo de energía curativa que recorre todo el cuerpo del caster.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "HealingAuraEmitterSystem.update")
    def update(self, world, camera=None):
        now = time.time()
        for caster, aura in world.components.get('AuraComponent', {}).items():
            # 1. Obtener componente Position del caster
            pos_cmp = world.components.get('Position', {}).get(caster)
            if not pos_cmp:
                continue
            base_x = pos_cmp.x
            base_y = pos_cmp.y

            # 2. Determinar dimensiones del sprite (si existe) o usar valores estimados
            sprite_cmp = world.components.get('Sprite', {}).get(caster)
            if sprite_cmp:
                w, h = sprite_cmp.image.get_size()
                cx = base_x + w / 2            # Centro horizontal del sprite
                feet_y = base_y + h            # Y de los pies (parte inferior)
                head_y = base_y                # Y de la cabeza (parte superior)
                half_width = w / 2
                half_height = h / 2
                # Centro del óvalo: mitad de camino entre cabeza y pies
                ellipse_cy = head_y + half_height
            else:
                # Si no hay sprite, definimos parámetros arbitrarios
                cx = base_x
                feet_y = base_y
                head_y = base_y - 32
                half_width = 16
                half_height = 16
                ellipse_cy = head_y + half_height
                w = half_width * 2
                h = half_height * 2

            # 3. Calcular velocidad extra inversa al movimiento para inercia suave
            vel_cmp = world.components.get('Velocity', {}).get(caster)
            if vel_cmp:
                dirv = Vector2(vel_cmp.vx, vel_cmp.vy)
                if dirv.length() > 0:
                    dirv = dirv.normalize()
                extra = -0.5 * dirv
            else:
                extra = Vector2(0, 0)

            # 4. Emitir partículas en cada frame
            for _ in range(aura.particles_per_frame):
                # 4.1. Muestreo uniforme dentro de un óvalo que cubre todo el sprite
                #      Ecuación paramétrica del óvalo centrado en (cx, ellipse_cy):
                #      (dx / half_width)^2 + (dy / half_height)^2 <= 1
                #      Generamos (dx, dy) mediante técnica de rechazo
                while True:
                    dx_ell = random.uniform(-half_width, half_width)
                    dy_ell = random.uniform(-half_height, half_height)
                    if (dx_ell / half_width) ** 2 + (dy_ell / half_height) ** 2 <= 1:
                        break
                spawn_x = cx + dx_ell + aura.offset_x
                spawn_y = ellipse_cy + dy_ell

                # Asegurar que el punto de origen nunca esté por encima de la cabeza ni por debajo de los pies
                spawn_y = max(head_y, min(feet_y, spawn_y))

                # 4.2. Definir velocidad de ascenso vertical con ligera variación horizontal
                vertical_speed = -abs(aura.particle_speed)  # negativo → hacia arriba
                horizontal_variation = random.uniform(-0.3, 0.3) * aura.particle_speed
                vx = horizontal_variation + extra.x
                vy = vertical_speed + extra.y

                # 4.3. Color y tamaño aleatorios dentro de parámetros del aura
                size  = random.randint(aura.particle_min_size, aura.particle_max_size)
                color = random.choice(aura.particle_colors)

                # 4.4. Calcular lifespan de la partícula para que desaparezca al llegar a la cabeza
                #      dist_vertical = spawn_y - head_y (distancia desde punto de origen hasta la cabeza)
                dist_vertical = spawn_y - head_y
                #      Frames necesarios = dist_vertical / |-vy|
                if abs(vy) > 0:
                    frames_to_head = int(dist_vertical / abs(vy))
                else:
                    frames_to_head = aura.particle_lifespan
                #      No exceder lifespan máximo definido
                lifespan_frames = min(frames_to_head, aura.particle_lifespan)

                # 4.5. Crear entidad de partícula en ECS
                pid = world.create_entity()
                world.components['Position'][pid] = Position(spawn_x, spawn_y)
                world.components['ParticleComponent'][pid] = ParticleComponent(
                    vx, vy, color, size, lifespan_frames
                )
