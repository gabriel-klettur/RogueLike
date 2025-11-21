import pygame
from roguelike_game.config.hot_reload import reload_all_game_data_and_code
import logging

logger = logging.getLogger(__name__)


def handle_hot_reload_anywhere(game, events) -> bool:
    for event in events:
        if event.type == pygame.KEYDOWN:
            try:
                reload_key = game.input_config.get_key('reload_data')
            except Exception:
                reload_key = pygame.K_F1
            if event.key in (reload_key, pygame.K_F1):
                try:
                    mods = pygame.key.get_mods()
                except Exception:
                    mods = 0
                # Only reload on F1 with NO modifiers (no Alt/Ctrl/Shift)
                if not bool(mods & (pygame.KMOD_LALT | pygame.KMOD_RALT | pygame.KMOD_LCTRL | pygame.KMOD_RCTRL | pygame.KMOD_LSHIFT | pygame.KMOD_RSHIFT)):
                    try:
                        summary = reload_all_game_data_and_code(game)
                        try:
                            total_groups = sum(1 for _ in (summary or {}).items())
                            total_items = sum(int(v or 0) for v in (summary or {}).values())
                            logger.info("[core.events] reload_data: groups=%d total_items=%d summary=%s", int(total_groups), int(total_items), summary)
                        except Exception:
                            logger.info("[core.events] reload_data summary: %s", summary)
                    except Exception:
                        logger.exception("[core.events] reload_data binding failed")
                    return True
    return False
