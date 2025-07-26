import enum
import pytest
from roguelike_editors.tiles.common.events import cycle_enum


class TestEnum(enum.Enum):
    RED = 1
    GREEN = 2
    BLUE = 3


def test_cycle_forward():
    assert cycle_enum(TestEnum.RED, 1, TestEnum) == TestEnum.GREEN
    assert cycle_enum(TestEnum.GREEN, 1, TestEnum) == TestEnum.BLUE


def test_cycle_wrap_forward():
    assert cycle_enum(TestEnum.BLUE, 1, TestEnum) == TestEnum.RED


def test_cycle_backward():
    assert cycle_enum(TestEnum.RED, -1, TestEnum) == TestEnum.BLUE
    assert cycle_enum(TestEnum.GREEN, -1, TestEnum) == TestEnum.RED


def test_cycle_with_large_delta():
    assert cycle_enum(TestEnum.RED, 4, TestEnum) == TestEnum.GREEN  # 4 % 3 == 1
    assert cycle_enum(TestEnum.BLUE, -4, TestEnum) == TestEnum.GREEN  # -4 % 3 == 2


def test_invalid_current():
    class AnotherEnum(enum.Enum):
        A = 1
    with pytest.raises(ValueError):
        cycle_enum(AnotherEnum.A, 1, TestEnum)
