import pygame
import pytest

from roguelike_game.managers.core.render.map_renderer import render_map
from roguelike_engine.map.model.layer import Layer


@pytest.fixture(autouse=True)
def _pygame_init_teardown():
    pygame.init()
    try:
        yield
    finally:
        pygame.quit()


class ViewStub:
    def __init__(self):
        self.invalidate_calls = 0
        self.layers_keys_seen = None

    def invalidate_cache(self):
        self.invalidate_calls += 1

    def render(self, screen, camera, map_model):
        # Capture which layers are visible at render-time
        self.layers_keys_seen = tuple(sorted(map_model.layers.keys(), key=lambda l: l.value))
        return []


class MapStub:
    def __init__(self):
        # 2x2 map
        self.matrix = ["..", ".."]
        # Full layers (codes)
        self.layers = {
            Layer.Ground: [["floor", "floor"], ["floor", "floor"]],
            Layer.ObjectsLow: [["", ""], ["", ""]],
        }
        # Tiles by layer (can be any structure, not used directly here)
        self.tiles_by_layer = dict(self.layers)
        self.view = ViewStub()


class CameraStub:
    zoom = 1.0
    offset_x = 0
    offset_y = 0


class MapEditorStateStub:
    def __init__(self, visible_layers: dict):
        self.active = True
        self.visible_layers = visible_layers


class MapEditorStub:
    def __init__(self, visible_layers):
        self.editor_state = MapEditorStateStub(visible_layers)


class TilesEditorStateStub:
    def __init__(self):
        self.active = False
        class ToolbarState:
            show_collisions = False
            show_collisions_overlay = False
            visible_layers = {layer: True for layer in Layer}
        self.toolbar_state = ToolbarState()


class TilesEditorStub:
    def __init__(self):
        self.editor_state = TilesEditorStateStub()


class ManagerStub:
    def __init__(self, visible_layers):
        self.map_editor = MapEditorStub(visible_layers)
        self.tiles_editor = TilesEditorStub()
        self._last_map_visible_layers = {}
        self._last_visible_layers = {}

    # Collision rendering path is not used when map editor is active
    def _render_collisions(self, screen, camera, map_):
        return []


def test_map_editor_filters_layers_and_invalidates_once():
    screen = pygame.Surface((64, 64))
    camera = CameraStub()
    m = MapStub()

    visible = {layer: True for layer in Layer}
    # Hide Ground layer
    visible[Layer.Ground] = False

    manager = ManagerStub(visible)

    # First render: should invalidate cache and only pass non-hidden layers to view
    dirty = render_map(manager, camera, screen, m)
    assert dirty == []
    assert m.view.invalidate_calls == 1
    assert m.view.layers_keys_seen == (Layer.ObjectsLow,)

    # After render, original map_.layers must be restored
    assert set(m.layers.keys()) == {Layer.Ground, Layer.ObjectsLow}

    # Second render with same visibility: no extra invalidation
    dirty = render_map(manager, camera, screen, m)
    assert m.view.invalidate_calls == 1
    assert m.view.layers_keys_seen == (Layer.ObjectsLow,)


def test_map_editor_show_all_layers():
    screen = pygame.Surface((64, 64))
    camera = CameraStub()
    m = MapStub()

    visible = {layer: True for layer in Layer}
    manager = ManagerStub(visible)

    render_map(manager, camera, screen, m)
    # Both layers should be present during render
    assert m.view.layers_keys_seen == (Layer.Ground, Layer.ObjectsLow)
