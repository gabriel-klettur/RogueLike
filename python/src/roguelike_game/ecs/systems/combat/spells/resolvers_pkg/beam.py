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
        # Use robust defaults when spells.json lacks explicit particle params to avoid invisible beams
        eff = cfg.get('effect', {}) if isinstance(cfg, dict) else {}
        vfx = cfg.get('vfx', {}) if isinstance(cfg, dict) else {}
        sprite = vfx.get('sprite', {}) if isinstance(vfx, dict) else {}
        pc = int(cfg.get('particle_count', 0) or 60)
        disp = float(cfg.get('particle_dispersion', 0) or 4)
        colors = cfg.get('particle_colors') or [(0, 255, 255), (150, 255, 255), (255, 255, 255)]
        # Lifespan and damage come from effect in spells.json
        life = float(eff.get('lifetime', cfg.get('lifespan', 0)) or 0)
        # Visual scale fallback to vfx.sprite.scale when provided
        scale = float(cfg.get('scale', sprite.get('scale', 1.0)) or 1.0)
        dmg = float(eff.get('damage', cfg.get('damage', 0)) or 0)
        # Continuous beam: no fixed duration, removed on mouse/keys release by emitter system
        world.components.setdefault('LaserBeamComponent', {})[caster] = LaserBeamComponent(
            cx, cy, wx, wy,
            particle_count=pc,
            dispersion=disp,
            colors=colors,
            lifespan=life,
            scale=scale,
            damage=dmg,
            duration=None
        )
