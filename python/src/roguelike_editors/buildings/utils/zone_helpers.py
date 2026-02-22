from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_engine.config.map_config import global_map_settings

def assign_zone_and_relatives(building) -> None:
    # 1) Detectar zona basándonos en el centro inferior del sprite
    w_px, h_px = building.image.get_size()
    cx_px = building.x + w_px / 2
    cy_px = building.y + h_px

    tile_x = int(cx_px) // TILE_SIZE
    tile_y = int(cy_px) // TILE_SIZE

    zone = get_zone_for_tile(tile_x, tile_y)

    # 2) Offset de la zona en tiles
    ox, oy = global_map_settings.zone_offsets.get(zone, (0, 0))

    # 3) Convertir ese offset a píxeles
    origin_px_x = ox * TILE_SIZE
    origin_px_y = oy * TILE_SIZE

    # 4) Calcular posición relativa en píxeles
    rel_x = building.x - origin_px_x
    rel_y = building.y - origin_px_y

    # 5) Asignar
    building.zone = zone
    building.rel_x = int(rel_x)
    building.rel_y = int(rel_y)
    # 6) Si este building está vinculado a un spawner, sincronizar su posición/zona
    try:
        eid = getattr(building, "_spawner_eid", None)
        world = getattr(building, "_world_ref", None)
        if eid is not None and world is not None:
            comps = getattr(world, 'components', {})
            cfg_map = comps.get('SpawnerConfig') or {}
            cfg = cfg_map.get(eid)
            if cfg is not None:
                # Calcular tile local a la zona usando el centro del sprite (coincide con lógica de colocación)
                local_tx = int((building.rel_x + w_px / 2) // TILE_SIZE)
                local_ty = int((building.rel_y + h_px / 2) // TILE_SIZE)
                # Convertir a tiles globales usando el offset de la zona detectada
                off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
                cfg.zone = zone
                cfg.anchor_tile = (int(off_x + local_tx), int(off_y + local_ty))
                # Invalida el índice espacial si existe, por si afecta a consultas
                try:
                    if hasattr(world, 'invalidate_spatial_index'):
                        world.invalidate_spatial_index()
                except Exception:
                    pass
    except Exception:
        # No romper el editor por errores de sync
        pass

def detect_zone_from_px(x_px: float, y_px: float) -> tuple[str, tuple[int,int]]:
    """
    Dado un punto en píxeles, devuelve (zone_name, (ox,oy)).
    Si no cae en ninguna zona, devuelve ("no zone", (0,0)).
    """
    tile_x = int(x_px) // TILE_SIZE
    tile_y = int(y_px) // TILE_SIZE
    try:
        zone = get_zone_for_tile(tile_x, tile_y)
    except ValueError:
        zone = "no zone"
    offset = global_map_settings.zone_offsets.get(zone, (0, 0))
    return zone, offset