import pytest
from roguelike_editors.tiles.common.state import deep_copy_state


def test_deep_copy_nested_structures():
    original = {"a": [1, 2, {"b": 3}], "c": (4, 5)}
    copied = deep_copy_state(original)
    assert copied == original
    assert copied is not original
    # Modify copied nested dict/list
    copied["a"][2]["b"] = 99
    assert original["a"][2]["b"] == 3


def test_deep_copy_custom_object():
    class Dummy:
        def __init__(self, value):
            self.value = value
    original = Dummy([1, 2, 3])
    copied = deep_copy_state(original)
    assert isinstance(copied, Dummy)
    assert copied is not original
    assert copied.value == [1, 2, 3]
    # mutate copy
    copied.value.append(4)
    assert original.value == [1, 2, 3]


def test_deep_copy_simple_types():
    for obj in [42, "string", 3.14, None]:
        copied = deep_copy_state(obj)
        assert copied == obj
