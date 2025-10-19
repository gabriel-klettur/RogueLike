from __future__ import annotations

from typing import Optional
from roguelike_editors.particles.services.instances_service import load_particles_instances
from roguelike_game.config.particles_config import get_preset
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.particles.particle_preset_component import ParticlePresetComponent
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
        # Create a persistent preset entity (rendered by ParticlePresetRenderSystem)
        try:
            eid = world.create_entity()
            world.components.setdefault('Position', {})[eid] = Position(float(wx), float(wy))
            entry_id = None
            try:
                entry_id = int(e.get('id')) if e.get('id') is not None else None
            except Exception:
                entry_id = None
            world.components.setdefault('ParticlePresetComponent', {})[eid] = ParticlePresetComponent(str(preset_id), entry_id)
            spawned += 1
        except Exception:
            continue
    return spawned
