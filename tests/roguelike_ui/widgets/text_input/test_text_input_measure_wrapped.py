import types

from roguelike_ui.widgets.text_input.text_input import TextInput


class FakeFont:
    def __init__(self, w_per_char: int = 8, line_h: int = 16):
        self._w = w_per_char
        self._h = line_h

    def size(self, text: str):
        # width proportional to characters; height not used in wrap decision
        return (len(text) * self._w, self._h)

    def get_linesize(self):
        return self._h

    def get_height(self):
        return self._h


def test_measure_wrapped_updates_cache_and_dimensions():
    font = FakeFont(w_per_char=10, line_h=12)
    ti = TextInput(font)
    ti.text = "hello world this is a test"

    # Max width 50px -> ~5 chars per line -> expect multiple lines
    n_lines, total_h = ti.measure_wrapped(max_width=50)

    assert n_lines >= 3
    assert total_h == n_lines * font.get_linesize()

    # Ensure internal cache updated for interactions
    assert ti._wrap_lines is not None
    assert ti._wrap_max_w == 50
