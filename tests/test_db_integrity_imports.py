from __future__ import annotations

"""Referential integrity and idempotent re-import tests.

- Verifies there are no orphan `spawn_table_entries.entity_id` without matching `entities.id`.
- Verifies the three importers are idempotent (second run reports no changes).

Run:
    python -m unittest -v tests/test_db_integrity_imports.py
"""

from pathlib import Path
import sys
import unittest
import importlib.util

# Ensure src/ is importable; project root is already on sys.path when running from repo root
sys.path.append(str(Path(__file__).resolve().parents[1] / "src"))

from sqlalchemy import select

from roguelike_engine.db.engine import session_scope
from roguelike_engine.db import models as M

def _load_module_from_path(mod_name: str, rel_path: str):
    """Load a module by file path so tests don't depend on package import paths."""
    file_path = Path(rel_path).resolve()
    spec = importlib.util.spec_from_file_location(mod_name, file_path)
    if spec is None or spec.loader is None:
        raise ImportError(f"Cannot load module {mod_name} from {file_path}")
    mod = importlib.util.module_from_spec(spec)
    # Ensure the module is visible during execution (needed by dataclasses/type resolution)
    sys.modules[mod_name] = mod
    mod.__file__ = str(file_path)
    if mod_name.rfind(".") != -1:
        mod.__package__ = mod_name.rpartition(".")[0]
    else:
        mod.__package__ = ""
    spec.loader.exec_module(mod)  # type: ignore[attr-defined]
    return mod

imp_entities = _load_module_from_path("import_entities", "scripts/import_entities.py")
imp_spawners = _load_module_from_path("import_spawners", "scripts/import_spawners.py")
imp_buildings = _load_module_from_path("import_buildings", "scripts/import_buildings.py")


class TestReferentialIntegrity(unittest.TestCase):
    def test_spawn_table_entries_have_valid_entities(self) -> None:
        """All entries must reference an existing `entities.id`."""
        with session_scope() as s:
            missing = (
                s.execute(
                    select(M.SpawnTableEntry.entity_id)
                    .outerjoin(M.Entity, M.Entity.id == M.SpawnTableEntry.entity_id)
                    .where(M.Entity.id.is_(None))
                    .distinct()
                )
                .scalars()
                .all()
            )
        self.assertEqual(missing, [], msg=f"Orphan entity_ids found: {missing}")


class TestReimportIdempotency(unittest.TestCase):
    def test_entities_import_is_idempotent(self) -> None:
        sources = [
            Path("data/entities/new_hostiles.json"),
            Path("data/entities/new_neutrals.json"),
            Path("data/entities/new_players.json"),
        ]
        # First pass: ensure a run happens (imported or skipped acceptable)
        for src in sources:
            _ = imp_entities.import_one(src)
        # Second pass: must be skipped (imported == False)
        for src in sources:
            outcome = imp_entities.import_one(src)
            self.assertFalse(outcome.imported, msg=f"Entities re-import not idempotent for {src}")

    def test_spawners_import_is_idempotent(self) -> None:
        # First run to establish baseline
        _ = imp_spawners.import_spawners()
        # Second run should report unchanged
        res2 = imp_spawners.import_spawners()
        self.assertFalse(res2.imported, msg="Spawners re-import should be skipped on unchanged input")

    def test_buildings_import_is_idempotent(self) -> None:
        # First run to establish baseline
        _ = imp_buildings.import_buildings()
        # Second run should report unchanged
        res2 = imp_buildings.import_buildings()
        self.assertFalse(res2.imported, msg="Buildings re-import should be skipped on unchanged input")


if __name__ == "__main__":
    unittest.main(verbosity=2)
