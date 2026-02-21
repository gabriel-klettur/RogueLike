from types import SimpleNamespace
from roguelike_game.managers.buildings.calibrator import BuildingsCalibrator


def test_calibrator_skips_when_missing_zone_or_rel_x():
    # Falta zone
    b1 = SimpleNamespace(zone=None, rel_x=0, x=1, y=2, rect=type("R", (), {"topleft": (0, 0)})())
    # Falta rel_x
    b2 = SimpleNamespace(zone=1, rel_x=None, x=3, y=4, rect=type("R", (), {"topleft": (0, 0)})())
    # Falta rect
    b3 = SimpleNamespace(zone=1, rel_x=0, x=5, y=6)

    BuildingsCalibrator().recalibrate([b1, b2, b3])
    # No debe lanzar excepción y no modifica topleft de b1/b2
    assert getattr(b1, "rect").topleft == (0, 0)
    assert getattr(b2, "rect").topleft == (0, 0)
