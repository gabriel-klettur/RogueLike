import json
import importlib
import types

import pygame
import pytest


# [UTL-001] Carga básica
@pytest.mark.usefixtures("pygame_context")
def test_utl_001_load_buildings_basic_fields(tmp_path, monkeypatch):
    # Patch loader to avoid disk IO and control sizes
    import roguelike_engine.utils.loader as loader_mod
    def _fake_load_image(path, scale=None):
        # Base surface; BuildingModel will scale to entry["scale"]
        surf = pygame.Surface((32, 32), pygame.SRCALPHA)
        surf.fill((0, 0, 0, 0))
        return surf
    monkeypatch.setattr(loader_mod, "load_image", _fake_load_image, raising=True)
    import roguelike_engine.buildings.building_model as building_model_mod
    monkeypatch.setattr(building_model_mod, "load_image", _fake_load_image, raising=True)

    # Prepare JSON files and patch module constants
    b_data = [
        {
            "zone": "Lobby",  # should canonicalize to 'lobby'
            "rel_x": 10,
            "rel_y": 20,
            "image_path": "assets/buildings/dummy.png",
            "solid": True,
            "scale": [96, 64],
            "original_scale": [96, 64],
            "split_ratio": 0.33,
            "z_bottom": 5,
            "z_top": 9,
            "collider_scope": "CG",
        }
    ]
    buildings_json = tmp_path / "buildings_data.json"
    buildings_json.write_text(json.dumps(b_data), encoding="utf-8")

    collisions_json = tmp_path / "buildings_collisions_data.json"
    collisions_json.write_text(json.dumps({}), encoding="utf-8")

    load_mod = importlib.import_module("roguelike_editors.buildings.utils.load_buildings_from_json")
    monkeypatch.setattr(load_mod, "BUILDINGS_DATA_PATH", str(buildings_json), raising=True)
    monkeypatch.setattr(load_mod, "BUILDINGS_COLLISIONS_DATA_PATH", str(collisions_json), raising=True)

    buildings = load_mod.load_buildings_from_json()
    assert isinstance(buildings, list) and len(buildings) == 1
    b = buildings[0]

    # Basic fields
    assert b.zone == "lobby"  # canonicalized
    assert b.rel_x == 10 and b.rel_y == 20
    assert b.image_path == "assets/buildings/dummy.png"
    assert b.solid is True
    assert b.image.get_size() == (96, 64)
    assert pytest.approx(b.split_ratio, rel=1e-3) == 0.33
    assert b.z_bottom == 5 and b.z_top == 9
    assert getattr(b, "collider_scope", "CG") == "CG"


# [UTL-002] Normalización de colisiones (padding/truncado a ceil tiles)
@pytest.mark.usefixtures("pygame_context")
def test_utl_002_collision_map_normalization(tmp_path, monkeypatch):
    import roguelike_engine.utils.loader as loader_mod
    def _fake_load_image(path, scale=None):
        return pygame.Surface((32, 32), pygame.SRCALPHA)
    monkeypatch.setattr(loader_mod, "load_image", _fake_load_image, raising=True)
    import roguelike_engine.buildings.building_model as building_model_mod
    monkeypatch.setattr(building_model_mod, "load_image", _fake_load_image, raising=True)

    # Entry image 96x64 -> desired tiles: cols=3, rows=2 (TILE_SIZE=32)
    entry = {
        "zone": "lobby",
        "rel_x": 0,
        "rel_y": 0,
        "image_path": "assets/buildings/dummy.png",
        "solid": True,
        "scale": [96, 64],
    }
    buildings_json = tmp_path / "buildings_data.json"
    buildings_json.write_text(json.dumps([entry]), encoding="utf-8")

    # Collisions smaller than desired (1x1) -> must pad to 2x3
    collisions = {
        "assets/buildings/dummy.png": {
            "collision": [["#"]]
        }
    }
    collisions_json = tmp_path / "buildings_collisions_data.json"
    collisions_json.write_text(json.dumps(collisions), encoding="utf-8")

    load_mod = importlib.import_module("roguelike_editors.buildings.utils.load_buildings_from_json")
    monkeypatch.setattr(load_mod, "BUILDINGS_DATA_PATH", str(buildings_json), raising=True)
    monkeypatch.setattr(load_mod, "BUILDINGS_COLLISIONS_DATA_PATH", str(collisions_json), raising=True)

    buildings = load_mod.load_buildings_from_json()
    assert len(buildings) == 1
    b = buildings[0]

    rows = len(b.collision_map)
    cols = len(b.collision_map[0]) if rows > 0 else 0
    assert (rows, cols) == (2, 3)


