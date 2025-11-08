from __future__ import annotations

import sys
from pathlib import Path
import pytest

# Ensure 'src' is importable
ROOT = Path(__file__).resolve().parents[1]
src_path = ROOT / 'src'
if str(src_path) not in sys.path:
    sys.path.insert(0, str(src_path))

from roguelike_game.config.spells_config import SPELLS  # type: ignore
from roguelike_game.ecs.components.transform.position import Position  # type: ignore
from roguelike_game.ecs.components.abilities.meteor_shower_component import MeteorShowerComponent  # type: ignore
from roguelike_game.ecs.systems.combat.spells.meteor_shower_system import MeteorShowerSystem  # type: ignore
from roguelike_game.ecs.utils.spell_vfx import get_meteor_scale  # type: ignore


@pytest.fixture()
def shower_entity(world):
    owner = world.create_entity()
    eid = world.create_entity()
    world.components.setdefault('Position', {})[eid] = Position(100.0, 100.0)
    world.components.setdefault('MeteorShowerComponent', {})[eid] = MeteorShowerComponent(
        count=1,
        interval=0.0,
        area_radius=0.0,
        impact_damage=40.0,
        impact_radius=160.0,
        owner=owner,
        spell_key='meteor_shower',
    )
    return eid


def test_spawns_one_meteor_with_scale_from_config(world, camera, shower_entity, monkeypatch):
    sys_under_test = MeteorShowerSystem()

    class _Dummy:
        def get_size(self):
            return (10, 10)

    # Mock image load used by Sprite component
    monkeypatch.setattr(
        'roguelike_game.ecs.components.rendering.sprite.load_image',
        lambda path: _Dummy(),
        raising=True,
    )

    sys_under_test.update(world, camera)

    falls = world.components.get('MeteorFallComponent', {})
    assert len(falls) == 1, 'Debe crear un meteorito (MeteorFallComponent)'

    (meteor_eid, mfall), = list(falls.items())

    scale_map = world.components.get('Scale', {})
    assert meteor_eid in scale_map

    cfg = SPELLS.get('meteor_shower')
    expected = float(get_meteor_scale(cfg, 0.10))
    got = float(getattr(scale_map[meteor_eid], 'scale', 0.0))
    assert abs(got - expected) < 1e-4

    # Nace por encima del target en Y
    pos = world.components['Position'][meteor_eid]
    assert pos.y < mfall.target_y + 1e-3
