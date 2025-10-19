import types
import roguelike_engine.map.model.map_model as mm
from roguelike_engine.map.model.layer import Layer


def _fake_loader(matrix, codes=None):
    h = len(matrix)
    w = len(matrix[0]) if h else 0
    return [[types.SimpleNamespace(x=x, y=y, code=(codes[y][x] if codes else '')) for x in range(w)] for y in range(h)]


def test_model_small_integration_two_layers(monkeypatch):
    monkeypatch.setattr(mm, 'load_tiles_from_text', _fake_loader, raising=True)

    matrix = ["....", "...."]
    ground = [["g" for _ in range(4)] for _ in range(2)]
    deco = [["d" for _ in range(4)] for _ in range(2)]

    tiles_by_layer = {
        Layer.Ground: _fake_loader(matrix, ground),
        Layer.Decorations: _fake_loader(matrix, deco),
    }
    m = mm.Map(matrix=matrix, layers={Layer.Ground: [row[:] for row in ground], Layer.Decorations: [row[:] for row in deco]}, tiles_by_layer=tiles_by_layer, metadata={}, name="m")

    # Legacy overlay equals Ground, and we can access another layer
    assert m.overlay == m.layers[Layer.Ground]
    assert m.get_tiles_for_layer(Layer.Decorations)
    # Mutate Decorations via set_tile and see tiles_by_layer updated for that layer only
    m.set_tile(Layer.Decorations, 1, 0, "X")
    assert m.tiles_by_layer[Layer.Decorations][0][1].code == "X"
    assert m.tiles_by_layer[Layer.Ground][0][1].code == "g"
