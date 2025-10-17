import types

import pytest

from roguelike_game.ecs.systems.spawner.spawner_runtime import SpawnerRuntimeSystem


class DummyMapManager:
    def __init__(self):
        self.solid_tiles = []


class DummyWorld:
    def __init__(self, editor_active: bool):
        # Minimal ECSWorld-like
        self.components = {}
        self.entities = []
        self.map_manager = DummyMapManager()
        self.buildings = []
        # Provide access path used by SpawnerRuntimeSystem for TTL logic
        self.game = types.SimpleNamespace(
            state=types.SimpleNamespace(editor=types.SimpleNamespace(active=editor_active))
        )

    def get_entities_with(self, *comps):
        return []


@pytest.mark.parametrize("active, expected_ttl_min", [
    (False, 30),  # default TTL when editor inactive
    (True, 60),   # boosted TTL when editor active
])
def test_spawner_runtime_ttl_changes_with_editor_state(active, expected_ttl_min):
    sys = SpawnerRuntimeSystem()  # default blocked_ttl_frames=30 as patched
    world = DummyWorld(editor_active=active)

    # Execute one update to apply TTL policy
    sys.update(world, camera=None)

    assert sys.caches._blocked_cache_ttl_frames >= expected_ttl_min

    # Flip state and ensure TTL policy restores/updates accordingly
    world.game.state.editor.active = not active
    sys.update(world, camera=None)
    if active:
        # after deactivating, should be back to default
        assert sys.caches._blocked_cache_ttl_frames == sys._default_blocked_ttl
    else:
        # after activating, should be boosted
        assert sys.caches._blocked_cache_ttl_frames >= 60
