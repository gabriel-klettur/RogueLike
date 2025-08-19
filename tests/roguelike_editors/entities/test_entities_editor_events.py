import pygame
from types import SimpleNamespace

from roguelike_editors.entities.entities_editor_events import EntitiesEditorEventHandler


def make_env():
    model = SimpleNamespace(active=False, spawn_mode_active=False, spawn_entity_type=None)
    # controller delegates; default returns False
    controller = SimpleNamespace(handle_event=lambda ev: False)
    camera = SimpleNamespace(offset_x=0.0, offset_y=0.0, zoom=2.0)
    return model, controller, camera


def test_f5_no_longer_toggles_editor_locally():
    model, controller, camera = make_env()
    handler = EntitiesEditorEventHandler(model, controller)

    # Initially inactive; F5 should not be handled here and should not change state
    e1 = SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_F5)
    assert handler.handle([e1], camera) is False
    assert model.active is False

    # Set active and press F5 again; still no local toggle or consumption
    model.active = True
    e2 = SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_F5)
    assert handler.handle([e2], camera) is False
    assert model.active is True


def test_escape_closes_editor():
    model, controller, camera = make_env()
    model.active = True
    handler = EntitiesEditorEventHandler(model, controller)

    e = SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_ESCAPE)
    assert handler.handle([e], camera) is True
    assert model.active is False


def test_middle_mouse_panning_updates_camera_and_stops_on_button_up():
    model, controller, camera = make_env()
    handler = EntitiesEditorEventHandler(model, controller)

    down = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=2, pos=(100, 100))
    assert handler.handle([down], camera) is True
    assert handler.panning is True
    assert handler.pan_start == (100, 100)

    # move by (40, 20). With zoom=2.0, offsets change by dx/zoom, dy/zoom
    motion = SimpleNamespace(type=pygame.MOUSEMOTION, pos=(140, 120))
    assert handler.handle([motion], camera) is True
    # offset starts at 0, then becomes -dx/zoom
    assert camera.offset_x == -20.0
    assert camera.offset_y == -10.0

    up = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=2)
    assert handler.handle([up], camera) is True
    assert handler.panning is False


def test_delegates_to_controller_when_not_consumed():
    calls = {"count": 0}

    def _delegate(ev):
        calls["count"] += 1
        return True

    model, _, camera = make_env()
    controller = SimpleNamespace(handle_event=_delegate)
    handler = EntitiesEditorEventHandler(model, controller)

    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(0, 0))
    assert handler.handle([ev], camera) is True
    assert calls["count"] == 1
