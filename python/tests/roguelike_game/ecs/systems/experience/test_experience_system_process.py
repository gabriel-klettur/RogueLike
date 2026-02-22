import types

import roguelike_game.ecs.systems.experience.experience_system as xs
from roguelike_game.ecs.components.experience_component import ExperienceComponent
from roguelike_game.ecs.components.inventory_component import InventoryComponent


def test_experience_system_consumes_orbs_and_levels_up(monkeypatch):
    # Stub de catálogo de items: xp_orb con 60 XP
    monkeypatch.setattr(
        xs, 'load_items', lambda path: {'xp_orb': types.SimpleNamespace(experience=60)}, raising=True
    )

    # Mundo: jugador con XP y dos orbes (2 * 60 = 120)
    eid = 1
    inv = InventoryComponent(capacity=3, player_id='p')
    inv.add('xp_orb', 2)
    xp_comp = ExperienceComponent(xp=0, level=0, xp_to_next_level=100)

    world = types.SimpleNamespace(components={
        'ExperienceComponent': {eid: xp_comp},
        'InventoryComponent': {eid: inv},
    })

    sys = xs.ExperienceSystem(items_path='ignored.json')
    sys.update(world)

    # 120 XP: sube 1 nivel y quedan 20 para el siguiente
    assert xp_comp.level == 1
    assert xp_comp.xp == 20
    # Items consumidos del inventario
    assert inv.has('xp_orb', 1) is False
