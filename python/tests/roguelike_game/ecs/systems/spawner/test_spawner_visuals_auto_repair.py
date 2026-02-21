from __future__ import annotations

import types

import pytest


def test_auto_repair_binds_existing_and_reconstructs_missing_building(monkeypatch):
    # Imports deferred to allow sys.path adjustments from conftest
    from roguelike_game.ecs.systems.spawner.placement.visuals_auto_repair import (
        auto_repair_state_visuals,
    )

    # --- World/cfg/inst stubs ---
    class World:
        def __init__(self):
            self.buildings = []  # no building with id=2 yet

    world = World()
    eid = 999
    cfg = types.SimpleNamespace(state_visuals=None, visible_in_game=True)
    inst = {
        "id": "survival_10_forest_14_9",
        "zone": "Forest",
        "tile": (25, 18),
        "visuals": {
            # Reference to existing building id=2, with known template 155
            "WaitCooldown": {"instance_id": 2, "template_id": 155}
        },
    }

    # --- Patch data sources: buildings instances already include id=2 ---
    b_arr = [
        {
            "id": 1,
            "template_id": 157,
            "zone": "Forest",
            "rel_x": 0,
            "rel_y": 0,
        },
        {
            "id": 2,
            "template_id": 155,
            "zone": "Forest",
            "rel_x": 100,
            "rel_y": 200,
        },
    ]
    existing_ids = {1, 2}
    max_id = 2

    monkeypatch.setattr(
        "roguelike_game.ecs.systems.spawner.placement.visuals_auto_repair.load_buildings_data",
        lambda: (b_arr, existing_ids, max_id),
    )

    # --- Patch templates map and append function to avoid real Building deps ---
    templates = [
        {"id": 155, "assets": {"idle": "assets/buildings/fake.png"}},
        {"id": 157, "assets": {"idle": "assets/buildings/fake2.png"}},
    ]
    tmap = {155: templates[0], 157: templates[1]}
    monkeypatch.setattr(
        "roguelike_game.ecs.systems.spawner.placement.visuals_auto_repair.load_templates_map",
        lambda: (templates, tmap),
    )

    added: list[dict] = []

    def _append(world_obj, inst_entry, tpl_entry, img_path):
        # Append a minimal dummy carrying the id for assertion
        dummy = types.SimpleNamespace(id=inst_entry.get("id"))
        world_obj.buildings.append(dummy)

    monkeypatch.setattr(
        "roguelike_game.ecs.systems.spawner.placement.visuals_auto_repair.append_building_object_in_world",
        _append,
    )

    # Act: auto-repair should bind mapping to id=2 and create missing building object in memory
    auto_repair_state_visuals(world, eid, cfg, inst)

    # Assert: mapping updated and world now contains the reconstructed building id=2
    assert isinstance(cfg.state_visuals, dict)
    assert cfg.state_visuals.get("WaitCooldown") == 2
    ids = [getattr(b, "id", None) for b in world.buildings]
    assert 2 in ids, f"Expected reconstructed building id=2 in world, got {ids}"
