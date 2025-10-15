from roguelike_ui.widgets.text_input.text_input import TextInput


class FakeFont:
    def __init__(self, line_h: int = 16):
        self._h = line_h

    def get_height(self):
        return self._h


def test_text_input_selection_activate_select_all():
    font = FakeFont()
    ti = TextInput(font)
    ti.activate(initial_text="hello", select_all=True)

    assert ti.active is True
    assert ti.text == "hello"
    assert ti.cursor == len("hello")
    assert ti.selection_start == 0
    assert ti.selection_end == len("hello")
