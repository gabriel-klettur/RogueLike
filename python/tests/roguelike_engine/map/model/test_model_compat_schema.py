import types
import roguelike_engine.map.model.map_model as mm
from roguelike_engine.map.model.layer import Layer


def _fake_loader(matrix, codes=None):
    h = len(matrix)
    w = len(matrix[0]) if h else 0
    return [[types.SimpleNamespace(x=x, y=y, code=(codes[y][x] if codes else '')) for x in range(w)] for y in range(h)]


def test_model_getstate_schema_and_setstate_compat(monkeypatch):
    monkeypatch.setattr(mm, 'load_tiles_from_text', _fake_loader, raising=True)

    matrix = ["..", ".."]
    ground = [["g", "g"], ["g", "g"]]
    tiles_by_layer = {Layer.Ground: _fake_loader(matrix, ground)}
    m = mm.Map(matrix=matrix, layers={Layer.Ground: ground}, tiles_by_layer=tiles_by_layer, metadata={"v": 1}, name="n")

    state = m.__getstate__()
    assert set(state.keys()) == {"matrix", "layers", "metadata", "name"}

    # Remove an optional key from layers to simulate older schema (still dict)
    restored = mm.Map.__new__(mm.Map)
    restored.__setstate__(state)

    assert restored.name == "n"
    assert restored.layers[Layer.Ground]
    # tiles_by_layer reconstructed and overlay set from Ground
    assert Layer.Ground in restored.tiles_by_layer
    assert restored.overlay is restored.layers[Layer.Ground]
