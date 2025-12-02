"""
Module: movement_collision_system.py
Handles movement-based collision for entities using their 'feet' collider.
Checks collisions against solid tiles and buildings, resolving movement per axis.
"""
import pygame
import math
import time
from typing import Dict, Optional, Tuple
from roguelike_game.ecs.utils.collider_utils import (
    build_collider_rect,
    get_circle_world,
    circle_overlaps_rect,
    circle_overlaps_circle,
    circle_rect_mtv,
    circle_circle_mtv,
    circle_obb_mtv,
)
from roguelike_game.ecs.components.physics.circle_collider import CircleCollider
from roguelike_engine.utils.benchmark.benchmark import benchmark


class MovementCollisionSystem:
    """
    Sistema que resuelve colisiones de movimiento usando el collider 'feet'.
    Se aplica un test por separado en X y en Y para un movimiento suave y consistente.
    """

    def __init__(self, perf_log):
        self.perf_log = perf_log

    # --- Circle sliding helper (tiles + NPCs) ---
    def _slide_circle(self,
                      cx: float,
                      cy: float,
                      r: float,
                      dx: float,
                      dy: float,
                      tile_query,
                      walls_data,
                      npc_circles: Optional[Dict[int, Tuple[float, float, float]]],
                      npc_rects: Optional[Dict[int, pygame.Rect]],
                      max_iters: int = 5) -> Tuple[float, float]:
        """Attempt to move a circle by (dx,dy), sliding along obstacles using MTV resolution.
        Returns new (cx, cy)."""
        if (dx == 0 and dy == 0) or r <= 0:
            return cx, cy
        vx, vy = float(dx), float(dy)
        for _ in range(max_iters):
            if abs(vx) < 1e-4 and abs(vy) < 1e-4:
                break
            nx = cx + vx
            ny = cy + vy
            # Broad-phase query via AABB (slightly expanded to avoid edge gaps)
            left = math.floor(nx - r - 1)
            top = math.floor(ny - r - 1)
            w = math.ceil(2 * r) + 2
            h = math.ceil(2 * r) + 2
            aabb = pygame.Rect(left, top, w, h)
            tiles = tile_query(aabb)

            # Gather MTVs and pick the one that most opposes current velocity (reduces corner snag)
            collided = False
            oppose_best_mtv = (0.0, 0.0)
            oppose_best_dot = float('inf')  # most negative dot is best
            mag_best_mtv = (0.0, 0.0)
            mag_best_len2 = 0.0
            # Accumulate MTVs to stabilize corner resolution
            sum_mtv_x = 0.0
            sum_mtv_y = 0.0
            coll_count = 0

            # Tiles (AABB obstacles)
            for tile in tiles:
                if circle_overlaps_rect(nx, ny, r, tile):
                    collided = True
                    mtv = circle_rect_mtv(nx, ny, r, tile)
                    ml2 = mtv[0]*mtv[0] + mtv[1]*mtv[1]
                    if ml2 > mag_best_len2:
                        mag_best_mtv = mtv
                        mag_best_len2 = ml2
                    dot = vx * mtv[0] + vy * mtv[1]
                    if dot < oppose_best_dot:
                        oppose_best_dot = dot
                        oppose_best_mtv = mtv
                    sum_mtv_x += mtv[0]
                    sum_mtv_y += mtv[1]
                    coll_count += 1

            # Walls (OBB obstacles)
            if walls_data:
                for w in walls_data:
                    try:
                        if not w.get('blocks_units', True):
                            continue
                        # Broad-phase culling by OBB's AABB
                        waabb = w['aabb']
                        if not aabb.colliderect(waabb):
                            continue
                        mtw_x, mtw_y = circle_obb_mtv(
                            nx, ny, r,
                            w['wx'], w['wy'], w['half_w'], w['half_h'], w['cos'], w['sin']
                        )
                        if mtw_x != 0.0 or mtw_y != 0.0:
                            collided = True
                            mtv = (mtw_x, mtw_y)
                            ml2 = mtv[0]*mtv[0] + mtv[1]*mtv[1]
                            if ml2 > mag_best_len2:
                                mag_best_mtv = mtv
                                mag_best_len2 = ml2
                            dot = vx * mtv[0] + vy * mtv[1]
                            if dot < oppose_best_dot:
                                oppose_best_dot = dot
                                oppose_best_mtv = mtv
                            sum_mtv_x += mtv[0]
                            sum_mtv_y += mtv[1]
                            coll_count += 1
                    except Exception:
                        # Ignorar muros mal formados o datos incompletos
                        continue

            # Other NPC circles
            if npc_circles:
                for _, c in npc_circles.items():
                    if circle_overlaps_circle((nx, ny, r), c):
                        collided = True
                        mtv = circle_circle_mtv((nx, ny, r), c)
                        ml2 = mtv[0]*mtv[0] + mtv[1]*mtv[1]
                        if ml2 > mag_best_len2:
                            mag_best_mtv = mtv
                            mag_best_len2 = ml2
                        dot = vx * mtv[0] + vy * mtv[1]
                        if dot < oppose_best_dot:
                            oppose_best_dot = dot
                            oppose_best_mtv = mtv
                        sum_mtv_x += mtv[0]
                        sum_mtv_y += mtv[1]
                        coll_count += 1

            # Other NPC rectangles (compat)
            if npc_rects:
                for _, rr in npc_rects.items():
                    if circle_overlaps_rect(nx, ny, r, rr):
                        collided = True
                        mtv = circle_rect_mtv(nx, ny, r, rr)
                        ml2 = mtv[0]*mtv[0] + mtv[1]*mtv[1]
                        if ml2 > mag_best_len2:
                            mag_best_mtv = mtv
                            mag_best_len2 = ml2
                        dot = vx * mtv[0] + vy * mtv[1]
                        if dot < oppose_best_dot:
                            oppose_best_dot = dot
                            oppose_best_mtv = mtv
                        sum_mtv_x += mtv[0]
                        sum_mtv_y += mtv[1]
                        coll_count += 1

            if not collided:
                # Free move
                cx, cy = nx, ny
                vx, vy = 0.0, 0.0
                break

            # Choose MTV:
            # 1) If multiple overlaps, use accumulated MTV for stability
            # 2) Otherwise prefer the one that most opposes velocity; fallback to largest magnitude
            if coll_count > 1 and (sum_mtv_x*sum_mtv_x + sum_mtv_y*sum_mtv_y) > 1e-8:
                use_mtv = (sum_mtv_x, sum_mtv_y)
            else:
                use_mtv = oppose_best_mtv if oppose_best_dot < -1e-8 else mag_best_mtv

            # Apply separation push and slide remaining velocity along tangent
            cx += use_mtv[0]
            cy += use_mtv[1]
            # normal is direction of MTV used
            ml = (use_mtv[0]*use_mtv[0] + use_mtv[1]*use_mtv[1]) ** 0.5
            if ml > 1e-6:
                nxn = use_mtv[0] / ml
                nyn = use_mtv[1] / ml
                dot = vx * nxn + vy * nyn
                # remove normal component (slide along surface)
                vx = vx - dot * nxn
                vy = vy - dot * nyn
                # Apply a small outward skin to avoid immediate re-collision due to rounding
                skin = 0.5
                cx += nxn * skin
                cy += nyn * skin
            else:
                # Degenerate MTV, abort remaining velocity
                vx, vy = 0.0, 0.0

        return cx, cy
    
    def update(self, world, camera=None):
        """
        Recorre todas las entidades con Position, Velocity y MultiCollider, y para cada una:
          1. Obtiene el collider de 'feet' y lo posiciona en el mundo.
          2. Intenta desplazarlo en el eje X:
             - Mueve el rectángulo del collider.
             - Consulta colisiones broad-phase usando el spatial index.
             - Si no hay colisión, actualiza la posición X de la entidad.
             - Si colisiona, restaura la posición original y anula la velocidad X.
          3. Repite el mismo proceso para el eje Y.
        """
        # 1) Preparar referencias locales para eficiencia
        comps     = world.components
        pos_map   = comps['Position']
        vel_map   = comps['Velocity']
        multi_map = comps['MultiCollider']
        tile_query = world.get_solid_tiles_for_rect  # spatial index query

        # Preparar rects de pies de NPCs para colisión mutua
        npc_feet_rects = {}
        npc_feet_circles = {}
        stab_map = comps.get('SpawnStabilizer', {})
        stabilized_ids = set(stab_map.keys()) if stab_map else set()
        for nid in world.get_entities_with('Position', 'MultiCollider'):
            if nid in comps.get('PlayerTagComponent', {}):
                continue
            # Omitir colisiones con NPCs muertos
            if nid in comps.get('DeathTimer', {}):
                continue
            npos = pos_map[nid]
            nmulti = multi_map[nid]
            nfeet = nmulti.colliders.get('feet')
            if nfeet:
                if hasattr(nfeet, "radius"):
                    npc_feet_circles[nid] = get_circle_world(npos.x, npos.y, nfeet)
                else:
                    npc_feet_rects[nid] = build_collider_rect(npos.x, npos.y, nfeet)

        # Precompute walls data for OBB collisions
        walls_data = []
        try:
            wmap = comps.get('WallSegmentComponent', {})
            pmap = comps.get('Position', {})
            for wid, w in list(wmap.items()):
                posw = pmap.get(wid)
                if posw is None:
                    continue
                half_w = float(getattr(w, 'half_w', getattr(w, 'width', 0.0) * 0.5) or 0.0)
                half_h = float(getattr(w, 'half_h', getattr(w, 'height', 0.0) * 0.5) or 0.0)
                cos_a = float(getattr(w, 'cos_a', 1.0))
                sin_a = float(getattr(w, 'sin_a', 0.0))
                # AABB bounding box of the OBB for broad-phase
                ext_x = abs(cos_a) * half_w + abs(sin_a) * half_h
                ext_y = abs(sin_a) * half_w + abs(cos_a) * half_h
                aabb = pygame.Rect(int(posw.x - ext_x), int(posw.y - ext_y), int(ext_x * 2), int(ext_y * 2))
                walls_data.append({
                    'wx': float(posw.x), 'wy': float(posw.y),
                    'half_w': half_w, 'half_h': half_h,
                    'cos': cos_a, 'sin': sin_a,
                    'blocks_units': bool(getattr(w, 'blocks_units', True)),
                    'aabb': aabb,
                })
        except Exception:
            walls_data = []

        # 2) Iterar sobre entidades que puedan moverse y colisionar
        for eid in world.get_entities_with('Position', 'Velocity', 'MultiCollider'):
            # No mover entidades que están en proceso de muerte (cadáveres)
            if eid in comps.get('DeathTimer', {}):
                continue
            pos   = pos_map[eid]
            vel   = vel_map[eid]
            multi = multi_map[eid]

            # Robust movement lock: prevent final_boss_barbol from moving during wind-up and post-fire lock
            try:
                mt = comps.get('MonsterArchetype', {}).get(eid)
                mtype = (getattr(mt, 'type', None) or '').lower() if mt else None
                if isinstance(mtype, str) and mtype.startswith('final_boss_barbol'):
                    npc_state = comps.get('NPCState', {}).get(eid)
                    fsm = getattr(npc_state, 'fsm', None)
                    ctx = getattr(fsm, 'context', {}) if fsm else {}
                    now = time.time()
                    start_t = float(ctx.get('attack_start') or 0.0)
                    windup_s = float(ctx.get('attack_windup_s', 0.0))
                    lock_until = float(ctx.get('lock_move_until', 0.0) or 0.0)
                    in_windup = (windup_s > 0.0) and (now - start_t < windup_s)
                    locked = in_windup or (now < lock_until)
                    if locked:
                        vel.vx = 0
                        vel.vy = 0
            except Exception:
                pass

            # 2a) Obtener el collider de los pies; si no existe, saltar
            feet = multi.colliders.get('feet')
            if not feet:
                continue

            # 2b) Resolver movimiento según tipo de collider
            if hasattr(feet, "radius"):
                # Centro y radio actuales
                cx, cy, r = get_circle_world(pos.x, pos.y, feet)
                # Preparar conjuntos de NPCs contra los que colisionar (excluyendo self y estabilizados)
                others_circles = {i: c for i, c in npc_feet_circles.items() if i != eid and i not in stabilized_ids}
                others_rects   = {i: rr for i, rr in npc_feet_rects.items() if i != eid and i not in stabilized_ids}

                nx, ny = self._slide_circle(cx, cy, r, vel.vx, vel.vy, tile_query, walls_data, others_circles, others_rects)
                # Aplicar delta a Position (mantener vel para coherencia con comportamiento previo)
                pos.x += (nx - cx)
                pos.y += (ny - cy)
                # Actualizar cache para siguientes entidades del mismo frame
                npc_feet_circles[eid] = (nx, ny, r)
                continue  # ya resuelto como círculo

            # Rectangular fallback
            feet.rect = build_collider_rect(pos.x, pos.y, feet)

            # 3) Resolver movimiento en X (AABB tiles; OBB walls no soportado para feet rect)
            if vel.vx != 0:
                # Guardar la posición original en caso de colisión
                old_x = feet.rect.x
                # Mover el collider en X
                feet.rect.x += vel.vx

                # Test broad-phase + precisa: tiles del mundo
                nearby = tile_query(feet.rect)
                if feet.rect.collidelist(nearby) != -1:
                    # Colisión con tile: revertir y detener
                    feet.rect.x = old_x
                    vel.vx = 0
                else:
                    # Sin colisión con tile, verificar NPCs (omitir mientras SpawnStabilizer activo)
                    check_npc = eid not in stabilized_ids
                    if check_npc and any(feet.rect.colliderect(r) for id2, r in npc_feet_rects.items() if id2 != eid and id2 not in stabilized_ids):
                        # Colisión con otro NPC: revertir
                        feet.rect.x = old_x
                    else:
                        pos.x += vel.vx
                        npc_feet_rects[eid] = feet.rect.copy()

            # 4) Resolver movimiento en Y (idéntico al de X)
            if vel.vy != 0:
                old_y = feet.rect.y
                feet.rect.y += vel.vy

                nearby = tile_query(feet.rect)
                if feet.rect.collidelist(nearby) != -1:
                    # Colisión con tile: revertir y detener
                    feet.rect.y = old_y
                    vel.vy = 0
                else:
                    check_npc = eid not in stabilized_ids
                    if check_npc and any(feet.rect.colliderect(r) for id2, r in npc_feet_rects.items() if id2 != eid and id2 not in stabilized_ids):
                        # Colisión con otro NPC: revertir
                        feet.rect.y = old_y
                    else:
                        pos.y += vel.vy
                        npc_feet_rects[eid] = feet.rect.copy()