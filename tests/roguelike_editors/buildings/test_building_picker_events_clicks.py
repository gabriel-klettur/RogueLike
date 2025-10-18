import types
import pygame
import pytest


class _Entry:
    def __init__(self, name: str, path: str, is_dir: bool):
        self.name = name
        self.path = path
        self.is_dir = is_dir


class _CtrlSpy:
    def __init__(self):
        self.calls = []

    def go_back(self):
        self.calls.append(("back",))

    def change_dir(self, path: str):
        self.calls.append(("cd", path))

    # Unused in these tests but present in handler API
    def start_drag(self, entry):
        self.calls.append(("drag", getattr(entry, "path", None)))

    def place_building(self, pos, cam, buildings):
        self.calls.append(("place", pos))


@pytest.fixture()
def editor_state():
    # Minimal editor state with picker metrics injected (as the view would)
    ed = types.SimpleNamespace()
    ed.entries = []
    ed.history = []

    # Panel and grid metrics (no scrollbar)
    m = 8
    cw = 64
    ch = 64
    pad = 8
    footer_h = 0

    ed.picker_panel_rect = pygame.Rect(10, 10, 10 + 2 * (cw + pad) + 2 * m, 10 + (ch + pad) + 2 * m + footer_h)
    ed.picker_internal_margin = m
    ed.picker_cell_w = cw
    ed.picker_cell_h = ch
    ed.picker_padding = pad
    ed.picker_footer_h = footer_h
    ed.picker_max_columns = None
    ed.picker_visible_rows = 1
    ed.picker_needs_scroll = False
    ed.picker_scroll_row = 0
    return ed


def _center_of_cell(ed, col: int, row: int) -> tuple[int, int]:
    m = ed.picker_internal_margin
    cw = ed.picker_cell_w
    ch = ed.picker_cell_h
    pad = ed.picker_padding
    gx = ed.picker_panel_rect.left + m
    gy = ed.picker_panel_rect.top + m
    x = gx + col * (cw + pad) + cw // 2
    y = gy + row * (ch + pad) + ch // 2
    return (int(x), int(y))


def _make_handler(monkeypatch, editor_state):
    import roguelike_editors.buildings.buildings_picker.building_picker_events as bpe

    # Avoid disk IO when loading icons
    monkeypatch.setattr(bpe, "load_image", lambda *a, **k: pygame.Surface((16, 16), pygame.SRCALPHA), raising=True)

    ctrl = _CtrlSpy()
    handler = bpe.BuildingPickerEventHandler(editor_state, ctrl, buildings=[])
    return handler, ctrl


def test_click_back_icon_counts_once(editor_state, camera, monkeypatch):
    # Seed state: has back history and one folder entry (so after going back, there would be a next cell)
    editor_state.history = ["parent"]
    editor_state.entries = [
        _Entry("FolderA", "A", True),
        _Entry("file.png", "file.png", False),
    ]

    handler, ctrl = _make_handler(monkeypatch, editor_state)

    # Click on cell (0,0) -> back icon
    pos = _center_of_cell(editor_state, col=0, row=0)
    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 1, "pos": pos})
    ev_up = pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 1, "pos": pos})

    handler.handle(ev_down, camera)
    handler.handle(ev_up, camera)

    back_calls = [c for c in ctrl.calls if c[0] == "back"]
    cd_calls = [c for c in ctrl.calls if c[0] == "cd"]

    assert len(back_calls) == 1, "Back should be invoked exactly once for a full click (DOWN+UP)."
    assert len(cd_calls) == 0, "Mouse UP must not select a folder after going back."


def test_click_folder_counts_once(editor_state, camera, monkeypatch):
    # Seed state: has back plus at least one folder right after it
    editor_state.history = ["parent"]
    editor_state.entries = [
        _Entry("FolderA", "A", True),  # visual index 1 (after back)
        _Entry("FolderB", "B", True),
    ]

    handler, ctrl = _make_handler(monkeypatch, editor_state)

    # With our metrics, cols >= 2, so FolderA at visual col=1, row=0
    pos = _center_of_cell(editor_state, col=1, row=0)
    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 1, "pos": pos})
    ev_up = pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 1, "pos": pos})

    handler.handle(ev_down, camera)
    handler.handle(ev_up, camera)

    cd_calls = [c for c in ctrl.calls if c[0] == "cd"]

    assert len(cd_calls) == 1 and cd_calls[0][1] == "A", "Folder click should change_dir exactly once (on DOWN)." 
