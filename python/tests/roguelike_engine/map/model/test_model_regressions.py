import random
import types
import roguelike_engine.map.model.map_model as mm
from roguelike_engine.map.model.layer import Layer


def _fake_loader(matrix, codes=None):
    h = len(matrix)
    w = len(matrix[0]) if h else 0
    return [[types.SimpleNamespace(x=x, y=y, code=(codes[y][x] if codes else '')) for x in range(w)] for y in range(h)]


def test_overlay_reference_stable_after_multiple_ground_updates(monkeypatch):
    monkeypatch.setattr(mm, 'load_tiles_from_text', _fake_loader, raising=True)

    matrix = ["....", "...."]
    ground = [["a", "b", "c", "d"], ["e", "f", "g", "h"]]
    tiles_by_layer = {Layer.Ground: _fake_loader(matrix, ground)}
    m = mm.Map(matrix=matrix, layers={Layer.Ground: [row[:] for row in ground]}, tiles_by_layer=tiles_by_layer, metadata={}, name="m")

    overlay_id_before = id(m.overlay)
    m.set_tile(Layer.Ground, 0, 0, "X")
    m.set_tile(Layer.Ground, 3, 1, "Y")
    # Overlay object remains the same reference and values changed accordingly
    assert id(m.overlay) == overlay_id_before
    assert m.overlay[0][0] == "X" and m.overlay[1][3] == "Y"


def test_model_fuzz_inputs_no_crash(monkeypatch):
    rng = random.Random(1337)
    monkeypatch.setattr(mm, 'load_tiles_from_text', _fake_loader, raising=True)

    for _ in range(10):
        w = rng.randint(1, 16)
        h = rng.randint(1, 12)
        matrix = ["." * w for _ in range(h)]
        ground = [["g" for _ in range(w)] for _ in range(h)]
        m = mm.Map(matrix=matrix, layers={Layer.Ground: [row[:] for row in ground]}, tiles_by_layer={Layer.Ground: _fake_loader(matrix, ground)}, metadata={}, name="m")
        # exercise accessors
        _ = m.get_tile(Layer.Ground, 0, 0)
        _ = m.get_tiles_for_layer(Layer.Ground)
        # out of bounds must be None
        assert m.get_tile(Layer.Ground, w, h) is None
