import inspect
import importlib
import json
import types

import pygame
import pytest


def test_utils_callables_present():
    load_mod = importlib.import_module("roguelike_editors.buildings.utils.load_buildings_from_json")
    save_mod = importlib.import_module("roguelike_editors.buildings.utils.save_buildings_to_json")
    zones_mod = importlib.import_module("roguelike_editors.buildings.utils.zone_helpers")

    assert callable(load_mod.load_buildings_from_json)
    assert callable(save_mod.save_buildings_to_json)
    assert zones_mod is not None

    # Basic signature sanity (do not enforce exact names/arity beyond being callable)
    assert isinstance(inspect.signature(load_mod.load_buildings_from_json), inspect.Signature)
    assert isinstance(inspect.signature(save_mod.save_buildings_to_json), inspect.Signature)


# [UTL-006] Guardado: JSON contiene campos esperados y collision_override para CU
def test_utl_006_save_buildings_json_contains_expected_fields(tmp_path, surface_factory):
    save_mod = importlib.import_module("roguelike_editors.buildings.utils.save_buildings_to_json")

    class _B:
        def __init__(self, image: pygame.Surface, scope="CG"):
            self.zone = "Lobby"  # debe canonicalizarse a key válida
            self.rel_x = 123
            self.rel_y = 45
            self.image_path = "assets/buildings/dummy.png"
            self.solid = True
            self.image = image
            self.original_scale = image.get_size()
            self.split_ratio = 0.42
            self.z_bottom = 10
            self.z_top = 20
            self.collider_scope = scope
            # collision_map sólo relevante para CU
            self.collision_map = [["#", "."], [".", "#"]]

    # Patch split output paths to temp files
    t_out = tmp_path / "buildings_templates.json"
    i_out = tmp_path / "buildings_instances.json"
    setattr(save_mod, "BUILDINGS_TEMPLATES_PATH", str(t_out))
    setattr(save_mod, "BUILDINGS_INSTANCES_PATH", str(i_out))

    b1 = _B(surface_factory(64, 48), scope="CG")
    b2 = _B(surface_factory(96, 64), scope="CU")

    # Call legacy wrapper (delegates to split)
    save_mod.save_buildings_to_json([b1, b2], filepath=str(tmp_path / "ignored.json"))

    # Validate templates.json
    with open(t_out, "r", encoding="utf-8") as f:
        templates = json.load(f)
    assert isinstance(templates, list) and len(templates) == 2
    for te in templates:
        # Campos base presentes en templates
        assert set(["id", "assets", "solid", "split_ratio", "collider_scope"]).issubset(te.keys())
        assert isinstance(te.get("assets"), dict) and "idle" in te["assets"]

    # Validate instances.json
    with open(i_out, "r", encoding="utf-8") as f:
        instances = json.load(f)
    assert isinstance(instances, list) and len(instances) == 2

    # Instancia CG
    inst_cg = instances[0]
    assert set(["id", "template_id", "zone", "rel_x", "rel_y"]).issubset(inst_cg.keys())
    assert inst_cg["zone"].lower() == "lobby"
    assert inst_cg["rel_x"] == 123 and inst_cg["rel_y"] == 45
    assert isinstance(inst_cg.get("overrides"), dict)
    assert inst_cg["overrides"]["scale"] == [64, 48]

    # Instancia CU incluye collision_override en overrides
    inst_cu = instances[1]
    assert set(["id", "template_id", "zone", "rel_x", "rel_y"]).issubset(inst_cu.keys())
    assert isinstance(inst_cu.get("overrides"), dict)
    ov = inst_cu["overrides"]
    assert ov.get("collider_scope") == "CU"
    co = ov.get("collision_override")
    assert co is not None
    assert co["width"] == 2  # columnas
    assert co["height"] == 2 # filas
    assert co["collision"] == [["#", "."], [".", "#"]]


# [UTL-009] Redimensionado del collision_map tras resize del BuildingModel
def test_utl_009_building_model_resize_resamples_collision_map(monkeypatch):
    from roguelike_engine.config.config_tiles import TILE_SIZE
    # Monkeypatch loader to avoid disk IO
    import roguelike_engine.utils.loader as loader_mod

    def _fake_load_image(path, scale=None):
        # base 64x64; caller will scale as needed
        surf = pygame.Surface((64, 64), pygame.SRCALPHA)
        surf.fill((0, 0, 0, 0))
        return surf

    monkeypatch.setattr(loader_mod, "load_image", _fake_load_image, raising=True)

    # Also patch the symbol used inside building_model module so __init__ uses fake loader
    import roguelike_engine.buildings.building_model as building_model_mod
    monkeypatch.setattr(building_model_mod, "load_image", _fake_load_image, raising=True)
    from roguelike_engine.buildings.building_model import BuildingModel

    # Inicial: 64x64 => 2x2 tiles
    b = BuildingModel(rel_x=0, rel_y=0, image_path="assets/buildings/dummy.png", solid=True, scale=(64, 64))
    # collision_map inicial 2x2
    b.collision_map = [["#", "."], [".", "#"]]

    # Redimensionar a 96x64 => 3x2 tiles
    b.resize(96, 64)
    rows = len(b.collision_map)
    cols = len(b.collision_map[0]) if rows > 0 else 0
    assert rows == (64 + TILE_SIZE - 1) // TILE_SIZE  # 64/32 => 2
    assert cols == (96 + TILE_SIZE - 1) // TILE_SIZE  # 96/32 => 3

    # Redimensionar a 64x32 => 2x1 tiles
    b.resize(64, 32)
    rows = len(b.collision_map)
    cols = len(b.collision_map[0]) if rows > 0 else 0
    assert rows == (32 + TILE_SIZE - 1) // TILE_SIZE  # 32/32 => 1
    assert cols == (64 + TILE_SIZE - 1) // TILE_SIZE  # 64/32 => 2
