import pytest
import pygame
from types import SimpleNamespace
import json
from pathlib import Path

from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_controller import EntityPropertiesPanelController
from roguelike_editors.entities.services.history import HistoryManager
import roguelike_editors.entities.services.commands as cmd_mod

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
    # Dummy editor controller with minimal render, game.screen and ecs, plus history for commands
    ecs_world = SimpleNamespace()
    game = SimpleNamespace(screen=None, ecs=SimpleNamespace(ecs_world=ecs_world))
    editor_controller = SimpleNamespace(game=game, render=lambda screen: None, history=HistoryManager())
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


def test_monster_stat_edit_updates_stats_nested(controller):
    # Simulate editing stat 'hp'
    controller.model.editing_property = 'hp'
    controller.model.editing_text = '250'
    captured = {}

    # Stub save to capture entry (module-level save uses 5 args)
    def fake_save(*args, **kwargs):
        entry = kwargs.get('entry')
        if entry is None and len(args) >= 2:
            entry = args[1]
        captured['entry'] = (entry or {}).copy()

    # Capture via controller module-level save function and commands module
    epc_mod.save_entity_data = fake_save
    cmd_mod.save_entity_data = fake_save
    controller._save_entity_data = fake_save
    # Stub commands ECS/stat updates
    cmd_mod.update_player_stats = lambda *args, **kwargs: None
    cmd_mod.update_monster_stats = lambda *args, **kwargs: None
    # Execute commit_edit
    controller._commit_edit()

    entry = captured.get('entry', {})
    # Verify nested stats update
    assert 'stats' in entry, "Entry should contain 'stats' key"
    assert entry['stats']['hp'] == 250, "hp should be updated to 250 in nested stats"
    assert 'hp' not in entry, "Top-level hp key should not be set"

def test_commit_edit_triggers_reload_and_cache_clear(monkeypatch, controller):
    calls = []
    # Spy on reload_monster_defs
    monkeypatch.setattr(epc_mod, 'reload_monster_defs', lambda: calls.append(True))
    # Seed cache entries
    mc_mod._loaded_variants.add('mon1')
    mc_mod._SPRITE_SURFACES['mon1'] = object()
    mc_mod._DEATH_SURFACES['mon1'] = object()
    # Simulate editing hp
    controller.model.editing_property = 'hp'
    controller.model.editing_text = '777'
    # Stub save to no-op
    epc_mod.save_entity_data = lambda *args, **kwargs: None
    controller._save_entity_data = lambda *args, **kwargs: None
    # Execute commit
    controller._commit_edit()
    # Verify reload called once
    assert calls == [True], "reload_monster_defs should be called exactly once"
    # Verify cache entries are cleared
    assert 'mon1' not in mc_mod._loaded_variants
    assert 'mon1' not in mc_mod._SPRITE_SURFACES
    assert 'mon1' not in mc_mod._DEATH_SURFACES
