import time

from roguelike_ui.widgets.double_click_detector import DoubleClickDetector


def test_double_click_detector_interval_and_key_change(monkeypatch):
    # Control time progression
    now = [1000]
    monkeypatch.setattr(time, 'time', lambda: now[0] / 1000.0, raising=True)

    dcd = DoubleClickDetector(interval_ms=300)

    # First click (key 'A') at t=1000ms -> not double
    assert dcd.is_double_click('A') is False

    # Second click after 200ms with same key -> double
    now[0] += 200
    assert dcd.is_double_click('A') is True

    # Next single click with different key resets sequence
    assert dcd.is_double_click('B') is False

    # Second click after interval exceeded -> not double
    now[0] += 301
    assert dcd.is_double_click('B') is False
