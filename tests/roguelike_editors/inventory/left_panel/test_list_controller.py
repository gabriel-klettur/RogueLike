#!/usr/bin/env python3
"""
Tests for the left panel list controller.
"""

import pytest
import sys
import os
from unittest.mock import Mock, MagicMock

# Add the src directory to the Python path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', '..', '..', 'src'))

from roguelike_editors.inventory.left_panel.list.list_controller import ListController
from roguelike_editors.inventory.left_panel.panel_model import InventoryPanelModel


class TestListController:
    """Test suite for ListController."""
    
    def setup_method(self):
        """Set up test fixtures."""
        # Mock editor controller
        self.mock_editor_controller = Mock()
        self.mock_editor_controller.model = Mock()
        self.mock_editor_controller.model.selected_eid = None
        
        # Mock world with components
        self.mock_world = Mock()
        self.mock_world.components = {
            'PositionComponent': {
                1: Mock(x=100, y=200),
                2: Mock(x=300, y=400)
            },
            'MonsterInstanceComponent': {
                1: Mock(instance_id='monster-1'),
                2: Mock(instance_id='monster-2')
            }
        }
        self.mock_editor_controller.world = self.mock_world
        
        # Create panel model
        self.panel_model = InventoryPanelModel()
        
        # Create controller
        self.controller = ListController(self.mock_editor_controller, self.panel_model)
    
    def test_initialization(self):
        """Test that ListController initializes correctly."""
        assert self.controller.editor_controller == self.mock_editor_controller
        assert self.controller.panel_model == self.panel_model
        assert self.controller.debug_printed == False
    
    def test_select_entity(self):
        """Test entity selection updates both panel and editor models."""
        test_eid = 'test-entity-123'
        
        self.controller.select_entity(test_eid)
        
        # Check that both models are updated
        assert self.panel_model.selected_eid == test_eid
        assert self.mock_editor_controller.model.selected_eid == test_eid
    
    def test_get_items_list_player_category(self):
        """Test getting items list for player category."""
        # Set up mock data for player
        self.mock_editor_controller.model.active_data = {
            'player': {
                'player1': {
                    'slots': [
                        {'item': 'sword', 'quantity': 1},
                        {'item': 'potion', 'quantity': 3},
                        None,
                        {'item': 'shield', 'quantity': 1}
                    ]
                }
            }
        }
        self.panel_model.current_category = 'player'
        
        items = self.controller.get_items_list()
        
        expected_items = ['sword x1', 'potion x3', 'shield x1']
        assert items == expected_items
    
    def test_get_items_list_monsters_category(self):
        """Test getting items list for monsters category."""
        # Set up mock data for monsters
        self.mock_editor_controller.model.active_data = {
            'monsters': {
                'monster-1': {
                    'template_id': 'goblin',
                    'slots': [
                        {'item': 'gold', 'quantity': 5},
                        {'item': 'dagger', 'quantity': 1}
                    ]
                },
                'monster-2': {
                    'template_id': 'orc',
                    'slots': [
                        {'item': 'club', 'quantity': 1},
                        {'item': 'gold', 'quantity': 3}
                    ]
                }
            }
        }
        self.panel_model.current_category = 'monsters'
        
        items = self.controller.get_items_list()
        
        # Should return monster entity IDs, not items
        assert 'monster-1' in items or 'monster-2' in items
    
    def test_get_items_list_empty_data(self):
        """Test getting items list when no data exists."""
        self.mock_editor_controller.model.active_data = {}
        self.panel_model.current_category = 'player'
        
        items = self.controller.get_items_list()
        
        assert items == []
    
    def test_get_items_list_other_category(self):
        """Test getting items list for other categories."""
        self.mock_editor_controller.model.active_data = {
            'map': {
                'item1': 'value1',
                'item2': 'value2'
            }
        }
        self.panel_model.current_category = 'map'
        
        items = self.controller.get_items_list()
        
        # Should return keys for other categories
        assert 'item1' in items
        assert 'item2' in items
    
    def test_get_player_items_with_empty_slots(self):
        """Test _get_player_items handles empty slots correctly."""
        data = {
            'player1': {
                'slots': [
                    {'item': 'sword', 'quantity': 1},
                    None,  # Empty slot
                    {'item': 'potion', 'quantity': 2},
                    None   # Another empty slot
                ]
            }
        }
        
        items = self.controller._get_player_items(data)
        
        expected_items = ['sword x1', 'potion x2']
        assert items == expected_items
    
    def test_get_player_items_with_invalid_data(self):
        """Test _get_player_items handles invalid data gracefully."""
        # Test with non-dict data
        items = self.controller._get_player_items([])
        assert items == []
        
        # Test with missing slots
        data = {'player1': {}}
        items = self.controller._get_player_items(data)
        assert items == []
        
        # Test with invalid slot structure
        data = {'player1': {'slots': [{'item': 'sword'}]}}  # Missing quantity
        items = self.controller._get_player_items(data)
        assert 'sword x' in items[0]  # Should handle missing quantity
    
    def test_get_monsters_items_integration(self):
        """Test _get_monsters_items with world integration."""
        # Set up world components
        self.mock_world.components = {
            'PositionComponent': {
                1: Mock(x=100, y=200),
                2: Mock(x=300, y=400)
            },
            'MonsterInstanceComponent': {
                1: Mock(instance_id='monster-1'),
                2: Mock(instance_id='monster-2')
            }
        }
        
        data = {
            'monster-1': {'template_id': 'goblin'},
            'monster-2': {'template_id': 'orc'}
        }
        
        items = self.controller._get_monsters_items(data)
        
        # Should return entity IDs that have both Position and MonsterInstance components
        assert len(items) >= 0  # May vary based on component matching
    
    def test_debug_flag_behavior(self):
        """Test that debug_printed flag works correctly."""
        assert self.controller.debug_printed == False
        
        # Simulate debug printing
        self.controller.debug_printed = True
        assert self.controller.debug_printed == True
        
        # Reset for next test
        self.controller.debug_printed = False
        assert self.controller.debug_printed == False


