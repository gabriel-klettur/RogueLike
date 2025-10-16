import types
import copy
import roguelike_engine.map.model.map_model as mm
from roguelike_engine.map.model.layer import Layer


def _fake_loader(matrix, codes=None):
    h = len(matrix)
    w = len(matrix[0]) if h else 0
    # Create distinct objects so we can detect unintended aliasing
    return [[types.SimpleNamespace(x=x, y=y, code=(codes[y][x] if codes else '')) for x in range(w)] for y in range(h)]


def test_set_tile_does_not_mutate_other_layers_or_external_inputs(monkeypatch):
    monkeypatch.setattr(mm, 'load_tiles_from_text', _fake_loader, raising=True)

    matrix = ["....", "...."]
    ground = [["g" for _ in range(4)] for _ in range(2)]
    deco = [["d" for _ in range(4)] for _ in range(2)]
    # Keep an external copy to ensure Map does not mutate our source structure by accident
    ground_external = copy.deepcopy(ground)

    m = mm.Map(
        matrix=matrix,
        layers={Layer.Ground: copy.deepcopy(ground), Layer.Decorations: copy.deepcopy(deco)},
        tiles_by_layer={Layer.Ground: _fake_loader(matrix, ground), Layer.Decorations: _fake_loader(matrix, deco)},
        metadata={},
        name="m",
    )

    before_deco_grid_id = id(m.tiles_by_layer[Layer.Decorations])

    # Mutate Ground
    m.set_tile(Layer.Ground, 1, 0, "X")

    # Other layer tiles grid remains the same object (no unintended rebuild)
    assert id(m.tiles_by_layer[Layer.Decorations]) == before_deco_grid_id

    # External ground source untouched
    assert ground_external[0][1] == "g"

    # Overlay reference remains the same object as layers[Ground]
    assert m.overlay is m.layers[Layer.Ground]
