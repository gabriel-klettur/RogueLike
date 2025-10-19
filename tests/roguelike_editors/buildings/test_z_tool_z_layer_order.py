import types
import pytest

from roguelike_editors.buildings.tools.z_tool.z_tool import ZTool


class _ZStateSpy:
    def __init__(self):
        self.calls = []
        self.last_set = None

    def set(self, building, z_value):
        self.calls.append((building, z_value))
        self.last_set = (building, z_value)


def _make_tool(target: str = "bottom") -> tuple[ZTool, types.SimpleNamespace]:
    state = types.SimpleNamespace(z_state=_ZStateSpy())
    editor_state = types.SimpleNamespace()
    tool = ZTool(state, editor_state, target=target)
    return tool, state


def test_bottom_update_does_not_force_top_up():
    tool, state = _make_tool(target="bottom")
    building = types.SimpleNamespace(z_bottom=5, z_top=2)

    tool._update_z(building, +1)

    assert building.z_bottom == 6, "Bottom should increment as requested"
    assert building.z_top == 2, "Top must remain unchanged (can be below bottom)"
    assert state.z_state.last_set == (building, 6), "z_state must sync with bottom layer"


@pytest.mark.parametrize(
    "start_top, delta, expected_top",
    [
        (2, +1, 3),   # normal increase, still below bottom
        (2, -5, 0),   # clamp to non-negative
    ],
)
def test_top_update_allows_below_bottom_and_clamps_non_negative(start_top, delta, expected_top):
    tool, _ = _make_tool(target="top")
    # Bottom greater than start_top to validate that top can stay below bottom
    building = types.SimpleNamespace(z_bottom=5, z_top=start_top)

    tool._update_z(building, delta)

    assert building.z_bottom == 5, "Bottom must remain unchanged when editing top"
    assert building.z_top == expected_top, "Top should update freely and clamp at 0"


def test_bottom_clamps_non_negative_and_keeps_top_unchanged():
    tool, state = _make_tool(target="bottom")
    building = types.SimpleNamespace(z_bottom=1, z_top=7)

    tool._update_z(building, -5)

    assert building.z_bottom == 0, "Bottom must clamp to non-negative"
    assert building.z_top == 7, "Top must remain unchanged when editing bottom"
    assert state.z_state.last_set == (building, 0), "z_state must reflect clamped bottom value"
