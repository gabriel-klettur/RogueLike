from __future__ import annotations

from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.buildings.utils.load_buildings_from_json import load_buildings_from_json
from roguelike_game.managers.ecs.particles_loader import refresh_particles_from_world as _refresh_particles_from_world

import logging
logger = logging.getLogger(__name__)


class BuildingPortalSystem:
    """
    Detecta al jugador sobre un edificio marcado como portal y ejecuta teleport.

    Requisitos en cada Building:
    - b.is_portal == True
    - b.portal_dest_world (opcional)
    - b.portal_dest_zone (reservado)
    - b.portal_dest_x / b.portal_dest_y (opcionales)
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def update(self, world, *args):
        # Acceso a componentes y jugador
        comps = world.components
        positions = comps.get('Position', {})
        player_tags = comps.get('PlayerTagComponent', {})
        if not player_tags:
            return
        try:
            player_eid = next(iter(player_tags))
        except StopIteration:
            return
        ppos = positions.get(player_eid)
        if ppos is None:
            return
        px, py = getattr(ppos, 'x', None), getattr(ppos, 'y', None)
        if px is None or py is None:
            return

        buildings = getattr(world, 'buildings', None) or []
        if not buildings:
            return

        cur_world = getattr(global_map_settings, 'current_world', 'base')

        for b in buildings:
            if not getattr(b, 'is_portal', False):
                continue
            # Evaluar activación: punto jugador dentro del rect del edificio o cerca del centro
            try:
                rect = b.rect
                trigger = rect.collidepoint(px, py)
                if not trigger:
                    cx, cy = rect.centerx, rect.centery
                    dx, dy = px - cx, py - cy
                    trigger = (dx * dx + dy * dy) <= (TILE_SIZE * TILE_SIZE)
                if not trigger:
                    continue
            except Exception:
                continue

            # Destino del portal
            dest_world = getattr(b, 'portal_dest_world', None)
            dest_zone = getattr(b, 'portal_dest_zone', None)  # reservado
            tx_raw = getattr(b, 'portal_dest_x', None)
            ty_raw = getattr(b, 'portal_dest_y', None)

            # Cross-world
            if dest_world and dest_world != cur_world:
                try:
                    logger.info(
                        f"[BuildingPortalSystem] Cross-world portal: {cur_world} -> {dest_world} at tile=({tx_raw},{ty_raw})"
                    )
                except Exception:
                    pass
                try:
                    tile_pos = None if (tx_raw is None or ty_raw is None) else (int(tx_raw), int(ty_raw))
                    world.map_manager.swap_world_and_spawn(dest_world, tile_pos)
                    # Reload buildings from the destination world's instances (per-world path is already active)
                    try:
                        z_state = getattr(world, 'z_state', None)
                        world.buildings = load_buildings_from_json(z_state)
                        try:
                            logger.info("[BuildingPortalSystem] Reloaded buildings from destination world: n=%d", len(world.buildings or []))
                        except Exception:
                            pass
                    except Exception:
                        pass
                    # Clear all runtime particle/ribbon/trail entities from the previous world
                    try:
                        comps = world.components
                        to_remove: set[int] = set()
                        for key in (
                            'ParticleComponent',
                            'ParticlePresetComponent',
                            'RibbonComponent',
                            'TrailComponent',
                            'FlashComponent',
                        ):
                            try:
                                for eid in list(comps.get(key, {}).keys()):
                                    to_remove.add(eid)
                            except Exception:
                                continue
                        removed_cnt = 0
                        for eid in to_remove:
                            try:
                                world.remove_entity(eid)
                                removed_cnt += 1
                            except Exception:
                                continue
                        if removed_cnt:
                            try:
                                logger.info("[BuildingPortalSystem] Cleared %d particle-related entities after world swap", removed_cnt)
                            except Exception:
                                pass
                    except Exception:
                        pass
                    # Refresh per-world particle preset instances for the destination world
                    try:
                        _refresh_particles_from_world(world)
                    except Exception:
                        pass
                    # Reset spawners to the new world's data: remove existing spawner entities and NPC children
                    try:
                        comps = world.components
                        # Remove NPCs created by spawners
                        for eid in list(comps.get('SpawnerChild', {}).keys()):
                            world.remove_entity(eid)
                        # Remove spawner entities (SpawnerConfig/SpawnerState holders)
                        to_remove = set()
                        for eid in list(comps.get('SpawnerConfig', {}).keys()):
                            to_remove.add(eid)
                        for eid in list(comps.get('SpawnerState', {}).keys()):
                            to_remove.add(eid)
                        for eid in to_remove:
                            world.remove_entity(eid)
                        # Force placement system to reload from new world
                        for sys in getattr(world, 'update_systems', []) or []:
                            try:
                                if type(sys).__name__ == 'SpawnerPlacementSystem':
                                    setattr(sys, '_loaded', False)
                            except Exception:
                                pass
                    except Exception:
                        pass
                    try:
                        world.invalidate_spatial_index()
                    except Exception:
                        pass
                except Exception as e:
                    try:
                        logger.error(f"[BuildingPortalSystem] swap_world_and_spawn failed: {e}")
                    except Exception:
                        pass
                break

            # Intra-world
            try:
                logger.info(f"[BuildingPortalSystem] Intra-world portal to tile=({tx_raw},{ty_raw})")
            except Exception:
                pass
            try:
                tile_pos2 = None if (tx_raw is None or ty_raw is None) else (int(tx_raw), int(ty_raw))
                if tile_pos2 is None:
                    # fallback: center of lobby
                    world.map_manager.swap_world_and_spawn(cur_world, None)
                else:
                    world.map_manager.spawn_player(tile_pos2)
                try:
                    world.invalidate_spatial_index()
                except Exception:
                    pass
            except Exception as e:
                try:
                    logger.error(f"[BuildingPortalSystem] spawn_player failed: {e}")
                except Exception:
                    pass
            break
