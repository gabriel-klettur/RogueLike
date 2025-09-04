import types
import pygame
import pytest


class _DummyBuilding:
    def __init__(self, image: pygame.Surface, image_path: str = "dummy.png"):
        self.image = image
        self.image_path = image_path
        self.x = 0
        self.y = 0
        self.rect = pygame.Rect(self.x, self.y, *self.image.get_size())
        # Fields for save
        self.zone = "no zone"
        self.rel_x = 0
        self.rel_y = 0
        self.solid = True
        self.original_scale = self.image.get_size()
        self.split_ratio = 0.5
        self.z_bottom = 0
        self.z_top = 0
        self.collider_scope = "CG"
        self.collision_map = [["."]]


def _make_handler(camera, surface_factory):
    from roguelike_editors.buildings.building_editor_events import BuildingEditorEventHandler
    from roguelike_editors.buildings.building_editor_controller import BuildingEditorController
    from roguelike_editors.buildings.building_editor_model import BuildingsEditorModel

    # Minimal state and entities
    state = types.SimpleNamespace(z_state=object(), running=True)
    editor = BuildingsEditorModel()
    buildings = [_DummyBuilding(surface_factory(8, 8))]
    entities = types.SimpleNamespace(buildings=buildings)
    controller = BuildingEditorController(state, editor, buildings, camera)
    zone_offsets = {"no zone": (0, 0)}
    handler = BuildingEditorEventHandler(state, editor, controller, buildings, zone_offsets)
    return handler, state, editor, controller, entities


# [EVT-001] Panning con MMB
def test_evt_001_panning_mmb(camera, surface_factory):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)

    # Start panning with MMB down
    down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 2, "pos": (100, 100)})
    handler.handle(camera, entities, [down])
    # Move mouse: camera offsets should change inversely to rel/zoom
    camera.zoom = 2.0
    motion = pygame.event.Event(pygame.MOUSEMOTION, {"pos": (110, 120), "rel": (10, 20)})
    handler.handle(camera, entities, [motion])
    assert camera.offset_x == pytest.approx(-10 / 2.0)
    assert camera.offset_y == pytest.approx(-20 / 2.0)
    # Stop panning
    up = pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 2, "pos": (110, 120)})
    handler.handle(camera, entities, [up])


# [EVT-012] Persistencia tras mouse up
def test_evt_012_persist_on_mouse_up(camera, surface_factory, monkeypatch):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)
    editor.active = True
    calls = []

    # Monkeypatch save at the exact import site used by the events module
    import roguelike_editors.buildings.building_editor_events as ev_mod

    def _fake_save(buildings, filepath=None, z_state=None, zone_offsets=None, **kwargs):
        calls.append((buildings, filepath, z_state, zone_offsets))
        return True

    monkeypatch.setattr(ev_mod, "save_buildings_to_json", _fake_save, raising=True)

    up = pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 1, "pos": (0, 0)})
    handler.handle(camera, entities, [up])
    assert len(calls) == 1, "save should be called exactly once on mouse up"


# [EVT-016] Delegación a panel de colisiones consume evento
def test_evt_016_delegation_to_colliders_consumes_event(camera, surface_factory, monkeypatch):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)

    # Spy controller to ensure it is NOT called when colliders consume event
    called = {"down": 0}

    def _spy_on_mouse_down(pos, button, cam, buildings):
        called["down"] += 1

    monkeypatch.setattr(controller, "on_mouse_down", _spy_on_mouse_down, raising=True)

    class FakeColliders:
        def is_active(self):
            return True

        def handle_event(self, ev, cam, buildings):
            return True  # consume always

    # Attach fake colliders to handler
    handler.colliders = FakeColliders()

    # This event would normally be routed to controller.on_mouse_down
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 1, "pos": (5, 5)})
    handler.handle(camera, entities, [ev])
    assert called["down"] == 0, "Controller should not receive event when colliders consume it"


def test_evt_002_quit_persists_and_stops(camera, surface_factory, monkeypatch):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)
    editor.active = True

    import roguelike_editors.buildings.building_editor_events as ev_mod
    calls = []

    def _fake_save(buildings, filepath=None, z_state=None, zone_offsets=None, **kwargs):
        calls.append((buildings, filepath, z_state, zone_offsets))
        return True

    monkeypatch.setattr(ev_mod, "save_buildings_to_json", _fake_save, raising=True)

    ev = pygame.event.Event(pygame.QUIT)
    handler.handle(camera, entities, [ev])

    assert state.running is False
    assert len(calls) == 1


