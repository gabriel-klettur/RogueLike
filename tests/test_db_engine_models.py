from __future__ import annotations

"""Unit tests for database engine and ORM models.

- No external dependencies (uses unittest from stdlib).
- Covers session_scope commit/rollback, PRAGMAs best-effort, and CRUD across models.
- Tests run against the project's configured SQLite file. They clean up their own rows.

Run:
    python -m unittest -v tests/test_db_engine_models.py
"""

import os
import unittest
from pathlib import Path

# Ensure src/ is importable
import sys
sys.path.append(str(Path(__file__).resolve().parents[1] / "src"))

from sqlalchemy import text, select, func

from roguelike_engine.db.engine import engine, session_scope
from roguelike_engine.db import models as M


class TestEngineSession(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        # Ensure tables exist (safe if already created)
        M.Base.metadata.create_all(bind=engine)

    def test_session_scope_commit_and_rollback(self) -> None:
        # Commit case
        spell_id_commit = "test_spell_commit"
        with session_scope() as s:
            s.add(M.Spell(id=spell_id_commit, name="Commit", type=None, element=None,
                          mana_cost=None, cooldown_ms=None, tags=None, extra_json=None))
        # Verify committed
        with session_scope() as s:
            exists = s.execute(
                select(func.count()).select_from(M.Spell).where(M.Spell.id == spell_id_commit)
            ).scalar_one()
            self.assertEqual(exists, 1)
        # Cleanup
        with session_scope() as s:
            s.execute(text("DELETE FROM spells WHERE id = :id"), {"id": spell_id_commit})

        # Rollback case
        spell_id_rb = "test_spell_rollback"
        with self.assertRaises(RuntimeError):
            with session_scope() as s:
                s.add(M.Spell(id=spell_id_rb, name="Rollback", type=None, element=None,
                              mana_cost=None, cooldown_ms=None, tags=None, extra_json=None))
                # Force an error so context manager rolls back
                raise RuntimeError("force rollback")
        # Verify not present
        with session_scope() as s:
            exists = s.execute(
                select(func.count()).select_from(M.Spell).where(M.Spell.id == spell_id_rb)
            ).scalar_one()
            self.assertEqual(exists, 0)

    def test_engine_pragmas_best_effort(self) -> None:
        # We expect PRAGMAs to be set on SQLite; but don't fail hard if unsupported.
        with session_scope() as s:
            # journal_mode
            jm = s.execute(text("PRAGMA journal_mode;"))
            row = jm.first()
            self.assertIsNotNone(row)
            # value typically 'wal' or 'delete' depending on support
            # ensure it returns something non-empty
            self.assertTrue(str(row[0]).strip() != "")

            # synchronous
            syn = s.execute(text("PRAGMA synchronous;"))
            syn_row = syn.first()
            self.assertIsNotNone(syn_row)
            # value is integer 0/1/2; ensure it's parsable as int
            int(str(syn_row[0]))


class TestModelsCRUD(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        # Ensure tables exist
        M.Base.metadata.create_all(bind=engine)

    def test_crud_all_models(self) -> None:
        # Prepare IDs
        ent_id = "test_entity"
        spawner_id = "test_spawner"
        spawn_table_id = "test_spawn_table"
        building_instance_id = "test_building"
        spell_id = "test_spell"

        # Insert rows
        with session_scope() as s:
            # Entity
            s.add(M.Entity(id=ent_id, kind="hostile", name="Test Entity", level=1,
                           hp=10, atk=2, def_=1, speed=1.5, ai_behavior="idle",
                           loot_table_id=None, extra_json=None))
            # SpawnerInstance
            s.add(M.SpawnerInstance(id=spawner_id, map_id="TestMap", x=5, y=7, radius=10,
                            max_count=3, respawn_seconds=5, conditions_json="{}",
                            spawn_table_id=spawn_table_id))
            # BuildingInstance
            s.add(M.BuildingInstance(instance_id=building_instance_id, image_id=None,
                                     spawn_id=None, zone_id="ZoneA"))
            # BuildingCollision
            s.add(M.BuildingCollision(instance_id=building_instance_id, kind="tile",
                                      shape_wkt="MULTIPOLYGON EMPTY", extra_json=None))
            # Spell
            s.add(M.Spell(id=spell_id, name="Bolt", type="active", element="fire",
                          mana_cost=5.0, cooldown_ms=250, tags="dmg,fire", extra_json=None))
            # ImportLog
            s.add(M.ImportLog(source_path="tests://unit", content_hash="abc123",
                               imported_at="2025-10-21T00:00:00Z", row_count=6,
                               version="ut_v1"))

        # Query-back assertions
        with session_scope() as s:
            # Entity
            e = s.execute(select(M.Entity).where(M.Entity.id == ent_id)).scalar_one()
            self.assertEqual(e.name, "Test Entity")
            self.assertEqual(e.hp, 10)
            # SpawnerInstance
            sp = s.execute(select(M.SpawnerInstance).where(M.SpawnerInstance.id == spawner_id)).scalar_one()
            self.assertEqual(sp.map_id, "TestMap")
            # BuildingInstance
            bi = s.execute(select(M.BuildingInstance).where(M.BuildingInstance.instance_id == building_instance_id)).scalar_one()
            self.assertEqual(bi.zone_id, "ZoneA")
            # BuildingCollision
            bc = s.execute(select(M.BuildingCollision).where(M.BuildingCollision.instance_id == building_instance_id)).scalar_one()
            self.assertTrue("MULTIPOLYGON" in bc.shape_wkt)
            # Spell
            spk = s.execute(select(M.Spell).where(M.Spell.id == spell_id)).scalar_one()
            self.assertEqual(spk.element, "fire")
            # ImportLog
            ilc = s.execute(select(func.count()).select_from(M.ImportLog).where(M.ImportLog.source_path == "tests://unit")).scalar_one()
            self.assertEqual(ilc, 1)

        # Cleanup
        with session_scope() as s:
            s.execute(text("DELETE FROM spawners_instances WHERE id = :id"), {"id": spawner_id})
            s.execute(text("DELETE FROM building_collisions WHERE instance_id = :id"), {"id": building_instance_id})
            s.execute(text("DELETE FROM building_instances WHERE instance_id = :id"), {"id": building_instance_id})
            s.execute(text("DELETE FROM entities WHERE id = :id"), {"id": ent_id})
            s.execute(text("DELETE FROM spells WHERE id = :id"), {"id": spell_id})
            s.execute(text("DELETE FROM import_log WHERE source_path = :sp"), {"sp": "tests://unit"})


if __name__ == "__main__":
    unittest.main(verbosity=2)
