from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings
from roguelike_game.managers.ecs.particles_loader import refresh_particles_from_world as _refresh_particles_from_world

import logging
logger = logging.getLogger(__name__)


class TeleportSystem:
    """
    Sistema ECS que detecta colisión jugador↔portal y ejecuta teletransporte.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def update(self, world, *args):
        components = world.components
        positions = components.get('Position', {})
        teleports = components.get('TeleportComponent', {})
        player_tags = components.get('PlayerTagComponent', {})
        if not teleports or not player_tags:
            return
        player_eid = next(iter(player_tags))
        player_pos = positions.get(player_eid)
        if not player_pos:
            return
        for eid, tp in teleports.items():
            item_pos = positions.get(eid)
            if not item_pos:
                continue
            dx = player_pos.x - item_pos.x
            dy = player_pos.y - item_pos.y
            if dx * dx + dy * dy <= TILE_SIZE * TILE_SIZE:
                # Teleport trigger
                try:
                    dest_world = getattr(tp, 'dest_world', None)
                    dest_zone = getattr(tp, 'dest_zone', None)  # reservado para futura lógica por zona
                    tx_raw = getattr(tp, 'dest_x', None)
                    ty_raw = getattr(tp, 'dest_y', None)
                except Exception:
                    dest_world = None
                    dest_zone = None
                    tx_raw = None
                    ty_raw = None
                # Cross-world
                cur_world = getattr(global_map_settings, 'current_world', 'base')
                if dest_world and dest_world != cur_world:
                    try:
                        logger.info(f"[TeleportSystem] Cross-world teleport: {cur_world} -> {dest_world} at tile=({tx_raw},{ty_raw})")
                    except Exception:
                        pass
                    try:
                        tile_pos = None if (tx_raw is None or ty_raw is None) else (int(tx_raw), int(ty_raw))
                        world.map_manager.swap_world_and_spawn(dest_world, tile_pos)
                        # Sincronizar también la Position del jugador con el nuevo tile
                        try:
                            mgr = getattr(world, 'map_manager', None)
                            spawn_tile = tile_pos
                            if mgr is not None and spawn_tile is None:
                                try:
                                    local_state = getattr(mgr, '_local_state', {}) or {}
                                    spawn_tile = local_state.get('player_pos')
                                except Exception:
                                    spawn_tile = None
                            if mgr is not None and spawn_tile is not None:
                                px, py = mgr.get_spawn_pixel(tuple(spawn_tile))
                                player_pos.x, player_pos.y = px, py
                        except Exception:
                            pass
                        # Refresh particle instances for destination world
                        try:
                            _refresh_particles_from_world(world)
                        except Exception:
                            pass
                        # Marcar índice espacial para reconstrucción tras el swap
                        try:
                            world.invalidate_spatial_index()
                        except Exception:
                            pass
                    except Exception as e:
                        try:
                            logger.error(f"[TeleportSystem] swap_world_and_spawn failed: {e}")
                        except Exception:
                            pass
                    break
                # Intra-world
                try:
                    logger.info(f"[TeleportSystem] Intra-world teleport to tile=({tx_raw},{ty_raw})")
                except Exception:
                    pass
                try:
                    tile_pos2 = None if (tx_raw is None or ty_raw is None) else (int(tx_raw), int(ty_raw))
                    spawn_tile = None
                    if tile_pos2 is None:
                        # fallback: place at lobby center
                        world.map_manager.swap_world_and_spawn(cur_world, None)
                        try:
                            mgr2 = getattr(world, 'map_manager', None)
                            if mgr2 is not None:
                                local_state2 = getattr(mgr2, '_local_state', {}) or {}
                                spawn_tile = local_state2.get('player_pos')
                        except Exception:
                            spawn_tile = None
                    else:
                        world.map_manager.spawn_player(tile_pos2)
                        spawn_tile = tile_pos2
                    # Mover la entidad del jugador al pixel correspondiente al tile de spawn
                    try:
                        mgr = getattr(world, 'map_manager', None)
                        if mgr is not None and spawn_tile is not None:
                            px, py = mgr.get_spawn_pixel(tuple(spawn_tile))
                            player_pos.x, player_pos.y = px, py
                    except Exception:
                        pass
                    try:
                        world.invalidate_spatial_index()
                    except Exception:
                        pass
                except Exception as e:
                    try:
                        logger.error(f"[TeleportSystem] spawn_player failed: {e}")
                    except Exception:
                        pass
                break
