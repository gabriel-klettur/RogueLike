import os
import json
import pytest
import pygame
from types import SimpleNamespace

from roguelike_editors.inventory.controller.editor_controller import InventoryEditorController
from roguelike_game.ecs.core.manager import ECSWorld
from roguelike_game.ecs.components.inventory_component import InventoryComponent


def create_world_with_entities():
    world = ECSWorld(screen=None, map_manager=SimpleNamespace(), buildings=[], perf_log=None)
    # Player entity
    p_eid = world.create_entity()
    world.components['PlayerTagComponent'][p_eid] = None
    player_inv = InventoryComponent(capacity=3, player_id='player1')
    world.components['InventoryComponent'][p_eid] = player_inv
    # NPC entity
    n_eid = world.create_entity()
    world.components['NPCTagComponent'][n_eid] = None
    npc_inv = InventoryComponent(capacity=2)
    world.components['InventoryComponent'][n_eid] = npc_inv
    return world, p_eid, n_eid

@ pytest.fixture
def controller_fixture(screen):
    world, p_eid, n_eid = create_world_with_entities()
    assets = {}
    font = pygame.font.SysFont(None, 16)
    ctrl = InventoryEditorController(world, assets, font)
    return ctrl, world, p_eid, n_eid


def test_toggle_sets_entities_and_selected(controller_fixture):
    ctrl, world, p_eid, n_eid = controller_fixture
    evt = SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_F6)
    # Open editor
    ctrl.handle_event(evt)
    assert ctrl.model.visible is True
    # Entities list includes player then NPC
    assert ctrl.model.entities == [p_eid, n_eid]
    assert ctrl.model.selected_eid == p_eid
    # Toggle off
    ctrl.handle_event(evt)
    assert ctrl.model.visible is False


def test_arrow_selection(controller_fixture):
    ctrl, world, p_eid, n_eid = controller_fixture
    # Open editor
    ctrl.handle_event(SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_F6))
    # Press RIGHT
    evt_r = SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_RIGHT)
    ctrl.handle_event(evt_r)
    assert ctrl.model.selected_eid == n_eid
    assert ctrl.model.prev_right is True
    # Press LEFT
    evt_l = SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_LEFT)
    ctrl.handle_event(evt_l)
    assert ctrl.model.selected_eid == p_eid
    assert ctrl.model.prev_left is True


def test_mouse_drag_and_drop(controller_fixture):
    ctrl, world, p_eid, n_eid = controller_fixture
    # Open editor
    ctrl.handle_event(SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_F6))
    inv = world.components['InventoryComponent'][p_eid]
    # Populate first slot
    inv.slots[0] = SimpleNamespace(item_id='gold', quantity=5)
    # Monkeypatch slot detection
    ctrl.view.get_slot_at_pos = lambda pos, count: 0
    # Mouse down
    evt_down = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(0,0))
    ctrl.handle_event(evt_down)
    # model.drag_item debe ser el objeto stack inicial
    got = ctrl.model.drag_item
    assert hasattr(got, 'item_id') and got.item_id == 'gold'
    assert hasattr(got, 'quantity') and got.quantity == 5
    assert ctrl.model.drag_slot == 0
    assert inv.slots[0] is None
    # Mouse up
    evt_up = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=1, pos=(0,0))
    ctrl.handle_event(evt_up)
    assert ctrl.model.drag_item is None
    assert ctrl.model.drag_slot is None
    # inv.slots[0] debe contener un objeto con item_id y quantity
    got2 = inv.slots[0]
    assert hasattr(got2, 'item_id') and got2.item_id == 'gold'
    assert hasattr(got2, 'quantity') and got2.quantity == 5
    assert inv.slots[0].item_id == 'gold'
    assert inv.slots[0].quantity == 5


def test_save_and_apply(monkeypatch, tmp_path, controller_fixture):
    ctrl, world, p_eid, n_eid = controller_fixture
    # Open editor
    ctrl.handle_event(SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_F6))
    # Override file paths
    player_path = tmp_path / 'player.json'
    active_path = tmp_path / 'active.json'
    ctrl.default_player_path = str(player_path)
    ctrl.active_player_path = str(active_path)
    # Prepare inventory
    inv = world.components['InventoryComponent'][p_eid]
    inv.player_id = 'player_test'
    inv.capacity = 2
    inv.slots = [SimpleNamespace(item_id='gold', quantity=1), None]
    # Test save
    ctrl._save_template(inv)
    data = json.loads(player_path.read_text())
    assert data['player_id'] == 'player_test'
    assert data['capacity'] == 2
    assert data['slots'] == [{'item': 'gold', 'quantity': 1}, None]
    assert 'schema_version' in data
    # Test apply
    ctrl._apply_changes(inv)
    d = json.loads(active_path.read_text())
    assert str(p_eid) in d
    assert d[str(p_eid)]['slots'] == [{'item': 'gold', 'quantity': 1}, None]
