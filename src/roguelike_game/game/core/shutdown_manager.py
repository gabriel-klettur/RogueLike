
# Path: src/roguelike_game/game/core/shutdown_manager.py
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.factories.player.config import RENDERED_SPRITE_SIZE

class ShutdownManager:
    """
    Se encarga de todo lo necesario antes de cerrar el juego:
     - Guardar posición del jugador en el mapa actual.
     - Actualizar WorldManager (maps, current_level, etc.).
     - Serializar y guardar el mundo en disco.
    """
    def __init__(self, game):
        self.game = game

    def shutdown(self):
        g = self.game
        try:
            # 1) Obtener la entidad del jugador
            eid = g.ecs.ecs_world.player_entity
            pos = g.ecs.ecs_world.components["Position"][eid]

            # 2) Calcular coordenadas de tile usando el centro del collider 'feet'
            w, h = RENDERED_SPRITE_SIZE
            fh = h // 4
            half_fh = fh // 2
            feet_cx = pos.x + w//2
            feet_cy = pos.y + (h - half_fh)

            tx = int(feet_cx // TILE_SIZE)
            ty = int(feet_cy // TILE_SIZE)

            # 3) Hacer spawn del jugador en el mapa (para que guarde la nueva posición)
            g.map.spawn_player((tx, ty))

            # 4) Actulizar WorldManager
            g.world.maps[g.map.name] = g.map
            g.world.current_level     = g.map.name

            # 5) Salvar el mundo en disco
            g.world.save_world()

        except Exception as exc:
            print(f"[WARN] No se pudo guardar al cerrar: {exc}")