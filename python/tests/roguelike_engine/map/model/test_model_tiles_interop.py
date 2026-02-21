import types
import roguelike_engine.map.model.map_model as mm
from roguelike_engine.map.model.layer import Layer


def _fake_loader(matrix, codes=None):
    h = len(matrix)
    w = len(matrix[0]) if h else 0
    return [[types.SimpleNamespace(x=x, y=y, code=(codes[y][x] if codes else '')) for x in range(w)] for y in range(h)]


def test_tiles_interop_multiple_layers(monkeypatch):
    monkeypatch.setattr(mm, 'load_tiles_from_text', _fake_loader, raising=True)

    matrix = ["....", "....", "...."]
    ground = [["g" for _ in range(4)] for _ in range(3)]
    walls = [["w" for _ in range(4)] for _ in range(3)]

    tiles_by_layer = {
        Layer.Ground: _fake_loader(matrix, ground),
        Layer.WallsTop: _fake_loader(matrix, walls),
    }
    m = mm.Map(
        matrix=matrix,
        layers={Layer.Ground: [row[:] for row in ground], Layer.WallsTop: [row[:] for row in walls]},
        tiles_by_layer=tiles_by_layer,
        metadata={},
        name="m",
    )

    # Overlay equals ground
    assert m.overlay == m.layers[Layer.Ground]
    # Both layers have tiles
    assert m.get_tiles_for_layer(Layer.Ground)
    assert m.get_tiles_for_layer(Layer.WallsTop)
