import types

import roguelike_ui.widgets.text_input.text_input as ti_mod
from roguelike_ui.widgets.text_input.text_input import TextInput


class FakeFont:
    def __init__(self, line_h: int = 16):
        self._h = line_h

    def get_height(self):
        return self._h


def test_text_input_rendering_wrapped_calls_draw_wrapped_block(monkeypatch):
    # Deterministic caret
    monkeypatch.setattr(ti_mod, 'caret_on', lambda interval: True, raising=True)

    # Stub wrapped draw to record and return a last_rect and metadata
    calls = {}

    def fake_draw_wrapped_block(surface, font, text, x, y, max_width, color, align_bottom, selection_start, selection_end, cursor, caret_visible):
        calls['args'] = {
            'text': text,
            'x': x,
            'y': y,
            'max_width': max_width,
            'align_bottom': align_bottom,
            'selection_start': selection_start,
            'selection_end': selection_end,
            'cursor': cursor,
            'caret_visible': caret_visible,
        }
        # Return (last_rect, lines, start_y, line_h)
        last_rect = types.SimpleNamespace(x=x, y=y, w=10, h=10)
        lines = [{'text': 'a', 'start': 0, 'end': 1}]
        start_y = y
        line_h = font.get_height()
        return last_rect, lines, start_y, line_h

    monkeypatch.setattr(ti_mod, '_draw_wrapped_block', fake_draw_wrapped_block, raising=True)

    font = FakeFont()
    ti = TextInput(font)
    ti.activate("abcdef", select_all=True)

    ti.draw_wrapped(surface=None, x=3, y=4, max_width=123, color=(9, 9, 9))

    assert calls['args']['text'] == "abcdef"
    assert calls['args']['max_width'] == 123
    assert ti._wrap_lines is not None and isinstance(ti.last_rect, types.SimpleNamespace)