# [UTL-003] Override CU normalizado
@pytest.mark.usefixtures("pygame_context")
def test_utl_003_collision_override_cu_normalization(tmp_path, monkeypatch):
    import roguelike_engine.utils.loader as loader_mod
    def _fake_load_image(path, scale=None):
        return pygame.Surface((32, 32), pygame.SRCALPHA)
    monkeypatch.setattr(loader_mod, "load_image", _fake_load_image, raising=True)
    import roguelike_engine.buildings.building_model as building_model_mod
    monkeypatch.setattr(building_model_mod, "load_image", _fake_load_image, raising=True)

    entry = {
        "zone": "lobby",
        "rel_x": 0,
        "rel_y": 0,
        "image_path": "assets/buildings/dummy.png",
        "solid": True,
        "scale": [96, 64],
        "collider_scope": "CU",
        "collision_override": {
            "width": 1,
            "height": 1,
            "collision": [["."]],
        },
    }
    buildings_json = tmp_path / "buildings_data.json"
    buildings_json.write_text(json.dumps([entry]), encoding="utf-8")

    # Global collisions (ignored by CU override)
    collisions = {
        "assets/buildings/dummy.png": {
            "collision": [["#", "#"], ["#", "#"]]
        }
    }
    collisions_json = tmp_path / "buildings_collisions_data.json"
    collisions_json.write_text(json.dumps(collisions), encoding="utf-8")

    load_mod = importlib.import_module("roguelike_editors.buildings.utils.load_buildings_from_json")
    monkeypatch.setattr(load_mod, "BUILDINGS_DATA_PATH", str(buildings_json), raising=True)
    monkeypatch.setattr(load_mod, "BUILDINGS_COLLISIONS_DATA_PATH", str(collisions_json), raising=True)

    buildings = load_mod.load_buildings_from_json()
    assert len(buildings) == 1
    b = buildings[0]
    rows = len(b.collision_map)
    cols = len(b.collision_map[0]) if rows > 0 else 0
    assert (rows, cols) == (2, 3)  # normalized to image tiles


# [UTL-004] Inyección de Z desde z_state
@pytest.mark.usefixtures("pygame_context")
def test_utl_004_inject_z_when_z_state_provided(tmp_path, monkeypatch):
    import roguelike_engine.utils.loader as loader_mod
    def _fake_load_image(path, scale=None):
        return pygame.Surface((32, 32), pygame.SRCALPHA)
    monkeypatch.setattr(loader_mod, "load_image", _fake_load_image, raising=True)
    import roguelike_engine.buildings.building_model as building_model_mod
    monkeypatch.setattr(building_model_mod, "load_image", _fake_load_image, raising=True)

    entry = {
        "zone": "lobby",
        "rel_x": 0,
        "rel_y": 0,
        "image_path": "assets/buildings/dummy.png",
        "solid": True,
        "scale": [64, 64],
        "z": 17,
    }
    buildings_json = tmp_path / "buildings_data.json"
    buildings_json.write_text(json.dumps([entry]), encoding="utf-8")
    collisions_json = tmp_path / "buildings_collisions_data.json"
    collisions_json.write_text(json.dumps({}), encoding="utf-8")

    load_mod = importlib.import_module("roguelike_editors.buildings.utils.load_buildings_from_json")
    monkeypatch.setattr(load_mod, "BUILDINGS_DATA_PATH", str(buildings_json), raising=True)
    monkeypatch.setattr(load_mod, "BUILDINGS_COLLISIONS_DATA_PATH", str(collisions_json), raising=True)

    class ZState:
        def __init__(self):
            self.calls = []
        def set(self, entity, z):
            self.calls.append((entity, z))
        def get(self, entity):
            return 0

    z_state = ZState()
    buildings = load_mod.load_buildings_from_json(z_state=z_state)
    assert len(buildings) == 1
    b = buildings[0]
    assert b.z == 17
    assert len(z_state.calls) == 1
    assert z_state.calls[0][1] == 17


