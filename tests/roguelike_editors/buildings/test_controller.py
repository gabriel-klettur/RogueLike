import types
import pygame
import pytest


class _DummyBuilding:
    def __init__(self, image: pygame.Surface, image_path: str = "assets/buildings/dummy.png"):
        self.image = image
        self.image_path = image_path
        self.x = 0
        self.y = 0
        self.rect = pygame.Rect(self.x, self.y, *self.image.get_size())
        self.zone = "no zone"
        self.rel_x = 0
        self.rel_y = 0
        self.solid = True
        self.original_scale = self.image.get_size()
        self.split_ratio = 0.5
        self.z_bottom = 0
        self.z_top = 0
        self.collider_scope = "CG"


def _make_controller(camera, surface_factory):
    from roguelike_editors.buildings.building_editor_controller import BuildingEditorController
    from roguelike_editors.buildings.building_editor_model import BuildingsEditorModel

    state = types.SimpleNamespace()
    editor = BuildingsEditorModel()
    buildings = [_DummyBuilding(surface_factory(64, 64))]
    ctl = BuildingEditorController(state, editor, buildings, camera)
    return ctl, editor, buildings


# [CTL-001] Respeta UI blocker en on_mouse_down
def test_ctl_001_on_mouse_down_respects_ui_blocker(camera, surface_factory, monkeypatch):
    ctl, editor, buildings = _make_controller(camera, surface_factory)

    # Force UI blocker to be active at the click position
    import roguelike_ui.ui_blocker as blocker
    monkeypatch.setattr(blocker, "is_blocked", lambda mx, my: True, raising=True)

    # Pre-state
    assert editor.selected_building is None
    assert editor.resizing is False and editor.dragging is False and editor.split_dragging is False

    # Attempt to click; should early-return without changing state or invoking tools
    ctl.on_mouse_down((10, 10), 1, camera, buildings)

    # Post-state unchanged
    assert editor.selected_building is None
    assert editor.resizing is False and editor.dragging is False and editor.split_dragging is False


# [CTL-010] Housekeeping en mouse up + asignación de zona/relativos
def test_ctl_010_mouse_up_housekeeping_and_zone_assignment(camera, surface_factory, monkeypatch):
    ctl, editor, buildings = _make_controller(camera, surface_factory)

    # Select a building and set drag/resize flags
    editor.selected_building = buildings[0]
    editor.dragging = True
    editor.resizing = True
    editor.split_dragging = True

    # Spy assign_zone_and_relatives at the controller module import site
    import roguelike_editors.buildings.building_editor_controller as ctl_mod
    calls = []

    def _fake_assign(b):
        calls.append(b)

    monkeypatch.setattr(ctl_mod, "assign_zone_and_relatives", _fake_assign, raising=True)

    # Invoke mouse up
    ctl.on_mouse_up(1, camera, buildings)

    # Flags cleared and selection cleaned
    assert editor.dragging is False
    assert editor.resizing is False
    assert editor.split_dragging is False
    assert editor.selected_building is None
    # assign_zone_and_relatives called exactly once with former selected building
    assert len(calls) == 1 and calls[0] is not None


# [CTL-002] Colliders mode gating: only collider scope handle responds to LMB; rest ignored
def test_ctl_002_colliders_mode_only_scope_toggle(camera, surface_factory):
    ctl, editor, buildings = _make_controller(camera, surface_factory)
    b = buildings[0]

    # Activate colliders mode and set active building
    editor.colliders_mode = True
    editor.active_building = b

    # Initial scope is CG per tool default
    assert getattr(b, "collider_scope", "CG") == "CG"

    # Click inside collider scope handle -> toggles to CU
    rect = ctl.collider_scope_tool.get_handle_rect(b, camera)
    ctl.on_mouse_down(rect.center, 1, camera, buildings)
    assert b.collider_scope == "CU"

    # RMB anywhere should not start dragging due to colliders_mode
    ctl.on_mouse_down((b.x + 5, b.y + 5), 3, camera, buildings)
    assert editor.dragging is False and editor.selected_building is None


