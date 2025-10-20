import pygame
import types
import pytest
from roguelike_game.ecs.components.spawn.spawn_request import SpawnRequest
from roguelike_game.ecs.components.spawner.spawner_child import SpawnerChild
from roguelike_editors.spawner.spawner_instances_panel.spawner_list_instances_controller import SpawnerListInstancesController

from roguelike_editors.spawner.spawner_editor_model import SpawnerEditorModel
from roguelike_editors.spawner.events.handlers.mouse_left import handle_mousedown_left
from roguelike_editors.spawner.events.handlers import helpers as h_helpers
from roguelike_editors.spawner.spawner_instance_toolbar.spawner_instance_toolbar_controller import (
    SpawnerInstanceToolbarController,
)


class DummyWorld:
    def __init__(self):
        self.components = {
            'SpawnerConfig': {},
            'SpawnerState': {},
        }
        self.entities = []
        self.buildings = []
        self.state = types.SimpleNamespace()
        self._spatial_invalidated = False

    def create_entity(self):
        eid = (max(self.entities) + 1) if self.entities else 1
        self.entities.append(eid)
        return eid

    def remove_entity(self, eid):
        if eid in self.entities:
            self.entities.remove(eid)
        for comp in self.components.values():
            try:
                comp.pop(eid, None)
            except Exception:
                pass

    def invalidate_spatial_index(self):
        self._spatial_invalidated = True


class DummyCtx:
    def __init__(self, world, controller):
        self.world = world
        self.controller = controller
        self.camera = types.SimpleNamespace(zoom=1.0, apply=lambda p: p)
        self.model = controller.model


class DummyHandler:
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model


class DummyController:
    def __init__(self):
        self.model = SpawnerEditorModel()
        self.spawner_instances = types.SimpleNamespace(refresh_from_disk=lambda: None)
        self.spawner_toolbar = types.SimpleNamespace(model=types.SimpleNamespace(active_tool=None))
        self.instance_toolbar = types.SimpleNamespace(model=types.SimpleNamespace(add_mode_active=False, add_templates=[]))
        self.view = types.SimpleNamespace()
        self.instance_properties = types.SimpleNamespace(model=types.SimpleNamespace(visible=False))
        self.game = types.SimpleNamespace(ecs=types.SimpleNamespace(ecs_world=DummyWorld()))


@pytest.fixture(autouse=True)
def _ensure_pygame_events_clean():
    yield
    pygame.event.clear()


def test_remove_mode_pick_sets_pending_delete(monkeypatch):
    ctrl = DummyController()
    h = DummyHandler(ctrl)
    ctx = DummyCtx(ctrl.game.ecs.ecs_world, ctrl)

    # Prepare a spawner entity in the world
    world = ctx.world
    eid = world.create_entity()
    cfg = types.SimpleNamespace(
        template_id='tpl_x',
        zone='lobby',
        anchor_tile=(100, 100),
    )
    world.components['SpawnerConfig'][eid] = cfg
    world.components['SpawnerState'][eid] = types.SimpleNamespace()

    # Force remove mode
    h.model.remove_mode_active = True

    # Monkeypatch picking to return our eid
    monkeypatch.setattr(
        'roguelike_editors.spawner.events.handlers.mouse_left.pick_spawner_under_cursor',
        lambda world, camera, mx, my: eid,
    )

    # Click anywhere -> should prepare pending_delete_confirm
    e = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': (10, 10), 'button': 1})
    handled = handle_mousedown_left(h, ctx, e)
    assert handled is True
    p = h.model.pending_delete_confirm
    assert isinstance(p, dict)
    assert p.get('eid') == eid
    assert p.get('template_id') == 'tpl_x'
    assert p.get('zone') == 'lobby'
    assert isinstance(p.get('local_tile'), tuple)


def test_delete_confirm_removes_entity_and_visuals_runtime(monkeypatch):
    ctrl = DummyController()
    h = DummyHandler(ctrl)
    ctx = DummyCtx(ctrl.game.ecs.ecs_world, ctrl)

    # World with one spawner entity and two visuals buildings
    world = ctx.world
    eid = world.create_entity()
    inst_id = 'my_spawner_inst_1'
    cfg = types.SimpleNamespace(template_id='tpl_x', zone='lobby', anchor_tile=(100, 100))
    world.components['SpawnerConfig'][eid] = cfg
    world.components['SpawnerState'][eid] = types.SimpleNamespace()

    # Two visual buildings tied to the spawner
    b1 = types.SimpleNamespace(id=372, _is_spawner_visual=True, spawner_instance_id=inst_id)
    b2 = types.SimpleNamespace(id=373, _is_spawner_visual=True, spawn_id=inst_id)
    world.buildings.extend([b1, b2])

    # In-memory spawners_instances
    store = [{
        'id': inst_id,
        'template_id': 'tpl_x',
        'zone': 'lobby',
        'tile': [11, 11],
        'visuals': {
            'Finished': {'instance_id': 372},
            'WaitClear': {'instance_id': 373},
        },
    }]

    # Prepare pending_delete_confirm as if LMB remove prepared it
    h.model.pending_delete_confirm = {
        'eid': eid,
        'template_id': 'tpl_x',
        'zone': 'lobby',
        'local_tile': (11, 11),
    }

    # Monkeypatch confirmations persistence helpers
    def _find_instance_in_json(tpl_id, zone, local_tile):
        for i, e in enumerate(store):
            if e.get('template_id') == tpl_id and e.get('zone') == zone and tuple(e.get('tile')) == tuple(local_tile):
                return store, i, None
        return store, None, None

    def _write_instances_json(data):
        store.clear()
        store.extend(data)

    monkeypatch.setattr('roguelike_editors.spawner.events.confirmations.find_instance_in_json', _find_instance_in_json)

    # KeyDown Y should accept and remove both entity and visuals in runtime
    e = pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_y})
    handled = h_helpers.handle_keydown(h, ctx, e)
    assert handled is True

    # Store should be empty
    assert len(store) == 1
    # Entity removed from world
    assert eid not in world.entities
    assert eid not in world.components['SpawnerConfig']
    # Visual buildings removed and spatial index invalidated
    assert world.buildings == []
    assert world._spatial_invalidated is True


