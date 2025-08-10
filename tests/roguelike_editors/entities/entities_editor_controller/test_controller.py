import pygame
import types
from types import SimpleNamespace

import pytest

from roguelike_editors.entities.entities_editor_model import EntitiesEditorModel
from roguelike_editors.entities.entities_editor_controller import EntitiesEditorController
from roguelike_editors.entities.services.constants import ENTITIES_TOOL_ON_MAP


@pytest.fixture
def font(pygame_headless):
    return pygame.font.SysFont(None, 16)


def make_controller(font):
    model = EntitiesEditorModel()
    ctrl = EntitiesEditorController(model, font)
    # Attach a minimal game stub to satisfy any references
    cam = SimpleNamespace(offset_x=0.0, offset_y=0.0, zoom=2.0)
    ecs_world = SimpleNamespace()
    ctrl.game = SimpleNamespace(camera=cam, ecs=SimpleNamespace(ecs_world=ecs_world))
    return ctrl


def test_enter_and_exit_spawn_mode_clears_properties_and_sets_picker_flags(monkeypatch, font):
    ctrl = make_controller(font)

    # Replace properties_controller with stub to observe clearing + hide call
    calls = {"hide": 0}

    def _hide():
        calls["hide"] += 1

    pc_model = SimpleNamespace(
        editing_property="x",
        focused_property="y",
        hovered_property="z",
        panel_rect=(1, 2, 3, 4),
        selected_id="id",
        hovered_entity_id="h",
    )
    ctrl.properties_controller = SimpleNamespace(model=pc_model, assets_picker_controller=SimpleNamespace(hide=_hide))

    # Capture cursor changes
    last_cursor = {"val": None}
    monkeypatch.setattr(pygame.mouse, "set_cursor", lambda cur: last_cursor.__setitem__("val", cur))

    ctrl.enter_spawn_mode("player_knight")
    assert ctrl.model.spawn_mode_active is True
    assert ctrl.model.spawn_entity_type == "player_knight"
    assert ctrl.picker_controller.model.blink is True
    assert ctrl.picker_controller.model.visible is True
    assert ctrl.picker_controller.model.selected_id is None
    # properties cleared and assets picker hidden
    assert calls["hide"] == 1
    assert pc_model.editing_property is None
    assert pc_model.focused_property is None
    assert pc_model.hovered_property is None
    assert pc_model.panel_rect is None
    assert pc_model.selected_id is None
    assert pc_model.hovered_entity_id is None

    ctrl.exit_spawn_mode()
    assert ctrl.model.spawn_mode_active is False
    assert ctrl.model.spawn_entity_type is None
    assert ctrl.picker_controller.model.blink is False
    assert ctrl.picker_controller.model.selection_blink is False
    assert last_cursor["val"] == pygame.SYSTEM_CURSOR_ARROW


def test_enter_and_exit_delete_mode_clears_properties_and_sets_cursor(monkeypatch, font):
    ctrl = make_controller(font)
    # Start from spawn active to verify it exits spawn on enter_delete
    ctrl.model.spawn_mode_active = True

    # Stub properties controller to observe hide + clear
    calls = {"hide": 0}
    pc_model = SimpleNamespace(
        editing_property="x",
        focused_property="y",
        hovered_property="z",
        panel_rect=(1, 2, 3, 4),
        selected_id="id",
        hovered_entity_id="h",
    )
    ctrl.properties_controller = SimpleNamespace(
        model=pc_model,
        assets_picker_controller=SimpleNamespace(hide=lambda: calls.__setitem__("hide", calls["hide"] + 1)),
    )

    last_cursor = {"val": None}
    monkeypatch.setattr(pygame.mouse, "set_cursor", lambda cur: last_cursor.__setitem__("val", cur))

    ctrl.enter_delete_mode()
    assert ctrl.model.delete_mode_active is True
    assert ctrl.model.spawn_mode_active is False
    assert last_cursor["val"] == pygame.SYSTEM_CURSOR_CROSSHAIR
    assert calls["hide"] == 1
    assert pc_model.selected_id is None

    ctrl.exit_delete_mode()
    assert ctrl.model.delete_mode_active is False
    assert last_cursor["val"] == pygame.SYSTEM_CURSOR_ARROW


