import types
import time as _time
import pytest

from roguelike_game.ecs.components.abilities.dash_meter_component import DashMeterComponent
from roguelike_game.ecs.systems.abilities.dash_resource_system import DashResourceSystem


def test_dash_recharge_progress_and_fill(monkeypatch):
    # Controlar el tiempo para hacer determinista el avance
    t0 = 1_000.0
    t1 = t0 + 0.1  # con recharge_s=0.1 debería completar 1 carga
    times = [t0, t1]
    monkeypatch.setattr("time.time", lambda: times.pop(0))

    # Mundo mínimo con un medidor de dash
    eid = 1
    meter = DashMeterComponent(total=3, current=1, recharge_s=0.1)
    world = types.SimpleNamespace(components={
        'DashMeterComponent': {eid: meter},
        'DeathTimer': {},
        'PlayerTagComponent': {},
    })

    sys = DashResourceSystem()
    # Primera llamada inicializa last_time y no avanza
    sys.update(world)
    assert meter.current == 1
    # Segunda llamada: debe sumar una carga y limpiar progress
    sys.update(world)
    assert meter.current == 2
    assert meter.progress == pytest.approx(0.0, abs=1e-9)


def test_dash_recharge_refill_on_revive(monkeypatch):
    # Secuencia de tiempo estable para evitar dependencias de dt
    t0 = 2_000.0
    times = [t0, t0]
    monkeypatch.setattr("time.time", lambda: times.pop(0))

    eid = 2
    meter = DashMeterComponent(total=4, current=2, recharge_s=1.0)
    world = types.SimpleNamespace(components={
        'DashMeterComponent': {eid: meter},
        'DeathTimer': {eid: object()},  # muerto
        'PlayerTagComponent': {eid: object()},
    })

    sys = DashResourceSystem()
    sys.update(world)  # registra estado muerto
    # Revivir: quitar DeathTimer y llamar update -> debe rellenar
    world.components['DeathTimer'].pop(eid)
    times.append(t0)  # mismo tiempo para no sumar progreso
    sys.update(world)

    assert meter.current == meter.total
    assert meter.progress == pytest.approx(0.0, abs=1e-9)
