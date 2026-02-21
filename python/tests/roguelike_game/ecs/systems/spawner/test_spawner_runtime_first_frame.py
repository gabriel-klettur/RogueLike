from __future__ import annotations

import types

import pytest

from roguelike_game.ecs.systems.spawner.spawner_runtime import SpawnerRuntimeSystem


class Bld:
    def __init__(self, id_):
        self.id = id_
        self.runtime_hidden = True
        self.zone = None
        self.rel_x = 0
        self.rel_y = 0


class WorldStub:
    def __init__(self):
        self.components = {"SpawnerConfig": {}, "SpawnerState": {}}
        self.buildings = []
        self.entities: set[int] = set()

    def get_entities_with(self, *names: str) -> list[int]:
        if all(n in self.components for n in names):
            # Return intersection of ids having each component
            sets = [set(self.components[n].keys()) for n in names]
            common = set.intersection(*sets) if sets else set()
            return list(common)
        return []


@pytest.fixture()
def world_runtime():
    w = WorldStub()
    # Single spawner entity
    eid = 101
    cfg = types.SimpleNamespace(
        zone="lobby",
        anchor_tile=(0, 0),
        state_visuals={"awaittrigger": 1, "wait_cooldown": 2},
        visible_in_game=True,
        visuals_offsets_px=None,
    )
    st = types.SimpleNamespace(fsm_state=None, visual_override_token=None)
    w.components["SpawnerConfig"][eid] = cfg
    w.components["SpawnerState"][eid] = st
    # Two buildings available
    a = Bld(1)
    b = Bld(2)
    w.buildings = [a, b]
    return w, eid, a, b


def test_runtime_updates_state_before_visual_sync(monkeypatch, world_runtime):
    w, eid, a, b = world_runtime

    # Monkeypatch process_spawner in the runtime module to set the state before sync
    import roguelike_game.ecs.systems.spawner.spawner_runtime as runtime_mod

    def _stub_process_spawner(**kwargs):
        st = kwargs.get("st")
        # Establish known initial state the very first frame
        st.fsm_state = "await_trigger"
        return None

    monkeypatch.setattr(runtime_mod, "process_spawner", _stub_process_spawner)

    # Avoid depending on placement/colliders; ensure blocked sets are empty
    s = SpawnerRuntimeSystem()
    monkeypatch.setattr(s.caches, "collect_blocked", lambda world: (set(), set()))

    # Act
    s.update(w)

    # Assert: building id=1 (await_trigger) becomes visible, id=2 stays hidden
    assert a.runtime_hidden is False and b.runtime_hidden is True