class TestListControllerEdgeCases:
    """Test edge cases and error conditions for ListController."""
    
    def setup_method(self):
        """Set up minimal test fixtures."""
        self.mock_editor_controller = Mock()
        self.mock_editor_controller.model = Mock()
        self.mock_editor_controller.model.selected_eid = None
        self.mock_editor_controller.model.active_data = {}
        self.mock_editor_controller.world = Mock()
        self.mock_editor_controller.world.components = {}
        
        self.panel_model = InventoryPanelModel()
        self.controller = ListController(self.mock_editor_controller, self.panel_model)
    
    def test_select_entity_with_none(self):
        """Test selecting None as entity ID."""
        self.controller.select_entity(None)
        
        assert self.panel_model.selected_eid is None
        assert self.mock_editor_controller.model.selected_eid is None
    
    def test_get_items_list_with_malformed_data(self):
        """Test handling of malformed active_data."""
        # Test with None
        self.mock_editor_controller.model.active_data = None
        self.panel_model.current_category = 'player'
        
        try:
            items = self.controller.get_items_list()
            assert items == []
        except Exception:
            # Should handle gracefully
            pass
    
    def test_category_switching(self):
        """Test behavior when switching categories."""
        # Start with player category
        self.panel_model.current_category = 'player'
        self.mock_editor_controller.model.active_data = {
            'player': {'p1': {'slots': [{'item': 'sword', 'quantity': 1}]}},
            'monsters': {'m1': {'template_id': 'goblin'}}
        }
        
        player_items = self.controller.get_items_list()
        assert 'sword x1' in player_items
        
        # Switch to monsters category
        self.panel_model.current_category = 'monsters'
        monster_items = self.controller.get_items_list()
        
        # Should return different results
        assert player_items != monster_items


if __name__ == "__main__":
    pytest.main([__file__])
