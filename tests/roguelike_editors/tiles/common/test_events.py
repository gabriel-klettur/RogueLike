import enum
import pytest
from roguelike_editors.tiles.common.events import cycle_enum


class ColorEnum(enum.Enum):
    RED = 1
    GREEN = 2
    BLUE = 3


def test_cycle_forward():
    assert cycle_enum(ColorEnum.RED, 1, ColorEnum) == ColorEnum.GREEN
    assert cycle_enum(ColorEnum.GREEN, 1, ColorEnum) == ColorEnum.BLUE


def test_cycle_wrap_forward():
    assert cycle_enum(ColorEnum.BLUE, 1, ColorEnum) == ColorEnum.RED


def test_cycle_backward():
    assert cycle_enum(ColorEnum.RED, -1, ColorEnum) == ColorEnum.BLUE
    assert cycle_enum(ColorEnum.GREEN, -1, ColorEnum) == ColorEnum.RED


def test_cycle_with_large_delta():
    assert cycle_enum(ColorEnum.RED, 4, ColorEnum) == ColorEnum.GREEN  # 4 % 3 == 1
    assert cycle_enum(ColorEnum.BLUE, -4, ColorEnum) == ColorEnum.GREEN  # -4 % 3 == 2


def test_invalid_current():
    class AnotherEnum(enum.Enum):
        A = 1
    with pytest.raises(ValueError):
        cycle_enum(AnotherEnum.A, 1, ColorEnum)
