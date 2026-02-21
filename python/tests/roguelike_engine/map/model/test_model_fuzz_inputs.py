import random
import types
import roguelike_engine.map.model.map_model as mm
from roguelike_engine.map.model.layer import Layer


def _fake_loader(matrix, codes=None):
    h = len(matrix)
    w = len(matrix[0]) if h else 0
    return [[types.SimpleNamespace(x=x, y=y, code=(codes[y][x] if codes else '')) for x in range(w)] for y in range(h)]


def test_model_fuzz_inputs_random_sizes_and_updates(monkeypatch):
    rng = random.Random(2025)
    monkeypatch.setattr(mm, 'load_tiles_from_text', _fake_loader, raising=True)

    for _ in range(15):
        w = rng.randint(1, 24)
        h = rng.randint(1, 18)
        matrix = ['.' * w for _ in range(h)]
        ground = [["g" for _ in range(w)] for _ in range(h)]
        m = mm.Map(matrix=matrix, layers={Layer.Ground: [row[:] for row in ground]}, tiles_by_layer={Layer.Ground: _fake_loader(matrix, ground)}, metadata={}, name="m")

        # Perform random valid and invalid updates
        for _ in range(10):
            x = rng.randint(-2, w + 2)
            y = rng.randint(-2, h + 2)
            try:
                m.set_tile(Layer.Ground, x, y, 'Z')
            except Exception as e:
                raise AssertionError(f"set_tile raised unexpectedly: {e}")

        # Invariants: dimensions preserved; overlay and layers[Ground] identity
        assert len(m.matrix) == h and all(len(r) == w for r in m.matrix)
        assert m.overlay is m.layers[Layer.Ground]
