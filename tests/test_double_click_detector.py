import pytest
import time as real_time
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector

@ pytest.mark.parametrize("interval_ms, times, expected", [
    (500, [0.0, 0.3, 1.0], [False, True, False]),
    (200, [0.0, 0.1, 0.5], [False, True, False]),
])
def test_double_click(monkeypatch, interval_ms, times, expected):
    # Simulate time.time calls
    seq = times.copy()
    def fake_time():
        return seq.pop(0)
    # Patch time.time in the module
    import roguelike_ui.widgets.double_click_detector as m
    monkeypatch.setattr(m.time, 'time', fake_time)
    dcd = DoubleClickDetector(interval_ms=interval_ms)
    results = [dcd.is_double_click('key'), dcd.is_double_click('key'), dcd.is_double_click('key')]
    assert results == expected
