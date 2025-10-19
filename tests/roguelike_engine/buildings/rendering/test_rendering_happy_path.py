import pygame

from roguelike_engine.buildings.building_view import BuildingView
from roguelike_engine.buildings.building_model import BuildingModel


def _strip_alpha(color):
    return (color[0], color[1], color[2])


def test_render_top_and_bottom_colors(pygame_init, patch_loader, screen, fake_camera):
    patch_loader(size=(10, 10))
    m = BuildingModel(rel_x=5, rel_y=7, image_path="dummy.png", solid=True, split_ratio=0.5)
    # Craft an image with red top and green bottom
    surf = pygame.Surface((10, 10), flags=pygame.SRCALPHA)
    surf.fill((255, 0, 0))
    pygame.draw.rect(surf, (0, 255, 0), pygame.Rect(0, 5, 10, 5))
    m.image = surf

    v = BuildingView(m, fake_camera)

    # Render top
    v.render_part(screen, top=True)
    assert _strip_alpha(screen.get_at((5, 7))) == (255, 0, 0)

    # Clear screen and render bottom
    screen.fill((0, 0, 0, 0))
    v.render_part(screen, top=False)
    # Bottom starts 5px below
    assert _strip_alpha(screen.get_at((5, 7 + 5))) == (0, 255, 0)