def test_add_entities_on_system_mode_expands_and_restores_properties_layout(font):
    ctrl = make_controller(font)
    # Ensure properties view has a draggable panel with a known pos
    pp_view = ctrl.properties_controller.view
    initial_pos = (pp_view.draggable_panel.pos if hasattr(pp_view, "draggable_panel") else (50, 60))
    if not hasattr(pp_view, "draggable_panel"):
        pp_view.draggable_panel = SimpleNamespace(pos=initial_pos)
    # Ensure picker has a position
    picker_pos = (ctrl.picker_controller.view.x, ctrl.picker_controller.view.y)

    ctrl.enter_add_entities_on_system_mode()
    assert ctrl.picker_controller.model.visible is False
    assert ctrl.properties_controller.model.expand_into_picker_space is True
    assert ctrl.properties_controller.model.panel_left_x_override == picker_pos[0]
    assert pp_view.draggable_panel.pos == picker_pos

    ctrl.exit_add_entities_on_system_mode()
    assert ctrl.picker_controller.model.visible is True
    assert ctrl.properties_controller.model.expand_into_picker_space is False
    assert ctrl.properties_controller.model.panel_left_x_override is None
    # restored position
    assert isinstance(pp_view.draggable_panel.pos, tuple)


def test_handle_event_pushes_spawn_and_delete_commands(monkeypatch, font):
    ctrl = make_controller(font)
    # Enable toolbar tool to allow controller to process map interactions
    ctrl.model.toolbar_model.active_tool = ENTITIES_TOOL_ON_MAP

    # Monkeypatch history.push to capture commands
    pushed = []
    ctrl.history = SimpleNamespace(push=lambda cmd: pushed.append(cmd))

    # Avoid creating system cursors in headless mode
    last_cursor = {"val": None}
    monkeypatch.setattr(pygame.mouse, "set_cursor", lambda cur: last_cursor.__setitem__("val", cur))

    # Monkeypatch helpers used inside controller
    monkeypatch.setattr(
        'roguelike_editors.entities.entities_editor_controller.screen_to_tile',
        lambda camera, sx, sy, tile_size: (7, 9),
    )
    monkeypatch.setattr(
        'roguelike_editors.entities.entities_editor_controller.find_clickable_entity_at',
        lambda game, mx, my: 42,
    )

    # Spawn: activate mode and entity type selected
    ctrl.enter_spawn_mode("player_knight")
    # Simulate click on map
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(300, 400))
    assert ctrl.handle_event(ev) is True
    # After spawn command, mode should exit
    assert ctrl.model.spawn_mode_active is False
    # Ensure a SpawnEntityCommand was pushed with expected tile coords
    from roguelike_editors.entities.services.commands import SpawnEntityCommand
    assert any(isinstance(c, SpawnEntityCommand) and (c.tx, c.ty) == (7, 9) for c in pushed)

    # Delete: activate and click, should push DeleteEntityCommand and exit
    ctrl.enter_delete_mode()
    ev2 = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(100, 100))
    assert ctrl.handle_event(ev2) is True
    from roguelike_editors.entities.services.commands import DeleteEntityCommand
    assert any(isinstance(c, DeleteEntityCommand) and c.eid == 42 for c in pushed)
    assert ctrl.model.delete_mode_active is False


def test_delete_definition_via_picker_click_pushes_command(monkeypatch, font):
    ctrl = make_controller(font)
    # Enable toolbar tool to allow picker/delete handling
    ctrl.model.toolbar_model.active_tool = ENTITIES_TOOL_ON_MAP
    # Simulate picker panel rect and selection
    ctrl.picker_controller.model.panel_rect = pygame.Rect(10, 10, 200, 200)
    ctrl.picker_controller.model.selected_id = 'some_entity'

    # Monkeypatch history.push
    pushed = []
    ctrl.history = SimpleNamespace(push=lambda cmd: pushed.append(cmd))

    # Avoid creating system cursors in headless mode
    last_cursor = {"val": None}
    monkeypatch.setattr(pygame.mouse, "set_cursor", lambda cur: last_cursor.__setitem__("val", cur))

    # Activate delete mode and click inside picker rect
    ctrl.enter_delete_mode()
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(20, 20))
    assert ctrl.handle_event(ev) is True

    from roguelike_editors.entities.services.commands import DeleteEntityDefinitionCommand
    assert any(isinstance(c, DeleteEntityDefinitionCommand) for c in pushed)
    assert ctrl.model.delete_mode_active is False
