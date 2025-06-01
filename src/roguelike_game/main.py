# Path: src/roguelike_game/main.py
import pygame
from collections import defaultdict

from roguelike_engine.config.config import SCREEN_WIDTH, SCREEN_HEIGHT
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.config_player import RENDERED_SPRITE_SIZE

from roguelike_game.game.game import Game

def init_debug():
    pygame.mouse.set_visible(True)
    return defaultdict(list)

def main():
    pygame.init()
    screen = pygame.display.set_mode(
        (SCREEN_WIDTH, SCREEN_HEIGHT),
        pygame.HWSURFACE | pygame.DOUBLEBUF | pygame.RESIZABLE
    )
    pygame.display.set_caption("Roguelike")

    # -------- Inicializar performance_log --------
    performance_log = init_debug()

    # Creamos el juego pasándole el log
    game = Game(
        screen,
        perf_log        = performance_log,        
        map_name        = None,
        loading_bg      = "ui/background_ini.png"
    )
    if not hasattr(game, 'state'):
        raise RuntimeError("Game state not initialized properly!")

    try:
        game.run()  
    except Exception as e:
        print(f"An error occurred: {e}")
        raise
    finally:
        # Guardar posición del jugador antes de cerrar
        try:
            eid = game.ecs.ecs_world.player_entity
            pos = game.ecs.ecs_world.components['Position'][eid]
            # Calcular coords de tile usando centro del collider 'feet'
            w, h = RENDERED_SPRITE_SIZE
            fh = h // 4
            half_fh = fh // 2
            feet_cx = pos.x + w // 2
            feet_cy = pos.y + (h - half_fh)
            tx = int(feet_cx // TILE_SIZE)
            ty = int(feet_cy // TILE_SIZE)
            game.map.spawn_player((tx, ty))
            # Registrar mapa actual en WorldManager
            game.world.maps[game.map.name] = game.map
            game.world.current_level = game.map.name
            game.world.save_world()
        except Exception:
            pass
        pygame.quit()
    


if __name__ == "__main__":
    main()