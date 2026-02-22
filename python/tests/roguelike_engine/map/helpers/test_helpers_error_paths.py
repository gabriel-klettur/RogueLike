from roguelike_engine.map.helpers.geometry import intersect, center_of


def test_intersect_degenerate_and_negative_coords():
    # Degenerate rectangles (zero-area) should behave consistently
    a = (0, 0, 0, 0)
    b = (0, 0, 0, 0)
    assert intersect(a, b) is True  # touching at a point counts as intersect under <=/>= logic

    # Negative coordinates
    c = (-5, -5, -1, -1)
    d = (0, 0, 3, 3)
    assert intersect(c, d) is False


def test_center_of_integer_math_on_varied_sizes():
    assert center_of((0, 0, 1, 1)) == (0, 0)
    assert center_of((0, 0, 2, 2)) == (1, 1)
    assert center_of((-3, -1, 3, 1)) == (0, 0)
