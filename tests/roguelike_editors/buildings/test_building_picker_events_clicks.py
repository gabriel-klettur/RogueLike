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


@pytest.mark.parametrize("y_offset", [2, 32, 62])
def test_click_back_icon_counts_once(editor_state, camera, monkeypatch, y_offset):
    # Seed state: has back history and one folder entry (so after going back, there would be a next cell)
    editor_state.history = ["parent"]
    editor_state.entries = [
        _Entry("FolderA", "A", True),
        _Entry("file.png", "file.png", False),
    ]

    handler, ctrl = _make_handler(monkeypatch, editor_state)

    # Click on cell (0,0) -> back icon; test near top/center/bottom
    tlx, tly = _cell_top_left(editor_state, col=0, row=0)
    pos = (tlx + 10, tly + y_offset)
    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 1, "pos": pos})
    ev_up = pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 1, "pos": pos})

    handler.handle(ev_down, camera)
    handler.handle(ev_up, camera)

    back_calls = [c for c in ctrl.calls if c[0] == "back"]
    cd_calls = [c for c in ctrl.calls if c[0] == "cd"]

    assert len(back_calls) == 1, "Back should be invoked exactly once for a full click (DOWN+UP)."
    assert len(cd_calls) == 0, "Mouse UP must not select a folder after going back."


@pytest.mark.parametrize("y_offset", [2, 32, 62])
def test_click_folder_counts_once(editor_state, camera, monkeypatch, y_offset):
    # Seed state: has back plus at least one folder right after it
    editor_state.history = ["parent"]
    editor_state.entries = [
        _Entry("FolderA", "A", True),  # visual index 1 (after back)
        _Entry("FolderB", "B", True),
    ]

    handler, ctrl = _make_handler(monkeypatch, editor_state)

    # With our metrics, cols >= 2, so FolderA at visual col=1, row=0
    tlx, tly = _cell_top_left(editor_state, col=1, row=0)
    pos = (tlx + 10, tly + y_offset)
    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 1, "pos": pos})
    ev_up = pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 1, "pos": pos})

    handler.handle(ev_down, camera)
    handler.handle(ev_up, camera)

    cd_calls = [c for c in ctrl.calls if c[0] == "cd"]

    assert len(cd_calls) == 1 and cd_calls[0][1] == "A", "Folder click should change_dir exactly once (on DOWN)." 


def _cell_top_left(ed, col: int, row: int) -> tuple[int, int]:
    m = ed.picker_internal_margin
    cw = ed.picker_cell_w
    ch = ed.picker_cell_h
    pad = ed.picker_padding
    gx = ed.picker_panel_rect.left + m
    gy = ed.picker_panel_rect.top + m
    x = gx + col * (cw + pad)
    y = gy + row * (ch + pad)
    return (int(x), int(y))


@pytest.mark.parametrize("y_offset", [2, 32, 62])  # near top, center, near bottom within a 64px cell
def test_click_file_anywhere_in_cell_selects(editor_state, camera, monkeypatch, y_offset):
    # Seed: no back, two entries; second is a file in col=1, row=0
    editor_state.history = []
    editor_state.entries = [
        _Entry("FolderA", "A", True),
        _Entry("file.png", "file.png", False),
    ]

    handler, ctrl = _make_handler(monkeypatch, editor_state)

    # Target cell: col=1, row=0 (file)
    tlx, tly = _cell_top_left(editor_state, col=1, row=0)
    pos = (tlx + 10, tly + y_offset)  # inside the cell bounds regardless of y_offset param

    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 1, "pos": pos})
    ev_up = pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 1, "pos": pos})

    handler.handle(ev_down, camera)
    # Selection should happen on DOWN for files
    assert getattr(editor_state, "selected_entry", None) is not None, "Click inside cell should select a file on DOWN"
    assert editor_state.selected_entry.path == "file.png"

    # UP should not change the selection or cause duplicates
    handler.handle(ev_up, camera)
    assert editor_state.selected_entry.path == "file.png"


