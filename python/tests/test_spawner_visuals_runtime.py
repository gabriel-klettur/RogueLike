from __future__ import annotations

import sys
import os


def main() -> int:
    # Ensure src on path
    repo_root = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir))
    src_dir = os.path.join(repo_root, "src")
    if src_dir not in sys.path:
        sys.path.insert(0, src_dir)

    # Imports
    import roguelike_engine.config.config as cfg
    from roguelike_game.ecs.systems.spawner.spawner_visuals import SpawnerVisualSync

    # Test double: minimal world and Building-like objects
    class Bld:
        def __init__(self, id_):
            self.id = id_
            self.runtime_hidden = True
            self.zone = None
            self.rel_x = 0
            self.rel_y = 0

    class World:
        def __init__(self):
            self.buildings = []
            self.state = type("S", (), {})()
            self.state.spawner_editor_active = False

    # Config and state stubs
    class Cfg:
        def __init__(self):
            self.zone = "lobby"
            self.anchor_tile = (0, 0)
            self.state_visuals = {"awaittrigger": 1, "wait_cooldown": 2}
            self.visible_in_game = True

    class St:
        def __init__(self, state):
            self.fsm_state = state
            self.visual_override_token = None

    # Prepare world and buildings
    w = World()
    a = Bld(1)
    b = Bld(2)
    w.buildings = [a, b]

    cfg.DEBUG_SPAWNER = False
    vis = SpawnerVisualSync()

    # Case 1: gameplay (no editor preview) -> exclusive visible
    w.state.spawner_editor_active = False
    vis.sync(w, eid=101, cfg=Cfg(), st=St("await_trigger"), frame_idx=0)
    assert a.runtime_hidden is False and b.runtime_hidden is True, "Only desired visual should be visible in gameplay"

    # Case 2: editor active but DEBUG_SPAWNER False -> still exclusive (no preview)
    w.state.spawner_editor_active = True
    vis.sync(w, eid=101, cfg=Cfg(), st=St("wait_cooldown"), frame_idx=1)
    assert a.runtime_hidden is True and b.runtime_hidden is False, "Exclusive even with editor unless DEBUG_SPAWNER"

    # Case 3: editor active and DEBUG_SPAWNER True -> multi preview (both visible)
    cfg.DEBUG_SPAWNER = True
    w.state.spawner_editor_active = True
    vis.sync(w, eid=101, cfg=Cfg(), st=St("await_trigger"), frame_idx=2)
    assert a.runtime_hidden is False and b.runtime_hidden is False, "Both visuals should be visible in editor+debug preview"

    # Cleanup
    cfg.DEBUG_SPAWNER = False
    print("[test_spawner_visuals_runtime] OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