def test_remove_mode_toggle_debounce(monkeypatch):
    # Editor with toolbar controller to test debouncing
    editor = types.SimpleNamespace(
        model=SpawnerEditorModel(),
        spawner_toolbar=types.SimpleNamespace(model=types.SimpleNamespace(active_tool='spawner_instances')),
        game=types.SimpleNamespace(ecs=types.SimpleNamespace(ecs_world=DummyWorld())),
    )
    ctrl = SpawnerInstanceToolbarController(editor)

    # Freeze time and toggle on
    t0 = 1000.0
    monkeypatch.setattr('roguelike_editors.spawner.spawner_instance_toolbar.spawner_instance_toolbar_controller.time.time', lambda: t0)
    ctrl.on_remove_spawner()
    assert editor.model.remove_mode_active is True

    # Within debounce window (700ms): toggle should be ignored
    monkeypatch.setattr('roguelike_editors.spawner.spawner_instance_toolbar.spawner_instance_toolbar_controller.time.time', lambda: t0 + 0.3)
    ctrl.on_remove_spawner()
    assert editor.model.remove_mode_active is True  # unchanged

    # After debounce: toggle off
    monkeypatch.setattr('roguelike_editors.spawner.spawner_instance_toolbar.spawner_instance_toolbar_controller.time.time', lambda: t0 + 1.1)
    ctrl.on_remove_spawner()
    assert editor.model.remove_mode_active is False


def test_delete_confirm_removes_requests_and_children(monkeypatch):
    ctrl = DummyController()
    h = DummyHandler(ctrl)
    ctx = DummyCtx(ctrl.game.ecs.ecs_world, ctrl)

    world = ctx.world
    eid = world.create_entity()
    inst_id = 'inst_rm_1'
    cfg = types.SimpleNamespace(template_id='tpl_y', zone='lobby', anchor_tile=(50, 50))
    world.components['SpawnerConfig'][eid] = cfg
    world.components['SpawnerState'][eid] = types.SimpleNamespace()

    req_eid = world.create_entity()
    world.components.setdefault('SpawnRequest', {})[req_eid] = SpawnRequest(
        prototype='goblin', position=(5, 6), spawner_eid=eid, wave_idx=0
    )
    npc_eid = world.create_entity()
    world.components.setdefault('SpawnerChild', {})[npc_eid] = SpawnerChild(eid, 0)

    h.model.pending_delete_confirm = {
        'eid': eid,
        'template_id': 'tpl_y',
        'zone': 'lobby',
        'local_tile': (1, 1),
    }

    store = [{'id': inst_id, 'template_id': 'tpl_y', 'zone': 'lobby', 'tile': [1, 1]}]

    def _find_instance_in_json(tpl_id, zone, local_tile):
        return store, 0, None

    calls = []
    ctrl.spawner_instances = types.SimpleNamespace(
        hide_instance_by_id=lambda i: calls.append(('hide', i)),
        refresh_from_disk=lambda: None,
    )

    monkeypatch.setattr('roguelike_editors.spawner.events.confirmations.find_instance_in_json', _find_instance_in_json)

    e = pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_y})
    handled = h_helpers.handle_keydown(h, ctx, e)
    assert handled is True

    assert req_eid not in world.entities
    assert req_eid not in world.components.get('SpawnRequest', {})
    assert npc_eid not in world.entities
    assert npc_eid not in world.components.get('SpawnerChild', {})
    assert ('hide', inst_id) in calls


def test_instances_panel_hide_filter(monkeypatch):
    ctrl = SpawnerListInstancesController()
    data = [
        {'id': 'abc', 'template_id': 'tpl', 'zone': 'lobby', 'tile': [0, 0]},
        {'id': 'def', 'template_id': 'tpl', 'zone': 'lobby', 'tile': [1, 1]},
    ]
    monkeypatch.setattr(
        'roguelike_editors.spawner.spawner_instances_panel.spawner_list_instances_controller.load_instances_json',
        lambda: list(data),
    )
    ctrl.refresh_from_disk()
    assert len(ctrl.model.items) >= 2
    ctrl.hide_instance_by_id('abc')
    assert all('abc' not in s for s in ctrl.model.items)
