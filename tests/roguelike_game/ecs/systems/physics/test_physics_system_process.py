import types
import time as _time

import roguelike_game.ecs.systems.physics.facing_system as fs


def test_facing_system_updates_direction_with_cooldown(monkeypatch):
    # Tiempo controlado: dos llamadas separadas por > cooldown
    t0 = 1000.0
    t1 = t0 + 2.0
    times = [t0, t1]
    monkeypatch.setattr('time.time', lambda: times.pop(0))

    # Mundo NPC con Velocity y Animator
    eid = 1
    vel = types.SimpleNamespace(vx=1, vy=0)
    animator = types.SimpleNamespace(current_state='down', animations={'down', 'right', 'up', 'left'})
    world = types.SimpleNamespace(components={
        'Velocity': {eid: vel},
        'Animator': {eid: animator},
        'FacingCooldown': {},
        'PlayerTagComponent': {},
        'NPCState': {},
    })

    sys = fs.FacingSystem(perf_log=None)
    # Primera llamada: inicializa cooldown, puede cambiar estado
    sys.update(world)
    # Con vx>0, dy=0 => 'right'
    assert animator.current_state == 'right'
    # Segunda llamada con el mismo estado y sin movimiento: idle no aplica a NPC (no-player)
    vel.vx, vel.vy = 0, 0
    sys.update(world)
    assert animator.current_state == 'right'


def test_facing_system_respects_cooldown(monkeypatch):
    # Tiempo: segunda llamada antes del cooldown (1.0s)
    t0 = 2000.0
    t1 = t0 + 0.5
    times = [t0, t1]
    monkeypatch.setattr('time.time', lambda: times.pop(0))

    eid = 2
    vel = types.SimpleNamespace(vx=1, vy=0)
    animator = types.SimpleNamespace(current_state='left', animations={'left', 'right'})
    world = types.SimpleNamespace(components={
        'Velocity': {eid: vel},
        'Animator': {eid: animator},
        'FacingCooldown': {},
        'PlayerTagComponent': {},
        'NPCState': {},
    })

    sys = fs.FacingSystem(perf_log=None)
    sys.update(world)  # set to 'right' and start cooldown
    assert animator.current_state == 'right'

    # Intentar cambiar a 'left' antes de cooldown
    vel.vx = -1
    sys.update(world)
    # Debe permanecer 'right' por cooldown activo
    assert animator.current_state == 'right'
