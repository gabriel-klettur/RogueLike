import pytest
import pygame
from types import SimpleNamespace

from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_controller import EntityPropertiesPanelController

# Initialize pygame font module for tests
pygame.font.init()

# Stub monster cache reload functions to prevent real cache loading
import roguelike_editors.entities.entities_properties_panel.entities_properties_panel_controller as epc_mod
epc_mod.reload_monster_defs = lambda: None
import roguelike_game.factories.monster.cache as mc_mod
mc_mod.load_caches_for = lambda variants: None

@pytest.fixture
def controller():
    # Dummy editor controller with minimal render and game.screen
    editor_controller = SimpleNamespace(game=SimpleNamespace(screen=None), render=lambda screen: None)
    # Initialize controller with no player_stats and one monster entry
    ctrl = EntityPropertiesPanelController(
        editor_controller,
        player_stats={},
        monsters={'mon1': {}},
        player_assets={},
        font=None
    )
    ctrl.model.selected_id = 'mon1'
    return ctrl


def test_monster_stat_edit_updates_stats_nested(controller):
    # Simulate editing stat 'hp'
    controller.model.editing_property = 'hp'
    controller.model.editing_text = '250'
    captured = {}

    # Stub save to capture entry
    def fake_save(ent_id, entry, path, data):
        captured['entry'] = entry.copy()

    controller._save_entity_data = fake_save
    # Execute commit_edit
    controller._commit_edit()

    entry = captured.get('entry', {})
    # Verify nested stats update
    assert 'stats' in entry, "Entry should contain 'stats' key"
    assert entry['stats']['hp'] == 250, "hp should be updated to 250 in nested stats"
    assert 'hp' not in entry, "Top-level hp key should not be set"
