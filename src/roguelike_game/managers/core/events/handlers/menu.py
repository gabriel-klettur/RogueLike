import pygame
import roguelike_engine.config.config as config
import logging

logger = logging.getLogger(__name__)


def handle_menu(game, events) -> bool:
    if not getattr(getattr(game, 'menu', None), 'show_menu', False):
        return False
    for event in events:
        mode = getattr(game.menu, 'mode', '')
        if mode == 'load_list':
            if event.type in (pygame.KEYDOWN, pygame.MOUSEMOTION, pygame.MOUSEBUTTONDOWN, pygame.MOUSEWHEEL):
                game.menu.handle_input(event)
            continue
        try:
            if getattr(game.menu, '_press_start_active', False) and mode == 'start':
                if event.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEWHEEL):
                    try:
                        game.menu.handle_input(event)
                    except Exception:
                        pass
                    continue
        except Exception:
            pass
        if event.type == pygame.KEYDOWN:
            result = game.menu.handle_input(event)
            if result:
                game.menu.execute_menu_option(result, game.state)
        elif event.type == pygame.MOUSEMOTION:
            mx, my = event.pos
            try:
                if getattr(game.menu, '_press_start_active', False) and getattr(game.menu, 'mode', '') == 'start':
                    continue
            except Exception:
                pass
            try:
                options = game.menu.handler.get_options()
            except Exception:
                options = []
            panel_rect = getattr(game.menu.renderer, 'last_menu_panel_rect', None)
            if panel_rect is None:
                try:
                    width, height = game.menu.renderer._measure_menu(options)
                except Exception:
                    width, height = 320, 200
                screen_w, screen_h = game.screen.get_size()
                width = min(width, int(screen_w * 0.9))
                height = min(height, int(screen_h * 0.85))
                x = (screen_w - width) // 2
                y = (screen_h - height) // 2
                panel_rect = pygame.Rect(x, y, width, height)
            if panel_rect.collidepoint(mx, my) and options:
                pad_y = getattr(game.menu.renderer, 'padding_y', 24)
                line_h = getattr(game.menu.renderer, 'line_height', 36)
                gap = getattr(game.menu.renderer, 'item_gap', 12)
                block_h = line_h + gap
                inner_top = panel_rect.top + pad_y
                inner_y = my - inner_top
                rows_h = len(options) * line_h + max(0, (len(options) - 1)) * gap
                if 0 <= inner_y <= rows_h:
                    idx = int(inner_y // block_h)
                    idx = max(0, min(idx, len(options) - 1))
                    if game.menu.handler.selected != idx:
                        game.menu.handler.selected = idx
                        if getattr(config, 'DEBUG', False):
                            logger.debug("[Menu Hover] pos=(%s,%s) -> idx=%s", mx, my, idx)
        elif event.type == pygame.MOUSEBUTTONDOWN:
            mx, my = event.pos
            try:
                if getattr(game.menu, '_press_start_active', False) and getattr(game.menu, 'mode', '') == 'start':
                    continue
            except Exception:
                pass
            try:
                options = game.menu.handler.get_options()
            except Exception:
                options = []
            panel_rect = getattr(game.menu.renderer, 'last_menu_panel_rect', None)
            if panel_rect is None:
                try:
                    width, height = game.menu.renderer._measure_menu(options)
                except Exception:
                    width, height = 300, 200
                screen_w, screen_h = game.screen.get_size()
                width = min(width, int(screen_w * 0.9))
                height = min(height, int(screen_h * 0.85))
                x = (screen_w - width) // 2
                y = (screen_h - height) // 2
                panel_rect = pygame.Rect(x, y, width, height)
            if panel_rect.collidepoint(mx, my):
                pad_y = getattr(game.menu.renderer, 'padding_y', 24)
                line_h = getattr(game.menu.renderer, 'line_height', 36)
                gap = getattr(game.menu.renderer, 'item_gap', 12)
                block_h = line_h + gap
                inner_top = panel_rect.top + pad_y
                inner_y = my - inner_top
                total = len(options)
                rows_h = (total or 1) * line_h + max(0, (total - 1)) * gap
                if 0 <= inner_y <= rows_h:
                    idx = int(inner_y // block_h)
                    if getattr(config, 'DEBUG', False):
                        logger.debug("[Menu Click] pos=(%s,%s) panel=%s idx=%s total=%s", mx, my, panel_rect, idx, total)
                    if 0 <= idx < total:
                        game.menu.execute_menu_option(options[idx], game.state)
                    else:
                        if getattr(config, 'DEBUG', False):
                            logger.debug("[Menu Click] idx fuera de rango: %s", idx)
                else:
                    if getattr(config, 'DEBUG', False):
                        logger.debug("[Menu Click] fuera del área de items: inner_y=%s rows_h=%s", inner_y, rows_h)
    return True
