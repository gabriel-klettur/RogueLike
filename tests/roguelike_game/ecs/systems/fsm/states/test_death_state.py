import json
import types
from pathlib import Path

import pytest

from roguelike_game.ecs.systems.fsm.states.death_state import DeathState
from roguelike_game.ecs.systems.fsm.states.unconscious_state import UnconsciousState
from roguelike_engine.config import config
from roguelike_engine.config.config_z_layer import Z_LAYERS
from roguelike_game.ecs.components.transform.z_layer import ZLayer


class _World:
    def __init__(self):
        # Minimal ECS storage used by DeathState
        self.components = {}
        # Map manager with a name attribute (used for logging/state)
        self.map_manager = types.SimpleNamespace(name="global_map")

    def remove_entity(self, eid: int):
        removed = self.components.setdefault("_removed", set())
        removed.add(eid)


@pytest.mark.parametrize("eid", [42, 777])
def test_death_state_cleans_monster_inventory_and_no_crash(tmp_path: Path, monkeypatch, eid: int):
    # Arrange: isolate filesystem by redirecting DATA_DIR to tmp_path / data
    data_dir = tmp_path / "data"
    monkeypatch.setattr(config, "DATA_DIR", str(data_dir))

    inv_dir = data_dir / "inventory" / "active"
    inv_dir.mkdir(parents=True, exist_ok=True)
    inv_path = inv_dir / "inventory_monsters.json"

    # Seed inventory with the entity id so DeathState should remove it
    initial = {str(eid): {"slots": [None, None]}}
    inv_path.write_text(json.dumps(initial), encoding="utf-8")

    world = _World()
    # Ensure it is treated as NPC (no PlayerTagComponent)
    entity = types.SimpleNamespace(world=world, id=eid)

    # Act: entering DeathState for NPC should not raise and should clean inventory entry
    DeathState().enter(entity)

    # Assert: entity removed from world and inventory updated
    updated = json.loads(inv_path.read_text(encoding="utf-8"))
    assert str(eid) not in updated
    assert eid in world.components.get("_removed", set())


def test_death_state_player_applies_grayscale_and_zlayer_no_removal(tmp_path: Path, monkeypatch):
    # Arrange: point DATA_DIR to tmp to avoid touching real FS (not strictly needed for player branch)
    data_dir = tmp_path / "data"
    monkeypatch.setattr(config, "DATA_DIR", str(data_dir))

    eid = 99
    world = _World()
    # Mark as player so NPC branch is skipped
    world.components["PlayerTagComponent"] = {eid: object()}
    entity = types.SimpleNamespace(world=world, id=eid)

    # Act
    DeathState().enter(entity)

    # Assert: no removal and grayscale + zlayer set
    assert eid not in world.components.get("_removed", set())
    assert eid in world.components.get("GrayscaleComponent", {})
    zc = world.components.get("ZLayer", {}).get(eid)
    assert isinstance(zc, ZLayer)
    assert getattr(zc, "layer", None) == Z_LAYERS.get("player", 4)


def test_unconscious_to_death_triggers_death_enter_and_cleans_inventory(tmp_path: Path, monkeypatch):
    # Arrange: filesystem isolation for inventory path used by DeathState
    data_dir = tmp_path / "data"
    monkeypatch.setattr(config, "DATA_DIR", str(data_dir))

    eid = 123
    inv_dir = data_dir / "inventory" / "active"
    inv_dir.mkdir(parents=True, exist_ok=True)
    inv_path = inv_dir / "inventory_monsters.json"
    inv_path.write_text(json.dumps({str(eid): {"slots": [None]}}), encoding="utf-8")

    world = _World()
    # Provide minimal NPCState with an FSM whose change_state calls enter() on the new state
    class _FakeFSM:
        def __init__(self):
            self.current_state = UnconsciousState()
        def change_state(self, state, entity):
            # Simulate standard FSM behavior: enter() of new state
            self.current_state = state
            state.enter(entity)

    world.components["NPCState"] = {eid: types.SimpleNamespace(fsm=_FakeFSM())}
    entity = types.SimpleNamespace(world=world, id=eid)

    # Act: execute UnconsciousState without DeathTimer to force immediate transition to DeathState
    UnconsciousState().execute(entity, dt=0.0)

    # Assert: entity removed and inventory cleaned via DeathState.enter
    updated = json.loads(inv_path.read_text(encoding="utf-8"))
    assert str(eid) not in updated
    assert eid in world.components.get("_removed", set())
