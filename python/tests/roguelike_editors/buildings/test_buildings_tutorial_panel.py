import types
from types import SimpleNamespace
import pygame
import pytest

from roguelike_editors.buildings.buildings_tutorial_panel.buildings_tutorial_panel_controller import BuildingsTutorialPanelController
from roguelike_editors.buildings.buildings_tutorial_panel.buildings_tutorial_panel_view import BuildingsTutorialPanelView


class DummyEditorView:
    def __init__(self):
        # Expose cached rects for highlight logic
        self._last_active_building_rect = None
        self._last_hovered_building_rect = None
        self.title_view = types.SimpleNamespace(widget=types.SimpleNamespace(rect=pygame.Rect(8, 8, 200, 28)))


class DummyManager:
    def __init__(self):
        self.buildings_toolbar_model = types.SimpleNamespace(active_tool=None)


def _make_controller(editor_state=None):
    if editor_state is None:
        editor_state = SimpleNamespace()
    editor_view = DummyEditorView()
    manager = DummyManager()
    return BuildingsTutorialPanelController(state=None, editor_state=editor_state, editor_view=editor_view, editor_manager=manager)


def _find_step_index_by_title(controller, title_substr):
    for i, step in enumerate(controller.model.steps):
        if title_substr.lower() in step.get("title", "").lower():
            return i
    raise AssertionError(f"Step with title containing '{title_substr}' not found")


def test_activate_clears_pulses_and_sets_active():
    es = SimpleNamespace(
        tutorial_resized_pulse=True,
        tutorial_reset_pulse=True,
        tutorial_deleted_pulse=True,
        tutorial_undo_delete_pulse=True,
        tutorial_colliders_choice_pulse=True,
        tutorial_colliders_painted_pulse=True,
        tutorial_colliders_painted_on_selected_pulse=True,
        tutorial_colliders_picker_moved_pulse=True,
        tutorial_colliders_saved_button_pulse=True,
        colliders_mode=False,
        picker_active=False,
    )
    c = _make_controller(es)
    # make sure step_index initially negative to ensure it is set
    c.model.step_index = -1
    c.activate()

    assert c.is_active() is True
    assert c.model.step_index == 0
    # pulses should be cleared
    assert es.tutorial_resized_pulse is False
    assert es.tutorial_reset_pulse is False
    assert es.tutorial_deleted_pulse is False
    assert es.tutorial_undo_delete_pulse is False
    assert es.tutorial_colliders_choice_pulse is False
    assert es.tutorial_colliders_painted_pulse is False
    assert es.tutorial_colliders_painted_on_selected_pulse is False
    assert es.tutorial_colliders_picker_moved_pulse is False
    assert es.tutorial_colliders_saved_button_pulse is False


def test_checklist_progress_for_resize_and_reset_pulses():
    es = SimpleNamespace(
        colliders_mode=False,
        picker_active=False,
        tutorial_resized_pulse=False,
        tutorial_reset_pulse=False,
    )
    c = _make_controller(es)
    # Move to "Redimensionar y Reset" step
    idx = _find_step_index_by_title(c, "Redimensionar")
    c.model.step_index = idx
    c.model.checklist_done_by_step[idx] = set()

    # Simulate active building state tracking so size_changed comparisons work across frames
    class Bld:
        def __init__(self):
            self.id = 1
            self.x, self.y = 10, 20
            # mock image size changes across calls via controller's pulses
            self.image = types.SimpleNamespace(get_size=lambda: (100, 100))
            self.split_ratio = 0.5
            self.z_bottom = 0
            self.z_top = 1
    active = Bld()

    # Seed tracking values as if previous frame existed
    c.model.last_active_building_id = active.id
    c.model.last_active_pos = (active.x, active.y)
    c.model.last_split_ratio = active.split_ratio
    c.model.last_z_bottom = active.z_bottom
    c.model.last_z_top = active.z_top
    c.model.last_image_size = (100, 100)

    es.active_building = active

    # Trigger resized via pulse
    es.tutorial_resized_pulse = True
    c._update_checklist_progress()
    done = c.model.checklist_done_by_step[idx]
    assert "resized" in done
    # Pulse should be consumed
    assert es.tutorial_resized_pulse is False

    # Trigger reset via pulse
    es.tutorial_reset_pulse = True
    c._update_checklist_progress()
    done = c.model.checklist_done_by_step[idx]
    assert "reset_done" in done
    assert es.tutorial_reset_pulse is False


