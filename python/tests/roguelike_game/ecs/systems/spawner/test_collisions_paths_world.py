from __future__ import annotations

from pathlib import Path
import importlib

import pytest


def test_collisions_write_uses_world_paths(tmp_path, monkeypatch):
    # Isolate data dir and reload config
    monkeypatch.setenv("RL_DATA_DIR", str(tmp_path))
    import roguelike_engine.config.config as cfg
    importlib.reload(cfg)

    # Activate a world to redirect paths
    from roguelike_engine.worlds.service import WorldService

    svc = WorldService(worlds_root=Path(tmp_path) / "worlds")
    svc.activate("tw")

    # Import persistence after activation so defaults capture redirected cfg paths
    from roguelike_editors.buildings.buildings_colliders_panel.collision_persistence import (
        CollisionPersistence,
    )

    # Minimal editor/model stubs
    class _E: pass
    class _M: pass

    # Create persistence with default paths and invoke write
    p = CollisionPersistence(editor_state=_E(), model=_M(), logger=cfg.logging.getLogger(__name__)) if hasattr(cfg, 'logging') else CollisionPersistence(editor_state=_E(), model=_M(), logger=__import__('logging').getLogger(__name__))

    # Call internal writer directly with empty payloads
    p._ensure_output_directory()
    p._write_files(by_image={}, by_spawn={}, by_instance={})

    # Assert: world-scoped files exist for spawn and instance
    by_spawn = Path(cfg.BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH)
    by_instance = Path(cfg.BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH)
    assert by_spawn.is_file(), f"Expected world collisions-by-spawn at {by_spawn}"
    assert by_instance.is_file(), f"Expected world collisions-by-instance at {by_instance}"

    # Assert: no global legacy files created under data/buildings of this tmp DATA_DIR
    global_dir = Path(tmp_path) / "buildings"
    assert not (global_dir / "buildings_collisions_by_spawn_id.json").exists()
    assert not (global_dir / "buildings_collisions_by_building_instance_id.json").exists()


def test_collisions_loader_reads_world_paths(tmp_path, monkeypatch):
    # Isolate data dir and reload config
    monkeypatch.setenv("RL_DATA_DIR", str(tmp_path))
    import roguelike_engine.config.config as cfg
    importlib.reload(cfg)

    # Activate a world to redirect paths
    from roguelike_engine.worlds.service import WorldService

    svc = WorldService(worlds_root=Path(tmp_path) / "worlds")
    svc.activate("tw")

    # Create files in the world paths with known content
    spawn_path = Path(cfg.BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH)
    inst_path = Path(cfg.BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH)
    spawn_path.parent.mkdir(parents=True, exist_ok=True)
    spawn_path.write_text('{"foo_spawn":"ok"}', encoding="utf-8")
    inst_path.write_text('{"42":{"width":1,"height":1,"collision":[[1]]}}', encoding="utf-8")

    # Also create different content in the legacy global paths (should be ignored)
    global_dir = Path(tmp_path) / "buildings"
    global_dir.mkdir(parents=True, exist_ok=True)
    (global_dir / "buildings_collisions_by_spawn_id.json").write_text('{"foo_spawn":"legacy"}', encoding="utf-8")
    (global_dir / "buildings_collisions_by_building_instance_id.json").write_text('{"42":{"width":2}}', encoding="utf-8")

    # Load via collisions_io and verify it picks world-level content
    from roguelike_editors.buildings.utils.collisions_io import load_collisions_sources

    _by_image, by_spawn, by_instance = load_collisions_sources()
    assert by_spawn.get("foo_spawn") == "ok"
    assert by_instance.get("42", {}).get("width") == 1
