from __future__ import annotations

import sys
import os
import time
import unittest
from unittest.mock import patch
from pathlib import Path

# Ensure 'src' is importable
ROOT = Path(__file__).resolve().parents[1]
src_path = ROOT / 'src'
if str(src_path) not in sys.path:
    sys.path.insert(0, str(src_path))

from roguelike_game.config.spells_config import SPELLS  # type: ignore
from roguelike_game.ecs.components.transform.position import Position  # type: ignore
from roguelike_game.ecs.components.abilities.meteor_shower_component import MeteorShowerComponent  # type: ignore
from roguelike_game.ecs.systems.combat.spells.meteor_shower_system import MeteorShowerSystem  # type: ignore
from tests.utils.fakes import FakeWorld, FakeCamera
from roguelike_game.ecs.utils.spell_vfx import get_meteor_scale, get_meteor_sprite_path  # type: ignore


class TestMeteorShowerSystem(unittest.TestCase):
    def setUp(self) -> None:
        self.world = FakeWorld()
        self.camera = FakeCamera()

    def _make_shower(self, owner: int) -> int:
        eid = self.world.create_entity()
        # Spawn at origin
        self.world.components.setdefault('Position', {})[eid] = Position(100.0, 100.0)
        # area_radius=0 para que el punto de impacto sea determinista
        self.world.components.setdefault('MeteorShowerComponent', {})[eid] = MeteorShowerComponent(
            count=1,
            interval=0.0,
            area_radius=0.0,
            impact_damage=40.0,
            impact_radius=160.0,
            owner=owner,
            spell_key='meteor_shower',
        )
        return eid

    def test_spawns_one_meteor_with_scale_from_config(self) -> None:
        owner = self.world.create_entity()
        shower_eid = self._make_shower(owner)
        sys_under_test = MeteorShowerSystem()
        # Mock Sprite.load_image to avoid pygame
        class _Dummy:
            def get_size(self):
                return (10, 10)
        with patch('roguelike_game.ecs.components.rendering.sprite.load_image', return_value=_Dummy()):
            sys_under_test.update(self.world, self.camera)
        # Debe haber exactamente 1 MeteorFallComponent creado
        falls = self.world.components.get('MeteorFallComponent', {})
        self.assertEqual(len(falls), 1, 'Debe crear un meteorito (MeteorFallComponent)')
        (meteor_eid, mfall), = list(falls.items())
        # Verificar que hay Scale leido del config
        scale_map = self.world.components.get('Scale', {})
        self.assertIn(meteor_eid, scale_map)
        cfg = SPELLS.get('meteor_shower')
        expected_scale = float(get_meteor_scale(cfg, 0.10))
        got_scale = float(getattr(scale_map[meteor_eid], 'scale', 0.0))
        self.assertAlmostEqual(got_scale, expected_scale, places=4)
        # Posición debe estar por encima del objetivo (altura aplicada)
        pos = self.world.components['Position'][meteor_eid]
        self.assertLess(pos.y, mfall.target_y + 1e-3)


if __name__ == '__main__':
    unittest.main()
