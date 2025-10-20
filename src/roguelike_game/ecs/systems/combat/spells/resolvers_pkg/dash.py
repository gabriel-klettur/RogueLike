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
        # Read flattened values then prefer nested effect overrides if provided
        knockback = cfg.get('knockback')
        collision_damage = cfg.get('collision_damage')
        try:
            eff = getattr(cfg, 'extra', {}).get('effect', {})
            if 'knockback' in eff:
                knockback = eff.get('knockback')
            if 'collision_damage' in eff:
                collision_damage = eff.get('collision_damage')
        except Exception:
            pass
        world.components.setdefault('DashComponent', {})[caster] = DashComponent(
            dir_x, dir_y, speed, duration,
            knockback=knockback,
            collision_damage=collision_damage,
        )
        # Particles: allow overrides via SpellConfig (flattened from vfx.particles)
        try:
            count = int(cfg.get('particle_count', 10) or 10)
        except Exception:
            count = 10
        try:
            lifespan = int(cfg.get('particle_lifespan', 15) or 15)
        except Exception:
            lifespan = 15
        sr = cfg.get('size_range')
        if not (isinstance(sr, (list, tuple)) and len(sr) >= 2):
            sr = [3, 6]
        try:
            size_range = (int(sr[0]), int(sr[1]))
        except Exception:
            size_range = (3, 6)
        # Colors: accept list of colors or single color; default to bluish palette
        color_choices = ((200,200,255),(150,150,255),(255,255,255))
        try:
            cols = cfg.get('particle_colors')
            if isinstance(cols, (list, tuple)) and cols:
                # If a single color triplet is provided, wrap it; if palette, convert all
                if all(isinstance(c, int) for c in cols) and len(cols) >= 3:
                    color_choices = (tuple(int(x) for x in cols[:3]),)
                else:
                    color_choices = tuple(tuple(int(x) for x in c[:3]) for c in cols if isinstance(c, (list, tuple)) and len(c) >= 3)
                    if not color_choices:
                        color_choices = ((200,200,255),(150,150,255),(255,255,255))
        except Exception:
            pass
        speed_range = (1.0, 3.0)
        world.components.setdefault('DashEmitterComponent', {})[caster] = DashEmitterComponent(
            count=count,
            lifespan=lifespan,
            size_range=size_range,
            color_choices=color_choices,
            speed_range=speed_range,
        )
