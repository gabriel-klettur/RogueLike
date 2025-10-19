import logging

from roguelike_game.ecs.components.abilities.dash_component import DashComponent
from roguelike_game.ecs.components.particles.dash_emitter_component import DashEmitterComponent

from .base import BaseSpellResolver
from .utils import get_entity_center, mouse_world, direction_from_to


logger = logging.getLogger(__name__)


class DashResolver(BaseSpellResolver):
    """Resolver for dash spells: registers DashComponent for continuous dash movement."""
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Gating por cargas: consumir 1 carga si hay disponible (omitir en godmode jugador)
        godmode = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and (caster == getattr(world, 'player_entity', None))
        meter = world.components.get('DashMeterComponent', {}).get(caster)
        if meter is not None and not godmode:
            if getattr(meter, 'current', 0) <= 0:
                logger.debug("[DashResolver] Sin cargas de dash: abortar resolución")
                return
            # Consumir una carga y reiniciar progreso de recarga (sequential)
            meter.current = max(0, int(meter.current) - 1)
            try:
                # resetear el progreso para iniciar de inmediato la recarga de la siguiente carga
                meter.progress = 0.0
                meter.ensure_timer()
            except Exception:
                pass
        cx, cy = get_entity_center(world, caster)
        wx, wy = mouse_world(camera)
        dir_x, dir_y, _ = direction_from_to(cx, cy, wx, wy)
        speed = cfg.get('speed', 0)
        duration = cfg.get('duration', 0)
        world.components.setdefault('DashComponent', {})[caster] = DashComponent(dir_x, dir_y, speed, duration)
        world.components.setdefault('DashEmitterComponent', {})[caster] = DashEmitterComponent(
            count=10,
            lifespan=15,
            size_range=(3, 6),
            color_choices=((200,200,255),(150,150,255),(255,255,255)),
            speed_range=(1.0, 3.0)
        )
