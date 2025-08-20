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

    out = tmp_path / "buildings.json"

    b1 = _B(surface_factory(64, 48), scope="CG")
    b2 = _B(surface_factory(96, 64), scope="CU")

    save_mod.save_buildings_to_json([b1, b2], filepath=str(out))

    with open(out, "r", encoding="utf-8") as f:
        data = json.load(f)

    # Deben existir dos entradas
    assert isinstance(data, list) and len(data) == 2
    e1, e2 = data

    # Campos base presentes
    for e in (e1, e2):
        assert set(["zone", "rel_x", "rel_y", "image_path", "solid", "scale", "original_scale", "split_ratio", "z_bottom", "z_top", "collider_scope"]).issubset(e.keys())

    # 'scale' refleja tamaño actual de imagen
    assert e1["scale"] == [64, 48]
    assert e2["scale"] == [96, 64]

    # CU incluye collision_override con dimensiones coherentes
    assert e2["collider_scope"] == "CU"
    co = e2.get("collision_override")
    assert co is not None
    assert co["width"] == 2  # columnas de collision_map
    assert co["height"] == 2 # filas de collision_map
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
