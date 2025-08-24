import pytest


import pygame
from types import SimpleNamespace

from roguelike_game.managers.core.render_manager import RendererManager
from roguelike_editors.buildings.buildings_tool_bar_panel.buildings_tool_bar_panel_model import (
    BuildingsToolBarPanelModel,
)
from roguelike_editors.buildings.buildings_tool_bar_panel.buildings_tool_bar_panel_view import (
    BuildingsToolBarPanelView,
)
from roguelike_editors.buildings.buildings_tool_bar_panel.buildings_tool_bar_panel_events import (
    BuildingsToolBarPanelEventHandler,
)
from roguelike_editors.buildings.buildings_tool_bar_panel.buildings_tool_bar_panel_controller import (
    BuildingsToolBarPanelController,
)


@pytest.mark.skip(reason="[MGR-*] Manager/Renderer integration tests pending implementation.")
def test_manager_renderer_matrix_placeholder():
    """
    MGR-001..MGR-004 per README (renderer invokes BuildingEditorManager.render,
    toolbar centering uses panel width, toolbar toggles picker+add/remove and
    orchestrates colliders panel activation/deactivation).
    """
    assert True


def test_mgr_001_renderer_calls_manager_render_not_view(surface_factory, camera):
    """RendererManager must call buildings_editor.render(), not buildings_editor.view.render()."""

    class FakeView:
        def render(self, *args, **kwargs):  # pragma: no cover - should NOT be called
            raise AssertionError("Renderer should not call view.render")

    class FakeBuildingsEditor:
        def __init__(self):
            self.editor_state = SimpleNamespace(active=True)
            self.view = FakeView()
            self.render_called = False

        def render(self, screen, camera, buildings):
            self.render_called = True

    screen = surface_factory(800, 600)

    buildings_editor = FakeBuildingsEditor()
    tiles_editor = SimpleNamespace(editor_state=SimpleNamespace(active=False))
    map_editor = SimpleNamespace(editor_state=SimpleNamespace(active=False))
    entities = SimpleNamespace(buildings=[])

    # Minimal ECS/minimap stubs
    ecs = SimpleNamespace(
        ecs_world=SimpleNamespace(components={}, get_entities_with=lambda *a: [], player_position=(0, 0))
    )
    minimap = SimpleNamespace(render=lambda s: pygame.Rect(0, 0, 0, 0))

    rm = RendererManager(
        screen=screen,
        camera=camera,
        map=None,
        entities=entities,
        buildings_editor=buildings_editor,
        tiles_editor=tiles_editor,
        map_editor=map_editor,
        perf_log=None,
        minimap=minimap,
        ecs=ecs,
    )
    rm._last_state = SimpleNamespace()

    # Call only editors path
    rm._render_editors()

    assert buildings_editor.render_called is True


def test_mgr_002_toolbar_centering_uses_panel_width(monkeypatch, surface_factory):
    """Toolbar should center using actual panel width when no title rect available."""

    # Avoid disk IO for icons
    def _fake_load_image(path, scale=None):
        sz = scale[0] if scale else 64
        surf = pygame.Surface((sz, sz), pygame.SRCALPHA)
        surf.fill((123, 123, 123, 255))
        return surf

    monkeypatch.setattr(
        "roguelike_editors.buildings.buildings_tool_bar_panel.buildings_tool_bar_panel_view.load_image",
        _fake_load_image,
        raising=False,
    )

    model = BuildingsToolBarPanelModel()
    controller = SimpleNamespace(editor_view=None)  # no title -> fallback to centered
    view = BuildingsToolBarPanelView(controller, model)

    screen = surface_factory(1000, 600)
    view.render(screen)  # positions then draws

    panel_w = view.widget.panel.surface.get_width()
    expected_x = (screen.get_width() - panel_w) // 2
    assert view.widget.panel.pos == (expected_x, view.y)


