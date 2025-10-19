import types
import time as _time

import pytest

from roguelike_game.ecs.systems.abilities.combo_system import ComboSystem
from roguelike_game.ecs.components.abilities.combo_counter_component import ComboCounterComponent
from roguelike_game.ecs.components.abilities.combo_rules_component import ComboRulesComponent


def _world_with_combo(attacker=1):
    counter = ComboCounterComponent(window_s=1.0, same_target_cooldown_s=0.0)
    rules = ComboRulesComponent(allowed_sources={'melee': True, 'hitbox': True}, min_damage=0.0)
    world = types.SimpleNamespace(components={
        'ComboCounterComponent': {attacker: counter},
        'ComboRulesComponent': {attacker: rules},
        'ComboEventQueue': [],
        'PlayerTagComponent': {},
    }, player_entity=attacker)
    return world, counter


def test_combo_increments_on_valid_hit_and_refreshes_window(monkeypatch):
    # Evitar recarga de reglas desde disco
    monkeypatch.setattr(ComboSystem, '_maybe_reload_rules', lambda self, world: None, raising=True)
    # Controlar el tiempo para verificar que el window_end_time avanza
    t0 = 1000.0
    monkeypatch.setattr('time.time', lambda: t0)

    world, counter = _world_with_combo(attacker=1)
    sys = ComboSystem()

    # Evento válido
    world.components['ComboEventQueue'].append({
        'attacker': 1,
        'target': 2,
        'damage': 5.0,
        'source': 'melee',
        'time': t0,
    })

    sys.update(world)

    assert counter.current == 1
    assert counter.window_end_time > t0
    assert counter.last_target_id == 2


def test_combo_break_event_records_and_resets(monkeypatch):
    monkeypatch.setattr(ComboSystem, '_maybe_reload_rules', lambda self, world: None, raising=True)
    t0 = 2000.0
    monkeypatch.setattr('time.time', lambda: t0)
    world, counter = _world_with_combo(attacker=1)
    # Pre-cargar estado de combo activo
    counter.current = 3
    counter.window_end_time = t0 + 1.0

    sys = ComboSystem()
    world.components['ComboEventQueue'].append({'type': 'break', 'entity': 1, 'time': t0})

    sys.update(world)

    assert counter.current == 0
    assert counter.last_completed_count == 3
    assert counter.total_completed >= 1
    assert counter.break_flash_end_time >= t0