def test_evt_003_escape_closes_and_saves(camera, surface_factory, monkeypatch):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)
    editor.active = True
    editor.dragging = True
    editor.resizing = True
    editor.split_dragging = True
    editor.selected_building = entities.buildings[0]

    import roguelike_editors.buildings.building_editor_events as ev_mod
    calls = []

    def _fake_save(buildings, filepath=None, z_state=None, zone_offsets=None, **kwargs):
        calls.append((buildings, filepath, z_state, zone_offsets))
        return True

    monkeypatch.setattr(ev_mod, "save_buildings_to_json", _fake_save, raising=True)

    ev = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_ESCAPE})
    handler.handle(camera, entities, [ev])

    assert editor.active is False
    assert editor.dragging is False
    assert editor.resizing is False
    assert editor.split_dragging is False
    assert editor.selected_building is None
    assert len(calls) == 1


def test_evt_004_toggle_picker_with_p(camera, surface_factory):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)
    start_val = getattr(editor, "picker_active", False)

    ev_p = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_p})
    handler.handle(camera, entities, [ev_p])
    assert editor.picker_active is (not start_val)

    handler.handle(camera, entities, [ev_p])
    assert editor.picker_active is start_val


def test_evt_005_reset_with_d_applies_default_tool(camera, surface_factory, monkeypatch):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)
    editor.hovered_building = entities.buildings[0]
    editor.colliders_mode = False

    called = {"reset": 0, "arg": None}

    def _spy_reset(b):
        called["reset"] += 1
        called["arg"] = b

    monkeypatch.setattr(controller.default_tool, "apply_reset", _spy_reset, raising=True)

    # New behavior: D applies reset only on active_building
    editor.active_building = entities.buildings[0]

    ev_d = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_d})
    handler.handle(camera, entities, [ev_d])

    assert called["reset"] == 1
    assert called["arg"] is entities.buildings[0]


def test_evt_006_resize_with_r_keydown_and_keyup(camera, surface_factory, monkeypatch):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)
    editor.hovered_building = entities.buildings[0]
    editor.colliders_mode = False

    # Spy _start_resize and make it set resizing True
    calls = {"start": 0, "args": None}

    def _spy_start_resize(building, mouse_start):
        calls["start"] += 1
        calls["args"] = (building, mouse_start)
        editor.resizing = True

    monkeypatch.setattr(controller, "_start_resize", _spy_start_resize, raising=True)
    monkeypatch.setattr(pygame.mouse, "get_pos", lambda: (50, 60), raising=True)

    # New behavior: R starts resize only on active_building
    editor.active_building = entities.buildings[0]
    ev_down = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_r})
    handler.handle(camera, entities, [ev_down])
    assert calls["start"] == 1
    assert editor.resizing is True

    ev_up = pygame.event.Event(pygame.KEYUP, {"key": pygame.K_r})
    handler.handle(camera, entities, [ev_up])
    assert editor.resizing is False


def test_evt_007_undo_ctrl_z_restores_building(camera, surface_factory):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)

    # Prepare an entry in undo_stack: a removed building at index 0
    removed = _DummyBuilding(surface_factory(8, 8), image_path="removed.png")
    editor.undo_stack = [(removed, 0)]
    # Initially one building exists
    assert len(entities.buildings) == 1

    ev = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_z, "mod": pygame.KMOD_CTRL})
    handler.handle(camera, entities, [ev])

    # Building restored at index 0
    assert len(entities.buildings) == 2
    assert entities.buildings[0] is removed
    # Focus updated
    assert editor.hovered_building is removed
    assert editor.selected_building is removed


def test_rbt_004_undo_on_empty_stack_is_safe(camera, surface_factory):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)

    # Ensure empty or missing undo_stack
    if hasattr(editor, "undo_stack"):
        editor.undo_stack = []
    start_len = len(entities.buildings)

    # Ctrl+Z should not raise or change buildings when stack is empty
    ev = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_z, "mod": pygame.KMOD_CTRL})
    handler.handle(camera, entities, [ev])

    assert len(entities.buildings) == start_len


def test_evt_008_ctrl_s_persists_without_closing(camera, surface_factory, monkeypatch):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)
    editor.active = True

    import roguelike_editors.buildings.building_editor_events as ev_mod
    calls = []

    def _fake_save(buildings, filepath=None, z_state=None, zone_offsets=None, **kwargs):
        calls.append((buildings, filepath, z_state, zone_offsets))
        return True

    monkeypatch.setattr(ev_mod, "save_buildings_to_json", _fake_save, raising=True)

    ev = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_s, "mod": pygame.KMOD_CTRL})
    handler.handle(camera, entities, [ev])

    assert len(calls) == 1
    assert editor.active is True


def test_evt_009_place_with_n_increases_buildings(camera, surface_factory, monkeypatch):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)

    start_len = len(entities.buildings)

    def _fake_place(buildings_list):
        buildings_list.append(_DummyBuilding(surface_factory(8, 8), image_path="new.png"))

    monkeypatch.setattr(controller.placer_tool, "place_building_at_mouse", _fake_place, raising=True)

    ev = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_n})
    handler.handle(camera, entities, [ev])

    assert len(entities.buildings) == start_len + 1
    assert entities.buildings[-1].image_path == "new.png"


