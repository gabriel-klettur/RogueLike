from types import SimpleNamespace
from roguelike_game.managers.buildings.calibrator import BuildingsCalibrator


class Rect:
    def __init__(self):
        self.topleft = (0, 0)


def test_calibrator_sets_rect_topleft_from_abs_xy():
    b = SimpleNamespace(zone=1, rel_x=0, x=10, y=20, rect=Rect())
    BuildingsCalibrator().recalibrate([b])
    assert b.rect.topleft == (10, 20)
