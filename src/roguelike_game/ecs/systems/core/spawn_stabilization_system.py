"""
SpawnStabilizationSystem
- Durante pocos frames tras el spawn, detecta entidades con solape (misma tile de 'feet')
  y las reubica a la tile libre más cercana usando búsqueda en espiral.
- Evita jitter continuo y resuelve solapes múltiples de forma estable.
- Usa reservas por frame para que múltiples estabilizaciones no se pisen entre sí.
"""
from __future__ import annotations

from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.utils.collider_utils import build_collider_rect


class SpawnStabilizationSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def _iter_spiral_tiles(self, cx: int, cy: int, max_radius: int):
        yield (cx, cy)
        for r in range(1, max_radius + 1):
            x0, x1 = cx - r, cx + r
            y0, y1 = cy - r, cy + r
            for x in range(x0, x1 + 1):
                yield (x, y0)
                yield (x, y1)
            for y in range(y0 + 1, y1):
                yield (x0, y)
                yield (x1, y)

    def _collect_blocked_tiles(self, world):
        solid_coords = {(t.rect.x // TILE_SIZE, t.rect.y // TILE_SIZE) for t in world.map_manager.solid_tiles}
        building_coords = {(r.x // TILE_SIZE, r.y // TILE_SIZE) for b in world.buildings for r in getattr(b, 'collision_tiles', [])}
        return solid_coords, building_coords
    
    def update(self, world, camera=None):
        comps = world.components
        pos_map = comps.get('Position', {})
        multi_map = comps.get('MultiCollider', {})
        death_map = comps.get('DeathTimer', {})
        stab_map = comps.get('SpawnStabilizer', {})
        if not stab_map:
            return

        # Bloqueos estáticos
        solid, building = self._collect_blocked_tiles(world)
        map_manager = getattr(world, 'map_manager', None)
        tile_query = getattr(world, 'get_solid_tiles_for_rect', None)

        # Capturar rects de 'feet' y tiles ocupadas actuales
        entities = [eid for eid in world.get_entities_with('Position', 'MultiCollider') if eid not in death_map]
        feet_rects = {}
        for eid in entities:
            feet = multi_map[eid].colliders.get('feet')
            if not feet:
                continue
            p = pos_map[eid]
            rect = build_collider_rect(p.x, p.y, feet)
            feet_rects[eid] = rect
        
        # Detectar solapes reales por collider, centrado en entidades con SpawnStabilizer
        stab_entities = [eid for eid in stab_map.keys() if eid in feet_rects]
        overlapped_ids = set()
        for eid in stab_entities:
            r = feet_rects[eid]
            # Si colisiona con cualquier otro rect, marcar
            if any(r.colliderect(r2) for oid, r2 in feet_rects.items() if oid != eid):
                overlapped_ids.add(eid)

        # Reservas por frame para destinos escogidos
        reserved_tiles = set()

        # Procesar sólo entidades marcadas con SpawnStabilizer
        to_remove = []
        for eid, stab in list(stab_map.items()):
            # Si ya no existe o murió, quitar marca
            if eid not in entities:
                to_remove.append(eid)
                continue

            # Si no está solapado, consumir frames y quitar cuando expire
            if eid not in overlapped_ids:
                stab.frames_remaining -= 1
                if stab.frames_remaining <= 0:
                    to_remove.append(eid)
                continue

            # Buscar tile libre cercana respecto a la tile actual del 'feet'
            rect = feet_rects[eid]
            cur_tx = int(rect.centerx // TILE_SIZE)
            cur_ty = int(rect.centery // TILE_SIZE)

            found = None
            for tx, ty in self._iter_spiral_tiles(cur_tx, cur_ty, max(1, int(stab.max_search_radius))):
                # Evitar estáticos
                if (tx, ty) in solid or (tx, ty) in building:
                    continue
                # Walkability
                if map_manager and hasattr(map_manager, 'is_walkable'):
                    try:
                        if not map_manager.is_walkable(tx, ty):
                            continue
                    except Exception:
                        pass
                # Evitar que dos entidades tomen la misma tile destino este frame
                if (tx, ty) in reserved_tiles:
                    continue
                # Construir rect candidato alineando el centro del feet al centro de la tile
                target_cx = tx * TILE_SIZE + TILE_SIZE // 2
                target_cy = ty * TILE_SIZE + TILE_SIZE // 2
                dx = target_cx - rect.centerx
                dy = target_cy - rect.centery
                cand = rect.move(dx, dy)
                # Validar contra tiles sólidos con el spatial index si existe
                if tile_query is not None:
                    nearby = tile_query(cand)
                    if cand.collidelist(nearby) != -1:
                        continue
                # Validar contra otros NPCs (colliders reales)
                if any(cand.colliderect(r2) for oid, r2 in feet_rects.items() if oid != eid):
                    continue
                found = (tx, ty, dx, dy, cand)
                break

            if found is None:
                # No hay hueco cercano este frame, reintentar próximos frames
                stab.frames_remaining -= 1
                if stab.frames_remaining <= 0:
                    to_remove.append(eid)
                continue

            # Reubicar alineando el centro del collider 'feet' al centro de la tile
            tx, ty, dx, dy, new_rect = found
            reserved_tiles.add((tx, ty))
            pos_map[eid].x += dx
            pos_map[eid].y += dy
            feet_rects[eid] = new_rect

            # Consumir un frame de estabilización
            stab.frames_remaining -= 1
            if stab.frames_remaining <= 0:
                to_remove.append(eid)

        for eid in to_remove:
            stab_map.pop(eid, None)
