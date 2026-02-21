from roguelike_ui.widgets.text_input.text_input import TextInput


class FakeFont:
    def __init__(self, w_per_char: int = 8, line_h: int = 16):
        self._w = w_per_char
        self._h = line_h

    def size(self, text: str):
        return (len(text) * self._w, self._h)

    def get_linesize(self):
        return self._h

    def get_height(self):
        return self._h


def test_text_input_wrapping_basic():
    font = FakeFont(w_per_char=10, line_h=12)
    ti = TextInput(font)
    ti.text = "lorem ipsum dolor sit amet"

    # Width ~20 chars max -> should wrap into multiple lines
    n_lines, total_h = ti.measure_wrapped(max_width=200)

    assert n_lines >= 2
    assert total_h == n_lines * font.get_linesize()