def test_mgr_003_toolbar_buildings_manager_toggle(monkeypatch):
    """Clicking 'buildings_manager' activates Add/Remove + picker and deactivates colliders; clicking again closes them."""

    # Avoid disk IO for icons
    def _fake_load_image(path, scale=None):
        sz = scale[0] if scale else 64
        surf = pygame.Surface((sz, sz), pygame.SRCALPHA)
        return surf

    monkeypatch.setattr(
        "roguelike_editors.buildings.buildings_tool_bar_panel.buildings_tool_bar_panel_view.load_image",
        _fake_load_image,
        raising=False,
    )

    model = BuildingsToolBarPanelModel()

    class _AddRemove:
        def __init__(self):
            self._active = False
            self.activated = False
            self.deactivated = False

        def is_active(self):
            return self._active

        def activate(self):
            self._active = True
            self.activated = True

        def deactivate(self):
            self._active = False
            self.deactivated = True

    class _Colliders:
        def __init__(self):
            self._active = True
            self.deactivated = False
            self.activated = False

        def is_active(self):
            return self._active

        def activate(self):
            self._active = True
            self.activated = True

        def deactivate(self):
            self._active = False
            self.deactivated = True

    editor_state = SimpleNamespace(picker_active=False)
    editor_manager = SimpleNamespace(
        editor_state=editor_state,
        add_remove=_AddRemove(),
        colliders=_Colliders(),
    )

    view = BuildingsToolBarPanelView(None, model)
    events = BuildingsToolBarPanelEventHandler(None, model)
    controller = BuildingsToolBarPanelController(editor_manager, model, view, events)
    view.controller = controller
    events.controller = controller

    # Prepare clickable rect
    view.widget.icon_rects["buildings_manager"] = pygame.Rect(10, 10, 64, 64)

    # First click -> activate manager
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=(20, 20))
    assert events.handle_event(ev) is True
    assert model.active_tool == "buildings_manager"
    assert editor_state.picker_active is True
    assert editor_manager.add_remove.is_active() is True
    assert editor_manager.add_remove.activated is True
    # Colliders should be turned off
    assert editor_manager.colliders.deactivated is True

    # Second click -> deactivate manager and close picker/add_remove
    assert events.handle_event(ev) is True
    assert model.active_tool is None
    assert editor_state.picker_active is False
    assert editor_manager.add_remove.deactivated is True


def test_mgr_004_toolbar_colliders_toggle(monkeypatch):
    """Clicking 'buildings_colliders' toggles colliders panel and suppresses picker/add_remove when active."""

    # Avoid disk IO for icons
    def _fake_load_image(path, scale=None):
        sz = scale[0] if scale else 64
        surf = pygame.Surface((sz, sz), pygame.SRCALPHA)
        return surf

    monkeypatch.setattr(
        "roguelike_editors.buildings.buildings_tool_bar_panel.buildings_tool_bar_panel_view.load_image",
        _fake_load_image,
        raising=False,
    )

    model = BuildingsToolBarPanelModel()

    class _AddRemove:
        def __init__(self):
            self._active = True
            self.deactivated = False

        def is_active(self):
            return self._active

        def deactivate(self):
            self._active = False
            self.deactivated = True

        def activate(self):
            self._active = True

    class _Colliders:
        def __init__(self):
            self._active = False
            self.activated = False
            self.deactivated = False

        def is_active(self):
            return self._active

        def activate(self):
            self._active = True
            self.activated = True

        def deactivate(self):
            self._active = False
            self.deactivated = True

    editor_state = SimpleNamespace(picker_active=True)
    editor_manager = SimpleNamespace(
        editor_state=editor_state,
        add_remove=_AddRemove(),
        colliders=_Colliders(),
    )

    view = BuildingsToolBarPanelView(None, model)
    events = BuildingsToolBarPanelEventHandler(None, model)
    controller = BuildingsToolBarPanelController(editor_manager, model, view, events)
    view.controller = controller
    events.controller = controller

    # Prepare clickable rect
    view.widget.icon_rects["buildings_colliders"] = pygame.Rect(20, 20, 64, 64)

    # First click -> activate colliders, disable picker and add/remove
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=(25, 25))
    assert events.handle_event(ev) is True
    assert model.active_tool == "buildings_colliders"
    assert editor_state.picker_active is False
    assert editor_manager.colliders.is_active() is True
    assert editor_manager.colliders.activated is True
    assert editor_manager.add_remove.deactivated is True

    # Second click -> deactivate colliders
    assert events.handle_event(ev) is True
    assert model.active_tool is None
    assert editor_manager.colliders.deactivated is True
