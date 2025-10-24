import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
import roguelike_engine.config.config as config


def draw_debug_rect(screen, camera, rect, color=(255, 255, 255), width=1):
    # Respect global DEBUG and optional feature flag for building collision overlay
    if not getattr(config, 'DEBUG', False):
        return
    if not getattr(config, 'DEBUG_BUILDING_COLLISION', True):
        return
    scaled_rect = pygame.Rect(camera.apply(rect.topleft), camera.scale(rect.size))
    pygame.draw.rect(screen, color, scaled_rect, width)


def draw_debug_mask_outline(screen, camera, surface, origin, color=(255, 0, 0), width=1):
    if not config.DEBUG:
        return
    mask = pygame.mask.from_surface(surface)
    outline = mask.outline()
    pts = [camera.apply((origin[0] + x, origin[1] + y)) for x, y in outline]
    if len(pts) >= 3:
        pygame.draw.polygon(screen, color, pts, width)


def draw_zone_border(screen, camera, tiles, zone_name, colors, border_width):
    if not config.DEBUG or not tiles:
        return
    xs = [t.x for t in tiles]
    ys = [t.y for t in tiles]
    min_x, max_x = min(xs), max(xs) + TILE_SIZE
    min_y, max_y = min(ys), max(ys) + TILE_SIZE
    top_left = camera.apply((min_x, min_y))
    bottom_right = camera.apply((max_x, max_y))
    w = bottom_right[0] - top_left[0]
    h = bottom_right[1] - top_left[1]
    rect = pygame.Rect(top_left, (w, h))
    color = colors.get(zone_name, (200, 200, 200))
    pygame.draw.rect(screen, color, rect, border_width)
