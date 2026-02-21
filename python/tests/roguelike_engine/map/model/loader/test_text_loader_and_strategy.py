import types

import roguelike_engine.map.model.loader.text_loader as tl
import roguelike_engine.map.model.loader.text_loader_strategy as tls
from roguelike_engine.map.model.layer import Layer


def test_parse_map_text_basic():
    data = [".#", "##"]
    grid = tl.parse_map_text(data)
    assert grid == [[".", "#"], ["#", "#"]]


def test_text_map_loader_happy_blank_layers_and_tiles(monkeypatch):
    # Count overlay generator calls to verify one-time generation
    calls = {"gen": 0}
    monkeypatch.setattr(tls, 'generate_overlay_map', lambda: calls.__setitem__("gen", calls["gen"] + 1), raising=True)

    # No layers returned -> Ground blank of correct size
    monkeypatch.setattr(tls, 'load_layers', lambda name: {}, raising=True)

    # Fake tiles generator returns simple tuples; we only assert dimensions
    def fake_tiles(map_data, overlay):
        h = len(map_data)
        w = len(map_data[0]) if h else 0
        return [[(x, y) for x in range(w)] for y in range(h)]
    monkeypatch.setattr(tls, 'load_tiles_from_text', fake_tiles, raising=True)

    loader = tls.TextMapLoader()
    matrix, tiles_by_layer, raw_layers = loader.load(["..", ".."], map_name="test")

    # Overlay generated exactly once on first call
    assert calls["gen"] == 1

    # Matrix shape 2x2, and at least Ground layer present with same shape
    assert matrix == [[".", "."], [".", "."]]
    assert Layer.Ground in raw_layers
    assert len(raw_layers[Layer.Ground]) == 2 and len(raw_layers[Layer.Ground][0]) == 2

    # Tiles generated for each layer key; dimensions match
    for layer, grid in tiles_by_layer.items():
        assert len(grid) == 2 and len(grid[0]) == 2

    # Second call should not trigger overlay generation again
    loader.load([".."], map_name="test2")
    assert calls["gen"] == 1


def test_text_map_loader_merges_zone_layers(monkeypatch):
    # Provide global zone offsets with one zone at (1,0)
    class Settings:
        zone_offsets = {'z1': (1, 0)}
    # Patch onto module where it's read
    monkeypatch.setattr(tls, 'global_map_settings', Settings, raising=True)

    # load_layers returns map's own layers (empty) and for 'z1' returns one code at (0,0)
    def fake_load_layers(name):
        if name == 'z1':
            return {Layer.Ground: [["A"]]}  # 1x1 overlay
        # for map_name: no layers
        return {}
    monkeypatch.setattr(tls, 'load_layers', fake_load_layers, raising=True)

    # Fake tiles to avoid assets
    monkeypatch.setattr(tls, 'load_tiles_from_text', lambda m, o: [[0]*len(m[0]) for _ in range(len(m))], raising=True)

    loader = tls.TextMapLoader()
    # 1x3 map; after merging zone at offset (1,0), Ground[0][1] should be 'A'
    matrix, tiles_by_layer, raw_layers = loader.load(["..."], map_name="any")

    assert raw_layers[Layer.Ground][0][1] == "A"


def test_text_map_loader_adapts_layer_sizes(monkeypatch):
    # No zones
    monkeypatch.setattr(tls, 'global_map_settings', types.SimpleNamespace(zone_offsets={}), raising=True)

    # Return a layer smaller than the map so it must be padded
    def fake_load_layers(name):
        if name == 'm':
            return {Layer.Decorations: [["d"]]}  # 1x1
        return {}
    monkeypatch.setattr(tls, 'load_layers', fake_load_layers, raising=True)

    # Fake tiles
    monkeypatch.setattr(tls, 'load_tiles_from_text', lambda m, o: [[0]*len(m[0]) for _ in range(len(m))], raising=True)

    loader = tls.TextMapLoader()
    matrix, tiles_by_layer, raw_layers = loader.load(["..", ".."], map_name='m')

    # Decorations should be adapted to 2x2 with padding strings
    deco = raw_layers[Layer.Decorations]
    assert len(deco) == 2 and len(deco[0]) == 2
    assert deco[0][0] == "d" and deco[0][1] == ""
    assert deco[1] == ["", ""]
