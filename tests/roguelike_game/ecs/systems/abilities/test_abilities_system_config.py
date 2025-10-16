import json
import types

from roguelike_game.ecs.systems.abilities.combo_system import ComboSystem
from roguelike_game.ecs.components.abilities.combo_counter_component import ComboCounterComponent
from roguelike_game.ecs.components.abilities.combo_rules_component import ComboRulesComponent


def test_abilities_config_combo_rules_reload(monkeypatch, tmp_path):
    cfg = {
        'player': {
            'window_s': 2.5,
            'rules': {'allowed_sources': {'melee': True}, 'min_damage': 1.0},
        }
    }
    fpath = tmp_path / 'combo_rules.json'
    fpath.write_text(json.dumps(cfg), encoding='utf-8')

    eid = 1
    counter = ComboCounterComponent(window_s=1.0)
    rules = ComboRulesComponent(allowed_sources={'melee': False})
    world = types.SimpleNamespace(
        player_entity=eid,
        components={
            'ComboCounterComponent': {eid: counter},
            'ComboRulesComponent': {eid: rules},
        },
    )

    sys = ComboSystem()
    sys._rules_path = str(fpath)
    sys._reload_interval_s = 0.0

    # Forzar mtime distinto
    monkeypatch.setattr('os.path.getmtime', lambda p: 123.4)
    sys._maybe_reload_rules(world)

    assert counter.window_s == 2.5
    assert rules.allowed_sources.get('melee') is True
    assert rules.min_damage == 1.0
