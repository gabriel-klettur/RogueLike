import pickle
import types
import roguelike_engine.map.model.map_model as mm
from roguelike_engine.map.model.layer import Layer


def test_map_pickling_omits_tiles_and_rebuilds(monkeypatch):
    calls = {'count': 0}
    def fake_loader(matrix, codes=None):
        calls['count'] += 1
        h = len(matrix)
        w = len(matrix[0]) if h else 0
        return [[types.SimpleNamespace(x=x, y=y, code=(codes[y][x] if codes else '')) for x in range(w)] for y in range(h)]
    monkeypatch.setattr(mm, 'load_tiles_from_text', fake_loader, raising=True)

    matrix = ["....", "...."]
    ground = [["g" for _ in range(4)] for _ in range(2)]
    tiles_by_layer = {Layer.Ground: fake_loader(matrix, ground)}
    m = mm.Map(matrix=matrix, layers={Layer.Ground: ground}, tiles_by_layer=tiles_by_layer, metadata={}, name="m")

    blob = pickle.dumps(m)
    m2 = pickle.loads(blob)

    # After unpickle, tiles_by_layer must be reconstructed by __setstate__
    assert isinstance(m2.tiles_by_layer, dict)
    assert Layer.Ground in m2.tiles_by_layer
    # fake_loader called during __post_init__ and during __setstate__ reconstruction
    assert calls['count'] >= 2
