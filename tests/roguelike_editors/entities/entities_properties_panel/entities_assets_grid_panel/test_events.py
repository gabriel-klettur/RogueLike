import pygame
from types import SimpleNamespace

from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_events import (
    AssetsGridPanelEventHandler,
)
import roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_events as grid_events_module



def _make_controller_stub():
    # Model with grid entries
    rect1 = pygame.Rect(0, 0, 32, 32)
    rect2 = pygame.Rect(40, 0, 32, 32)
    model = SimpleNamespace(
        asset_cell_entries=[(rect1, "asset_idle_n"), (rect2, "asset_walk_s")],
        hovered_asset_cell=None,
        selected_asset_cell=None,
        active_set_rect=pygame.Rect(0, 40, 80, 20),
    )

    # Picker model with panel rect for positioning
    picker_model = SimpleNamespace(
        panel_rect=pygame.Rect(10, 200, 300, 100),
        hovered_id=None,
        selected_id=None,
    )

    history_calls = {"push": []}

    class HistoryStub:
        def push(self, cmd):
            history_calls["push"].append(cmd)

    # Editor controller and picker controller
    picker_controller = SimpleNamespace(model=picker_model)
    editor_controller = SimpleNamespace(picker_controller=picker_controller, history=HistoryStub())

    # Properties controller (parent of assets grid controller)
    def on_asset_chosen(key, path):
        pass

    assets_picker_calls = {"show": []}

    def assets_picker_show(key, x0, y0, width, cb, label_provider):
        assets_picker_calls["show"].append((key, x0, y0, width, cb, label_provider))

    properties_controller = SimpleNamespace(
        model=SimpleNamespace(selected_id="slime"),
        editor_controller=editor_controller,
        assets_picker_controller=SimpleNamespace(show=assets_picker_show),
        _on_asset_chosen=on_asset_chosen,
    )

    controller = SimpleNamespace(parent_controller=properties_controller, model=model, view=SimpleNamespace())

    return controller, model, picker_model, assets_picker_calls, history_calls


def test_hover_sets_hovered_cell():
    controller, model, *_ = _make_controller_stub()
    handler = AssetsGridPanelEventHandler(controller)

    ev = SimpleNamespace(type=pygame.MOUSEMOTION, pos=(5, 5))
    consumed = handler.handle(ev)
    assert consumed is True
    assert model.hovered_asset_cell == "asset_idle_n"

    ev = SimpleNamespace(type=pygame.MOUSEMOTION, pos=(200, 200))
    consumed = handler.handle(ev)
    assert consumed is False
    assert model.hovered_asset_cell is None


def test_click_selects_cell():
    controller, model, *_ = _make_controller_stub()
    handler = AssetsGridPanelEventHandler(controller)

    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(45, 5))
    consumed = handler.handle(ev)
    assert consumed is True
    assert model.selected_asset_cell == "asset_walk_s"


def test_double_click_opens_assets_picker_with_label_provider():
    controller, model, picker_model, assets_picker_calls, _ = _make_controller_stub()
    handler = AssetsGridPanelEventHandler(controller)

    # Force double-click
    handler.dc_detector = SimpleNamespace(is_double_click=lambda key: True)

    # Hovered should take precedence in label provider
    picker_model.hovered_id = "hovered_monster"
    picker_model.selected_id = "selected_monster"

    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(45, 5))
    consumed = handler.handle(ev)
    assert consumed is True

    assert len(assets_picker_calls["show"]) == 1
    key, x0, y0, width, cb, label_provider = assets_picker_calls["show"][0]

    assert key == "asset_walk_s"
    # Positioned from picker_model.panel_rect
    assert (x0, y0, width) == (10, 300, 300)

    # Label provider precedence: hovered > selected > ''
    assert label_provider() == "hovered_monster"
    picker_model.hovered_id = None
    assert label_provider() == "selected_monster"
    picker_model.selected_id = None
    assert label_provider() == ""


def test_click_active_set_rect_pushes_command(monkeypatch):
    controller, model, _, _, history_calls = _make_controller_stub()

    # Replace command class in module to a sentinel
    class DummyCmd:
        def __init__(self, prop_ctrl, ent_id):
            self.prop_ctrl = prop_ctrl
            self.ent_id = ent_id

    monkeypatch.setattr(grid_events_module, "ToggleActiveSetCommand", DummyCmd)

    handler = AssetsGridPanelEventHandler(controller)

    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(10, 45))
    consumed = handler.handle(ev)
    assert consumed is True

    # Verify history push called with DummyCmd instance and correct entity id
    assert len(history_calls["push"]) == 1
    cmd = history_calls["push"][0]
    assert isinstance(cmd, DummyCmd)
    assert cmd.ent_id == "slime"
