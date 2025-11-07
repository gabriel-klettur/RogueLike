from __future__ import annotations

import sys
from pathlib import Path
import unittest
from unittest.mock import patch

# Ensure 'src' is importable
ROOT = Path(__file__).resolve().parents[1]
src_path = ROOT / 'src'
if str(src_path) not in sys.path:
    sys.path.insert(0, str(src_path))

from roguelike_game.config.spells_config import SPELLS  # type: ignore
from roguelike_game.ecs.components.transform.position import Position  # type: ignore
from roguelike_game.ecs.components.abilities.meteor_fall_component import MeteorFallComponent  # type: ignore
from roguelike_game.ecs.components.combat.health import Health  # type: ignore
from roguelike_game.ecs.systems.combat.spells.meteor_fall_system import MeteorFallSystem  # type: ignore
from roguelike_game.ecs.utils.spell_vfx import get_impact_scale  # type: ignore
from tests.utils.fakes import FakeWorld, FakeCamera


class TestMeteorFallSystem(unittest.TestCase):
    def setUp(self) -> None:
        self.world = FakeWorld()
        self.camera = FakeCamera()
        self.cfg = SPELLS.get('meteor_shower')

    def _force_impact(self, meteor_eid: int) -> None:
        # Llamada 1: inicializa _last_time y sube el meteorito a target_y-1
        sys_under_test = MeteorFallSystem()
        with patch('roguelike_game.ecs.systems.combat.spells.meteor_fall_system.load_image') as pimg:
            # Devolver objeto dummy con get_size si en algún lado se usa
            class _Dummy:
                def get_size(self):
                    return (10, 10)
            pimg.return_value = _Dummy()
            sys_under_test.update(self.world, self.camera)
        # Forzar impacto: coloco y = target_y directamente
        mfall = self.world.components['MeteorFallComponent'][meteor_eid]
        pos = self.world.components['Position'][meteor_eid]
        pos.y = float(mfall.target_y)
        # Llamada 2: procesa impacto
        with patch('roguelike_game.ecs.systems.combat.spells.meteor_fall_system.load_image') as pimg:
            class _Dummy:
                def get_size(self):
                    return (10, 10)
            pimg.return_value = _Dummy()
            sys_under_test.update(self.world, self.camera)

    def test_impact_creates_single_puddle_with_radius_and_scale(self) -> None:
        owner = self.world.create_entity()
        # Crear meteorito justo encima del objetivo
        x, y = 200.0, 300.0
        meteor_eid = self.world.create_entity()
        self.world.components.setdefault('Position', {})[meteor_eid] = Position(x, y - 10.0)
        self.world.components.setdefault('MeteorFallComponent', {})[meteor_eid] = MeteorFallComponent(
            target_x=x,
            target_y=y,
            height_px=10.0,
            fall_speed_px_s=1000.0,
            impact_damage=40.0,
            impact_radius=160.0,
            owner=owner,
            spell_key='meteor_shower',
        )
        self._force_impact(meteor_eid)
        # Debe existir un único Puddle en esa posición
        puddles = self.world.components.get('PuddleComponent', {})
        positions = self.world.components.get('Position', {})
        marks = [eid for eid, _ in puddles.items() if abs(positions.get(eid).x - x) <= 0.5 and abs(positions.get(eid).y - y) <= 0.5]
        self.assertEqual(len(marks), 1, 'Solo una marca de impacto debe crearse')
        mark = marks[0]
        # Radio
        self.assertAlmostEqual(float(puddles[mark].radius), 160.0, places=4)
        # Escala desde config
        scale_map = self.world.components.get('Scale', {})
        expected_scale = float(get_impact_scale(self.cfg, 0.10))
        self.assertIn(mark, scale_map)
        self.assertAlmostEqual(float(getattr(scale_map[mark], 'scale', 0.0)), expected_scale, places=4)

    def test_damage_40_excludes_owner(self) -> None:
        owner = self.world.create_entity()
        # Víctima dentro del área
        victim = self.world.create_entity()
        self.world.components.setdefault('Health', {})[victim] = Health(max_hp=100, current_hp=100)
        # Entidad fuera del área
        outsider = self.world.create_entity()
        self.world.components.setdefault('Health', {})[outsider] = Health(max_hp=100, current_hp=100)
        # Owner con vida (no debe recibir daño)
        self.world.components.setdefault('Health', {})[owner] = Health(max_hp=100, current_hp=100)

        x, y = 400.0, 400.0
        self.world.components.setdefault('Position', {})[victim] = Position(x + 50.0, y)  # dentro de radius=160
        self.world.components.setdefault('Position', {})[outsider] = Position(x + 300.0, y)  # fuera
        self.world.components.setdefault('Position', {})[owner] = Position(x, y)

        meteor_eid = self.world.create_entity()
        self.world.components.setdefault('Position', {})[meteor_eid] = Position(x, y - 10.0)
        self.world.components.setdefault('MeteorFallComponent', {})[meteor_eid] = MeteorFallComponent(
            target_x=x,
            target_y=y,
            height_px=10.0,
            fall_speed_px_s=1000.0,
            impact_damage=40.0,
            impact_radius=160.0,
            owner=owner,
            spell_key='meteor_shower',
        )
        self._force_impact(meteor_eid)

        hmap = self.world.components.get('Health', {})
        self.assertEqual(hmap[owner].current_hp, 100, 'El caster no debe recibir daño')
        self.assertEqual(hmap[outsider].current_hp, 100, 'Fuera de radio no recibe daño')
        self.assertEqual(hmap[victim].current_hp, 60, 'Víctima dentro del radio recibe 40 de daño')

    def test_prevent_duplicate_mark_same_position(self) -> None:
        owner = self.world.create_entity()
        x, y = 500.0, 600.0
        # Primer meteorito
        m1 = self.world.create_entity()
        self.world.components.setdefault('Position', {})[m1] = Position(x, y - 10.0)
        self.world.components.setdefault('MeteorFallComponent', {})[m1] = MeteorFallComponent(
            target_x=x, target_y=y, height_px=10.0, fall_speed_px_s=1000.0,
            impact_damage=40.0, impact_radius=160.0, owner=owner, spell_key='meteor_shower')
        self._force_impact(m1)
        # Segundo meteorito misma posición e igual spell_key
        m2 = self.world.create_entity()
        self.world.components.setdefault('Position', {})[m2] = Position(x, y - 10.0)
        self.world.components.setdefault('MeteorFallComponent', {})[m2] = MeteorFallComponent(
            target_x=x, target_y=y, height_px=10.0, fall_speed_px_s=1000.0,
            impact_damage=40.0, impact_radius=160.0, owner=owner, spell_key='meteor_shower')
        self._force_impact(m2)
        # Debe existir solo una marca en esa posición
        puddles = self.world.components.get('PuddleComponent', {})
        positions = self.world.components.get('Position', {})
        marks = [eid for eid, _ in puddles.items() if abs(positions.get(eid).x - x) <= 0.5 and abs(positions.get(eid).y - y) <= 0.5]
        self.assertEqual(len(marks), 1, 'No deben duplicarse las marcas de impacto en la misma posición')


if __name__ == '__main__':
    unittest.main()
