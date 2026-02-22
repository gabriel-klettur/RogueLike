import os
import json
import pygame
import types
import pytest

from roguelike_editors.buildings.utils.collisions_apply import apply_collisions_to_loaded_buildings
from roguelike_game.ecs.core.spatial_index import SpatialIndex
from roguelike_engine.config.config_tiles import TILE_SIZE


class StubBuilding:
    """Minimal Building-like object for tests.
    Provides: image, x, y, image_path, collider_scope, id, collision_map, collision_tiles.
    """
    def __init__(self, *, x=0, y=0, w=64, h=64, image_path="assets/buildings/mine.png", scope="CG", bid=None):
        self.x = x
        self.y = y
        self.image = pygame.Surface((w, h))
        self.image_path = image_path
        self.collider_scope = scope
        self.id = bid
        cols = max(1, (w + TILE_SIZE - 1) // TILE_SIZE)
        rows = max(1, (h + TILE_SIZE - 1) // TILE_SIZE)
        self._collision_map = [["." for _ in range(cols)] for _ in range(rows)]

    @property
    def collision_map(self):
        return self._collision_map

    @collision_map.setter
    def collision_map(self, data):
        # setter should invalidate caches; we compute tiles on demand so nothing to do
        self._collision_map = data

    @property
    def collision_tiles(self):
        rects = []
        for r, row in enumerate(self._collision_map):
            for c, ch in enumerate(row):
                if ch == "#":
                    rects.append(pygame.Rect(self.x + c * TILE_SIZE, self.y + r * TILE_SIZE, TILE_SIZE, TILE_SIZE))
        return rects


class EmptyMapManager:
    def __init__(self):
        self.solid_tiles = []


@pytest.mark.parametrize("scope,expected_changes", [
    ("CG", 2),
])
def test_apply_collisions_to_loaded_buildings_cg_updates(scope, expected_changes):
    b1 = StubBuilding(image_path="assets/buildings/house.png", scope=scope)
    b2 = StubBuilding(image_path="assets/buildings/house.png", scope=scope)
    by_image = {
        "assets/buildings/house.png": {
            "width": 2,
            "height": 2,
            "collision": [["#", "."], [".", "."]],
        }
    }
    changed = apply_collisions_to_loaded_buildings(
        [b1, b2], by_image=by_image, by_binst={}, updated_by_img=["assets/buildings/house.png"], updated_by_inst=None
    )
    assert changed == expected_changes
    assert b1.collision_map[0][0] == "#"
    assert b2.collision_map[0][0] == "#"


def test_apply_collisions_to_loaded_buildings_cu_only_by_id():
    b1 = StubBuilding(image_path="assets/buildings/house.png", scope="CU", bid=123)
    b2 = StubBuilding(image_path="assets/buildings/house.png", scope="CG")
    by_binst = {
        "123": {
            "width": 2,
            "height": 2,
            "collision": [["#", "."], [".", "."]],
        }
    }
    changed = apply_collisions_to_loaded_buildings(
        [b1, b2], by_image={}, by_binst=by_binst, updated_by_img=None, updated_by_inst=["123"]
    )
    assert changed == 1
    assert b1.collision_map[0][0] == "#"
    assert b2.collision_map[0][0] == "."


def test_panel_save_writes_json_and_applies_in_memory(tmp_path, monkeypatch):
    # Prepare two buildings sharing image_path; we will change b1 and expect b2 to update via in-memory apply
    b1 = StubBuilding(image_path="assets/buildings/mine.png", scope="CG")
    b2 = StubBuilding(image_path="assets/buildings/mine.png", scope="CG")
    # Paint one cell in b1
    b1._collision_map[0][0] = "#"

    # Import the module under test and patch its file paths to temp files
    import importlib
    mod = importlib.import_module(
        "roguelike_editors.buildings.buildings_colliders_panel.building_colliders_panel_events"
    )
    img_path = tmp_path / "by_image.json"
    spawn_path = tmp_path / "by_spawn.json"
    inst_path = tmp_path / "by_inst.json"
    monkeypatch.setattr(mod, "BUILDINGS_COLLISIONS_BY_IMAGE_PATH", str(img_path))
    monkeypatch.setattr(mod, "BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH", str(spawn_path))
    monkeypatch.setattr(mod, "BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH", str(inst_path))

    # Minimal editor_state and model stubs
    editor_state = types.SimpleNamespace(collider_scope="CG", colliders_dirty=False)
    model = types.SimpleNamespace(active_building=b1)

    handler = mod.BuildingCollidersPanelEventHandler(state=None, editor_state=editor_state, model=model)
    # Do not provide ecs_world to avoid side effects; test focuses on file + in-memory apply
    handler._save_collisions([b1, b2], force=False)

    # JSON should exist and include our image path
    assert img_path.exists()
    data = json.loads(img_path.read_text("utf-8"))
    assert "assets/buildings/mine.png" in data
    # JSON should contain the painted '#'
    assert data["assets/buildings/mine.png"]["collision"][0][0] == "#"
    # In-memory apply should have propagated to b2 (same image, non-CU)
    assert b2.collision_map[0][0] == "#"


def test_panel_save_erase_updates_json_and_in_memory(tmp_path, monkeypatch):
    # Start with painted '#', then erase to '.' and verify both JSON and in-memory are updated
    b1 = StubBuilding(image_path="assets/buildings/mine.png", scope="CG")
    b2 = StubBuilding(image_path="assets/buildings/mine.png", scope="CG")
    b1._collision_map[0][0] = "#"

    import importlib
    mod = importlib.import_module(
        "roguelike_editors.buildings.buildings_colliders_panel.building_colliders_panel_events"
    )
    img_path = tmp_path / "by_image.json"
    spawn_path = tmp_path / "by_spawn.json"
    inst_path = tmp_path / "by_inst.json"
    monkeypatch.setattr(mod, "BUILDINGS_COLLISIONS_BY_IMAGE_PATH", str(img_path))
    monkeypatch.setattr(mod, "BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH", str(spawn_path))
    monkeypatch.setattr(mod, "BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH", str(inst_path))
    editor_state = types.SimpleNamespace(collider_scope="CG", colliders_dirty=False)
    model = types.SimpleNamespace(active_building=b1)
    handler = mod.BuildingCollidersPanelEventHandler(state=None, editor_state=editor_state, model=model)

    # First save with '#'
    handler._save_collisions([b1, b2], force=False)
    data = json.loads(img_path.read_text("utf-8"))
    assert data["assets/buildings/mine.png"]["collision"][0][0] == "#"
    assert b2.collision_map[0][0] == "#"

    # Now erase and save again
    b1._collision_map[0][0] = "."
    handler._save_collisions([b1, b2], force=False)
    data2 = json.loads(img_path.read_text("utf-8"))
    assert data2["assets/buildings/mine.png"]["collision"][0][0] == "."
    # In-memory apply should clear b2 as well
    assert b2.collision_map[0][0] == "."


def test_spatial_index_reflects_changes():
    b = StubBuilding(image_path="assets/buildings/hut.png", scope="CG", w=64, h=64)
    # Place one solid at (0,0)
    b._collision_map[0][0] = "#"
    si = SpatialIndex(EmptyMapManager(), [b])

    # Query a rect overlapping tile (0,0)
    got = si.get_solid_tiles_for_rect(pygame.Rect(0, 0, TILE_SIZE, TILE_SIZE))
    assert len(got) >= 1

    # Erase and rebuild index
    b._collision_map[0][0] = "."
    si2 = SpatialIndex(EmptyMapManager(), [b])
    got2 = si2.get_solid_tiles_for_rect(pygame.Rect(0, 0, TILE_SIZE, TILE_SIZE))
    assert len(got2) == 0
