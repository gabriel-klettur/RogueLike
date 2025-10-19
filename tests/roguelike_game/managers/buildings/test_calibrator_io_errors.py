import pytest
from types import SimpleNamespace
from roguelike_game.managers.buildings.calibrator import BuildingsCalibrator


class BadRect:
    def __init__(self):
        object.__setattr__(self, "ok", True)

    def __setattr__(self, name, value):
        if name == "topleft":
            raise RuntimeError("fail")
        object.__setattr__(self, name, value)


def test_calibrator_bubbles_errors():
    b = SimpleNamespace(zone=1, rel_x=0, x=1, y=2, rect=BadRect())
    with pytest.raises(RuntimeError):
        BuildingsCalibrator().recalibrate([b])
