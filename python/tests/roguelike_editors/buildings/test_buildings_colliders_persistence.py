import json
import types

import pygame
import pytest


@pytest.mark.usefixtures("pygame_context")
def test_col_001_cg_saves_by_image_path_and_ignores_spawner(monkeypatch, tmp_path, surface_factory, camera):
    # Import handler and model
    from roguelike_editors.buildings.buildings_colliders_panel.building_colliders_panel_events import (
        BuildingCollidersPanelEventHandler as Handler,
    )
    from roguelike_editors.buildings.buildings_colliders_panel.building_colliders_panel_model import (
        BuildingCollidersPanelModel as Model,
    )

    # Patch output path
    import roguelike_editors.buildings.buildings_colliders_panel.building_colliders_panel_events as ev_mod
    out_by_image = tmp_path / "buildings_collisions_by_image.json"
    out_by_spawn = tmp_path / "buildings_collisions_by_spawn_id.json"
    out_by_binst = tmp_path / "buildings_collisions_by_building_instance_id.json"
    monkeypatch.setattr(ev_mod, "BUILDINGS_COLLISIONS_BY_IMAGE_PATH", str(out_by_image), raising=True)
    monkeypatch.setattr(ev_mod, "BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH", str(out_by_spawn), raising=True)
    monkeypatch.setattr(ev_mod, "BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH", str(out_by_binst), raising=True)

    # Fake editor_state with UI scope = CG
    editor_state = types.SimpleNamespace(collider_scope="CG")

    # Create two buildings with same image_path: one normal, one spawner visual (ignored)
    img = surface_factory(64, 64)
    normal = types.SimpleNamespace(
        x=0, y=0, image=img, collision_map=[[".", "."], [".", "."]],
        image_path="/virtual/dummy.png", collider_scope="CU",  # building marked CU but UI says CG
        _is_spawner_visual=False, spawner_instance_id=None,
    )
    spawner_visual = types.SimpleNamespace(
        x=1000, y=1000, image=img, collision_map=[["#", "#"], ["#", "#"]],
        image_path="/virtual/dummy.png", collider_scope="CG",
        _is_spawner_visual=True, spawner_instance_id="inst-1",
    )
    buildings = [normal, spawner_visual]

    # Prepare handler/model
    model = Model(active=True, picker_open=False)
    model.choice = '#'
    h = Handler(state=types.SimpleNamespace(), editor_state=editor_state, model=model)

    # Paint one tile on the normal building via brush
    monkeypatch.setattr(pygame.mouse, "get_pos", lambda: (10, 10), raising=True)
    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 1, "pos": (10, 10)})
    h.handle(ev_down, camera, buildings)
    assert model.brush_dragging is True

    ev_up = pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 1, "pos": (10, 10)})
    h.handle(ev_up, camera, buildings)

    # Verify by_image file written and spawner visual was ignored
    with open(out_by_image, "r", encoding="utf-8") as f:
        data = json.load(f)
    assert "/virtual/dummy.png" in data
    cg_entry = data["/virtual/dummy.png"]
    assert cg_entry["width"] == 2 and cg_entry["height"] == 2
    # Saved collision should match normal.collision_map (not spawner_visual)
    assert cg_entry["collision"] == normal.collision_map
    # CU file should remain empty in this CG save
    assert not out_by_binst.exists() or json.loads(out_by_binst.read_text(encoding="utf-8")) == {}


