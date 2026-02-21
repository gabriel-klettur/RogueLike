import pygame
import types
import pytest

from roguelike_editors.spawner.controller.placement import begin_place_template
from roguelike_editors.spawner.events.handlers.mouse_left import handle_mousedown_left
from roguelike_editors.spawner.controller import orchestrator
from roguelike_editors.spawner.spawner_editor_model import SpawnerEditorModel


class DummyWorld:
    def __init__(self):
        self._next = 1
        self.components = {'SpawnerConfig': {}, 'SpawnerState': {}}
        self.buildings = []
        self.state = types.SimpleNamespace()

    def create_entity(self):
        eid = self._next
        self._next += 1
        return eid


class DummyCtx:
    def __init__(self, world):
        self.world = world
        self.camera = None


class DummyController:
    def __init__(self):
        self.model = SpawnerEditorModel()
        self.view = types.SimpleNamespace(render=lambda screen: None)
        self.tutorial = types.SimpleNamespace(render=lambda screen: None, handle_event=lambda e: False)
        self.spawner_toolbar = types.SimpleNamespace(model=types.SimpleNamespace(active_tool=None))
        self.instance_toolbar = types.SimpleNamespace(model=types.SimpleNamespace(visible=False))
        self.spawner_manager = types.SimpleNamespace(model=types.SimpleNamespace(visible=False), handle_event=lambda e: False, set_visible=lambda v: None)
        self.spawner_instances = types.SimpleNamespace(model=types.SimpleNamespace(visible=False), handle_event=lambda e: False, get_selected_instance=lambda: None, refresh_from_disk=lambda: None)
        self.instance_properties = types.SimpleNamespace(model=types.SimpleNamespace(visible=False), handle_event=lambda e: False)
        self.events = types.SimpleNamespace(handle_event=lambda e: False)
        self.game = types.SimpleNamespace(ecs=types.SimpleNamespace(ecs_world=DummyWorld()), camera=None)
        self._instances_visible_last = False
        self._manager_visible_last = False


def test_cursor_crosshair_then_restored(monkeypatch):
    set_calls = []
    def _set_cursor(cur):
        set_calls.append(cur)
    monkeypatch.setattr(pygame.mouse, 'set_cursor', _set_cursor, raising=False)

    c = DummyController()
    begin_place_template(c, 'tpl_valeria')
    assert pygame.SYSTEM_CURSOR_CROSSHAIR in set_calls

    # Prepare placement dependencies
    ctx = DummyCtx(c.game.ecs.ecs_world)
    monkeypatch.setattr('roguelike_editors.spawner.events.handlers.mouse_left.screen_to_tile', lambda camera, mx, my: (10, 10))
    monkeypatch.setattr('roguelike_editors.spawner.events.handlers.mouse_left.zone_for_global_tile', lambda tx, ty: 'zone_100_50')
    store = []
    monkeypatch.setattr('roguelike_editors.spawner.events.handlers.mouse_left.load_instances_json', lambda: list(store))
    def _write(arr):
        store.clear(); store.extend(arr)
    monkeypatch.setattr('roguelike_editors.spawner.events.handlers.mouse_left.write_instances_json', _write)
    monkeypatch.setattr('roguelike_editors.spawner.events.handlers.mouse_left.find_instance_in_json', lambda *a, **k: (None, 0 if store else None, None))
    monkeypatch.setattr('roguelike_editors.spawner.events.handlers.mouse_left.load_spawners_json', lambda: [{'id': 'tpl_valeria', 'waves': [], 'trigger': {}, 'policy': {}}])
    monkeypatch.setattr('roguelike_editors.spawner.events.handlers.mouse_left.load_waves', lambda: {})

    # Consume first click
    e1 = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': (1, 1), 'button': 1})
    handle_mousedown_left(types.SimpleNamespace(controller=c, model=c.model), ctx, e1)
    # Second click places and should restore arrow
    e2 = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': (2, 2), 'button': 1})
    handle_mousedown_left(types.SimpleNamespace(controller=c, model=c.model), ctx, e2)
    assert pygame.SYSTEM_CURSOR_ARROW in set_calls


def test_overlay_draws_cyan_circle(monkeypatch):
    drawn = []
    def _circle(surface, color, center, radius, width):
        drawn.append((color, center, radius, width))
    monkeypatch.setattr(pygame.draw, 'circle', _circle, raising=False)

    c = DummyController()
    c.model.visible = True
    c.model.placing_template_id = 'tpl_valeria'
    screen = pygame.Surface((800, 600))

    orchestrator.render(c, screen)

    assert any(col == (0, 255, 255) for col, *_ in drawn)
