from types import SimpleNamespace
from roguelike_game.managers.buildings.calibrator import BuildingsCalibrator


class Rect:
    def __init__(self):
        self.topleft = (0, 0)


def test_calibrator_duck_typing_acceptance():
    # Acepta cualquier objeto con atributos esperados
    obj = SimpleNamespace(zone=1, rel_x=0, x=7, y=9, rect=Rect())
    BuildingsCalibrator().recalibrate([obj])
    assert obj.rect.topleft == (7, 9)
