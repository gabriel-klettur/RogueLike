import types

import roguelike_ui.widgets.text_input.text_input as ti_mod
from roguelike_ui.widgets.text_input.text_input import TextInput


class FakeFont:
    def __init__(self, line_h: int = 16):
        self._h = line_h

    def get_height(self):
        return self._h


def test_text_input_rendering_single_calls_draw_singleline(monkeypatch):
    # Force caret visible deterministically
    monkeypatch.setattr(ti_mod, 'caret_on', lambda interval: True, raising=True)

    # Stub draw function to record inputs and return a fake rect
    calls = {}

    def fake_draw_singleline(surface, font, text, x, y, color, selection_start, selection_end, cursor, caret_visible):
        calls['args'] = {
            'text': text,
            'x': x,
            'y': y,
            'selection_start': selection_start,
            'selection_end': selection_end,
            'cursor': cursor,
            'caret_visible': caret_visible,
        }
        return types.SimpleNamespace(x=x, y=y, w=10, h=10)

    monkeypatch.setattr(ti_mod, '_draw_singleline', fake_draw_singleline, raising=True)

    font = FakeFont()
    ti = TextInput(font)
    ti.activate("abc", select_all=False)

    ti.draw(surface=None, x=5, y=7, color=(1, 2, 3))

    assert calls['args']['text'] == "abc"
    assert calls['args']['x'] == 5 and calls['args']['y'] == 7
    assert isinstance(ti.last_rect, types.SimpleNamespace)
    assert ti.last_draw_x == 5 and ti.last_draw_y == 7
