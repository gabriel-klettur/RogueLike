from __future__ import annotations

from pathlib import Path

import importlib

import pytest


def test_write_goes_to_world_buildings_dir(tmp_path, monkeypatch):
    # Arrange: isolate DATA_DIR to tmp and reload config
    monkeypatch.setenv("RL_DATA_DIR", str(tmp_path))
    import roguelike_engine.config.config as cfg
    importlib.reload(cfg)

    # Create a WorldService bound to tmp_path/worlds and activate a world
    from roguelike_engine.worlds.service import WorldService

    svc = WorldService(worlds_root=Path(tmp_path) / "worlds")
    svc.activate("tw")  # sets cfg.BUILDINGS_INSTANCES_PATH to worlds/tw/buildings/buildings_instances.json

    # Act: write via buildings_repo
    from roguelike_game.ecs.systems.spawner.placement.buildings_repo import (
        write_buildings_instances_json,
    )

    data = [{"id": 1, "template_id": 123, "zone": "Z", "rel_x": 0, "rel_y": 0}]
    write_buildings_instances_json(data)

    # Assert: file exists at the redirected per-world path
    path = Path(cfg.BUILDINGS_INSTANCES_PATH)
    assert path.is_file(), f"Expected per-world instances at {path}"
    txt = path.read_text(encoding="utf-8")
    assert "\"template_id\": 123" in txt

    # And no file should be created under the global data/buildings path of this tmp DATA_DIR
    global_instances = Path(tmp_path) / "buildings" / "buildings_instances.json"
    assert not global_instances.exists(), f"Global path must not be used: {global_instances}"
