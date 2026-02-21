from __future__ import annotations

import types

from roguelike_game.ecs.systems.spawner.spawner_visuals import SpawnerVisualSync


class Cfg:
    def __init__(self):
        # Mezcla de claves: snake, CamelCase, y dicts con instance_id
        self.state_visuals = {
            "awaittrigger": 1,
            "WaitCooldown": {"instance_id": 2},
            "Finished": {"id": 3},
        }
        self.visible_in_game = True
        self.zone = "lobby"
        self.anchor_tile = (0, 0)


def test_current_state_key_and_mapping_variants():
    vis = SpawnerVisualSync()

    # 1) fsm_state como clase
    class AwaitTriggerState:  # noqa: N801
        pass

    st = types.SimpleNamespace(fsm_state=AwaitTriggerState, visual_override_token=None)
    cfg = Cfg()
    desired = SpawnerVisualSync.desired_building_for_state(cfg, st)
    assert desired == 1

    # 2) fsm_state como instancia
    st2 = types.SimpleNamespace(fsm_state=AwaitTriggerState(), visual_override_token=None)
    desired2 = SpawnerVisualSync.desired_building_for_state(cfg, st2)
    assert desired2 == 1

    # 3) fsm_state como string con sufijo State
    st3 = types.SimpleNamespace(fsm_state="WaitCooldownState", visual_override_token=None)
    desired3 = SpawnerVisualSync.desired_building_for_state(cfg, st3)
    assert desired3 == 2

    # 4) fsm_state como string lowercase/underscore
    st4 = types.SimpleNamespace(fsm_state="finished", visual_override_token=None)
    desired4 = SpawnerVisualSync.desired_building_for_state(cfg, st4)
    assert desired4 == 3

    # 5) visual_override_token tiene precedencia
    st5 = types.SimpleNamespace(fsm_state="some_other", visual_override_token="wait_cooldown")
    desired5 = SpawnerVisualSync.desired_building_for_state(cfg, st5)
    assert desired5 == 2
