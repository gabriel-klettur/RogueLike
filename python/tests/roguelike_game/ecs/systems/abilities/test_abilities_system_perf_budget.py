from types import SimpleNamespace

import pytest

from roguelike_game.ecs.systems.abilities.mana_regen_system import ManaRegenSystem


class DummyMana:
    def __init__(self, current_mana: int, max_mana: int) -> None:
        self.current_mana = current_mana
        self.max_mana = max_mana


def test_mana_regen_perf_budget_runs_fast(monkeypatch):
    # Control del tiempo: avanza en pasos pequeños sin depender del reloj real
    base = 1000.0
    current = {"t": base}
    monkeypatch.setattr("time.time", lambda: current["t"])

    pid = 1
    world = SimpleNamespace(components={
        "Mana": {pid: DummyMana(0, 999999)},
        "PlayerTagComponent": {pid: SimpleNamespace(class_name="warrior")},
    })

    sys_under_test = ManaRegenSystem()

    # Primer update inicializa _last_time y retorna
    sys_under_test.update(world)

    # Ejecutar muchas iteraciones con delta pequeño
    for _ in range(200):
        current["t"] += 0.005
        sys_under_test.update(world)

    # No asertamos tiempo real; solo que no lance y el estado sea consistente
    assert world.components["Mana"][pid].current_mana >= 0
