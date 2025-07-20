#!/usr/bin/env python3
"""
Tests for the left panel controller components.
"""

import pytest
import sys
import os
from unittest.mock import Mock, MagicMock

# Add the src directory to the Python path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', '..', '..', 'src'))

from roguelike_editors.inventory.left_panel.panel_controller import PanelController
from roguelike_editors.inventory.left_panel.panel_model import InventoryPanelModel


class TestPanelController:
    """Test suite for PanelController."""
    
    def setup_method(self):
        """Set up test fixtures."""
        # Mock editor controller
        self.mock_editor_controller = Mock()
        self.mock_editor_controller.model = Mock()
        self.mock_editor_controller.model.active_data = {
            'player': {
                'player1': {
                    'slots': [
                        {'item': 'sword', 'quantity': 1},
                        {'item': 'potion', 'quantity': 3},
                        None
                    ]
                }
            },
            'monsters': {
                'monster1': {
                    'template_id': 'goblin',
                    'slots': [
                        {'item': 'gold', 'quantity': 5},
                        {'item': 'dagger', 'quantity': 1}
                    ]
                }
            }
        }
        
        # Create model and controller
        self.model = InventoryPanelModel()
        self.controller = PanelController(self.mock_editor_controller, self.model)
    
    def test_panel_controller_initialization(self):
        """Test that PanelController initializes correctly."""
        assert self.controller.editor_controller == self.mock_editor_controller
        assert isinstance(self.controller.model, InventoryPanelModel)
        assert hasattr(self.controller, 'tabs_controller')
        assert hasattr(self.controller, 'list_controller')
    
    def test_get_items_list_delegation(self):
        """Test that get_items_list delegates to list_controller."""
        # Mock the list_controller method
        expected_items = ['sword x1', 'potion x3']
        self.controller.list_controller.get_items_list = Mock(return_value=expected_items)
        
        result = self.controller.get_items_list()
        
        assert result == expected_items
        self.controller.list_controller.get_items_list.assert_called_once()
    
    def test_select_entity_delegation(self):
        """Test that select_entity delegates to list_controller."""
        test_eid = 'test-entity-123'
        self.controller.list_controller.select_entity = Mock()
        
        self.controller.select_entity(test_eid)
        
        self.controller.list_controller.select_entity.assert_called_once_with(test_eid)
    
    def test_change_category_delegation(self):
        """Test that change_category delegates to tabs_controller."""
        test_category = 'monsters'
        self.controller.tabs_controller.change_category = Mock()
        
        self.controller.change_category(test_category)
        
        self.controller.tabs_controller.change_category.assert_called_once_with(test_category)
    
    def test_model_property_access(self):
        """Test that model properties are accessible."""
        # Test categories
        assert self.controller.model.categories == ['player', 'monsters', 'map']
        
        # Test current_category
        assert self.controller.model.current_category == 'player'
        
        # Test selected_eid
        assert self.controller.model.selected_eid is None
        
        # Test setting selected_eid
        test_eid = 'test-123'
        self.controller.model.selected_eid = test_eid
        assert self.controller.model.selected_eid == test_eid
    
    def test_integration_with_editor_controller(self):
        """Test integration with editor controller."""
        # Test that editor controller is properly stored
        assert self.controller.editor_controller is not None
        assert hasattr(self.controller.editor_controller, 'model')
        
        # Test that active_data is accessible
        active_data = self.controller.editor_controller.model.active_data
        assert 'player' in active_data
        assert 'monsters' in active_data


if __name__ == "__main__":
    pytest.main([__file__])
