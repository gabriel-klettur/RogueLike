import pygame
from typing import Tuple, Iterable, Optional

from roguelike_engine.config.config_tiles import TILE_SIZE, TILE_COLORS
from roguelike_engine.config.map_config import global_map_settings
from roguelike_game.ecs.components.core.identity import Faction
from roguelike_engine.config.config_minimap import (
    MINIMAP_TILE_UPDATE_MS,
    MINIMAP_BUILDINGS_UPDATE_MS,
    MINIMAP_ENTITIES_UPDATE_MS,
    MINIMAP_MAX_ENTITIES,
    MINIMAP_COLORS,
    MINIMAP_ZONE_COLORS,
    MINIMAP_ZONE_BORDER_WIDTH,
)


class MinimapController:
    """
    Controlador del Minimap. Actualiza las capas con rate limits y calcula
    ventanas visibles según la posición del jugador.
    """

    def update(
        self,
        model,
        player_pos: Tuple[float, float],
        tiles: Iterable[object],
        buildings: Optional[Iterable] = None,
        world: Optional[object] = None,
    ) -> None:
        now = pygame.time.get_ticks()
        px = int(player_pos[0]) // TILE_SIZE
        py = int(player_pos[1]) // TILE_SIZE
        half_x, half_y = model.visible_half_tiles

        # Reset duro al cambiar de mundo: limpiar capas y cachés para evitar artefactos
        try:
            cur_world_id = getattr(global_map_settings, 'current_world', None)
        except Exception:
            cur_world_id = None
        if getattr(model, 'last_world_id', None) != cur_world_id:
            model.last_world_id = cur_world_id
            # Limpiar superficies de capas
            try:
                model.bg_tiles_surface.fill(MINIMAP_COLORS["bg"])
            except Exception:
                pass
            try:
                model.buildings_surface.fill((0, 0, 0, 0))
            except Exception:
                pass
            try:
                model.entities_surface.fill((0, 0, 0, 0))
            except Exception:
                pass
            try:
                model.zones_surface.fill((0, 0, 0, 0))
            except Exception:
                pass
            # Reset de caches/estados para forzar recomputo inmediato
            model.visible_tiles = []
            model.last_player_tile = None
            model.last_tiles_ms = 0
            model.last_buildings_ms = 0
            model.last_entities_ms = 0
            model.last_zones_ms = 0

        # 1) Tiles (fondo)
        if (now - model.last_tiles_ms >= MINIMAP_TILE_UPDATE_MS) or (model.last_player_tile != (px, py)):
            model.last_tiles_ms = now
            vis = []
            # Determine overlays presence and whether zones.json has user-defined zones
            try:
                has_overlays = False
                user_keys_count = 0
                if getattr(global_map_settings, 'use_zones_json', False):
                    from pathlib import Path as _P
                    odir = getattr(global_map_settings, 'overlays_dir', None)
                    has_overlays = bool(odir and len(list(_P(odir).glob('*.overlay.json'))) > 0)
                    try:
                        offsets = getattr(global_map_settings, 'zone_offsets', {})
                        user_keys_count = len([k for k in offsets.keys() if str(k).lower() not in ('no zone', 'no-zone')])
                    except Exception:
                        user_keys_count = 0
            except Exception:
                has_overlays = False
                user_keys_count = 0
            for t in tiles:
                try:
                    tx = (t.x // TILE_SIZE)
                    ty = (t.y // TILE_SIZE)
                except Exception:
                    continue
                # Overlays-driven policy: suppress tiles with no overlay_code ONLY IF there are no overlays AND no user zones.
                # Otherwise allow fallback (e.g., generated dungeon/lobby) as in main renderer.
                if getattr(global_map_settings, 'use_zones_json', False):
                    if (not has_overlays and user_keys_count == 0) and not getattr(t, 'overlay_code', None):
                        continue
                if abs(tx - px) <= half_x and abs(ty - py) <= half_y:
                    vis.append(t)
            model.visible_tiles = vis
            model.bg_tiles_surface.fill(MINIMAP_COLORS["bg"])            
            for t in model.visible_tiles:
                try:
                    tx = (t.x // TILE_SIZE) - px
                    ty = (t.y // TILE_SIZE) - py
                    x = model.width // 2 + tx * model.zoom
                    y = model.height // 2 + ty * model.zoom
                    color = TILE_COLORS.get(getattr(t, 'tile_type', None), (255, 0, 255))
                    pygame.draw.rect(model.bg_tiles_surface, color, (x, y, model.zoom, model.zoom))
                except Exception:
                    pass

        # 2) Edificios (semi-estático)
        if buildings is not None and (
            (now - model.last_buildings_ms >= MINIMAP_BUILDINGS_UPDATE_MS)
            or (model.last_player_tile != (px, py))
        ):
            model.last_buildings_ms = now
            model.buildings_surface.fill((0, 0, 0, 0))
            for b in buildings:
                try:
                    bx = b.x // TILE_SIZE
                    by = b.y // TILE_SIZE
                    img = getattr(b, 'image', None)
                    if img is None:
                        continue
                    bw = max(1, img.get_width() // TILE_SIZE)
                    bh = max(1, img.get_height() // TILE_SIZE)
                    if abs(bx - px) > (half_x + bw) or abs(by - py) > (half_y + bh):
                        continue
                    rel_x = model.width // 2 + (bx - px) * model.zoom
                    rel_y = model.height // 2 + (by - py) * model.zoom
                    pygame.draw.rect(
                        model.buildings_surface,
                        MINIMAP_COLORS["building"],
                        (rel_x, rel_y, bw * model.zoom, bh * model.zoom),
                        width=1,
                    )
                except Exception:
                    pass

        # 2.5) Zonas (semi-estático)
        if (now - model.last_zones_ms >= MINIMAP_BUILDINGS_UPDATE_MS) or (model.last_player_tile != (px, py)):
            model.last_zones_ms = now
            model.zones_surface.fill((0, 0, 0, 0))
            try:
                zone_w = int(getattr(global_map_settings, 'zone_width', 50))
                zone_h = int(getattr(global_map_settings, 'zone_height', 50))
                half_x, half_y = model.visible_half_tiles
                items = dict(getattr(global_map_settings, 'zone_offsets', {})).items()
                for name, (ox, oy) in items:
                    low = str(name).lower()
                    if low in ('no zone', 'no-zone'):
                        continue
                    if (ox + zone_w) < (px - half_x) or ox > (px + half_x):
                        continue
                    if (oy + zone_h) < (py - half_y) or oy > (py + half_y):
                        continue
                    rel_tx = ox - px
                    rel_ty = oy - py
                    x = model.width // 2 + rel_tx * model.zoom
                    y = model.height // 2 + rel_ty * model.zoom
                    w = max(1, zone_w * model.zoom)
                    h = max(1, zone_h * model.zoom)
                    color = MINIMAP_ZONE_COLORS.get(low, MINIMAP_ZONE_COLORS.get('default', (200, 200, 200)))
                    try:
                        pygame.draw.rect(model.zones_surface, color, pygame.Rect(x, y, w, h), width=int(MINIMAP_ZONE_BORDER_WIDTH))
                    except Exception:
                        pass
            except Exception:
                pass

        # 3) Entidades (dinámico)
        if world is not None and (
            (now - model.last_entities_ms >= MINIMAP_ENTITIES_UPDATE_MS)
            or (model.last_player_tile != (px, py))
        ):
            model.last_entities_ms = now
            model.entities_surface.fill((0, 0, 0, 0))
            try:
                pos_map = world.components.get('Position', {})
                id_map = world.components.get('Identity', {})
                player_eid = getattr(world, 'player_entity', None)
                count = 0
                for eid, pos in pos_map.items():
                    if eid == player_eid:
                        continue
                    ex = int(getattr(pos, 'x', 0)) // TILE_SIZE
                    ey = int(getattr(pos, 'y', 0)) // TILE_SIZE
                    if abs(ex - px) > half_x or abs(ey - py) > half_y:
                        continue
                    color = MINIMAP_COLORS["neutral"]
                    ident = id_map.get(eid)
                    if ident is not None:
                        try:
                            if ident.faction == Faction.GOOD:
                                color = MINIMAP_COLORS["ally"]
                            elif ident.faction == Faction.EVIL:
                                color = MINIMAP_COLORS["enemy"]
                            else:
                                color = MINIMAP_COLORS["neutral"]
                        except Exception:
                            pass
                    rx = model.width // 2 + (ex - px) * model.zoom
                    ry = model.height // 2 + (ey - py) * model.zoom
                    pygame.draw.rect(model.entities_surface, color, (rx, ry, model.zoom, model.zoom))
                    count += 1
                    if count >= MINIMAP_MAX_ENTITIES:
                        break
            except Exception:
                pass

        model.last_player_tile = (px, py)
