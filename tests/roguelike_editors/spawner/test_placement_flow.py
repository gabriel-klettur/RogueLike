import pygame
import types
import builtins
import pytest

from roguelike_editors.spawner.events.handlers.mouse_left import handle_mousedown_left
from roguelike_editors.spawner.controller.placement import begin_place_template
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
        self.spawner_instances = types.SimpleNamespace(refresh_from_disk=lambda: None)
        self.spawner_toolbar = types.SimpleNamespace(model=types.SimpleNamespace(active_tool=None))
        self.instance_toolbar = types.SimpleNamespace(model=types.SimpleNamespace(add_mode_active=False, add_templates=[]))
        self.view = types.SimpleNamespace()
        self.instance_properties = types.SimpleNamespace()
        self.game = types.SimpleNamespace(ecs=types.SimpleNamespace(ecs_world=DummyWorld()))


class DummyHandler:
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model


@pytest.fixture(autouse=True)
def _ensure_pygame_events_clean():
    yield
    pygame.event.clear()


def test_first_click_after_plus_is_consumed(monkeypatch):
    c = DummyController()
    h = DummyHandler(c)
    ctx = DummyCtx(c.game.ecs.ecs_world)

    # Enter placement via begin_place_template (sets placing_template_id and skip_first_placement_click)
    begin_place_template(c, template_id='valeria_tpl')
    assert c.model.placing_template_id == 'valeria_tpl'
    assert getattr(c.model, 'skip_first_placement_click', False) is True

    # First click must be consumed and clear the skip flag
    e1 = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': (100, 100), 'button': 1})
    handled = handle_mousedown_left(h, ctx, e1)
    assert handled is True
    assert getattr(c.model, 'skip_first_placement_click', False) is False
    # No entity created yet
    assert c.game.ecs.ecs_world.components['SpawnerConfig'] == {}
    assert c.game.ecs.ecs_world.components['SpawnerState'] == {}


def test_second_click_places_entity(monkeypatch):
    c = DummyController()
    h = DummyHandler(c)
    ctx = DummyCtx(c.game.ecs.ecs_world)

    # minimal in-memory persistence for instances
    instances_store = []

    def _screen_to_tile(camera, mx, my):
        return (110, 112)

    def _zone_for_global_tile(tx, ty):
        return 'zone_100_50'

    def _load_instances_json():
        return list(instances_store)

    def _write_instances_json(arr):
        instances_store.clear()
        instances_store.extend(arr)

    def _find_instance_in_json(tpl, zone, tile):
        for idx, it in enumerate(instances_store):
            if it.get('template_id') == tpl and it.get('zone') == zone and tuple(it.get('tile')) == tuple(tile):
                return None, idx, None
        return None, None, None

    def _load_spawners_json():
        return [{'id': 'valeria_tpl', 'waves': [], 'trigger': {}, 'policy': {}}]

    # Monkeypatch services used by handler
    monkeypatch.setattr('roguelike_editors.spawner.events.handlers.mouse_left.screen_to_tile', _screen_to_tile)
    monkeypatch.setattr('roguelike_editors.spawner.events.handlers.mouse_left.zone_for_global_tile', _zone_for_global_tile)
    monkeypatch.setattr('roguelike_editors.spawner.events.handlers.mouse_left.load_instances_json', _load_instances_json)
    monkeypatch.setattr('roguelike_editors.spawner.events.handlers.mouse_left.write_instances_json', _write_instances_json)
    monkeypatch.setattr('roguelike_editors.spawner.events.handlers.mouse_left.find_instance_in_json', _find_instance_in_json)
    monkeypatch.setattr('roguelike_editors.spawner.events.handlers.mouse_left.load_spawners_json', _load_spawners_json)

    # Also ensure load_waves returns empty dict to satisfy resolver
    monkeypatch.setattr('roguelike_editors.spawner.events.handlers.mouse_left.load_waves', lambda: {})
    # Track visuals auto-repair invocation
    calls = {'auto_repair': 0}
    def _auto_repair(world, eid, cfg, inst):
        calls['auto_repair'] += 1
    monkeypatch.setattr('roguelike_editors.spawner.events.handlers.mouse_left.auto_repair_state_visuals', _auto_repair, raising=False)

    # Enter placement and consume first click
    begin_place_template(c, template_id='valeria_tpl')
    e1 = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': (10, 10), 'button': 1})
    handle_mousedown_left(h, ctx, e1)

    # Second click places at map; verify ECS components assigned once
    e2 = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': (200, 200), 'button': 1})
    handled2 = handle_mousedown_left(h, ctx, e2)
    assert handled2 is True
    cfgs = c.game.ecs.ecs_world.components['SpawnerConfig']
    sts = c.game.ecs.ecs_world.components['SpawnerState']
    assert len(cfgs) == 1 and len(sts) == 1
    assert calls['auto_repair'] >= 1
    # Placement mode ends
    assert c.model.placing_template_id is None
