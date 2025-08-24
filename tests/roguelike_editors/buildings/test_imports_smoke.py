"""
Smoke tests for module imports and callable presence.
Covers IDs: EVT-*, CTL-*, VIW-*, UTL-* (presence only).
"""

import importlib


def test_import_core_modules():
    m_model = importlib.import_module("roguelike_editors.buildings.building_editor_model")
    m_controller = importlib.import_module("roguelike_editors.buildings.building_editor_controller")
    m_events = importlib.import_module("roguelike_editors.buildings.building_editor_events")
    m_view = importlib.import_module("roguelike_editors.buildings.building_editor_view")

    assert m_model is not None
    assert m_controller is not None
    assert m_events is not None
    assert m_view is not None


def test_import_utils_and_functions():
    load_mod = importlib.import_module("roguelike_editors.buildings.utils.load_buildings_from_json")
    save_mod = importlib.import_module("roguelike_editors.buildings.utils.save_buildings_to_json")
    zones_mod = importlib.import_module("roguelike_editors.buildings.utils.zone_helpers")

    assert hasattr(load_mod, "load_buildings_from_json")
    assert callable(getattr(load_mod, "load_buildings_from_json"))

    assert hasattr(save_mod, "save_buildings_to_json")
    assert callable(getattr(save_mod, "save_buildings_to_json"))

    # Zone helpers presence
    assert zones_mod is not None
