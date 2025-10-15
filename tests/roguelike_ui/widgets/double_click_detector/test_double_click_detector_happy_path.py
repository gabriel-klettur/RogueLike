import types

import roguelike_ui.widgets.double_click_detector as mod


def test_double_click_within_interval(monkeypatch):
    # Fake time source
    class Clock:
        def __init__(self):
            self.t = 1000.0
        def time(self):
            return self.t
        def advance(self, s):
            self.t += s

    clk = Clock()
    monkeypatch.setattr(mod.time, 'time', clk.time, raising=True)

    d = mod.DoubleClickDetector(interval_ms=500)

    # First click at t=1000s -> not double
    assert d.is_double_click('LMB') is False
    # Advance 0.2s -> within 500 ms
    clk.advance(0.2)
    assert d.is_double_click('LMB') is True

    # After detection, state resets; next single click is not double
    assert d.is_double_click('LMB') is False