def test_evt_010_delete_key_calls_delete_and_respects_colliders_mode(camera, surface_factory, monkeypatch):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)

    calls = {"delete": 0, "args": None}

    # Spy the controller's internal delete (current behavior)
    def _spy_delete_internal(building_arg, buildings_list):
        calls["delete"] += 1
        calls["args"] = (building_arg, list(buildings_list))

    monkeypatch.setattr(controller, "_delete_building", _spy_delete_internal, raising=True)

    # When not in colliders mode, it should call delete
    editor.colliders_mode = False
    # New behavior: Delete acts only on active_building
    editor.active_building = entities.buildings[0]
    ev = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_DELETE})
    handler.handle(camera, entities, [ev])
    assert calls["delete"] == 1

    # When colliders mode is active, it should not call delete
    editor.colliders_mode = True
    handler.handle(camera, entities, [ev])
    assert calls["delete"] == 1


def test_evt_011_mouse_events_delegated_to_controller(camera, surface_factory, monkeypatch):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)

    called = {"down": 0, "up": 0, "motion": 0}

    def _spy_down(pos, button, cam, buildings):
        called["down"] += 1

    def _spy_up(button, cam, buildings):
        called["up"] += 1

    def _spy_motion(pos, cam, buildings):
        called["motion"] += 1

    monkeypatch.setattr(controller, "on_mouse_down", _spy_down, raising=True)
    monkeypatch.setattr(controller, "on_mouse_up", _spy_up, raising=True)
    monkeypatch.setattr(controller, "on_mouse_motion", _spy_motion, raising=True)

    # Avoid disk IO on MOUSEBUTTONUP persistence
    import roguelike_editors.buildings.building_editor_events as ev_mod
    monkeypatch.setattr(ev_mod, "save_buildings_to_json", lambda *a, **k: True, raising=True)

    evs = [
        pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 1, "pos": (10, 10)}),
        pygame.event.Event(pygame.MOUSEMOTION, {"pos": (11, 12), "rel": (1, 2)}),
        pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 1, "pos": (11, 12)}),
    ]
    handler.handle(camera, entities, evs)

    assert called["down"] == 1
    assert called["motion"] == 1
    assert called["up"] == 1


def test_evt_013_ui_blocker_clears_hover_but_keeps_active_on_motion(camera, surface_factory, monkeypatch):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)

    # Seed active and hovered states
    editor.active_building = entities.buildings[0]
    editor.hovered_building = entities.buildings[0]
    editor.hovered_buildings = [entities.buildings[0]]

    # Monkeypatch is_blocked in the events module
    import roguelike_editors.buildings.building_editor_events as ev_mod

    monkeypatch.setattr(ev_mod, "is_blocked", lambda mx, my: True, raising=True)

    ev = pygame.event.Event(pygame.MOUSEMOTION, {"pos": (5, 5), "rel": (0, 0)})
    handler.handle(camera, entities, [ev])

    assert editor.hovered_buildings == []
    assert editor.hovered_building is None
    # New behavior: keep active selection when UI blocks mouse motion
    assert editor.active_building is entities.buildings[0]


def test_evt_014_select_sets_active_on_click_and_persists_on_motion(camera, surface_factory):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)
    editor.current_tool = 'select'

    # Click inside building rect -> should persist active_building
    ev_click = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 1, "pos": (1, 1)})
    handler.handle(camera, entities, [ev_click])
    assert editor.active_building is not None

    # Move outside building rect -> active_building should remain (no auto-clear on leave)
    ev2 = pygame.event.Event(pygame.MOUSEMOTION, {"pos": (999, 999), "rel": (0, 0)})
    handler.handle(camera, entities, [ev2])
    assert editor.active_building is not None


def test_evt_015_mouse_wheel_cycles_hovered(camera, surface_factory):
    handler, state, editor, controller, entities = _make_handler(camera, surface_factory)

    # Prepare multiple hovered candidates
    b1 = entities.buildings[0]
    b2 = _DummyBuilding(surface_factory(8, 8), image_path="b2.png")
    b3 = _DummyBuilding(surface_factory(8, 8), image_path="b3.png")
    editor.hovered_buildings = [b1, b2, b3]
    editor.hovered_building_index = 0
    editor.hovered_building = b1

    # Wheel up (y=1) -> next
    handler.handle(camera, entities, [pygame.event.Event(pygame.MOUSEWHEEL, {"y": 1})])
    assert editor.hovered_building is b2
    assert editor.hovered_building_index == 1

    # Wheel down (y=-1) -> previous
    handler.handle(camera, entities, [pygame.event.Event(pygame.MOUSEWHEEL, {"y": -1})])
    assert editor.hovered_building is b1
    assert editor.hovered_building_index == 0


@pytest.mark.skip(reason="[EVT-*] Remaining matrix pending implementation.")
def test_events_matrix_placeholder():
    """
    EVT-001..EVT-016 per README: see specific tests for implemented cases.
    """
    assert True
