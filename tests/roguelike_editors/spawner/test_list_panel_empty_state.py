import pytest


def test_list_panel_renders_when_empty(monkeypatch):
    # Import late to avoid pygame import at collection
    from roguelike_editors.spawner.common.list_panel_view import ListPanelView

    class DummyFont:
        def __init__(self, *a, **k):
            pass
        def render(self, text, antialias, color):
            return DummySurface((len(str(text)) + 1, 10))
        def get_linesize(self):
            return 12
        def size(self, s):
            return (len(str(s)), 12)

    class DummySurface:
        def __init__(self, size=(100, 100)):
            self._size = size
        def get_rect(self):
            class R:
                def __init__(self, w, h):
                    self.x = 0; self.y = 0; self.w = w; self.h = h
                def __iter__(self):
                    return iter((self.x, self.y, self.w, self.h))
            return R(*self._size)
        def fill(self, *a, **k):
            pass
        def blit(self, *a, **k):
            pass

    class DummyRect:
        def __init__(self, x, y, w, h):
            self.left, self.top, self.width, self.height = x, y, w, h
            self.x, self.y = x, y
        def move(self, pos):
            return DummyRect(self.left + pos[0], self.top + pos[1], self.width, self.height)

    class DummyPygame:
        SRCALPHA = 1
        MOUSEBUTTONDOWN = 1
        K_m = 109
        def __init__(self):
            self._mouse_pos = (0, 0)
        def Surface(self, size, *a, **k):
            return DummySurface(size)
        def Rect(self, x, y, w, h):
            return DummyRect(x, y, w, h)
        def draw(self):
            return self
        def rect(self, *a, **k):
            pass
        def mouse(self):
            return self
        def get_pos(self):
            return self._mouse_pos
        def font(self):
            return self
        def SysFont(self, *a, **k):
            return DummyFont()

    dummy = DummyPygame()

    # Monkeypatch pygame in module scope where it's imported
    monkeypatch.setitem(__import__("sys").modules, 'pygame', dummy)

    model = type('M', (), {
        'visible': True,
        'title': 'Templates',
        'panel_width': 320,
        'header_height': 28,
        'row_height': 20,
        'visible_rows': 11,
        'items': [],
        'empty_text': 'No templates',
        'empty_hint': 'Click + to create one',
    })()

    screen = DummySurface((800, 600))

    view = ListPanelView()
    rect = view.render(model, screen, anchor=(10, 10))

    assert rect is not None
    assert rect.left == 10 and rect.top == 10
    # Ensure we recorded a panel rect even with empty items
    assert view.panel_rect is not None
