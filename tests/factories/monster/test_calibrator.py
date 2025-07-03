import pytest
import roguelike_game.factories.monster.calibrator as mod
from roguelike_game.factories.monster.calibrator import calibrate_tile_position, MONSTER_DEFS

class DummySprite:
    class image:
        @staticmethod
        def get_size():
            return (8, 16)

@pytest.fixture(autouse=True)
def patch_calibrator(monkeypatch):
    # Monkeypatch calculate_position to a simple function
    import roguelike_game.factories.monster.physics as phys
    monkeypatch.setattr(phys, "calculate_position", lambda tx, ty, cfg, sprite: (tx * 10, ty * 20))

def test_calibrate_tile_position(monkeypatch):
    # Setup dummy config and sprite
    fake_cfg = {"scale": 1.0}
    monkeypatch.setitem(MONSTER_DEFS, "dummy", fake_cfg)
    monkeypatch.setattr(mod, "create_sprite_component", lambda mtype: (DummySprite(), None))
    x, y = calibrate_tile_position(2, 3, "dummy")
    assert (x, y) == (20, 60)
