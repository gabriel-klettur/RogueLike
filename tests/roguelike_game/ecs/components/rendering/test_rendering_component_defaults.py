import time
import dataclasses

from roguelike_game.ecs.components.rendering.flash_component import FlashComponent
from roguelike_game.ecs.components.rendering.grayscale_component import GrayscaleComponent


def test_flash_component_defaults_start_time_precision():
    before = time.time()
    comp = FlashComponent(color=(255, 255, 255), duration=0.25)
    after = time.time()
    assert comp.color == (255, 255, 255)
    assert comp.duration == 0.25
    # start_time set within the creation window
    assert before <= comp.start_time <= after


def test_grayscale_component_default_start_time_is_recent():
    t0 = time.time()
    comp = GrayscaleComponent()
    assert t0 <= comp.start_time <= time.time()


def test_flash_component_dataclass_serialization():
    comp = FlashComponent(color=(1, 2, 3), duration=1.0)
    data = dataclasses.asdict(comp)
    assert data["color"] == (1, 2, 3)
    assert data["duration"] == 1.0
    assert isinstance(data["start_time"], float)
