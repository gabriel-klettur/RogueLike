from roguelike_game.ecs.components.abilities.laser_beam_component import LaserBeamComponent

from .base import BaseSpellResolver
from .utils import get_entity_center, mouse_world, direction_from_to


class BeamResolver(BaseSpellResolver):
    """
    Resolver for continuous beam spells: spawns beam particles and applies damage along line.
    """
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        offset = spawn_meta.get('offset', 0)
        cx, cy = get_entity_center(world, caster)
        wx, wy = mouse_world(camera)
        dx, dy, length = direction_from_to(cx, cy, wx, wy)
        # Register continuous laser beam component to handle particle emission and damage over time
        # Continuous beam: no fixed duration, removed on mouse release
        world.components.setdefault('LaserBeamComponent', {})[caster] = LaserBeamComponent(
            cx, cy, wx, wy,
            particle_count=cfg.get('particle_count', 0),
            dispersion=cfg.get('particle_dispersion', 0),
            colors=cfg.get('particle_colors', []),
            lifespan=float(cfg.get('lifespan', 0)),
            scale=cfg.get('scale', 1.0),
            damage=cfg.get('damage', 0),
            duration=None
        )
