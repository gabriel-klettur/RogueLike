import dataclasses

from roguelike_game.ecs.components.rendering.flash_component import FlashComponent
from roguelike_game.ecs.components.rendering.grayscale_component import GrayscaleComponent


def test_flash_component_asdict():
    comp = FlashComponent(color=(9, 8, 7), duration=0.75)
    data = dataclasses.asdict(comp)
    assert data["color"] == (9, 8, 7)
    assert data["duration"] == 0.75
    assert isinstance(data["start_time"], float)


def test_grayscale_component_asdict():
    comp = GrayscaleComponent()
    data = dataclasses.asdict(comp)
    assert isinstance(data["start_time"], float)