@pytest.mark.usefixtures("pygame_context")
def test_col_002_cu_saves_by_building_instance_id(monkeypatch, tmp_path, surface_factory, camera):
    from roguelike_editors.buildings.buildings_colliders_panel.building_colliders_panel_events import (
        BuildingCollidersPanelEventHandler as Handler,
    )
    from roguelike_editors.buildings.buildings_colliders_panel.building_colliders_panel_model import (
        BuildingCollidersPanelModel as Model,
    )

    import roguelike_editors.buildings.buildings_colliders_panel.building_colliders_panel_events as ev_mod
    out_by_image = tmp_path / "buildings_collisions_by_image.json"
    out_by_spawn = tmp_path / "buildings_collisions_by_spawn_id.json"
    out_by_binst = tmp_path / "buildings_collisions_by_building_instance_id.json"
    monkeypatch.setattr(ev_mod, "BUILDINGS_COLLISIONS_BY_IMAGE_PATH", str(out_by_image), raising=True)
    monkeypatch.setattr(ev_mod, "BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH", str(out_by_spawn), raising=True)
    monkeypatch.setattr(ev_mod, "BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH", str(out_by_binst), raising=True)

    editor_state = types.SimpleNamespace(collider_scope="CU")

    img = surface_factory(64, 64)
    b = types.SimpleNamespace(
        x=0, y=0, image=img, collision_map=[[".", "."], [".", "."]],
        image_path="/virtual/dummy.png", collider_scope="CG", id=123,
        _is_spawner_visual=False, spawner_instance_id=None,
    )

    # Enforced behavior: brush paints only on the currently selected (active) building.
    # Set the active building so that CU saves by building_instance_id.
    editor_state.active_building = b

    model = Model(active=True)
    model.choice = '#'
    h = Handler(state=types.SimpleNamespace(), editor_state=editor_state, model=model)

    # Paint and persist
    monkeypatch.setattr(pygame.mouse, "get_pos", lambda: (10, 10), raising=True)
    h.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 1, "pos": (10, 10)}), camera, [b])
    h.handle(pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 1, "pos": (10, 10)}), camera, [b])

    with open(out_by_binst, "r", encoding="utf-8") as f:
        data = json.load(f)
    assert data.get("123") is not None
    cu_entry = data["123"]
    assert cu_entry["width"] == 2 and cu_entry["height"] == 2
    assert cu_entry["collision"] == b.collision_map
    # No CG saved when scope is CU
    assert not out_by_image.exists() or json.loads(out_by_image.read_text(encoding="utf-8")) == {}


@pytest.mark.usefixtures("pygame_context")
def test_col_003_loader_priority_by_scope(monkeypatch, tmp_path, surface_factory):
    # Patch loader to avoid disk IO and control sizes
    import roguelike_engine.utils.loader as loader_mod

    def _fake_load_image(path, scale=None):
        surf = pygame.Surface((64, 64), pygame.SRCALPHA)
        surf.fill((0, 0, 0, 0))
        return surf

    monkeypatch.setattr(loader_mod, "load_image", _fake_load_image, raising=True)
    import roguelike_engine.buildings.building_model as building_model_mod
    monkeypatch.setattr(building_model_mod, "load_image", _fake_load_image, raising=True)

    # Prepare split data: templates + instances, and split collisions files
    # Templates define visual/size; instances define position/zone and collider_scope via overrides
    templates = [
        {"id": 100, "assets": {"idle": "/virtual/dummy.png"}, "solid": True, "scale": [64, 64]},
        {"id": 101, "assets": {"idle": "/virtual/dummy.png"}, "solid": True, "scale": [64, 64]},
    ]
    instances = [
        {"id": 1, "template_id": 100, "zone": "lobby", "rel_x": 0, "rel_y": 0, "overrides": {"collider_scope": "CG"}},
        {"id": 2, "template_id": 101, "zone": "lobby", "rel_x": 64, "rel_y": 0, "overrides": {"collider_scope": "CU"}},
    ]
    templates_json = tmp_path / "buildings_templates.json"
    instances_json = tmp_path / "buildings_instances.json"
    templates_json.write_text(json.dumps(templates), encoding="utf-8")
    instances_json.write_text(json.dumps(instances), encoding="utf-8")

    # Split collisions: by_image_path and by_building_instance_id
    by_image = {
        "/virtual/dummy.png": {"width": 2, "height": 2, "collision": [["#", "#"], ["#", "#"]]}
    }
    by_binst = {
        "2": {"width": 2, "height": 2, "collision": [[".", "."], [".", "."]]}
    }
    by_spawn = {"legacy": {"width": 2, "height": 2, "collision": [["x", "x"], ["x", "x"]]}}
    by_image_json = tmp_path / "buildings_collisions_by_image.json"
    by_binst_json = tmp_path / "buildings_collisions_by_building_instance_id.json"
    by_spawn_json = tmp_path / "buildings_collisions_by_spawn_id.json"
    by_image_json.write_text(json.dumps(by_image), encoding="utf-8")
    by_binst_json.write_text(json.dumps(by_binst), encoding="utf-8")
    by_spawn_json.write_text(json.dumps(by_spawn), encoding="utf-8")

    # Load with patched split paths
    import importlib
    load_mod = importlib.import_module("roguelike_editors.buildings.utils.load_buildings_from_json")
    monkeypatch.setattr(load_mod, "BUILDINGS_TEMPLATES_PATH", str(templates_json), raising=True)
    monkeypatch.setattr(load_mod, "BUILDINGS_INSTANCES_PATH", str(instances_json), raising=True)
    monkeypatch.setattr(load_mod, "BUILDINGS_COLLISIONS_BY_IMAGE_PATH", str(by_image_json), raising=True)
    monkeypatch.setattr(load_mod, "BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH", str(by_spawn_json), raising=True)
    monkeypatch.setattr(load_mod, "BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH", str(by_binst_json), raising=True)

    buildings = load_mod.load_buildings_from_json()
    assert len(buildings) == 2

    # CG building should take by_image_path
    b_cg = next(b for b in buildings if getattr(b, "collider_scope", "CG") == "CG")
    # CU building should take by_building_instance_id
    b_cu = next(b for b in buildings if getattr(b, "collider_scope", "CG") == "CU")

    assert b_cg.collision_map == [["#", "#"], ["#", "#"]]
    assert b_cu.collision_map == [[".", "."], [".", "."]]


