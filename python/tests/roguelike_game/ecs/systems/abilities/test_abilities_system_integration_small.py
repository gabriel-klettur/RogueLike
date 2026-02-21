import time
from types import SimpleNamespace

from roguelike_game.ecs.components.abilities.combo_counter_component import ComboCounterComponent
from roguelike_game.ecs.systems.abilities.combo_system import ComboSystem


def test_combo_system_integration_valid_hit_increments_and_clears_queue(monkeypatch):
    t0 = 1000.0
    monkeypatch.setattr("time.time", lambda: t0)

    attacker, target = 1, 2
    counter = ComboCounterComponent(window_s=2.0)

    world = SimpleNamespace(components={
        "ComboEventQueue": [
            {"attacker": attacker, "target": target, "damage": 3.0, "time": t0, "source": "slash"}
        ],
        "ComboCounterComponent": {attacker: counter},
        "PlayerTagComponent": {},
    })

    sys_under_test = ComboSystem()
    sys_under_test.update(world)

    assert world.components["ComboEventQueue"] == []
    assert counter.current >= 1
    assert counter.window_end_time > t0
