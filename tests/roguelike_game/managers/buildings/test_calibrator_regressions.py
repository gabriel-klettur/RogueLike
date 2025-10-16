from types import SimpleNamespace
from roguelike_game.managers.buildings.calibrator import BuildingsCalibrator


class Rect:
    def __init__(self):
        self.topleft = (0, 0)


def test_calibrator_does_not_touch_when_no_rect_or_missing_fields():
    b1 = SimpleNamespace(zone=None, rel_x=0, x=1, y=2, rect=Rect())
    b2 = SimpleNamespace(zone=1, rel_x=None, x=3, y=4, rect=Rect())
    b3 = SimpleNamespace(zone=1, rel_x=0, x=5, y=6)  # sin rect

    BuildingsCalibrator().recalibrate([b1, b2, b3])
    assert b1.rect.topleft == (0, 0)
    assert b2.rect.topleft == (0, 0)
