from types import SimpleNamespace

from roguelike_editors.entities.services.ui_helpers import hide_assets_picker_and_clear_properties


def test_hide_assets_picker_and_clear_properties_invokes_hide_and_clears_fields():
    calls = {"hide": 0}

    def _hide():
        calls["hide"] += 1

    # properties controller stub
    model = SimpleNamespace(
        editing_property="name",
        focused_property="hp",
        hovered_property="speed",
        panel_rect=(0, 0, 10, 10),
        selected_id="player1",
        hovered_entity_id="npc1",
    )
    assets_picker_controller = SimpleNamespace(hide=_hide)
    properties_controller = SimpleNamespace(model=model, assets_picker_controller=assets_picker_controller)

    hide_assets_picker_and_clear_properties(properties_controller)

    assert calls["hide"] == 1
    assert model.editing_property is None
    assert model.focused_property is None
    assert model.hovered_property is None
    assert model.panel_rect is None
    assert model.selected_id is None
    assert model.hovered_entity_id is None
