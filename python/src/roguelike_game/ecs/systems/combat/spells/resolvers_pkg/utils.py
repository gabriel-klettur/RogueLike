import pygame


def get_entity_center(world, entity):
    pos_cmp = world.components['Position'][entity]
    cx, cy = pos_cmp.x, pos_cmp.y
    sprite_cmp = world.components.get('Sprite', {}).get(entity)
    if sprite_cmp:
        try:
            w, h = sprite_cmp.image.get_size()
            cx += w / 2
            cy += h / 2
        except Exception:
            pass
    return cx, cy


def mouse_world(camera):
    mx, my = pygame.mouse.get_pos()
    if camera:
        zoom = getattr(camera, 'zoom', 1.0)
        ox = getattr(camera, 'offset_x', 0)
        oy = getattr(camera, 'offset_y', 0)
        return mx / zoom + ox, my / zoom + oy
    return mx, my


def direction_from_to(x0, y0, x1, y1):
    dx, dy = x1 - x0, y1 - y0
    length = (dx * dx + dy * dy) ** 0.5 or 1.0
    return dx / length, dy / length, length


def spawn_at_offset(cx, cy, dir_x, dir_y, offset):
    return cx + dir_x * offset, cy + dir_y * offset
