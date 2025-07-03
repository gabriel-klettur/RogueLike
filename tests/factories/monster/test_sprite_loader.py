import pytest
from roguelike_game.factories.monster.sprite_loader import create_sprite_component, create_patrol_components
from roguelike_game.factories.monster.cache import _SPRITE_SURFACES, _DEATH_SURFACES

class DummySurface:
    def __init__(self):
        self.images = []
    def copy(self):
        return self

@pytest.fixture(autouse=True)
def clear_surfaces():
    _SPRITE_SURFACES.clear()
    _DEATH_SURFACES.clear()

def test_create_sprite_component():
    dummy = DummySurface()
    _SPRITE_SURFACES["d1"] = {"down": dummy}
    _DEATH_SURFACES["d1"] = "death"
    sprite, death_img = create_sprite_component("d1")
    assert hasattr(sprite, "image")
    assert sprite.image is dummy
    assert death_img == "death"

def test_create_patrol_components():
    dummy = DummySurface()
    _SPRITE_SURFACES["d2"] = {"down": dummy, "up": dummy}
    cfg = {"speed": 4}
    patrol, movement, animator = create_patrol_components(1, 2, "d2", cfg)
    assert movement.speed == 4
    assert hasattr(patrol, "waypoints")
    assert animator.current_state == "down"