# [UTL-005] Canonicalización de zonas (incluye 'no zone')
@pytest.mark.usefixtures("pygame_context")
def test_utl_005_zone_canonicalization_and_no_zone(tmp_path, monkeypatch):
    import roguelike_engine.utils.loader as loader_mod
    def _fake_load_image(path, scale=None):
        return pygame.Surface((32, 32), pygame.SRCALPHA)
    monkeypatch.setattr(loader_mod, "load_image", _fake_load_image, raising=True)
    import roguelike_engine.buildings.building_model as building_model_mod
    monkeypatch.setattr(building_model_mod, "load_image", _fake_load_image, raising=True)

    data = [
        {"zone": "Lobby", "rel_x": 0, "rel_y": 0, "image_path": "assets/buildings/a.png", "scale": [32, 32]},
        {"zone": "no zone", "rel_x": 0, "rel_y": 0, "image_path": "assets/buildings/b.png", "scale": [32, 32]},
        {"zone": "DUngEOn", "rel_x": 0, "rel_y": 0, "image_path": "assets/buildings/c.png", "scale": [32, 32]},
    ]
    buildings_json = tmp_path / "buildings_data.json"
    buildings_json.write_text(json.dumps(data), encoding="utf-8")
    collisions_json = tmp_path / "buildings_collisions_data.json"
    collisions_json.write_text(json.dumps({}), encoding="utf-8")

    load_mod = importlib.import_module("roguelike_editors.buildings.utils.load_buildings_from_json")
    monkeypatch.setattr(load_mod, "BUILDINGS_DATA_PATH", str(buildings_json), raising=True)
    monkeypatch.setattr(load_mod, "BUILDINGS_COLLISIONS_DATA_PATH", str(collisions_json), raising=True)

    buildings = load_mod.load_buildings_from_json()
    assert [b.zone for b in buildings] == ["lobby", "no zone", "dungeon"]


# [UTL-007] Asignación de zona y relativos
@pytest.mark.usefixtures("pygame_context")
def test_utl_007_assign_zone_and_relatives_sets_zone_and_rel(surface_factory):
    from roguelike_engine.config.config_tiles import TILE_SIZE
    from roguelike_engine.config.map_config import global_map_settings
    from roguelike_editors.buildings.utils.zone_helpers import assign_zone_and_relatives

    # Fake building object
    img = surface_factory(64, 64)
    b = types.SimpleNamespace(
        image=img,
        x=0,
        y=0,
        zone=None,
        rel_x=0,
        rel_y=0,
    )

    # Place it inside dungeon zone (bottom of lobby by default)
    ox, oy = global_map_settings.zone_offsets["dungeon"]
    b.x = ox * TILE_SIZE
    b.y = oy * TILE_SIZE

    assign_zone_and_relatives(b)
    assert b.zone == "dungeon"
    assert b.rel_x == b.x - ox * TILE_SIZE
    assert b.rel_y == b.y - oy * TILE_SIZE


# [UTL-008] Detección de zona por px fuera de zonas válidas => ("no zone", (0,0))
@pytest.mark.usefixtures("pygame_context")
def test_utl_008_detect_zone_from_px_outside_returns_no_zone():
    from roguelike_editors.buildings.utils.zone_helpers import detect_zone_from_px

    zone, offset = detect_zone_from_px(-10, -5)
    assert zone == "no zone"
    assert tuple(offset) == (0, 0)
