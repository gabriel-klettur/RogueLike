import time
from types import SimpleNamespace
from roguelike_game.managers.buildings.calibrator import BuildingsCalibrator


class Rect:
    def __init__(self):
        self.topleft = (0, 0)


def test_calibrator_perf_budget():
    buildings = [SimpleNamespace(zone=1, rel_x=0, x=i, y=i, rect=Rect()) for i in range(500)]
    t0 = time.perf_counter()
    BuildingsCalibrator().recalibrate(buildings)
    dt = time.perf_counter() - t0
    assert dt < 0.05
