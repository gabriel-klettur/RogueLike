import time
import random
import logging
import math
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.meteor_fall_component import MeteorFallComponent
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_engine.config.config_z_layer import Z_LAYERS
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.utils.spell_vfx import get_meteor_scale, get_meteor_sprite_path

logger = logging.getLogger(__name__)
METEOR_IMAGE = "assets/spells/meteor/meteor.png"

class MeteorShowerSystem:
    """
    Dispara impactos (meteoros) periódicos dentro de un área, aplicando daño de área
    y generando VFX de impacto reutilizando el sistema de explosiones existente.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'MeteorShowerSystem.update')
    def update(self, world, camera=None):
        now = time.time()
        showers = world.components.get('MeteorShowerComponent', {})
        pos_map = world.components.get('Position', {})

        to_remove = []

        for eid, shower in list(showers.items()):
            origin = pos_map.get(eid)
            if origin is None:
                # Sin posición no podemos operar; limpiar
                to_remove.append(eid)
                continue

            # Inicialización de tiempos
            if getattr(shower, 'start_time', 0.0) <= 0.0:
                shower.start_time = now
                shower.last_spawn_time = now - float(shower.interval)  # permite primer spawn inmediato

            # Spawning periódico de meteoritos (entidades que caen desde el cielo)
            if shower.spawns_done < shower.count and now >= shower.last_spawn_time + float(shower.interval):
                # Elegir un punto aleatorio dentro del círculo de área
                r = float(shower.area_radius) * (random.random() ** 0.5)
                a = random.random() * 2.0 * math.pi
                ix = float(origin.x) + r * math.cos(a)
                iy = float(origin.y) + r * math.sin(a)
                # Spawnear entidad meteorito que caerá hasta (ix, iy)
                try:
                    cfg = SPELLS.get(getattr(shower, 'spell_key', ''), {})
                    eff = getattr(cfg, 'extra', {}).get('effect', {}) if getattr(cfg, 'extra', None) else {}
                except Exception:
                    eff = {}
                fall_height = float(eff.get('fall_height', 800.0))
                fall_speed = float(eff.get('fall_speed', 1800.0))
                me = world.create_entity()
                world.components.setdefault('Position', {})[me] = Position(ix, iy - fall_height)
                world.components.setdefault('MeteorFallComponent', {})[me] = MeteorFallComponent(
                    target_x=ix,
                    target_y=iy,
                    height_px=fall_height,
                    fall_speed_px_s=fall_speed,
                    impact_damage=float(shower.impact_damage),
                    impact_radius=float(shower.impact_radius),
                    owner=shower.owner,
                    spell_key=getattr(shower, 'spell_key', ''),
                )
                try:
                    meteor_scale = float(get_meteor_scale(cfg, 0.10))
                    meteor_path = get_meteor_sprite_path(cfg, METEOR_IMAGE)
                    world.components.setdefault('Sprite', {})[me] = Sprite(meteor_path)
                    world.components.setdefault('ZLayer', {})[me] = ZLayer(Z_LAYERS.get('sky', 6))
                    world.components.setdefault('Scale', {})[me] = Scale(meteor_scale)
                except Exception:
                    pass

                shower.spawns_done += 1
                shower.last_spawn_time = now

            # Finalizar cuando se hayan generado todos los impactos
            if shower.spawns_done >= shower.count:
                to_remove.append(eid)

        # Limpieza de entidades terminadas
        for eid in to_remove:
            showers.pop(eid, None)
            world.components.get('Position', {}).pop(eid, None)
            try:
                world.remove_entity(eid)
            except Exception:
                pass
