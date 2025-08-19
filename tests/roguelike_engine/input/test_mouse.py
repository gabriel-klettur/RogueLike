import os
import types
import pygame
import pytest

from roguelike_engine.input.mouse import handle_mouse
from roguelike_engine.input import events as engine_events


@pytest.fixture(autouse=True, scope="module")
def _init_pygame():
    os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
    pygame.init()
    yield
    pygame.quit()


def make_state():
    return types.SimpleNamespace(mmb_panning=False)


def make_camera():
    return types.SimpleNamespace(offset_x=0.0, offset_y=0.0, zoom=1.0)


def test_mmb_pan_starts_and_moves_when_enabled():
    state = make_state()
    cam = make_camera()
    # Start pan (simulate editor-active context via mmb_pan_enabled=True)
    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 2, "pos": (100, 100)})
    handle_mouse(ev_down, state, cam, None, None, None, mmb_pan_enabled=True)
    assert state.mmb_panning is True
    assert getattr(state, 'mmb_start', None) == (100, 100)
    # Move pan
    ev_move = pygame.event.Event(pygame.MOUSEMOTION, {"pos": (120, 130)})
    handle_mouse(ev_move, state, cam, None, None, None, mmb_pan_enabled=True)
    # dx=20, dy=30 => camera moves -20, -30
    assert cam.offset_x == pytest.approx(-20.0)
    assert cam.offset_y == pytest.approx(-30.0)
    # End pan
    ev_up = pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 2})
    handle_mouse(ev_up, state, cam, None, None, None, mmb_pan_enabled=True)
    assert state.mmb_panning is False


def test_mmb_pan_cancels_when_disabled_mid_pan():
    state = make_state()
    cam = make_camera()
    # Begin with enabled
    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 2, "pos": (50, 50)})
    handle_mouse(ev_down, state, cam, None, None, None, mmb_pan_enabled=True)
    assert state.mmb_panning is True
    # Now context disables mid-pan; motion should cancel and not move
    ev_move = pygame.event.Event(pygame.MOUSEMOTION, {"pos": (70, 80)})
    handle_mouse(ev_move, state, cam, None, None, None, mmb_pan_enabled=False)
    assert state.mmb_panning is False
    # Camera did not change because cancel occurs before applying movement
    assert cam.offset_x == pytest.approx(0.0)
    assert cam.offset_y == pytest.approx(0.0)


def test_wheel_zoom_limits():
    state = make_state()
    cam = make_camera()
    # Scroll up increases zoom up to 2.0
    for _ in range(20):
        ev = pygame.event.Event(pygame.MOUSEWHEEL, {"y": 1})
        handle_mouse(ev, state, cam, None, None, None, mmb_pan_enabled=True)
    assert cam.zoom <= 2.0
    # Scroll down decreases zoom down to 0.5
    for _ in range(50):
        ev = pygame.event.Event(pygame.MOUSEWHEEL, {"y": -1})
        handle_mouse(ev, state, cam, None, None, None, mmb_pan_enabled=True)
    assert cam.zoom >= 0.5


def test_engine_events_mmb_enabled_only_in_editors():
    # Build minimal dummies for editors
    class DummyHandler:
        def handle(self, *args, **kwargs):
            pass
    class DummyEditor:
        def __init__(self, active=False):
            self.editor_state = types.SimpleNamespace(active=active)
            self.handler = DummyHandler()
        def toggle(self):
            self.editor_state.active = not self.editor_state.active
    class DummySpawner:
        def __init__(self, visible=False):
            self.model = types.SimpleNamespace(visible=visible)
    state = make_state()
    cam = make_camera()
    menu = types.SimpleNamespace()
    tile_ed = DummyEditor(active=False)
    bld_ed = DummyEditor(active=False)
    map_ed = DummyEditor(active=False)
    spawner = DummySpawner(visible=False)
    game_map = object()
    entities = object()

    # Gameplay: editors inactive -> MMB should NOT start panning (reserved for gameplay actions)
    evs = [
        pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 2, "pos": (10, 10)}),
    ]
    engine_events.handle_events(
        state, cam, None, menu, game_map, entities, tile_ed, bld_ed, map_ed, spawner, evs
    )
    assert getattr(state, 'mmb_panning', False) is False

    # Editors active -> MMB SHOULD start panning
    state.mmb_panning = False
    tile_ed.editor_state.active = True
    evs2 = [
        pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 2, "pos": (10, 10)}),
    ]
    engine_events.handle_events(
        state, cam, None, menu, game_map, entities, tile_ed, bld_ed, map_ed, spawner, evs2
    )
    assert getattr(state, 'mmb_panning', False) is True


def test_engine_events_mmb_enabled_when_spawner_visible():
    class DummyHandler:
        def handle(self, *args, **kwargs):
            pass
    class DummyEditor:
        def __init__(self, active=False):
            self.editor_state = types.SimpleNamespace(active=active)
            self.handler = DummyHandler()
    class DummySpawner:
        def __init__(self, visible=False):
            self.model = types.SimpleNamespace(visible=visible)
    state = make_state()
    cam = make_camera()
    menu = types.SimpleNamespace()
    tile_ed = DummyEditor(active=False)
    bld_ed = DummyEditor(active=False)
    map_ed = DummyEditor(active=False)
    spawner = DummySpawner(visible=True)
    game_map = object()
    entities = object()

    # With only spawner visible, MMB should enable panning
    evs = [
        pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 2, "pos": (5, 5)}),
    ]
    engine_events.handle_events(
        state, cam, None, menu, game_map, entities, tile_ed, bld_ed, map_ed, spawner, evs
    )
    assert getattr(state, 'mmb_panning', False) is True
