"""
Module: npc_separation_system.py
Resolves inter-NPC overlaps by gently separating their 'feet' colliders.
Runs after SpawnSystem so fresh spawns are de-overlapped immediately,
and every frame to guarantee they never overlap again.
"""
from roguelike_game.ecs.utils.collider_utils import build_collider_rect
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

    @benchmark(lambda self: self.perf_log, "4.2.2.NpcSeparationSystem.update")
    def update(self, world, camera=None):
        comps = world.components
        pos_map = comps.get('Position', {})
        multi_map = comps.get('MultiCollider', {})
        death_map = comps.get('DeathTimer', {})
        player_map = comps.get('PlayerTagComponent', {})
        tile_query = getattr(world, 'get_solid_tiles_for_rect', None)
        if tile_query is None:
            return

        # Construir rects de pies actuales
        entities = [eid for eid in world.get_entities_with('Position', 'MultiCollider') if eid not in death_map]
        feet_rects = {}
        for eid in entities:
            feet = multi_map[eid].colliders.get('feet')
            if not feet:
                continue
            pos = pos_map[eid]
            feet_rects[eid] = build_collider_rect(pos.x, pos.y, feet)

        if not feet_rects:
            return

        # Varias pasadas de separación
        for _ in range(self.max_iters):
            moved_any = False
            # Evaluar pares con solape
            ids = list(feet_rects.keys())
            for i in range(len(ids)):
                a_id = ids[i]
                a_rect = feet_rects[a_id]
                a_is_player = a_id in player_map
                for j in range(i + 1, len(ids)):
                    b_id = ids[j]
                    b_rect = feet_rects[b_id]
                    b_is_player = b_id in player_map
                    if not a_rect.colliderect(b_rect):
                        continue

                    # Calcular mínimo desplazamiento para separar por eje
                    dx1 = (b_rect.right - a_rect.left)   # empuje A a la izq de B
                    dx2 = (a_rect.right - b_rect.left)   # empuje A a la der de B
                    dy1 = (b_rect.bottom - a_rect.top)   # empuje A arriba de B
                    dy2 = (a_rect.bottom - b_rect.top)   # empuje A abajo de B

                    # Elegir el menor en valor absoluto por eje cruzado
                    # (magnitud mínima de empuje para resolver solape)
                    push_x = dx1 if abs(dx1) < abs(dx2) else -dx2
                    push_y = dy1 if abs(dy1) < abs(dy2) else -dy2

                    if abs(push_x) < abs(push_y):
                        sep_ax = 'x'
                        sep_val = push_x
                    else:
                        sep_ax = 'y'
                        sep_val = push_y

                    # Política de empuje: si involucra al jugador, sólo mueve al NPC
                    move_a = not a_is_player
                    move_b = not b_is_player

                    # Repartir el empuje entre ambos si ambos se pueden mover, sino todo a uno
                    if move_a and move_b:
                        a_push = sep_val * -0.5
                        b_push = sep_val * 0.5
                    elif move_a:
                        a_push = -sep_val
                        b_push = 0
                    elif move_b:
                        a_push = 0
                        b_push = sep_val
                    else:
                        # Ninguno movible (dos jugadores o entidades no movibles): saltar
                        continue

                    # Intentar aplicar empuje respetando colisión con tiles
                    if a_push != 0:
                        if sep_ax == 'x':
                            test = a_rect.move(a_push, 0)
                        else:
                            test = a_rect.move(0, a_push)
                        if test.collidelist(tile_query(test)) == -1:
                            # Aplicar
                            pos_map[a_id].x += a_push if sep_ax == 'x' else 0
                            pos_map[a_id].y += a_push if sep_ax == 'y' else 0
                            feet_rects[a_id] = test
                            moved_any = True

                    if b_push != 0:
                        if sep_ax == 'x':
                            test = b_rect.move(b_push, 0)
                        else:
                            test = b_rect.move(0, b_push)
                        if test.collidelist(tile_query(test)) == -1:
                            pos_map[b_id].x += b_push if sep_ax == 'x' else 0
                            pos_map[b_id].y += b_push if sep_ax == 'y' else 0
                            feet_rects[b_id] = test
                            moved_any = True
            if not moved_any:
                break