@pytest.mark.usefixtures("pygame_context")
def test_col_004_ui_scope_overrides_building_scope_for_saving(monkeypatch, tmp_path, surface_factory, camera):
    from roguelike_editors.buildings.buildings_colliders_panel.building_colliders_panel_events import (
        BuildingCollidersPanelEventHandler as Handler,
    )
    from roguelike_editors.buildings.buildings_colliders_panel.building_colliders_panel_model import (
        BuildingCollidersPanelModel as Model,
    )

    import roguelike_editors.buildings.buildings_colliders_panel.building_colliders_panel_events as ev_mod
    out_by_image = tmp_path / "buildings_collisions_by_image.json"
    out_by_spawn = tmp_path / "buildings_collisions_by_spawn_id.json"
    out_by_binst = tmp_path / "buildings_collisions_by_building_instance_id.json"
    monkeypatch.setattr(ev_mod, "BUILDINGS_COLLISIONS_BY_IMAGE_PATH", str(out_by_image), raising=True)
    monkeypatch.setattr(ev_mod, "BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH", str(out_by_spawn), raising=True)
    monkeypatch.setattr(ev_mod, "BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH", str(out_by_binst), raising=True)

    # Building marked CU but UI says CG
    editor_state = types.SimpleNamespace(collider_scope="CG")
    img = surface_factory(64, 64)
    b = types.SimpleNamespace(
        x=0, y=0, image=img, collision_map=[[".", "."], [".", "."]],
        image_path="/virtual/dummy.png", collider_scope="CU", id=999,
        _is_spawner_visual=False, spawner_instance_id=None,
    )

    model = Model(active=True)
    model.choice = '#'
    h = Handler(state=types.SimpleNamespace(), editor_state=editor_state, model=model)

    # Paint and persist
    monkeypatch.setattr(pygame.mouse, "get_pos", lambda: (10, 10), raising=True)
    h.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 1, "pos": (10, 10)}), camera, [b])
    h.handle(pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 1, "pos": (10, 10)}), camera, [b])

    with open(out_by_image, "r", encoding="utf-8") as f:
        data = json.load(f)
    # Should have saved by image_path because UI scope is CG
    assert "/virtual/dummy.png" in data
    # by_instance file should remain empty
    assert not out_by_binst.exists() or json.loads(out_by_binst.read_text(encoding="utf-8")) == {}