# [CTL-003] Split handle: clicking bar starts split drag and selects building
def test_ctl_003_split_handle_starts_drag(camera, surface_factory):
    ctl, editor, buildings = _make_controller(camera, surface_factory)
    b = buildings[0]

    # New behavior: split handle responds only for the currently active building
    editor.active_building = b

    # Compute a point within split handle rect
    bx, by = camera.apply((b.x, b.y))
    _, h_scaled = camera.scale(b.image.get_size())
    y_split = by + int(h_scaled * b.split_ratio)
    from roguelike_editors.buildings.buildings_editor_config import SPLIT_HANDLE_SIZE
    cx = bx + ctl.split_tool._handle_offset_x(b, camera) + SPLIT_HANDLE_SIZE // 2
    cy = y_split

    ctl.on_mouse_down((cx, cy), 1, camera, buildings)
    assert editor.split_dragging is True
    assert editor.selected_building is b


# [CTL-004] Collider scope toggle via handle in normal mode
def test_ctl_004_scope_toggle_in_normal_mode(camera, surface_factory):
    ctl, editor, buildings = _make_controller(camera, surface_factory)
    b = buildings[0]
    editor.active_building = b
    editor.colliders_mode = False
    assert getattr(b, "collider_scope", "CG") == "CG"

    rect = ctl.collider_scope_tool.get_handle_rect(b, camera)
    ctl.on_mouse_down(rect.center, 1, camera, buildings)
    assert b.collider_scope == "CU"


# [CTL-005] Delete handle removes building and pushes to undo_stack
def test_ctl_005_delete_handle_removes_and_stacks(camera, surface_factory):
    ctl, editor, buildings = _make_controller(camera, surface_factory)
    b = buildings[0]

    # New behavior: delete handle (red button) is only considered on the active building
    editor.active_building = b

    # Click inside delete handle rect
    rect = ctl.default_view.get_delete_handle_rect(b, camera)
    ctl.on_mouse_down(rect.center, 1, camera, buildings)

    # Building removed and undo_stack recorded
    assert b not in buildings
    assert hasattr(editor, "undo_stack") and len(editor.undo_stack) == 1
    building, idx = editor.undo_stack[-1]
    assert building is b and idx == 0
    assert editor.selected_building is None and editor.hovered_building is None


# [CTL-006] Reset handle calls DefaultTool.apply_reset once
def test_ctl_006_reset_handle_invokes_apply_reset(camera, surface_factory, monkeypatch):
    ctl, editor, buildings = _make_controller(camera, surface_factory)
    b = buildings[0]

    calls = []
    monkeypatch.setattr(ctl.default_tool, "apply_reset", lambda bb: calls.append(bb))

    # New behavior: reset handle acts only on active building
    editor.active_building = b

    rect = ctl.default_view.get_reset_handle_rect(b, camera)
    ctl.on_mouse_down(rect.center, 1, camera, buildings)
    assert calls == [b]
    # Ensure building still present
    assert b in buildings


# [CTL-007] Resize handle starts resizing and sets origin/initial_size
def test_ctl_007_resize_handle_starts_resizing(camera, surface_factory):
    ctl, editor, buildings = _make_controller(camera, surface_factory)
    b = buildings[0]

    # Compute a point inside controller's fixed-size resize handle (50x50)
    bx, by = camera.apply((b.x, b.y))
    bw, bh = camera.scale(b.image.get_size())
    left = bx + bw - ctl.resize_tool.handle_size
    pos = (left + 30, by + 20)

    # Sanity: this should be considered inside by the tool
    assert ctl.resize_tool.check_resize_handle_click(*pos, b, camera) is True

    # New behavior: resize handle acts only on active building
    editor.active_building = b

    ctl.on_mouse_down(pos, 1, camera, buildings)
    assert editor.resizing is True and editor.selected_building is b
    assert editor.resize_origin == pos
    assert editor.initial_size == b.image.get_size()


