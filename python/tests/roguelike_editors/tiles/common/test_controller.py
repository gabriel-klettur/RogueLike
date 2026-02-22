import pytest
from roguelike_editors.tiles.common.controller import flood_fill


def test_fill_single_cell():
    matrix = [[1, 2], [3, 1]]
    flood_fill(matrix, 0, 0, 1, 9)
    assert matrix == [[9, 2], [3, 1]]


def test_fill_connected_region():
    matrix = [
        [1, 1, 2],
        [1, 2, 1],
        [2, 1, 1],
    ]
    flood_fill(matrix, 0, 0, 1, 3)
    assert matrix == [
        [3, 3, 2],
        [3, 2, 1],
        [2, 1, 1],
    ]


def test_no_fill_if_start_not_target():
    matrix = [[0, 0], [0, 0]]
    flood_fill(matrix, 0, 0, 1, 9)
    assert matrix == [[0, 0], [0, 0]]


def test_fill_with_same_replacement():
    matrix = [[1, 1], [1, 1]]
    flood_fill(matrix, 0, 0, 1, 1)
    assert matrix == [[1, 1], [1, 1]]


def test_fill_entire_matrix():
    matrix = [[1, 1, 1], [1, 1, 1], [1, 1, 1]]
    flood_fill(matrix, 1, 1, 1, 0)
    assert all(cell == 0 for row in matrix for cell in row)
