from __future__ import annotations

from typing import Optional
from roguelike_editors.particles.services.instances_service import load_particles_instances
from roguelike_game.config.particles_config import get_preset
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE


def spawn_particles_from_instances(world) -> int:
    """Spawn runtime ECS particle entities from persisted instances.

    Returns the number of entities spawned.
    """
    data = load_particles_instances() or []
    spawned = 0
    for e in data:
        try:
            preset_id = str(e.get('preset_id'))
            zone = str(e.get('zone') or 'no zone')
            rel_x = int(e.get('rel_x') or 0)
            rel_y = int(e.get('rel_y') or 0)
        except Exception:
            continue
        # Compute world coordinates from zone offsets (in tiles)
        off_tx, off_ty = global_map_settings.zone_offsets.get(zone, (0, 0))
        wx = int(off_tx) * TILE_SIZE + int(rel_x)
        wy = int(off_ty) * TILE_SIZE + int(rel_y)
        # Read optional defaults from preset
        p = None
        try:
            p = get_preset(preset_id)
        except Exception:
            p = None
        color = (255, 220, 0)
        size = 8
        lifespan = 180
        try:
            if p is not None:
                vfx = getattr(p, 'vfx', {}) if hasattr(p, 'vfx') else (p.get('vfx', {}) if hasattr(p, 'get') else {})
                if isinstance(vfx.get('color'), (list, tuple)):
                    color = tuple(vfx.get('color'))
                if vfx.get('size') is not None:
                    size = int(vfx.get('size'))
                if vfx.get('lifespan') is not None:
                    lifespan = int(vfx.get('lifespan'))
        except Exception:
            pass
        # Create entity
        try:
            eid = world.create_entity()
            world.components.setdefault('Position', {})[eid] = Position(float(wx), float(wy))
            world.components.setdefault('ParticleComponent', {})[eid] = ParticleComponent(0.0, 0.0, color, int(size), int(lifespan))
            spawned += 1
        except Exception:
            continue
    return spawned
