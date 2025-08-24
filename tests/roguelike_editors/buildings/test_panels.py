import types
from types import SimpleNamespace

import pygame
import pytest


def _make_manager(monkeypatch, surface_factory, camera):
    """Create a minimal BuildingEditorManager with fake Game and monkeypatched loaders."""
    # Monkeypatch image loader to avoid disk IO across toolbar/picker/panels
    try:
        from roguelike_engine.utils import loader as loader_mod
    except Exception:  # pragma: no cover - import error means module layout changed
        pytest.skip("loader module not importable")

    monkeypatch.setattr(
        loader_mod,
        "load_image",
        lambda path, scale=None: pygame.transform.scale(
            surface_factory(32, 32, (120, 120, 120, 255)), scale or (32, 32)
        ),
        raising=True,
    )

    # Fake game container with a minimal dummy building so controller can derive building_class
    dummy_building = SimpleNamespace(
        x=0,
        y=0,
        image=surface_factory(64, 64, (80, 80, 80, 255)),
        rect=pygame.Rect(0, 0, 64, 64),
        split_ratio=0.5,
        image_path="/virtual/dummy.png",
        collision_map=[["."]],
    )
    fake_game = SimpleNamespace(
        state=SimpleNamespace(running=True, editor=None, z_state=SimpleNamespace()),
        buildings=SimpleNamespace(buildings=[dummy_building]),
        camera=camera,
    )

    from roguelike_game.managers.editors.buildings_editor_manager import BuildingEditorManager

    mgr = BuildingEditorManager(fake_game)
    # Open the editor
    mgr.editor_state.active = True
    return mgr


def _click(widget, key: str) -> tuple[int, int]:
    """Return a point inside the icon rect for a given key in a ToolbarView widget."""
    rect = widget.icon_rects.get(key)
    assert rect is not None, f"Icon rect for {key} not found"
    return rect.center


def _left_click_at(pos):
    return pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos": pos, "button": 1})


@pytest.mark.parametrize("key_a, key_b", [("buildings_manager", "buildings_colliders")])
def test_pnl_001_toolbar_toggles_add_remove_picker_and_colliders(monkeypatch, surface_factory, camera, key_a, key_b):
    """[PNL-001] Toolbar toggles: buildings_manager opens/closes Add/Remove + Picker and deactivates Colliders; buildings_colliders toggles colliders panel and suppresses picker/add_remove."""
    screen = pygame.Surface((800, 600), pygame.SRCALPHA)
    mgr = _make_manager(monkeypatch, surface_factory, camera)

    # Initial render so toolbar computes icon_rects
    mgr.buildings_toolbar_controller.render(screen)
    widget = mgr.buildings_toolbar_controller.view.widget

    # Click buildings_manager ON
    mgr.buildings_toolbar_controller.handle_event(_left_click_at(_click(widget, key_a)))
    assert mgr.buildings_toolbar_model.active_tool == key_a
    assert mgr.editor_state.picker_active is True
    assert mgr.add_remove.is_active() is True
    # Colliders should be off
    assert mgr.colliders.is_active() is False
    assert getattr(mgr.editor_state, "colliders_mode", False) is False

    # Click buildings_manager OFF
    mgr.buildings_toolbar_controller.handle_event(_left_click_at(_click(widget, key_a)))
    assert mgr.buildings_toolbar_model.active_tool is None
    assert mgr.editor_state.picker_active is False
    assert mgr.add_remove.is_active() is False

    # Click buildings_colliders ON
    mgr.buildings_toolbar_controller.handle_event(_left_click_at(_click(widget, key_b)))
    assert mgr.buildings_toolbar_model.active_tool == key_b
    assert mgr.colliders.is_active() is True
    assert mgr.add_remove.is_active() is False
    assert mgr.editor_state.picker_active is False
    assert getattr(mgr.editor_state, "colliders_mode", False) is True

    # Click buildings_colliders OFF
    mgr.buildings_toolbar_controller.handle_event(_left_click_at(_click(widget, key_b)))
    assert mgr.buildings_toolbar_model.active_tool is None
    assert mgr.colliders.is_active() is False
    assert getattr(mgr.editor_state, "colliders_mode", True) is False



def test_pnl_002_add_remove_exposes_rect_and_picker_anchors_right(monkeypatch, surface_factory, camera):
    """[PNL-002] Add/Remove publishes editor.add_remove_panel_rect; BuildingEditorView anchors picker to its right when visible."""
    screen = pygame.Surface((800, 600), pygame.SRCALPHA)
    mgr = _make_manager(monkeypatch, surface_factory, camera)

    # Turn ON buildings_manager to activate Add/Remove + Picker
    mgr.buildings_toolbar_controller.render(screen)
    widget = mgr.buildings_toolbar_controller.view.widget
    mgr.buildings_toolbar_controller.handle_event(_left_click_at(_click(widget, "buildings_manager")))

    # Full render pass to compute positions and publish rects
    mgr.render(screen, camera, mgr.game.buildings.buildings)

    rect = getattr(mgr.editor_state, "add_remove_panel_rect", None)
    assert rect is not None and rect.width > 0 and rect.height > 0

    # Picker should align to the right of Add/Remove panel
    pv = mgr.view.picker_view
    assert pv._left_anchor_x == rect.right + 8
    assert pv._top_anchor_y == rect.top


def test_pnl_003_colliders_consumes_events_and_sets_colliders_mode(monkeypatch, surface_factory, camera):
    """[PNL-003] When Colliders panel is active, its event handler consumes picker clicks and editor.colliders_mode is True; on deactivate it resets to False."""
    screen = pygame.Surface((800, 600), pygame.SRCALPHA)
    mgr = _make_manager(monkeypatch, surface_factory, camera)

    # Activate colliders via toolbar
    mgr.buildings_toolbar_controller.render(screen)
    widget = mgr.buildings_toolbar_controller.view.widget
    mgr.buildings_toolbar_controller.handle_event(_left_click_at(_click(widget, "buildings_colliders")))
    assert mgr.colliders.is_active() is True
    assert getattr(mgr.editor_state, "colliders_mode", False) is True

    # Render once so picker_rects are populated
    mgr.render(screen, camera, mgr.game.buildings.buildings)
    picker_rects = getattr(mgr.colliders.view.model, "picker_rects", {})
    # Click inside one of the picker options (e.g., '#')
    target_rect = picker_rects.get("#") or next(iter(picker_rects.values()))
    consumed = mgr.colliders.handle_event(_left_click_at(target_rect.center), camera, mgr.game.buildings.buildings)
    assert consumed is True

    # Deactivate and check flag reset
    mgr.buildings_toolbar_controller.handle_event(_left_click_at(_click(widget, "buildings_colliders")))
    assert mgr.colliders.is_active() is False
    assert getattr(mgr.editor_state, "colliders_mode", True) is False

