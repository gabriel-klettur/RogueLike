from __future__ import annotations

import sys
import os


def main() -> int:
    # Ensure src on path
    repo_root = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir))
    src_dir = os.path.join(repo_root, "src")
    if src_dir not in sys.path:
        sys.path.insert(0, src_dir)

    # Imports under test
    from roguelike_game.ecs.systems.spawner.spawner_wave import process_spawner
    from roguelike_game.ecs.systems.fsm.states.unconscious_state import UnconsciousState

    # Minimal doubles
    class FSM:
        def __init__(self, state):
            self.current_state = state

    class NPCState:
        def __init__(self, state):
            self.fsm = FSM(state)

    class World:
        def __init__(self):
            self.components = {
                'NPCState': {}
            }
            def _get_entities_with(key):
                return set(self.components.get(key, {}).keys())
            self.get_entities_with = _get_entities_with
        def create_entity(self):
            # Not needed in this test path
            return 99999
        def remove_entity(self, eid):
            for m in self.components.values():
                if isinstance(m, dict):
                    m.pop(eid, None)

    class Caches:
        @staticmethod
        def collect_npc_tiles(world):
            return set()

    class Cfg:
        def __init__(self, count_ko_as_clear: bool):
            self.template_id = "test_tpl"
            self.zone = "lobby"
            self.anchor_tile = (0, 0)
            self.spawner_shape = 'circle'
            self.policy = {
                'advance_on': 'clear',
                'count_ko_as_clear': bool(count_ko_as_clear),
                'cooldown_s': 0.1,
            }
            self.waves = [
                {'spawns': [{'kind': 'monster', 'id': 'x', 'count': 0}]}
            ]
            self.cooldown_frames = 0
            self.between_waves_cooldown_frames = 0
            self.restart_cooldown_frames = 0

    class St:
        def __init__(self):
            self.finished = False
            self.started = True
            self.fsm_state = None
            self.current_wave_idx = 0
            self.spawned_this_wave = True
            self.expected_this_wave = 2
            self.current_wave_entities = set()
            self.active_entities = set()
            self.cooldown_remaining = 0
            self.restart_cooldown_remaining = 0

    # Shared setup
    world = World()
    caches = Caches()
    e1, e2 = 101, 102
    ents_set = {e1, e2}
    world.components['NPCState'][e1] = NPCState(UnconsciousState())
    world.components['NPCState'][e2] = NPCState(UnconsciousState())

    # Case A: count_ko_as_clear = True -> wave should be considered cleared
    stA = St()
    stA.current_wave_entities = {e1, e2}
    process_spawner(world, eid=1, cfg=Cfg(True), st=stA, solid=set(), building=set(), caches=caches, ents_set=ents_set, reserved_global=set())
    assert stA.finished is True and stA.fsm_state == 'finished', f"KO-aware: expected finished, got finished={stA.finished}, state={stA.fsm_state}"

    # Case B: count_ko_as_clear = False -> should wait_clear and not finish
    stB = St()
    stB.current_wave_entities = {e1, e2}
    process_spawner(world, eid=2, cfg=Cfg(False), st=stB, solid=set(), building=set(), caches=caches, ents_set=ents_set, reserved_global=set())
    assert stB.finished is False and stB.fsm_state == 'wait_clear', f"Legacy: expected wait_clear, got finished={stB.finished}, state={stB.fsm_state}"

    print("[test_spawner_wave_ko_policy] OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
