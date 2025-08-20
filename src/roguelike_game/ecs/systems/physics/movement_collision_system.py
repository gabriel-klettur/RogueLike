"""
Module: movement_collision_system.py
Handles movement-based collision for entities using their 'feet' collider.
Checks collisions against solid tiles and buildings, resolving movement per axis.
"""
import pygame
from typing import Dict, Optional, Tuple
from roguelike_game.ecs.utils.collider_utils import (
    build_collider_rect,
    get_circle_world,
    circle_overlaps_rect,
    circle_overlaps_circle,
    circle_rect_mtv,
    circle_circle_mtv,
)
from roguelike_game.ecs.components.physics.circle_collider import CircleCollider
from roguelike_engine.utils.benchmark import benchmark


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
                      npc_circles: Optional[Dict[int, Tuple[float, float, float]]],
                      npc_rects: Optional[Dict[int, pygame.Rect]],
                      max_iters: int = 3) -> Tuple[float, float]:
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
            # Broad-phase query via AABB
            aabb = pygame.Rect(int(nx - r), int(ny - r), int(r * 2), int(r * 2))
            tiles = tile_query(aabb)

            # Find the most significant MTV among collisions
            collided = False
            best_mtv = (0.0, 0.0)
            best_len2 = 0.0

            # Tiles
            for tile in tiles:
                if circle_overlaps_rect(nx, ny, r, tile):
                    collided = True
                    mtv = circle_rect_mtv(nx, ny, r, tile)
                    ml2 = mtv[0]*mtv[0] + mtv[1]*mtv[1]
                    if ml2 > best_len2:
                        best_mtv = mtv
                        best_len2 = ml2

            # Other NPC circles
            if npc_circles:
                for _, c in npc_circles.items():
                    if circle_overlaps_circle((nx, ny, r), c):
                        collided = True
                        mtv = circle_circle_mtv((nx, ny, r), c)
                        ml2 = mtv[0]*mtv[0] + mtv[1]*mtv[1]
                        if ml2 > best_len2:
                            best_mtv = mtv
                            best_len2 = ml2

            # Other NPC rectangles (compat)
            if npc_rects:
                for _, rr in npc_rects.items():
                    if circle_overlaps_rect(nx, ny, r, rr):
                        collided = True
                        mtv = circle_rect_mtv(nx, ny, r, rr)
                        ml2 = mtv[0]*mtv[0] + mtv[1]*mtv[1]
                        if ml2 > best_len2:
                            best_mtv = mtv
                            best_len2 = ml2

            if not collided:
                # Free move
                cx, cy = nx, ny
                vx, vy = 0.0, 0.0
                break

            # Apply separation push and slide remaining velocity along tangent
            cx += best_mtv[0]
            cy += best_mtv[1]
            # normal is direction of MTV
            ml = (best_len2 ** 0.5)
            if ml > 1e-6:
                nxn = best_mtv[0] / ml
                nyn = best_mtv[1] / ml
                dot = vx * nxn + vy * nyn
                # remove normal component (slide along surface)
                vx = vx - dot * nxn
                vy = vy - dot * nyn
            else:
                # Degenerate MTV, abort remaining velocity
                vx, vy = 0.0, 0.0

        return cx, cy

    @benchmark(lambda self: self.perf_log, "4.2.2.MovementCollisionSystem.update")
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
                if isinstance(nfeet, CircleCollider):
                    npc_feet_circles[nid] = get_circle_world(npos.x, npos.y, nfeet)
                else:
                    npc_feet_rects[nid] = build_collider_rect(npos.x, npos.y, nfeet)

        # 2) Iterar sobre entidades que puedan moverse y colisionar
        for eid in world.get_entities_with('Position', 'Velocity', 'MultiCollider'):
            pos   = pos_map[eid]
            vel   = vel_map[eid]
            multi = multi_map[eid]

            # 2a) Obtener el collider de los pies; si no existe, saltar
            feet = multi.colliders.get('feet')
            if not feet:
                continue

            # 2b) Resolver movimiento según tipo de collider
            if isinstance(feet, CircleCollider):
                # Centro y radio actuales
                cx, cy, r = get_circle_world(pos.x, pos.y, feet)
                # Preparar conjuntos de NPCs contra los que colisionar (excluyendo self y estabilizados)
                others_circles = {i: c for i, c in npc_feet_circles.items() if i != eid and i not in stabilized_ids}
                others_rects   = {i: rr for i, rr in npc_feet_rects.items() if i != eid and i not in stabilized_ids}

                nx, ny = self._slide_circle(cx, cy, r, vel.vx, vel.vy, tile_query, others_circles, others_rects)
                # Aplicar delta a Position (mantener vel para coherencia con comportamiento previo)
                pos.x += (nx - cx)
                pos.y += (ny - cy)
                # Actualizar cache para siguientes entidades del mismo frame
                npc_feet_circles[eid] = (nx, ny, r)
                continue  # ya resuelto como círculo

            # Rectangular fallback
            feet.rect = build_collider_rect(pos.x, pos.y, feet)

            # 3) Resolver movimiento en X
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