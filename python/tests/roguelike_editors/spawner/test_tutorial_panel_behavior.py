import types
import pygame
import pytest

from roguelike_editors.spawner.spawner_toolbar.spawner_toolbar_controller import SpawnerToolbarController
from roguelike_editors.spawner.spawner_tutorial_panel import SpawnerTutorialPanelController
from roguelike_editors.spawner.spawner_editor_model import SpawnerEditorModel


class DummyEditor:
    def __init__(self):
        self.model = SpawnerEditorModel()
        # minimal editor view placeholder for tutorial controller
        self.view = types.SimpleNamespace()
        # toolbar controller wired to this editor
        self.spawner_toolbar = SpawnerToolbarController(self)
        # tutorial controller
        self.tutorial = SpawnerTutorialPanelController(self, self.view)
        # other attributes accessed defensively inside views
        self.instance_toolbar = types.SimpleNamespace(view=None)


@pytest.fixture(autouse=True)
def _init_pygame():
    pygame.init()
    yield
    pygame.event.clear()


def _tutorial_icon_center(toolbar_view) -> tuple[int, int]:
    # Ensure toolbar is constructed to read panel pos/metrics
    model = toolbar_view._last_model or getattr(toolbar_view, '_last_model_alt', None)
    if model is None:
        # Build one time for metrics (anchor defaults)
        toolbar_view.ensure_ready(types.SimpleNamespace(buttons=['spawner_instances','spawner_templates','tutorial_spawner','undo','redo']))
    tb = toolbar_view.toolbar
    # Compute center of 'tutorial_spawner' icon using toolbar metrics
    items = list(getattr(tb, 'items', []) or [])
    idx = items.index('tutorial_spawner')
    size = int(getattr(tb, 'size', 64) or 64)
    padding = int(getattr(tb, 'padding', 8) or 8)
    edge_padding = int(getattr(tb, 'edge_padding', 8) or 8)
    panel_pos = tb.panel.pos or (tb.x, tb.y)
    cx = panel_pos[0] + edge_padding + size // 2
    cy = panel_pos[1] + edge_padding + idx * (size + padding) + size // 2
    return (int(cx), int(cy))


def test_toolbar_click_activates_tutorial_and_persists():
    ed = DummyEditor()
    tb = ed.spawner_toolbar
    # Ensure toolbar is built before event for deterministic coords
    tb.view.ensure_ready(tb.model)
    pos = _tutorial_icon_center(tb.view)
    assert not ed.tutorial.is_active()

    e = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': pos, 'button': 1})
    consumed = tb.handle_event(e)
    assert consumed is True
    assert ed.tutorial.is_active() is True

    # Icon rects should be persisted for highlights
    icon_rects = getattr(getattr(tb.view, 'toolbar', None), 'icon_rects', {})
    assert 'tutorial_spawner' in icon_rects


def test_keyboard_T_toggles_tutorial():
    ed = DummyEditor()
    tb = ed.spawner_toolbar

    # Toggle ON
    e_on = pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_t})
    assert tb.handle_event(e_on) is True
    assert ed.tutorial.is_active() is True

    # Toggle OFF (ESC handled by tutorial events)
    e_esc = pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_ESCAPE})
    assert ed.tutorial.handle_event(e_esc) is True
    assert ed.tutorial.is_active() is False


def test_debounce_prevents_immediate_deactivate(monkeypatch):
    ed = DummyEditor()
    tut = ed.tutorial

    # Freeze time to a baseline, activate
    monkeypatch.setattr('pygame.time.get_ticks', lambda: 1000)
    tut.activate()
    assert tut.is_active() is True

    # Advance < 250 ms and request deactivate -> ignored
    monkeypatch.setattr('pygame.time.get_ticks', lambda: 1100)
    tut.deactivate()
    assert tut.is_active() is True

    # Advance beyond threshold -> deactivates
    monkeypatch.setattr('pygame.time.get_ticks', lambda: 1400)
    tut.deactivate()
    assert tut.is_active() is False


def test_orchestrator_auto_activates_when_toolbar_selected(monkeypatch):
    from roguelike_editors.spawner.controller.orchestrator import render as orchestrate_render
    # Dummy controller with minimal attributes
    ed = DummyEditor()
    ed.model.visible = True
    ed._instances_visible_last = False
    # Force toolbar active tool to tutorial
    ed.spawner_toolbar.model.active_tool = 'tutorial_spawner'
    # Provide a no-op view with render(screen)
    ed.view.render = lambda screen: None
    # Monkeypatch UI state functions used by orchestrator
    monkeypatch.setattr(
        'roguelike_editors.spawner.controller.orchestrator.compute_ui_state',
        lambda _c: types.SimpleNamespace(hold=False, placing_active=False, active_tool=None),
    )
    monkeypatch.setattr('roguelike_editors.spawner.controller.orchestrator.apply_ui_state', lambda _c, _s: None)

    screen = pygame.Surface((800, 600))
    assert not ed.tutorial.is_active()
    orchestrate_render(ed, screen)
    assert ed.tutorial.is_active() is True


def test_orchestrator_does_not_auto_deactivate_when_toolbar_clears(monkeypatch):
    from roguelike_editors.spawner.controller.orchestrator import render as orchestrate_render
    ed = DummyEditor()
    ed.model.visible = True
    ed.view.render = lambda screen: None
    # Activate tutorial first
    ed.tutorial.activate()
    assert ed.tutorial.is_active() is True
    # Clear toolbar selection
    ed.spawner_toolbar.model.active_tool = None
    # Monkeypatch UI state to minimal
    monkeypatch.setattr(
        'roguelike_editors.spawner.controller.orchestrator.compute_ui_state',
        lambda _c: types.SimpleNamespace(hold=False, placing_active=False, active_tool=None),
    )
    monkeypatch.setattr('roguelike_editors.spawner.controller.orchestrator.apply_ui_state', lambda _c, _s: None)

    screen = pygame.Surface((800, 600))
    orchestrate_render(ed, screen)
    # Tutorial should remain active (no auto-deactivate)
    assert ed.tutorial.is_active() is True
