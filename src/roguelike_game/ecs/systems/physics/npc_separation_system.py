"""
Module: npc_separation_system.py
Resolves inter-NPC overlaps by gently separating their 'feet' colliders.
Runs after SpawnSystem so fresh spawns are de-overlapped immediately,
and every frame to guarantee they never overlap again.
"""
import pygame
from roguelike_game.ecs.utils.collider_utils import (
    build_collider_rect,
    get_circle_world,
    circle_overlaps_rect,
    circle_overlaps_circle,
    circle_circle_mtv,
    circle_rect_mtv,
)
from roguelike_game.ecs.components.physics.circle_collider import CircleCollider
from roguelike_engine.utils.benchmark import benchmark


class NpcSeparationSystem:
    """
    Detecta solapes entre colliders 'feet' de NPCs y aplica pequeñas separaciones
    para que no compartan espacio. Respeta colisiones con el entorno sólido.
    - No mueve al jugador; sólo separa NPCs alrededor del jugador si fuera necesario.
    - Ejecuta varias iteraciones para resolver cadenas de solapes.
    """

    def __init__(self, perf_log=None, max_iters: int = 3):
        self.perf_log = perf_log
        self.max_iters = max_iters
    
    def update(self, world, camera=None):
        comps = world.components
        pos_map = comps.get('Position', {})
        multi_map = comps.get('MultiCollider', {})
        death_map = comps.get('DeathTimer', {})
        player_map = comps.get('PlayerTagComponent', {})
        no_sep_map = comps.get('NoNpcSeparation', {})
        tile_query = getattr(world, 'get_solid_tiles_for_rect', None)
        if tile_query is None:
            return

        # Construir geometrías actuales
        entities = [eid for eid in world.get_entities_with('Position', 'MultiCollider') if eid not in death_map]
        feet_rects: dict[int, pygame.Rect] = {}
        feet_circles: dict[int, tuple[float, float, float]] = {}
        for eid in entities:
            feet = multi_map[eid].colliders.get('feet')
            if not feet:
                continue
            pos = pos_map[eid]
            if isinstance(feet, CircleCollider):
                feet_circles[eid] = get_circle_world(pos.x, pos.y, feet)
            else:
                feet_rects[eid] = build_collider_rect(pos.x, pos.y, feet)

        if not feet_rects and not feet_circles:
            return

        # Varias pasadas de separación
        for _ in range(self.max_iters):
            moved_any = False
            # Evaluar pares con solape
            ids = sorted(set(list(feet_rects.keys()) + list(feet_circles.keys())))
            for i in range(len(ids)):
                a_id = ids[i]
                a_is_circle = a_id in feet_circles
                a_geom = feet_circles[a_id] if a_is_circle else feet_rects[a_id]
                a_is_player = a_id in player_map
                for j in range(i + 1, len(ids)):
                    b_id = ids[j]
                    b_is_circle = b_id in feet_circles
                    b_geom = feet_circles[b_id] if b_is_circle else feet_rects[b_id]
                    b_is_player = b_id in player_map
                    # Detectar solape según tipos
                    overlap = False
                    if a_is_circle and b_is_circle:
                        overlap = circle_overlaps_circle(a_geom, b_geom)
                    elif a_is_circle and not b_is_circle:
                        overlap = circle_overlaps_rect(*a_geom, b_geom)
                    elif (not a_is_circle) and b_is_circle:
                        overlap = circle_overlaps_rect(*b_geom, a_geom)
                    else:
                        overlap = a_geom.colliderect(b_geom)
                    if not overlap:
                        continue

                    # Política de empuje:
                    # - No mover jugador
                    # - No mover entidades marcadas con NoNpcSeparation (p.ej., vendors con ancla estricta)
                    move_a = (not a_is_player) and (a_id not in no_sep_map)
                    move_b = (not b_is_player) and (b_id not in no_sep_map)
                    if not move_a and not move_b:
                        continue

                    # Calcular MTV y proponer desplazamientos
                    if a_is_circle and b_is_circle:
                        mtv_x, mtv_y = circle_circle_mtv(a_geom, b_geom)
                        ax, ay = (-0.5 * mtv_x if move_a and move_b else (-mtv_x if move_a else 0),
                                  -0.5 * mtv_y if move_a and move_b else (-mtv_y if move_a else 0))
                        bx, by = (0.5 * mtv_x if move_a and move_b else (mtv_x if move_b else 0),
                                  0.5 * mtv_y if move_a and move_b else (mtv_y if move_b else 0))

                        # Intentar aplicar, respetando tiles
                        if move_a and (ax != 0 or ay != 0):
                            acx, acy, ar = feet_circles[a_id]
                            naabb = pygame.Rect(int(acx + ax - ar), int(acy + ay - ar), int(ar * 2), int(ar * 2))
                            tiles = tile_query(naabb)
                            if not any(circle_overlaps_rect(acx + ax, acy + ay, ar, t) for t in tiles):
                                pos_map[a_id].x += ax
                                pos_map[a_id].y += ay
                                feet_circles[a_id] = (acx + ax, acy + ay, ar)
                                moved_any = True
                        if move_b and (bx != 0 or by != 0):
                            bcx, bcy, br = feet_circles[b_id]
                            naabb = pygame.Rect(int(bcx + bx - br), int(bcy + by - br), int(br * 2), int(br * 2))
                            tiles = tile_query(naabb)
                            if not any(circle_overlaps_rect(bcx + bx, bcy + by, br, t) for t in tiles):
                                pos_map[b_id].x += bx
                                pos_map[b_id].y += by
                                feet_circles[b_id] = (bcx + bx, bcy + by, br)
                                moved_any = True

                    elif a_is_circle and not b_is_circle:
                        mtv_x, mtv_y = circle_rect_mtv(*a_geom, b_geom)
                        ax, ay = ( -mtv_x if move_a else 0, -mtv_y if move_a else 0)
                        if move_a and (ax != 0 or ay != 0):
                            acx, acy, ar = feet_circles[a_id]
                            naabb = pygame.Rect(int(acx + ax - ar), int(acy + ay - ar), int(ar * 2), int(ar * 2))
                            tiles = tile_query(naabb)
                            if not any(circle_overlaps_rect(acx + ax, acy + ay, ar, t) for t in tiles):
                                pos_map[a_id].x += ax
                                pos_map[a_id].y += ay
                                feet_circles[a_id] = (acx + ax, acy + ay, ar)
                                moved_any = True

                    elif (not a_is_circle) and b_is_circle:
                        mtv_x, mtv_y = circle_rect_mtv(*b_geom, a_geom)
                        bx, by = ( mtv_x if move_b else 0, mtv_y if move_b else 0)
                        if move_b and (bx != 0 or by != 0):
                            bcx, bcy, br = feet_circles[b_id]
                            naabb = pygame.Rect(int(bcx + bx - br), int(bcy + by - br), int(br * 2), int(br * 2))
                            tiles = tile_query(naabb)
                            if not any(circle_overlaps_rect(bcx + bx, bcy + by, br, t) for t in tiles):
                                pos_map[b_id].x += bx
                                pos_map[b_id].y += by
                                feet_circles[b_id] = (bcx + bx, bcy + by, br)
                                moved_any = True
                    else:
                        # Rect-Rect fallback original
                        a_rect = a_geom
                        b_rect = b_geom
                        # Calcular mínimo desplazamiento para separar por eje
                        dx1 = (b_rect.right - a_rect.left)
                        dx2 = (a_rect.right - b_rect.left)
                        dy1 = (b_rect.bottom - a_rect.top)
                        dy2 = (a_rect.bottom - b_rect.top)
                        push_x = dx1 if abs(dx1) < abs(dx2) else -dx2
                        push_y = dy1 if abs(dy1) < abs(dy2) else -dy2
                        if abs(push_x) < abs(push_y):
                            sep_ax = 'x'; sep_val = push_x
                        else:
                            sep_ax = 'y'; sep_val = push_y
                        a_push = b_push = 0
                        if move_a and move_b:
                            a_push = -0.5 * sep_val
                            b_push =  0.5 * sep_val
                        elif move_a:
                            a_push = -sep_val
                        elif move_b:
                            b_push =  sep_val
                        if a_push != 0:
                            test = a_rect.move(a_push if sep_ax=='x' else 0, a_push if sep_ax=='y' else 0)
                            if test.collidelist(tile_query(test)) == -1:
                                pos_map[a_id].x += a_push if sep_ax=='x' else 0
                                pos_map[a_id].y += a_push if sep_ax=='y' else 0
                                feet_rects[a_id] = test
                                moved_any = True
                        if b_push != 0:
                            test = b_rect.move(b_push if sep_ax=='x' else 0, b_push if sep_ax=='y' else 0)
                            if test.collidelist(tile_query(test)) == -1:
                                pos_map[b_id].x += b_push if sep_ax=='x' else 0
                                pos_map[b_id].y += b_push if sep_ax=='y' else 0
                                feet_rects[b_id] = test
                                moved_any = True
            if not moved_any:
                break
