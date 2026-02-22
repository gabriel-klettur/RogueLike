import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_events import TileToolbarEventHandler, Tool

@ pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def handler(monkeypatch):
    # Dummy controller with minimal methods
    ctrl = SimpleNamespace()
    # Editor state and toolbar state
    toolbar_state = SimpleNamespace(
        view_active=False,
        layers_view_open=False,
        show_collisions=False,
        show_collisions_overlay=False,
        collision_picker_open=False
    )
    picker_state = SimpleNamespace(open=True)
    editor_state = SimpleNamespace(
        toolbar_state=toolbar_state,
        current_tool=None,
        picker_state=picker_state
    )
    # Controller methods stub
    ctrl.editor_state = editor_state
    ctrl.delete_called = False
    ctrl.default_called = False
    def delete_tile(m): ctrl.delete_called = True
    def set_default(m): ctrl.default_called = True
    ctrl.delete_tile = delete_tile
    ctrl.set_default = set_default
    ctrl.editor_controller = SimpleNamespace(size_panel_controller=SimpleNamespace(state=SimpleNamespace(visible=False), show=lambda: setattr(ctrl.editor_state.toolbar_state, 'view_active', True), toggle=lambda: None))
    # Base icon_rects
    ctrl.icon_rects = {}
    # Stub stop_drag to avoid AttributeError
    ctrl.stop_drag = lambda: setattr(toolbar_state, 'dragging', False)
    # Setup drag defaults for start_drag
    toolbar_state.pos = None
    toolbar_state.dragging = False
    toolbar_state.drag_offset = (0, 0)
    # Controller position and size for panel drag calculations
    ctrl.x = 0
    ctrl.y = 0
    ctrl.size = 20
    ctrl.padding = 5
    # Setup drag defaults
    toolbar_state.pos = None
    toolbar_state.dragging = False
    toolbar_state.drag_offset = (0, 0)
    # Controller drag attributes
    ctrl.x = 0
    ctrl.y = 0
    ctrl.size = 20
    ctrl.padding = 5
    # Instantiate handler
    h = TileToolbarEventHandler(ctrl)
    return h, ctrl


def test_handle_click_non_left(handler):
    h, ctrl = handler
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=1, pos=(0, 0))
    assert not h.handle_click(ev, None)


def test_handle_click_outside_rect(handler):
    h, ctrl = handler
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(50, 50))
    assert not h.handle_click(ev, None)


def test_handle_click_delete_toggle(handler):
    h, ctrl = handler
    ctrl.icon_rects = {'delete': pygame.Rect(0, 0, 10, 10)}
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(5, 5))
    # First click: enter delete mode
    res = h.handle_click(ev, 'map')
    assert res is True
    assert ctrl.editor_state.current_tool == 'delete'
    assert ctrl.delete_called


def test_handle_click_default(handler):
    h, ctrl = handler
    ctrl.icon_rects = {'default': pygame.Rect(0, 0, 10, 10)}
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(5, 5))
    assert h.handle_click(ev, 'map')
    assert ctrl.editor_state.current_tool == 'default'
    assert ctrl.default_called


def test_handle_click_view_and_view_layers(handler):
    h, ctrl = handler
    # view
    ctrl.icon_rects = {'view': pygame.Rect(0, 0, 5, 5)}
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(2, 2))
    assert h.handle_click(ev, None)
    assert ctrl.editor_state.toolbar_state.view_active is True
    # view_layers
    ctrl.icon_rects = {'view_layers': pygame.Rect(0, 0, 5, 5)}
    ctrl.editor_state.toolbar_state.layers_view_open = False
    ev.pos = (2, 2)
    assert h.handle_click(ev, None)
    assert ctrl.editor_state.toolbar_state.layers_view_open is True


def test_handle_view_collisions(handler):
    h, ctrl = handler
    st = ctrl.editor_state.toolbar_state
    ctrl.icon_rects = {'view_collisions': pygame.Rect(0, 0, 5, 5)}
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(2, 2))
    # initial no collisions
    st.show_collisions = False
    st.show_collisions_overlay = False
    assert h.handle_click(ev, None)
    assert st.show_collisions is True
    assert st.collision_picker_open is True
    assert ctrl.editor_state.current_tool == 'brush'


def test_handle_select(handler):
    h, ctrl = handler
    ctrl.icon_rects = {'select': pygame.Rect(0, 0, 5, 5)}
    ctrl.editor_state.picker_state.open = True
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(2, 2))
    assert h.handle_click(ev, None)
    assert ctrl.editor_state.current_tool == 'select'
    assert ctrl.editor_state.picker_state.open is False


def test_handle_event_dragging_events(handler):
    h, ctrl = handler
    st = ctrl.editor_state.toolbar_state
    # Right mouse down start drag
    ev_down = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=3, pos=(10, 10))
    assert h.handle_event(ev_down)
    assert st.dragging is True
    # Mouse motion
    ev_motion = SimpleNamespace(type=pygame.MOUSEMOTION, pos=(12, 14))
    # Monkeypatch drag
    called = {}
    def fake_drag(pos): called['pos'] = pos
    ctrl.drag = fake_drag
    assert h.handle_event(ev_motion)
    assert called['pos'] == (12, 14)
    # Mouse up stop drag invokes stop_drag
    ev_up = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=3)
    st.dragging = True
    called2 = {}
    def fake_stop(): called2['stopped'] = True
    ctrl.stop_drag = fake_stop
    assert h.handle_event(ev_up)
    assert called2['stopped']
