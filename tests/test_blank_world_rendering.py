import os
from pathlib import Path
import json
import shutil
import tempfile
import unittest
from types import SimpleNamespace
from contextlib import contextmanager

import pygame
import sys

# Ensure 'src' is on sys.path so imports work when running tests from project root
PROJECT_ROOT = Path(__file__).resolve().parents[1]
SRC_DIR = PROJECT_ROOT / 'src'
if str(SRC_DIR) not in sys.path:
    sys.path.insert(0, str(SRC_DIR))

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.map.view.chunked_map_view import ChunkedMapView
from roguelike_engine.map.model.layer import Layer
from roguelike_engine.tile.utils.assets import get_sprite_for_tile, clear_sprite_caches
from roguelike_engine.config.config_tiles import OVERLAY_CODE_MAP
from roguelike_game.managers.core.render.map_renderer import render_map as core_render_map


@contextmanager
def temp_world(*, user_zones: dict | None, overlays: dict[str, dict] | None):
    """Create a temporary world under a temporary worlds_dir.
    - user_zones: dict for zones.json (e.g., {"lobby": [0,0]}) or {} for blank.
    - overlays: mapping from overlay file stem to layers data (we only need existence).
      Example: {"no zone.overlay": {"Ground": [[""]]} }
    Yields (worlds_dir_path, world_id)
    """
    root = tempfile.mkdtemp(prefix="rl_worlds_")
    try:
        worlds_dir = os.path.join(root, "worlds")
        world_id = "test_world"
        zones_dir = os.path.join(worlds_dir, world_id, "zones")
        overlays_dir = os.path.join(zones_dir, "overlays")
        os.makedirs(overlays_dir, exist_ok=True)
        # zones.json
        zindex = os.path.join(zones_dir, "zones.json")
        with open(zindex, "w", encoding="utf-8") as f:
            json.dump(user_zones or {}, f, indent=2)
        # overlays
        if overlays:
            for stem, layers in overlays.items():
                path = os.path.join(overlays_dir, f"{stem}.json")
                with open(path, "w", encoding="utf-8") as f:
                    json.dump(layers or {}, f)
        # required dirs for service scaffolding compatibility
        os.makedirs(os.path.join(worlds_dir, world_id, "collisions"), exist_ok=True)
        os.makedirs(os.path.join(worlds_dir, world_id, "buildings"), exist_ok=True)
        os.makedirs(os.path.join(worlds_dir, world_id, "spawners"), exist_ok=True)
        yield worlds_dir, world_id
    finally:
        shutil.rmtree(root, ignore_errors=True)


@contextmanager
def patched_world(worlds_dir: str, world_id: str):
    """Patch global_map_settings to point to the temp world, restoring after use."""
    old_worlds_dir = global_map_settings.worlds_dir
    old_world = global_map_settings.current_world
    try:
        global_map_settings.worlds_dir = Path(os.path.abspath(worlds_dir))
        global_map_settings.set_world(world_id)
        yield
    finally:
        global_map_settings.worlds_dir = old_worlds_dir
        global_map_settings.set_world(old_world)


class DummyCamera:
    def __init__(self, zoom: float = 1.0, offset_x: float = 0.0, offset_y: float = 0.0):
        self.zoom = zoom
        self.offset_x = offset_x
        self.offset_y = offset_y

    def apply(self, pos):
        x, y = pos
        return int((x - self.offset_x) * self.zoom), int((y - self.offset_y) * self.zoom)


class BlankWorldRenderingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
        pygame.init()

    @classmethod
    def tearDownClass(cls):
        pygame.quit()

    def setUp(self):
        clear_sprite_caches()

    def test_is_blank_world_true_when_zones_empty_and_only_sentinel(self):
        with temp_world(user_zones={}, overlays={"no zone.overlay": {}}) as (wdir, wid):
            with patched_world(wdir, wid):
                self.assertTrue(global_map_settings.is_blank_world())

    def test_is_blank_world_false_with_user_zone(self):
        with temp_world(user_zones={"lobby": [0, 0]}, overlays={"no zone.overlay": {}}) as (wdir, wid):
            with patched_world(wdir, wid):
                self.assertFalse(global_map_settings.is_blank_world())

    def test_get_sprite_overlay_only_no_fallback_on_blank(self):
        # Patch load_image to return a colored surface
        def fake_load_image(_name, size):
            surf = pygame.Surface(size, pygame.SRCALPHA)
            surf.fill((255, 0, 0, 255))
            return surf
        # Set blank world + sentinel only
        with temp_world(user_zones={}, overlays={"no zone.overlay": {}}) as (wdir, wid):
            with patched_world(wdir, wid):
                clear_sprite_caches()
                from unittest.mock import patch
                with patch("roguelike_engine.tile.utils.assets.load_image", fake_load_image):
                    # Without overlay code -> None in overlay-only
                    self.assertIsNone(get_sprite_for_tile('.', None))
                    # Invalid overlay code -> None in overlay-only
                    self.assertIsNone(get_sprite_for_tile('.', 'invalid_code'))
                    # Valid overlay code (if available) -> returns surface
                    valid_code = next(iter(OVERLAY_CODE_MAP), None)
                    if valid_code is not None:
                        spr = get_sprite_for_tile('.', valid_code)
                        self.assertIsInstance(spr, pygame.Surface)

    def test_get_sprite_overlay_only_no_fallback_on_non_base_world(self):
        """En cualquier mundo distinto de 'base' no debe haber fallback a tiles base.

        Incluso si el mundo tiene zonas de usuario, mientras overlay_only esté activo
        (política actual para worlds != 'base'), las consultas sin overlay_code o con
        códigos inválidos deben devolver None, pero un overlay_code válido debe
        producir un sprite.
        """

        def fake_load_image(_name, size):
            surf = pygame.Surface(size, pygame.SRCALPHA)
            surf.fill((0, 255, 0, 255))
            return surf

        # Mundo de prueba con una zona de usuario y un directorio de overlays no vacío
        with temp_world(user_zones={"lobby": [0, 0]}, overlays={"lobby.overlay": {}}) as (wdir, wid):
            with patched_world(wdir, wid):
                clear_sprite_caches()
                from unittest.mock import patch
                with patch("roguelike_engine.tile.utils.assets.load_image", fake_load_image):
                    # Sin overlay_code -> None en overlay-only
                    self.assertIsNone(get_sprite_for_tile('.', None))
                    # overlay_code inválido -> None en overlay-only
                    self.assertIsNone(get_sprite_for_tile('.', 'invalid_code'))
                    # overlay_code válido -> debe devolver surface
                    valid_code = next(iter(OVERLAY_CODE_MAP), None)
                    if valid_code is not None:
                        spr = get_sprite_for_tile('.', valid_code)
                        self.assertIsInstance(spr, pygame.Surface)

    def test_chunked_render_guard_returns_fullscreen_rect_on_blank_sentinel_only(self):
        with temp_world(user_zones={}, overlays={"no zone.overlay": {}}) as (wdir, wid):
            with patched_world(wdir, wid):
                screen = pygame.Surface((64, 64))
                cam = DummyCamera()
                # Minimal fake map model
                matrix = ["....", "....", "....", "...."]
                h = len(matrix)
                w = len(matrix[0])
                empty = [["" for _ in range(w)] for _ in range(h)]
                map_model = SimpleNamespace(matrix=matrix, layers={Layer.Ground: empty})
                view = ChunkedMapView(chunk_size=2)
                dirty = view.render(screen, cam, map_model)
                self.assertEqual(dirty, [screen.get_rect()])

    def test_core_render_guard_returns_fullscreen_rect_on_blank_sentinel_only(self):
        with temp_world(user_zones={}, overlays={"no zone.overlay": {}}) as (wdir, wid):
            with patched_world(wdir, wid):
                screen = pygame.Surface((64, 64))
                cam = DummyCamera()
                manager = SimpleNamespace(map_editor=SimpleNamespace(editor_state=SimpleNamespace(active=False)),
                                          tiles_editor=SimpleNamespace(editor_state=SimpleNamespace(active=False, toolbar_state=SimpleNamespace(show_collisions=False, show_collisions_overlay=False, visible_layers={}))))
                # Minimal map object with required attributes
                map_obj = SimpleNamespace(view=ChunkedMapView(), tiles_by_layer={}, layers={})
                dirty = core_render_map(manager, cam, screen, map_obj)
                self.assertEqual(dirty, [screen.get_rect()])

    def test_overlay_only_forced_when_sentinel_only_even_if_not_blank(self):
        # World has user zone (not blank), but overlays_dir contains only sentinel -> overlay-only policy must be forced
        with temp_world(user_zones={"lobby": [0, 0]}, overlays={"no zone.overlay": {}}) as (wdir, wid):
            with patched_world(wdir, wid):
                # Patch load_image to colored so any erroneous draw is visible
                def fake_load_image(_name, size):
                    surf = pygame.Surface(size, pygame.SRCALPHA)
                    surf.fill((0, 255, 0, 255))
                    return surf
                from unittest.mock import patch
                with patch("roguelike_engine.tile.utils.assets.load_image", fake_load_image):
                    clear_sprite_caches()
                    # Prepare a tiny map with base chars and NO overlays
                    matrix = ["..", ".."]
                    empty = [["", ""], ["", ""]]
                    map_model = SimpleNamespace(matrix=matrix, layers={Layer.Ground: empty})
                    view = ChunkedMapView(chunk_size=2)
                    # Build chunks at zoom 1
                    view._build_chunk_surfaces(map_model, zoom=1.0)
                    # There should be one chunk (0,0) of size 2x2 tiles -> 64x64 pixels with black fill
                    chunk = view.chunks_by_zoom[1.0][(0, 0)]
                    # Sample pixel to ensure it remains black (no base draw happened)
                    px = chunk.get_at((0, 0))
                    self.assertEqual(px, pygame.Color(0, 0, 0, 255))

    def test_invalidate_cache_clears_zoom_cache(self):
        with temp_world(user_zones={"lobby": [0, 0]}, overlays={"no zone.overlay": {}}) as (wdir, wid):
            with patched_world(wdir, wid):
                # Build a simple map and cache at zoom 1
                matrix = ["..", ".."]
                empty = [["", ""], ["", ""]]
                map_model = SimpleNamespace(matrix=matrix, layers={Layer.Ground: empty})
                view = ChunkedMapView(chunk_size=2)
                view._build_chunk_surfaces(map_model, zoom=1.0)
                self.assertIn(1.0, view.chunks_by_zoom)
                view.invalidate_cache()
                self.assertEqual(view.chunks_by_zoom, {})

    def test_update_chunks_overlay_only_keeps_black(self):
        with temp_world(user_zones={"lobby": [0, 0]}, overlays={"no zone.overlay": {}}) as (wdir, wid):
            with patched_world(wdir, wid):
                matrix = ["..", ".."]
                empty = [["", ""], ["", ""]]
                map_model = SimpleNamespace(matrix=matrix, layers={Layer.Ground: empty})
                cam = DummyCamera()
                view = ChunkedMapView(chunk_size=2)
                # Build base cache
                view._build_chunk_surfaces(map_model, zoom=1.0)
                # Update a cell; should keep black due to overlay-only policy
                view.update_chunks(map_model, cam, [(0, 0)])
                chunk = view.chunks_by_zoom[1.0][(0, 0)]
                self.assertEqual(chunk.get_at((0, 0)), pygame.Color(0, 0, 0, 255))

    def test_update_cells_all_zooms_overlay_only_keeps_black(self):
        with temp_world(user_zones={"lobby": [0, 0]}, overlays={"no zone.overlay": {}}) as (wdir, wid):
            with patched_world(wdir, wid):
                matrix = ["..", ".."]
                empty = [["", ""], ["", ""]]
                map_model = SimpleNamespace(matrix=matrix, layers={Layer.Ground: empty})
                view = ChunkedMapView(chunk_size=2)
                # Prebuild two zoom levels
                view._build_chunk_surfaces(map_model, zoom=1.0)
                view._build_chunk_surfaces(map_model, zoom=2.0)
                view.update_cells_all_zooms(map_model, [(0, 0)])
                for z in (1.0, 2.0):
                    chunk = view.chunks_by_zoom[z][(0, 0)]
                    self.assertEqual(chunk.get_at((0, 0)), pygame.Color(0, 0, 0, 255))


if __name__ == "__main__":
    unittest.main()