def test_colliders_step_pulses_and_scope_toggle():
    es = SimpleNamespace(
        colliders_mode=True,
        picker_active=False,
        tutorial_colliders_choice_pulse=True,
        tutorial_colliders_painted_pulse=True,
        tutorial_colliders_painted_on_selected_pulse=True,
        tutorial_colliders_picker_moved_pulse=True,
        tutorial_colliders_saved_button_pulse=True,
        collider_scope="CG",
    )
    c = _make_controller(es)
    idx = _find_step_index_by_title(c, "Colisiones")
    c.model.step_index = idx
    c.model.checklist_done_by_step[idx] = set()

    # First update: consumes pulses and marks items (except scope toggle which needs an actual toggle)
    c._update_checklist_progress()
    done = c.model.checklist_done_by_step[idx]
    assert {
        "colliders_mode",
        "colliders_choice",
        "colliders_painted",
        "colliders_painted_on_selected",
        "colliders_picker_moved",
        "colliders_saved_button",
    }.issubset(done)
    # Pulses consumed
    assert es.tutorial_colliders_choice_pulse is False
    assert es.tutorial_colliders_painted_pulse is False
    assert es.tutorial_colliders_painted_on_selected_pulse is False
    assert es.tutorial_colliders_picker_moved_pulse is False
    assert es.tutorial_colliders_saved_button_pulse is False

    # Now toggle scope to CU and verify toggle + CU condition get marked
    es.collider_scope = "CU"
    c._update_checklist_progress()
    done = c.model.checklist_done_by_step[idx]
    assert "colliders_scope_toggled" in done
    assert "colliders_scope_cu" in done

    # Toggle back to CG and verify CG condition gets marked too
    es.collider_scope = "CG"
    c._update_checklist_progress()
    done = c.model.checklist_done_by_step[idx]
    assert "colliders_scope_cg" in done


def test_view_highlight_prefers_active_over_hovered(monkeypatch):
    es = SimpleNamespace()
    dummy_view = DummyEditorView()
    controller = _make_controller(es)
    controller.view.editor_view = dummy_view

    # Create distinct active and hovered rects
    active_rect = pygame.Rect(100, 100, 40, 30)
    hovered_rect = pygame.Rect(300, 300, 20, 10)
    dummy_view._last_active_building_rect = active_rect
    dummy_view._last_hovered_building_rect = hovered_rect

    # Activate and move to a step that highlights editor building
    controller.model.active = True
    controller.model.step_index = _find_step_index_by_title(controller, "Selección")

    captured = []

    def capture_flash(screen, rect):
        captured.append(rect.copy())

    # Monkeypatch the draw function to capture the rect used for highlighting
    controller.view._draw_flash_highlight = capture_flash

    # Minimal surface to render on
    surf = pygame.Surface((800, 600))
    controller.view.render(surf)

    assert captured, "Expected a highlight to be drawn"
    # Expect it to prefer active (inflated by 10 on each axis)
    expect = active_rect.inflate(10, 10)
    got = captured[0]
    assert got.size == expect.size and got.center == expect.center, "Highlight should be based on active building rect"


def test_esc_closes_tutorial_and_clears_toolbar_active_tool():
    es = SimpleNamespace()
    controller = _make_controller(es)
    # Simulate toolbar having the tutorial tool active
    controller.editor_manager.buildings_toolbar_model.active_tool = 'tutorial_building'
    # Activate tutorial
    controller.model.active = True
    # Send ESC keydown event
    ev = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_ESCAPE})
    consumed = controller.handle_event(ev)
    assert consumed is True
    assert controller.is_active() is False
    # Toolbar active tool should be cleared on deactivate
    assert controller.editor_manager.buildings_toolbar_model.active_tool is None
