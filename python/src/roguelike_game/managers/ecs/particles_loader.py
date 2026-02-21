from __future__ import annotations

from typing import Optional
import logging
from roguelike_editors.particles.services.instances_service import load_particles_instances
from roguelike_game.config.particles_config import get_preset
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.particles.particle_preset_component import ParticlePresetComponent
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE

logger = logging.getLogger(__name__)


def spawn_particles_from_instances(world) -> int:
    """Spawn runtime ECS particle entities from persisted instances.

    Returns the number of entities spawned.
    """
    data = load_particles_instances() or []
    spawned = 0
    # Mapear zonas válidas del mundo actual (cuando usamos zones.json). Esto
    # evita que instancias declaradas para otros mundos (con nombres de zona
    # que no existen en el mundo activo) aparezcan en mundos en blanco u otros
    # worlds.
    try:
        offsets = getattr(global_map_settings, 'zone_offsets', {}) or {}
    except Exception:
        offsets = {}
    use_zones = bool(getattr(global_map_settings, 'use_zones_json', False))
    unknown_zones: set[str] = set()

    for e in data:
        try:
            preset_id = str(e.get('preset_id'))
            zone = str(e.get('zone') or 'no zone')
            rel_x = int(e.get('rel_x') or 0)
            rel_y = int(e.get('rel_y') or 0)
        except Exception:
            continue
        # Skip instances whose zone is not defined for the active world when
        # using zones.json (defensive against cross-world JSON reuse).
        if use_zones and isinstance(offsets, dict) and zone not in offsets:
            unknown_zones.add(zone)
            continue
        # Compute world coordinates from zone offsets (in tiles)
        off_tx, off_ty = offsets.get(zone, (0, 0))
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
            # Optional per-instance scale multiplier; make portals larger by default
            try:
                sval = e.get('scale_multiplier')
                if sval is not None:
                    scale_mul = float(sval)
                else:
                    pid = str(preset_id)
                    scale_mul = 2.0 if pid.startswith('portal_') else 1.0
            except Exception:
                pid = str(preset_id)
                scale_mul = 2.0 if pid.startswith('portal_') else 1.0
            world.components.setdefault('ParticlePresetComponent', {})[eid] = ParticlePresetComponent(
                str(preset_id), entry_id, scale_mul
            )
            spawned += 1
        except Exception:
            continue
    # Log una sola vez qué zonas se omitieron, para diagnóstico ligero.
    if unknown_zones:
        try:
            logger.info(
                "[ParticlesLoader] Skipped particles for unknown zones=%s in world=%s",
                sorted(unknown_zones),
                getattr(global_map_settings, 'current_world', '?'),
            )
        except Exception:
            pass
    return spawned


def clear_runtime_particle_entities(world) -> int:
    """Remove all particle-related runtime entities from the ECS world.

    This is intended for world swaps so that no particle, trail, ribbon or flash
    entity from the previous world survives into the destination world.

    Returns the number of entities removed.
    """
    try:
        comps = world.components
    except Exception:
        return 0
    to_remove: set[int] = set()
    for key in (
        "ParticleComponent",
        "ParticlePresetComponent",
        "RibbonComponent",
        "TrailComponent",
        "FlashComponent",
    ):
        try:
            for eid in list(comps.get(key, {}).keys()):
                to_remove.add(eid)
        except Exception:
            continue
    removed = 0
    for eid in to_remove:
        try:
            world.remove_entity(eid)
            removed += 1
        except Exception:
            continue
    if removed:
        try:
            logger.info(
                "[ParticlesLoader] Cleared %d particle-related entities before world swap",
                removed,
            )
        except Exception:
            pass
    return removed


def refresh_particles_from_world(world) -> int:
    """Clear existing particle preset entities and respawn from per-world JSON.

    Returns the number of entities spawned after refresh.
    """
    # Remove all particle-related entities from the previous world (effects + presets)
    clear_runtime_particle_entities(world)
    # Spawn from the active world's instances file (cfg.PARTICLES_INSTANCES_PATH)
    return spawn_particles_from_instances(world)
