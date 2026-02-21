import pygame
from roguelike_editors.buildings.utils.save_buildings_to_json import save_buildings_split
from roguelike_engine.config.map_config import global_map_settings


def handle_quit(game) -> bool:
    if pygame.event.peek(pygame.QUIT):
        pygame.event.get(pygame.QUIT)
        try:
            be = getattr(game, 'buildings_editor', None)
            bm = getattr(game, 'buildings', None)
            if be and bm and hasattr(be, 'colliders') and hasattr(be.colliders, 'events'):
                be.colliders.events._save_collisions(bm.buildings, force=True)
        except Exception:
            pass
        try:
            bm = getattr(game, 'buildings', None)
            if bm and hasattr(bm, 'buildings'):
                save_buildings_split(
                    bm.buildings,
                    z_state=getattr(game.state, 'z_state', None),
                    zone_offsets=getattr(global_map_settings, 'zone_offsets', None),
                )
        except Exception:
            pass
        game.state.running = False
        return True
    return False
