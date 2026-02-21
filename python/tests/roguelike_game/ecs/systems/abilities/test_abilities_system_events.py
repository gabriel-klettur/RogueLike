import types
import time

from roguelike_game.ecs.systems.abilities.combo_system import ComboSystem
from roguelike_game.ecs.components.abilities.combo_counter_component import ComboCounterComponent
from roguelike_game.ecs.components.abilities.combo_rules_component import ComboRulesComponent


def test_abilities_events_combo_break_and_kill(monkeypatch):
    # Evitar recarga de reglas desde disco
    monkeypatch.setattr(ComboSystem, '_maybe_reload_rules', lambda self, world: None, raising=True)

    # Mundo con combo del atacante
    attacker = 1
    counter = ComboCounterComponent(window_s=1.0, same_target_cooldown_s=0.0)
    rules = ComboRulesComponent(allowed_sources={'melee': True}, min_damage=0.0)
    world = types.SimpleNamespace(components={
        'ComboCounterComponent': {attacker: counter},
        'ComboRulesComponent': {attacker: rules},
        'ComboEventQueue': [],
        'PlayerTagComponent': {},
    }, player_entity=attacker)

    sys = ComboSystem()

    # Simular combo activo y luego evento de ruptura
    counter.current = 2
    world.components['ComboEventQueue'].append({'type': 'break', 'entity': attacker})
    sys.update(world)
    assert counter.current == 0
    assert counter.total_completed >= 1

    # Evento de kill dentro de ventana activa
    counter.current = 1
    # Simular ventana activa extendida (usar futuro relativo al ahora)
    counter.window_end_time = time.time() + 10
    world.components['ComboEventQueue'].append({'type': 'kill', 'entity': attacker})
    sys.update(world)
    assert counter.kill_combo_current >= 1
