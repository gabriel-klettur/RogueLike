import types

import pytest

from roguelike_game.ecs.systems.abilities.mana_regen_system import ManaRegenSystem


@pytest.mark.parametrize(
    "dt, expected",
    [
        (1.2, 1),  # ~1s -> +1 mana
        (2.1, 2),  # ~2s -> +2 mana
    ],
)
def test_mana_regen_for_player_in_idle_like(monkeypatch, dt, expected):
    # Controlar el tiempo para un dt concreto
    t0 = 10_000.0
    t1 = t0 + dt
    times = [t0, t1]
    monkeypatch.setattr("time.time", lambda: times.pop(0))

    # Mundo mínimo: jugador con Mana y quieto (sin FSM -> vel 0)
    eid = 1
    mana = types.SimpleNamespace(current_mana=0, max_mana=10)
    vel = types.SimpleNamespace(vx=0, vy=0)
    player_tag = types.SimpleNamespace(class_name="barbarian")

    world = types.SimpleNamespace(components={
        'PlayerTagComponent': {eid: player_tag},
        'Mana': {eid: mana},
        'Velocity': {eid: vel},
        'DeathTimer': {},
        'NPCState': {},
    })

    sys = ManaRegenSystem()
    # Primera llamada inicializa tiempo interno y retorna
    sys.update(world)
    # Segunda llamada: aplica dt y añade mana entero
    sys.update(world)

    assert mana.current_mana == expected
