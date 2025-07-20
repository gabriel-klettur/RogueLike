#!/usr/bin/env python3
"""
Tests for the left panel tabs controller.
"""

import pytest
import sys
import os
from unittest.mock import Mock, MagicMock

# Add the src directory to the Python path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', '..', '..', 'src'))

from roguelike_editors.inventory.left_panel.tabs.tabs_controller import TabsController
from roguelike_editors.inventory.left_panel.panel_model import InventoryPanelModel


class TestTabsController:
    """Test suite for TabsController."""
    
    def setup_method(self):
        """Set up test fixtures."""
        # Mock editor controller
        self.mock_editor_controller = Mock()
        self.mock_editor_controller.model = Mock()
        self.mock_editor_controller.model.current_category = 'player'
        
        # Create panel model
        self.panel_model = InventoryPanelModel()
        
        # Create controller
        self.controller = TabsController(self.mock_editor_controller, self.panel_model)
    
    def test_initialization(self):
        """Test that TabsController initializes correctly."""
        assert self.controller.editor_controller == self.mock_editor_controller
        assert self.controller.panel_model == self.panel_model
    
    def test_change_category_valid(self):
        """Test changing to a valid category."""
        # Test changing to monsters
        self.controller.change_category('monsters')
        
        assert self.panel_model.current_category == 'monsters'
        assert self.mock_editor_controller.model.current_category == 'monsters'
        
        # Test changing to map
        self.controller.change_category('map')
        
        assert self.panel_model.current_category == 'map'
        assert self.mock_editor_controller.model.current_category == 'map'
    
    def test_change_category_same_category(self):
        """Test changing to the same category."""
        initial_category = self.panel_model.current_category
        
        self.controller.change_category(initial_category)
        
        # Should still work and update both models
        assert self.panel_model.current_category == initial_category
        assert self.mock_editor_controller.model.current_category == initial_category
    
    def test_change_category_custom(self):
        """Test changing to a custom category not in default list."""
        custom_category = 'buildings'
        
        self.controller.change_category(custom_category)
        
        assert self.panel_model.current_category == custom_category
        assert self.mock_editor_controller.model.current_category == custom_category
    
    def test_change_category_updates_both_models(self):
        """Test that changing category updates both panel and editor models."""
        test_category = 'test_category'
        
        # Ensure models start with different values
        self.panel_model.current_category = 'player'
        self.mock_editor_controller.model.current_category = 'monsters'
        
        self.controller.change_category(test_category)
        
        # Both should be updated to the new category
        assert self.panel_model.current_category == test_category
        assert self.mock_editor_controller.model.current_category == test_category
    
    def test_get_categories(self):
        """Test getting available categories."""
        categories = self.controller.get_categories()
        
        expected_categories = ['player', 'monsters', 'map']
        assert categories == expected_categories
    
    def test_get_current_category(self):
        """Test getting current category."""
        # Test initial category
        current = self.controller.get_current_category()
        assert current == 'player'
        
        # Test after changing category
        self.controller.change_category('monsters')
        current = self.controller.get_current_category()
        assert current == 'monsters'
    
    def test_category_validation(self):
        """Test category validation behavior."""
        # The controller should accept any category string
        test_categories = [
            'player',
            'monsters', 
            'map',
            'custom_category',
            'buildings',
            'npcs',
            '',  # Empty string
            'category with spaces',
            'category-with-dashes',
            'category_with_underscores'
        ]
        
        for category in test_categories:
            self.controller.change_category(category)
            assert self.panel_model.current_category == category
            assert self.mock_editor_controller.model.current_category == category
    
    def test_integration_with_panel_model(self):
        """Test integration with panel model."""
        # Test that changes through controller affect panel model
        self.controller.change_category('monsters')
        assert self.panel_model.tabs_model.current_category == 'monsters'
        
        # Test that direct changes to panel model don't affect editor model
        self.panel_model.current_category = 'map'
        # Editor model should still have old value until controller is used
        assert self.mock_editor_controller.model.current_category == 'monsters'
        
        # Use controller to sync
        self.controller.change_category('map')
        assert self.mock_editor_controller.model.current_category == 'map'


class TestTabsControllerEdgeCases:
    """Test edge cases for TabsController."""
    
    def setup_method(self):
        """Set up minimal test fixtures."""
        self.mock_editor_controller = Mock()
        self.mock_editor_controller.model = Mock()
        self.mock_editor_controller.model.current_category = 'player'
        
        self.panel_model = InventoryPanelModel()
        self.controller = TabsController(self.mock_editor_controller, self.panel_model)
    
    def test_change_category_with_none(self):
        """Test changing category to None."""
        self.controller.change_category(None)
        
        assert self.panel_model.current_category is None
        assert self.mock_editor_controller.model.current_category is None
    
    def test_change_category_with_numeric_string(self):
        """Test changing category to numeric string."""
        numeric_category = '123'
        
        self.controller.change_category(numeric_category)
        
        assert self.panel_model.current_category == numeric_category
        assert self.mock_editor_controller.model.current_category == numeric_category
    
    def test_multiple_rapid_category_changes(self):
        """Test multiple rapid category changes."""
        categories = ['player', 'monsters', 'map', 'player', 'monsters']
        
        for category in categories:
            self.controller.change_category(category)
            assert self.panel_model.current_category == category
            assert self.mock_editor_controller.model.current_category == category
    
    def test_categories_list_modification(self):
        """Test that modifying categories list works correctly."""
        # Add new category to the list
        self.panel_model.categories.append('buildings')
        
        categories = self.controller.get_categories()
        assert 'buildings' in categories
        
        # Test changing to the new category
        self.controller.change_category('buildings')
        assert self.panel_model.current_category == 'buildings'
    
    def test_controller_state_consistency(self):
        """Test that controller maintains consistent state."""
        # Perform various operations
        self.controller.change_category('monsters')
        categories = self.controller.get_categories()
        current = self.controller.get_current_category()
        
        # State should be consistent
        assert current == 'monsters'
        assert current in categories
        assert self.panel_model.current_category == current
        assert self.mock_editor_controller.model.current_category == current


if __name__ == "__main__":
    pytest.main([__file__])