def test_click_in_padding_does_not_select(editor_state, camera, monkeypatch):
    # Seed with simple grid: 2 columns, 1 row, with padding between columns
    editor_state.history = []
    editor_state.entries = [
        _Entry("fileA.png", "A.png", False),
        _Entry("fileB.png", "B.png", False),
    ]

    handler, ctrl = _make_handler(monkeypatch, editor_state)

    # Compute a point that lies strictly within the horizontal padding between col 0 and col 1
    m = editor_state.picker_internal_margin
    cw = editor_state.picker_cell_w
    ch = editor_state.picker_cell_h
    pad = editor_state.picker_padding
    gx = editor_state.picker_panel_rect.left + m
    gy = editor_state.picker_panel_rect.top + m

    # Padding band X range: [gx + cw, gx + cw + pad)
    pad_x = gx + cw + max(1, pad // 2)
    # Y inside the cell row (not in vertical padding)
    pad_y = gy + ch // 2
    pos = (int(pad_x), int(pad_y))

    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 1, "pos": pos})
    ev_up = pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 1, "pos": pos})

    handler.handle(ev_down, camera)
    handler.handle(ev_up, camera)

    # No selection should happen when clicking precisely in the padding between cells
    assert getattr(editor_state, "selected_entry", None) is None, "Click in padding should not select any entry"


def test_click_in_scrollbar_reserved_area_does_not_select(editor_state, camera, monkeypatch):
    # Configure a state that declares a scrollbar present; do not define track_rect to hit the fallback early-return
    editor_state.history = []
    # Many entries to conceptually require multiple rows (logic relies on flag, we set it explicitly)
    editor_state.entries = [_Entry(f"file{i}.png", f"f{i}.png", False) for i in range(8)]
    editor_state.picker_needs_scroll = True
    # Ensure at least two columns
    # Compute gx/gy/gw/gh similar to handler
    m = editor_state.picker_internal_margin
    cw = editor_state.picker_cell_w
    ch = editor_state.picker_cell_h
    pad = editor_state.picker_padding
    footer_h = editor_state.picker_footer_h
    panel_rect = editor_state.picker_panel_rect
    gx = panel_rect.left + m
    gy = panel_rect.top + m
    gw = max(0, panel_rect.w - 2 * m)
    gh = max(0, panel_rect.h - 2 * m - footer_h)
    sb_pad = 4
    sb_w = 10
    gw_effective = max(0, gw - (sb_w + sb_pad))
    # Pick a point inside the reserved scrollbar area to the right of grid
    x = gx + gw_effective + 1
    y = gy + ch // 2

    handler, ctrl = _make_handler(monkeypatch, editor_state)
    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 1, "pos": (int(x), int(y))})
    ev_up = pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 1, "pos": (int(x), int(y))})
    handler.handle(ev_down, camera)
    handler.handle(ev_up, camera)
    assert getattr(editor_state, "selected_entry", None) is None, "Click in scrollbar reserved area must not select"


def test_click_in_footer_area_does_not_select(editor_state, camera, monkeypatch):
    # Seed a simple state
    editor_state.history = []
    editor_state.entries = [_Entry("fileA.png", "A.png", False)]
    handler, ctrl = _make_handler(monkeypatch, editor_state)

    # Ensure there's an actual footer area by setting a positive footer height and
    # expanding the panel height accordingly (the original fixture uses footer_h=0).
    m = editor_state.picker_internal_margin
    panel_rect = editor_state.picker_panel_rect
    editor_state.picker_footer_h = 20
    # Increase panel height by the footer amount so handler math matches
    panel_rect.height += editor_state.picker_footer_h

    footer_h = editor_state.picker_footer_h
    gx = panel_rect.left + m
    gy = panel_rect.top + m
    # Compute grid height exactly as the handler does
    gh = max(0, panel_rect.height - 2 * m - footer_h)
    # y just below grid, inside the footer region
    y = gy + gh + min(footer_h - 1, 10)
    x = gx + 5

    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 1, "pos": (int(x), int(y))})
    ev_up = pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 1, "pos": (int(x), int(y))})
    handler.handle(ev_down, camera)
    handler.handle(ev_up, camera)
    assert getattr(editor_state, "selected_entry", None) is None, "Click in footer area must not select"
