import dataclasses
import pygame

from roguelike_engine.buildings.rendering.parts import RenderablePart


def test_renderable_part_schema_and_callable(pygame_init):
    # pygame.Surface is not picklable; validate structure and callable contract instead.
    surf = pygame.Surface((4, 4), flags=pygame.SRCALPHA)

    def noop(screen, camera):
        return None

    part = RenderablePart(x=1, y=2, z=3, image=surf, render=noop)

    # Dataclass with slots and stable field names (schema compatibility test)
    assert dataclasses.is_dataclass(RenderablePart)
    assert hasattr(RenderablePart, "__slots__")
    field_names = [f.name for f in dataclasses.fields(RenderablePart)]
    assert field_names == ["x", "y", "z", "image", "render"]

    # Contract: render is callable and receives (screen, camera)
    assert callable(part.render)
    part.render(pygame.Surface((1, 1), flags=pygame.SRCALPHA), None)
