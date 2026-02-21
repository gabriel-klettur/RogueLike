import types
import sys
from types import SimpleNamespace

import pytest

from roguelike_game.ecs.systems.abilities.mana_regen_system import ManaRegenSystem


class DummyMana:
    def __init__(self, current_mana: int, max_mana: int) -> None:
        self.current_mana = current_mana
        self.max_mana = max_mana


@pytest.fixture
def fake_player_stats_module(monkeypatch):
    mod = types.ModuleType("roguelike_game.factories.player.config")
    mod.PLAYER_STATS = {
        "warrior": {"mana_regen_per_second": 2.0},
    }
    sys.modules["roguelike_game.factories.player.config"] = mod
    yield mod
    sys.modules.pop("roguelike_game.factories.player.config", None)


def test_mana_regen_accumulates_and_caps(monkeypatch, fake_player_stats_module):
    # World con componentes mínimos
    pid = 1
    mana = DummyMana(current_mana=3, max_mana=10)
    world = SimpleNamespace(components={
        "Mana": {pid: mana},
        "PlayerTagComponent": {pid: SimpleNamespace(class_name="warrior")},
        # Sin NPCState ni Velocity -> permite regen por fallback si está quieto
    })

    sys_under_test = ManaRegenSystem()

    # Controlar tiempo para evitar sleeps
    t0 = 1000.0
    monkeypatch.setattr("time.time", lambda: t0)
    sys_under_test.update(world)

    # Avance de 1.0s para que regen == 2.0 -> suma 2 enteros
    monkeypatch.setattr("time.time", lambda: t0 + 1.0)
    sys_under_test.update(world)
    assert mana.current_mana == 5

    # Avance de 10s -> 20, pero se capea a max_mana=10
    monkeypatch.setattr("time.time", lambda: t0 + 11.0)
    sys_under_test.update(world)
    assert mana.current_mana == 10