# [CTL-008] RMB drag starts on hovered or top-most colliding building
def test_ctl_008_rmb_drag_selection_and_topmost(camera, surface_factory):
    ctl, editor, buildings = _make_controller(camera, surface_factory)
    a = buildings[0]
    # Add a second building overlapping on purpose
    b = _DummyBuilding(surface_factory(64, 64))
    b.x, b.y = 0, 0
    b.rect.topleft = (0, 0)
    buildings.append(b)

    # Case A: RMB drag only works on active building
    editor.active_building = a
    editor.hovered_building = a
    ctl.on_mouse_down((a.x + 10, a.y + 10), 3, camera, buildings)
    assert editor.dragging is True and editor.selected_building is a

    # Reset and test top-most selection when no hovered
    editor.dragging = False
    editor.selected_building = None
    editor.hovered_building = None
    # New behavior: RMB drag requires active building; set top-most as active explicitly
    editor.active_building = b
    ctl.on_mouse_down((5, 5), 3, camera, buildings)
    assert editor.dragging is True and editor.selected_building is b


# [CTL-009] Z buttons click adjust z_bottom/z_top and update z_state
def test_ctl_009_z_buttons_click_updates_z(camera, surface_factory):
    ctl, editor, buildings = _make_controller(camera, surface_factory)
    b = buildings[0]
    b.z_bottom = 5
    b.z_top = 1

    class ZState:
        def __init__(self):
            self.calls = []
        def set(self, building, z):
            self.calls.append((building, z))

    zs = ZState()
    ctl.state.z_state = zs

    # New behavior: Z tools act only on the active building
    editor.active_building = b

    # Click '-' on bottom tool
    minus_rect, plus_rect = ctl.z_tool_bottom._get_button_rects(b, camera)
    ctl.on_mouse_down(minus_rect.center, 1, camera, buildings)
    assert b.z_bottom == 4
    assert zs.calls and zs.calls[-1] == (b, b.z_bottom)

    # Click '+' on top tool (increments and clamps to >= bottom)
    minus_rect_t, plus_rect_t = ctl.z_tool_top._get_button_rects(b, camera)
    ctl.on_mouse_down(plus_rect_t.center, 1, camera, buildings)
    assert b.z_top == 5 and b.z_top >= b.z_bottom


# [CTL-011] Motion builds hover list ordered by top-most and normalizes index
def test_ctl_011_motion_hover_list_and_index_normalization(camera, surface_factory):
    ctl, editor, buildings = _make_controller(camera, surface_factory)
    a = buildings[0]
    b = _DummyBuilding(surface_factory(64, 64))
    # Overlap both at origin
    a.x = a.y = 0
    a.rect.topleft = (0, 0)
    b.x = b.y = 0
    b.rect.topleft = (0, 0)
    buildings.append(b)

    # Set out-of-range index to force normalization
    editor.hovered_building_index = 999
    ctl.on_mouse_motion((5, 5), camera, buildings)

    assert editor.hovered_buildings[:2] == [b, a]
    assert editor.hovered_building is b
    assert editor.hovered_building_index == 0

    # Move mouse outside any building -> clears hovered_building
    ctl.on_mouse_motion((500, 500), camera, buildings)
    assert editor.hovered_building is None


# [CTL-012] Update adjusts position during drag and delegates on resize
def test_ctl_012_update_drag_and_resize_delegation(camera, surface_factory, monkeypatch):
    ctl, editor, buildings = _make_controller(camera, surface_factory)
    b = buildings[0]

    # Drag case
    editor.selected_building = b
    editor.dragging = True
    editor.offset_x = 10
    editor.offset_y = 15
    monkeypatch.setattr(pygame.mouse, "get_pos", lambda: (200, 150))
    ctl.update(camera)
    assert (b.x, b.y) == (190, 135)
    assert b.rect.topleft == (190, 135)

    # Resize case
    editor.dragging = False
    editor.resizing = True
    calls = []
    monkeypatch.setattr(ctl.resize_tool, "update_resizing", lambda pos: calls.append(pos))
    monkeypatch.setattr(pygame.mouse, "get_pos", lambda: (300, 220))
    ctl.update(camera)
    assert calls == [(300, 220)]
