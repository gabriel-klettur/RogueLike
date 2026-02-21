import pygame
from types import SimpleNamespace

from roguelike_editors.entities.entities_assets_picker_panel.entities_assets_picker_panel_events import (
    EntitiesAssetsPickerPanelEventHandler,
)



def _make_picker_controller_stub():
    calls = {"hide": 0, "navigate": 0, "chosen": []}

    # Model with filesystem picker-like API
    fs_model = SimpleNamespace(selected=None)

    def on_asset_chosen(key, path):
        calls["chosen"].append((key, path))

    model = SimpleNamespace(
        pos=(0, 0),
        width=200,
        panel_rect=pygame.Rect(0, 0, 200, 200),
        fs_model=fs_model,
        key="asset_idle_n",
        on_asset_chosen=on_asset_chosen,
        label_provider=None,
    )

    # View with entries list and a dummy surface in fs_view.panel
    surf = pygame.Surface((200, 200))
    fs_view = SimpleNamespace(panel=SimpleNamespace(surface=surf))
    view = SimpleNamespace(fs_view=fs_view, entry_rects=[])

    def hide():
        calls["hide"] += 1

    controller = SimpleNamespace(model=model, view=view, hide=hide)

    return controller, calls


def test_escape_hides_picker():
    controller, calls = _make_picker_controller_stub()
    handler = EntitiesAssetsPickerPanelEventHandler(controller)

    ev = SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_ESCAPE)
    consumed = handler.handle(ev)

    assert consumed is True
    assert calls["hide"] == 1


def test_click_outside_hides_picker():
    controller, calls = _make_picker_controller_stub()
    handler = EntitiesAssetsPickerPanelEventHandler(controller)

    # Click far outside panel_rect
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(300, 300))
    consumed = handler.handle(ev)

    assert consumed is True
    assert calls["hide"] == 1


def test_click_inside_but_not_on_entry_is_consumed():
    controller, calls = _make_picker_controller_stub()
    handler = EntitiesAssetsPickerPanelEventHandler(controller)

    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(100, 100))
    consumed = handler.handle(ev)

    assert consumed is True
    assert calls["hide"] == 0


def test_single_click_highlights_file_and_dir():
    controller, calls = _make_picker_controller_stub()
    handler = EntitiesAssetsPickerPanelEventHandler(controller)

    # Two entries: one dir, one file
    dir_entry = ("sprites", "/assets/sprites", True)
    file_entry = ("idle.png", "/assets/sprites/idle.png", False)
    controller.view.entry_rects = [
        (pygame.Rect(10, 10, 100, 20), dir_entry, 0),
        (pygame.Rect(10, 40, 100, 20), file_entry, 1),
    ]

    # Click dir (single click): should select only
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(15, 15))
    consumed = handler.handle(ev)
    assert consumed is True
    assert controller.model.fs_model.selected == dir_entry[1]

    # Click file (single click): should select only
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(15, 45))
    consumed = handler.handle(ev)
    assert consumed is True
    assert controller.model.fs_model.selected == file_entry[1]


def test_double_click_dir_navigates_and_double_click_file_invokes_callback():
    controller, calls = _make_picker_controller_stub()
    handler = EntitiesAssetsPickerPanelEventHandler(controller)

    # Stub double click detector to always return True
    handler.dc_detector = SimpleNamespace(is_double_click=lambda idx: True)

    dir_entry = ("sprites", "/assets/sprites", True)
    file_entry = ("idle.png", "/assets/sprites/idle.png", False)

    # Give fs_model a navigate method we can assert via side effect
    nav_calls = {"count": 0, "idxs": []}

    def navigate(idx):
        nav_calls["count"] += 1
        nav_calls["idxs"].append(idx)

    controller.model.fs_model.navigate = navigate

    controller.view.entry_rects = [
        (pygame.Rect(10, 10, 100, 20), dir_entry, 0),
        (pygame.Rect(10, 40, 100, 20), file_entry, 1),
    ]

    # Double-click on dir
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(15, 15))
    consumed = handler.handle(ev)
    assert consumed is True
    assert nav_calls["count"] == 1 and nav_calls["idxs"] == [0]

    # Double-click on file triggers on_asset_chosen
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(15, 45))
    consumed = handler.handle(ev)
    assert consumed is True
    assert calls["chosen"] == [(controller.model.key, "/assets/sprites/idle.png")]
