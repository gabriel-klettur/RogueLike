import json
import types

from roguelike_game.ecs.systems.abilities.combo_system import ComboSystem
from roguelike_game.ecs.components.abilities.combo_counter_component import ComboCounterComponent
from roguelike_game.ecs.components.abilities.combo_rules_component import ComboRulesComponent


def test_combo_config_reload_updates_params(monkeypatch, tmp_path):
    # Archivo de reglas temporal
    cfg = {
        'player': {
            'window_s': 3.0,
            'min_window_s': 0.7,
            'difficulty_increase_per_hit': 0.25,
            'break_flash_duration_s': 0.5,
            'same_target_cooldown_s': 0.4,
            'rules': {
                'allowed_sources': {'melee': True, 'fireball': False},
                'min_damage': 2.0,
                'require_enemy': False,
                'require_unique_target': True,
            },
        }
    }
    fpath = tmp_path / 'combo_rules.json'
    fpath.write_text(json.dumps(cfg), encoding='utf-8')

    # Mundo con jugador y componentes a actualizar
    eid = 1
    counter = ComboCounterComponent(window_s=1.0, min_window_s=0.3, difficulty_increase_per_hit=0.05)
    rules = ComboRulesComponent(allowed_sources={'melee': False, 'fireball': True}, min_damage=0.0, require_enemy=True, require_unique_target=False)
    world = types.SimpleNamespace(
        player_entity=eid,
        components={
            'ComboCounterComponent': {eid: counter},
            'ComboRulesComponent': {eid: rules},
        }
    )

    sys = ComboSystem()
    # Redirigir la ruta y forzar recarga
    sys._rules_path = str(fpath)
    sys._reload_interval_s = 0.0
    # Asegurar que vea "cambio" de mtime
    monkeypatch.setattr('os.path.getmtime', lambda p: 123.0)

    # Llamar método privado para recargar (evita efectos colaterales del loop completo)
    sys._maybe_reload_rules(world)

    # Verificar que los parámetros se actualizaron
    assert counter.window_s == 3.0
    assert counter.min_window_s == 0.7
    assert abs(counter.difficulty_increase_per_hit - 0.25) < 1e-6
    assert abs(counter.break_flash_duration_s - 0.5) < 1e-6
    assert abs(counter.same_target_cooldown_s - 0.4) < 1e-6

    assert rules.allowed_sources == {'melee': True, 'fireball': False}
    assert abs(rules.min_damage - 2.0) < 1e-6
    assert rules.require_enemy is False
    assert rules.require_unique_target is True
