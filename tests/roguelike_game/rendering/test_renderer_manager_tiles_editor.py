import types
import pygame
import pytest

import roguelike_game.managers.core.render_manager as rm_module
from roguelike_game.managers.core.render_manager import RendererManager


class DummyCamera:
    def __init__(self):
        self.zoom = 1.0
        self.offset_x = 0
        self.offset_y = 0
    def apply(self, pos):
        return pos


class DummyMinimap:
    def render(self, screen):
        return pygame.Rect(0, 0, 1, 1)


class DummyMenu:
    show_menu = False
    def draw(self, screen):
        return pygame.Rect(0, 0, 1, 1)


class DummyECS:
    def __init__(self):
        self.ecs_world = types.SimpleNamespace(
            components={},
            get_entities_with=lambda *a, **k: [],
            player_position=(0, 0),
        )


@pytest.fixture
def screen():
    return pygame.Surface((64, 64))


@pytest.fixture
def camera():
    return DummyCamera()


@pytest.fixture(autouse=True)
def patch_render_diagnostics(monkeypatch):
    # Avoid drawing the diagnostics overlay in tests
    monkeypatch.setattr(rm_module, "render_diagnostics_overlay", lambda *a, **k: None)


def _make_manager(monkeypatch):
    mgr = object.__new__(RendererManager)
    # Minimal attributes used by render_game/_render_editors/_render_map
    mgr.screen = pygame.Surface((64, 64))
    mgr.camera = DummyCamera()
    mgr.map = types.SimpleNamespace()  # will be replaced per test
    mgr.entities = types.SimpleNamespace(buildings=[])

    # Editors
    tiles_toolbar_state = types.SimpleNamespace(
        show_collisions=False,
        show_collisions_overlay=False,
        visible_layers={},
        show_buildings=True,
    )
    tiles_editor_state = types.SimpleNamespace(
        active=True,
        current_tool="brush",
        toolbar_state=tiles_toolbar_state,
    )
    mgr.tiles_editor = types.SimpleNamespace(
        editor_state=tiles_editor_state,
        view=types.SimpleNamespace(render=lambda *a, **k: None),
    )
    mgr.buildings_editor = types.SimpleNamespace(editor_state=types.SimpleNamespace(active=False, show_buildings=True), render=lambda *a, **k: None)
    mgr.map_editor = types.SimpleNamespace(editor_state=types.SimpleNamespace(active=False, visible_layers={}), render=lambda *a, **k: None)

    # Other systems
    mgr._dirty_rects = []
    mgr.diagnostics_overlay = object()
    mgr.zone_view = object()
    mgr.minimap = DummyMinimap()
    mgr.ecs = DummyECS()

    # Help overlay cache
    mgr._help_overlay_key = None
    mgr._help_overlay_surf = (pygame.Surface((1, 1), pygame.SRCALPHA), pygame.Rect(0, 0, 1, 1))

    # Internal caches
    mgr._last_visible_layers = None
    mgr._last_map_visible_layers = None
    mgr._collision_last_zoom = None
    mgr._collision_font = None
    mgr._collision_surf_solid = None
    mgr._collision_surf_walkable = None

    # Optional debug systems
    mgr._hitbox_debug_system = None
    mgr._spell_debug_system = None
    mgr._patrol_debug_system = None
    mgr._defend_debug_system = None

    return mgr


def test_render_game_uses_current_map_for_brush_rerender(monkeypatch, screen, camera):
    mgr = _make_manager(monkeypatch)

    old_map = types.SimpleNamespace(name="old", tiles_by_layer={}, tiles=[[types.SimpleNamespace(solid=False)]]*1, layers={})
    new_map = types.SimpleNamespace(name="new", tiles_by_layer={}, tiles=[[types.SimpleNamespace(solid=False)]]*1, layers={})

    mgr.map = old_map

    called_maps = []
    def fake_render_map(cam, scr, m):
        called_maps.append(m)
        return None
    mgr._render_map = fake_render_map

    # Avoid entity rendering in this test
    mgr._render_z_entities = lambda *a, **k: None
    mgr._render_tile_editor_layer = lambda *a, **k: None
    mgr._render_minimap = lambda *a, **k: None
    mgr._render_menu = lambda *a, **k: None

    menu = DummyMenu()
    state = types.SimpleNamespace()

    # Call render_game with the NEW map; expectation: all render-map calls use new_map
    mgr.render_game(state, screen, camera, perf_log=None, menu=menu, map=new_map, entities=types.SimpleNamespace(buildings=[]))

    assert called_maps, "_render_map should have been called at least once"
    assert all(m is new_map for m in called_maps), "Renderer should use the current map (new_map) after sync, never the old reference"
    assert mgr.map is new_map, "RendererManager.map should be synced to the new map inside render_game"


def test_render_map_collision_only_skips_tiles(monkeypatch):
    mgr = _make_manager(monkeypatch)
    # Enable collision-only mode
    mgr.tiles_editor.editor_state.toolbar_state.show_collisions = True
    mgr.tiles_editor.editor_state.toolbar_state.show_collisions_overlay = False

    # Map with a view that would fail if called
    class FailView:
        def render(self, *a, **k):
            pytest.fail("view.render should not be called in collision-only mode")
        def update_chunks(self, *a, **k):
            pass
        def invalidate_cache(self):
            pass
    m = types.SimpleNamespace(tiles=[[types.SimpleNamespace(solid=False)]], tiles_by_layer={}, layers={}, view=FailView())

    called = {"collisions": 0}
    def fake_collisions(screen, camera, map):
        called["collisions"] += 1
        return []
    mgr._render_collisions = fake_collisions

    mgr._render_map(mgr.camera, mgr.screen, m)
    assert called["collisions"] == 1, "Collision grid should be rendered when in collision-only mode"


def test_visible_layers_change_invalidates_cache(monkeypatch):
    mgr = _make_manager(monkeypatch)
    mgr.tiles_editor.editor_state.active = True

    inv_calls = {"count": 0}
    class View:
        def render(self, *a, **k):
            return []
        def update_chunks(self, *a, **k):
            pass
        def invalidate_cache(self):
            inv_calls["count"] += 1
    layers = {"Ground": [[""]], "Objects": [[""]]}
    m = types.SimpleNamespace(tiles=[[types.SimpleNamespace(solid=False)]], tiles_by_layer={}, layers=layers, view=View())

    # First render with visible layers (should invalidate once)
    mgr.tiles_editor.editor_state.toolbar_state.visible_layers = {"Ground": True, "Objects": True}
    mgr._last_visible_layers = {"Ground": False, "Objects": True}
    mgr._render_map(mgr.camera, mgr.screen, m)
    assert inv_calls["count"] == 1

    # Second render with same visibility should NOT invalidate
    mgr._render_map(mgr.camera, mgr.screen, m)
    assert inv_calls["count"] == 1, "No extra invalidation expected if visibility did not change"
