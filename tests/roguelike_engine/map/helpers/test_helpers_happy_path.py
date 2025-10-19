from roguelike_engine.map.helpers.geometry import intersect, center_of


def test_intersect_overlapping_rects():
    a = (0, 0, 10, 10)
    b = (5, 5, 15, 15)
    assert intersect(a, b) is True
    assert intersect(b, a) is True


def test_intersect_non_overlapping_rects():
    a = (0, 0, 4, 4)
    b = (5, 5, 9, 9)
    assert intersect(a, b) is False
    assert intersect(b, a) is False


def test_center_of_integer_center():
    r = (2, 4, 6, 8)
    assert center_of(r) == ((2 + 6) // 2, (4 + 8) // 2)
