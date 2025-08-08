import pytest
import pygame
from types import SimpleNamespace
import json
from pathlib import Path

from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_controller import EntityPropertiesPanelController
from roguelike_editors.entities.entities_properties_panel.services.assets_constants import (
    SUBTAB_SET,
    SUBTAB_NO_SET,
)

# Initialize pygame font module for tests
pygame.font.init()

# Stub monster cache reload functions to prevent real cache loading
import roguelike_editors.entities.entities_properties_panel.entities_properties_panel_controller as epc_mod
epc_mod.reload_monster_defs = lambda: None
import roguelike_game.factories.monster.cache as mc_mod
mc_mod.load_caches_for = lambda variants: None

def _load_monsters_fixture() -> dict:
    tests_dir = Path(__file__).resolve().parents[3]
    with open(tests_dir / 'fixtures' / 'monsters' / 'mon1.json', 'r', encoding='utf-8') as f:
        return json.load(f)

@pytest.fixture
def controller():
     # Dummy editor controller with minimal render and game.screen
     editor_controller = SimpleNamespace(game=SimpleNamespace(screen=None), render=lambda screen: None)
     # Initialize controller with no player_stats and one monster entry
     ctrl = EntityPropertiesPanelController(
         editor_controller,
         player_stats={},
         monsters=_load_monsters_fixture(),
         player_assets={},
         font=None
     )
     ctrl.model.selected_id = 'mon1'
     return ctrl


def test_monster_asset_set_updates_assets_structure(controller):
    # Simulate 'asset set' sub-tab
    controller.assets_subtabs_controller.model.active_sub_tab = SUBTAB_SET
    captured = {}

    # Stub save to capture entry
    def fake_save(*args, **kwargs):
        # Support both module save (5 args) and controller save (4 args)
        entry = kwargs.get('entry')
        if entry is None and len(args) >= 2:
            entry = args[1]
        ent_id = kwargs.get('ent_id') if 'ent_id' in kwargs else (args[0] if args else None)
        captured['ent_id'] = ent_id
        captured['entry'] = (entry or {}).copy()

    # Capture via controller module-level save function
    epc_mod.save_entity_data = fake_save
    controller._save_entity_data = fake_save
    # Choose an asset for state 'idle' (direction ignored for asset set)
    controller._on_asset_chosen('asset_idle_north', 'dummy.png')

    entry = captured.get('entry', {})
    # Verify assets structure updated correctly
    assert 'assets' in entry, "Entry should contain 'assets' key"
    sets = entry['assets'].get('sets', {})
    sprites_set = sets.get('sprites_set', {})
    assert sprites_set.get('idle') == ['dummy.png'], "Sprites_set idle should be list with dummy.png"
    # Old 'sprites' key should be removed
    assert 'sprites' not in entry, "Sprites key should be removed after update"


def test_monster_asset_no_set_updates_assets_structure(controller):
    # Simulate 'no-set' sub-tab
    controller.assets_subtabs_controller.model.active_sub_tab = SUBTAB_NO_SET
    captured = {}

    # Stub save to capture entry
    def fake_save(*args, **kwargs):
        entry = kwargs.get('entry')
        if entry is None and len(args) >= 2:
            entry = args[1]
        ent_id = kwargs.get('ent_id') if 'ent_id' in kwargs else (args[0] if args else None)
        captured['ent_id'] = ent_id
        captured['entry'] = (entry or {}).copy()

    epc_mod.save_entity_data = fake_save
    controller._save_entity_data = fake_save
    # Choose an asset for state 'idle' and direction 'south'
    controller._on_asset_chosen('asset_idle_south', 'dummy.png')

    entry = captured.get('entry', {})
    # Verify 'no-sets' assets updated correctly
    assert 'assets' in entry, "Entry should contain 'assets' key"
    no_sets = entry['assets'].get('no-sets', {})
    state_no_set = no_sets.get('idle', {})
    assert state_no_set.get('south') == 'dummy.png', "No-sets idle south should be dummy.png"
    # Old 'sprites' key should be removed
    assert 'sprites' not in entry, "Sprites key should be removed after update"
