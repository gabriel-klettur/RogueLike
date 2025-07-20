#!/usr/bin/env python3
"""
Tests for the left panel model components.
"""

import pytest
import sys
import os

# Add the src directory to the Python path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', '..', '..', 'src'))

from roguelike_editors.inventory.left_panel.panel_model import InventoryPanelModel
from roguelike_editors.inventory.left_panel.tabs.tabs_model import TabsModel
from roguelike_editors.inventory.left_panel.list.list_model import ListModel


class TestInventoryPanelModel:
    """Test suite for InventoryPanelModel."""
    
    def test_initialization(self):
        """Test that InventoryPanelModel initializes correctly."""
        model = InventoryPanelModel()
        
        # Check that sub-models are initialized
        assert isinstance(model.tabs_model, TabsModel)
        assert isinstance(model.list_model, ListModel)
        
        # Check default values
        assert model.categories == ['player', 'monsters', 'map']
        assert model.current_category == 'player'
        assert model.selected_eid is None
        assert model.camera_focus_target is None
    
    def test_categories_property(self):
        """Test categories property delegation to tabs_model."""
        model = InventoryPanelModel()
        
        # Test getter
        assert model.categories == ['player', 'monsters', 'map']
        
        # Test setter
        new_categories = ['player', 'npcs', 'items']
        model.categories = new_categories
        assert model.categories == new_categories
        assert model.tabs_model.categories == new_categories
    
    def test_current_category_property(self):
        """Test current_category property delegation to tabs_model."""
        model = InventoryPanelModel()
        
        # Test getter
        assert model.current_category == 'player'
        
        # Test setter
        model.current_category = 'monsters'
        assert model.current_category == 'monsters'
        assert model.tabs_model.current_category == 'monsters'
    
    def test_selected_eid_property(self):
        """Test selected_eid property delegation to list_model."""
        model = InventoryPanelModel()
        
        # Test getter
        assert model.selected_eid is None
        
        # Test setter
        test_eid = 'test-entity-123'
        model.selected_eid = test_eid
        assert model.selected_eid == test_eid
        assert model.list_model.selected_eid == test_eid
    
    def test_camera_focus_target(self):
        """Test camera_focus_target property."""
        model = InventoryPanelModel()
        
        # Test initial value
        assert model.camera_focus_target is None
        
        # Test setting value
        target = {'x': 100, 'y': 200}
        model.camera_focus_target = target
        assert model.camera_focus_target == target


class TestTabsModel:
    """Test suite for TabsModel."""
    
    def test_initialization(self):
        """Test that TabsModel initializes with default values."""
        model = TabsModel()
        
        assert model.categories == ['player', 'monsters', 'map']
        assert model.current_category == 'player'
    
    def test_custom_initialization(self):
        """Test TabsModel initialization with custom values."""
        custom_categories = ['player', 'npcs', 'items', 'buildings']
        model = TabsModel(categories=custom_categories, current_category='npcs')
        
        assert model.categories == custom_categories
        assert model.current_category == 'npcs'
    
    def test_categories_modification(self):
        """Test modifying categories list."""
        model = TabsModel()
        
        # Add new category
        model.categories.append('buildings')
        assert 'buildings' in model.categories
        assert len(model.categories) == 4
        
        # Remove category
        model.categories.remove('map')
        assert 'map' not in model.categories
        assert len(model.categories) == 3
    
    def test_current_category_validation(self):
        """Test that current_category can be set to any value."""
        model = TabsModel()
        
        # Set to existing category
        model.current_category = 'monsters'
        assert model.current_category == 'monsters'
        
        # Set to non-existing category (should still work)
        model.current_category = 'non_existing'
        assert model.current_category == 'non_existing'


class TestListModel:
    """Test suite for ListModel."""
    
    def test_initialization(self):
        """Test that ListModel initializes correctly."""
        model = ListModel()
        
        assert model.selected_eid is None
    
    def test_selected_eid_assignment(self):
        """Test selected_eid assignment and retrieval."""
        model = ListModel()
        
        # Test string EID
        eid1 = 'entity-123-abc'
        model.selected_eid = eid1
        assert model.selected_eid == eid1
        
        # Test numeric EID as string
        eid2 = '12345'
        model.selected_eid = eid2
        assert model.selected_eid == eid2
        
        # Test UUID-like EID
        eid3 = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890'
        model.selected_eid = eid3
        assert model.selected_eid == eid3
        
        # Test clearing selection
        model.selected_eid = None
        assert model.selected_eid is None
    
    def test_selected_eid_types(self):
        """Test that selected_eid accepts various types."""
        model = ListModel()
        
        # Test None
        model.selected_eid = None
        assert model.selected_eid is None
        
        # Test string
        model.selected_eid = "test-string"
        assert model.selected_eid == "test-string"
        
        # Test that it's typed as Optional[str]
        assert isinstance(model.selected_eid, str) or model.selected_eid is None


if __name__ == "__main__":
    pytest.main([__file__])
