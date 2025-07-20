#!/usr/bin/env python3
"""
Tests for the left panel view components.
"""

import pytest
import pygame
import sys
import os
from unittest.mock import Mock, MagicMock, patch

# Add the src directory to the Python path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', '..', '..', 'src'))

from roguelike_editors.inventory.left_panel.panel_view import PanelView
from roguelike_editors.inventory.left_panel.list.list_view import ListView
from roguelike_editors.inventory.left_panel.tabs.tabs_view import TabsView
from roguelike_editors.inventory.left_panel.panel_model import InventoryPanelModel


class TestPanelView:
    """Test suite for PanelView."""
    
    def setup_method(self):
        """Set up test fixtures."""
        pygame.init()
        
        # Create font for testing
        self.font = pygame.font.Font(None, 24)
        
        # Create view
        self.view = PanelView(self.font, margin=5)
        
        # Create test surface
        self.surface = pygame.Surface((800, 600))
        
        # Create test model
        self.model = InventoryPanelModel()
        
        # Create test rect
        self.panel_rect = pygame.Rect(10, 10, 300, 500)
        
        # Create test items
        self.items = ['Player 1', 'Monster 1', 'Monster 2']
    
    def teardown_method(self):
        """Clean up after tests."""
        pygame.quit()
    
    def test_initialization(self):
        """Test that PanelView initializes correctly."""
        assert self.view.font == self.font
        assert self.view.margin == 5
        assert hasattr(self.view, 'tabs_view')
        assert hasattr(self.view, 'list_view')
        assert isinstance(self.view.tabs_view, TabsView)
        assert isinstance(self.view.list_view, ListView)
    
    def test_draw_returns_dict(self):
        """Test that draw method returns a dictionary."""
        result = self.view.draw(self.surface, self.model, self.panel_rect, self.items)
        
        assert isinstance(result, dict)
        assert 'tab_rects' in result
    
    def test_draw_with_different_categories(self):
        """Test drawing with different categories."""
        categories = ['player', 'monsters', 'map']
        
        for category in categories:
            self.model.current_category = category
            result = self.view.draw(self.surface, self.model, self.panel_rect, self.items)
            
            assert isinstance(result, dict)
            assert 'tab_rects' in result
    
    def test_draw_with_empty_items(self):
        """Test drawing with empty items list."""
        empty_items = []
        result = self.view.draw(self.surface, self.model, self.panel_rect, empty_items)
        
        assert isinstance(result, dict)
        assert 'tab_rects' in result
    
    def test_draw_with_many_items(self):
        """Test drawing with many items (scrolling scenario)."""
        many_items = [f'Item {i}' for i in range(50)]
        result = self.view.draw(self.surface, self.model, self.panel_rect, many_items)
        
        assert isinstance(result, dict)
        assert 'tab_rects' in result
    
    def test_draw_updates_tab_rects(self):
        """Test that drawing updates tab_rects attribute."""
        result = self.view.draw(self.surface, self.model, self.panel_rect, self.items)
        
        # Should have tab_rects attribute set
        assert hasattr(self.view, 'tab_rects')
        assert self.view.tab_rects is not None
        assert len(self.view.tab_rects) == len(self.model.categories)


class TestListView:
    """Test suite for ListView."""
    
    def setup_method(self):
        """Set up test fixtures."""
        pygame.init()
        
        # Create font for testing
        self.font = pygame.font.Font(None, 24)
        
        # Create view
        self.view = ListView(self.font, margin=5)
        
        # Create test surface
        self.surface = pygame.Surface((800, 600))
        
        # Create test model
        self.model = InventoryPanelModel()
        
        # Create test rect
        self.base_rect = pygame.Rect(10, 50, 300, 400)
        
        # Create test items
        self.items = ['Player 1', 'Monster 1', 'Monster 2', 'Monster 3']
    
    def teardown_method(self):
        """Clean up after tests."""
        pygame.quit()
    
    def test_initialization(self):
        """Test that ListView initializes correctly."""
        assert self.view.font == self.font
        assert self.view.margin == 5
        assert hasattr(self.view, 'scroll_panel')
    
    def test_draw_returns_dict(self):
        """Test that draw method returns a dictionary."""
        result = self.view.draw(self.surface, self.model, self.base_rect, self.items)
        
        assert isinstance(result, dict)
    
    def test_draw_with_selected_item(self):
        """Test drawing with a selected item."""
        self.model.selected_eid = 'Monster 1'
        result = self.view.draw(self.surface, self.model, self.base_rect, self.items)
        
        assert isinstance(result, dict)
    
    def test_draw_with_empty_items(self):
        """Test drawing with empty items list."""
        empty_items = []
        result = self.view.draw(self.surface, self.model, self.base_rect, empty_items)
        
        assert isinstance(result, dict)
    
    def test_draw_with_long_items_list(self):
        """Test drawing with a long items list (scrolling)."""
        long_items = [f'Item {i}' for i in range(100)]
        result = self.view.draw(self.surface, self.model, self.base_rect, long_items)
        
        assert isinstance(result, dict)
    
    def test_scroll_panel_integration(self):
        """Test integration with scroll panel."""
        self.view.draw(self.surface, self.model, self.base_rect, self.items)
        
        # Scroll panel should be updated with items
        assert hasattr(self.view.scroll_panel, 'items')
        # Note: The actual scroll panel behavior depends on the implementation


class TestTabsView:
    """Test suite for TabsView."""
    
    def setup_method(self):
        """Set up test fixtures."""
        pygame.init()
        
        # Create font for testing
        self.font = pygame.font.Font(None, 24)
        
        # Create view
        self.view = TabsView(self.font, margin=5)
        
        # Create test surface
        self.surface = pygame.Surface((800, 600))
        
        # Create test model
        self.model = InventoryPanelModel()
        
        # Create test rect
        self.panel_rect = pygame.Rect(10, 10, 300, 500)
    
    def teardown_method(self):
        """Clean up after tests."""
        pygame.quit()
    
    def test_initialization(self):
        """Test that TabsView initializes correctly."""
        assert self.view.font == self.font
        assert self.view.margin == 5
    
    def test_draw_returns_list(self):
        """Test that draw method returns a list of rects."""
        result = self.view.draw(self.surface, self.model, self.panel_rect)
        
        assert isinstance(result, list)
        assert len(result) == len(self.model.categories)
        
        # Each item should be a Rect
        for rect in result:
            assert isinstance(rect, pygame.Rect)
    
    def test_draw_with_different_current_category(self):
        """Test drawing with different current categories."""
        categories = ['player', 'monsters', 'map']
        
        for category in categories:
            self.model.current_category = category
            result = self.view.draw(self.surface, self.model, self.panel_rect)
            
            assert isinstance(result, list)
            assert len(result) == len(self.model.categories)
    
    def test_draw_with_custom_categories(self):
        """Test drawing with custom categories."""
        custom_categories = ['player', 'npcs', 'buildings', 'items']
        self.model.categories = custom_categories
        
        result = self.view.draw(self.surface, self.model, self.panel_rect)
        
        assert isinstance(result, list)
        assert len(result) == len(custom_categories)
    
    def test_tab_rect_positions(self):
        """Test that tab rects are positioned correctly."""
        result = self.view.draw(self.surface, self.model, self.panel_rect)
        
        # Check that rects are positioned horizontally
        for i in range(1, len(result)):
            assert result[i].x > result[i-1].x
        
        # Check that all rects are within reasonable bounds
        for rect in result:
            assert rect.x >= self.panel_rect.x
            assert rect.y >= self.panel_rect.y
            assert rect.width > 0
            assert rect.height > 0
    
    def test_tab_rect_sizes(self):
        """Test that tab rects have reasonable sizes."""
        result = self.view.draw(self.surface, self.model, self.panel_rect)
        
        for rect in result:
            # Tabs should have positive dimensions
            assert rect.width > 0
            assert rect.height > 0
            
            # Tabs should not be too large
            assert rect.width <= self.panel_rect.width
            assert rect.height <= self.panel_rect.height


class TestViewIntegration:
    """Test integration between view components."""
    
    def setup_method(self):
        """Set up test fixtures."""
        pygame.init()
        
        self.font = pygame.font.Font(None, 24)
        self.surface = pygame.Surface((800, 600))
        self.model = InventoryPanelModel()
        self.panel_rect = pygame.Rect(10, 10, 300, 500)
        self.items = ['Player 1', 'Monster 1', 'Monster 2']
        
        self.panel_view = PanelView(self.font, margin=5)
    
    def teardown_method(self):
        """Clean up after tests."""
        pygame.quit()
    
    def test_panel_view_coordinates_tabs_and_list(self):
        """Test that PanelView coordinates tabs and list views properly."""
        result = self.panel_view.draw(self.surface, self.model, self.panel_rect, self.items)
        
        # Should return tab_rects
        assert 'tab_rects' in result
        assert isinstance(result['tab_rects'], list)
        
        # Tab rects should be accessible
        tab_rects = result['tab_rects']
        assert len(tab_rects) == len(self.model.categories)
    
    def test_view_state_consistency(self):
        """Test that view state remains consistent across multiple draws."""
        # Draw multiple times
        for i in range(5):
            result = self.panel_view.draw(self.surface, self.model, self.panel_rect, self.items)
            
            assert 'tab_rects' in result
            assert len(result['tab_rects']) == len(self.model.categories)
    
    def test_view_responds_to_model_changes(self):
        """Test that views respond to model changes."""
        # Initial draw
        result1 = self.panel_view.draw(self.surface, self.model, self.panel_rect, self.items)
        
        # Change model
        self.model.current_category = 'monsters'
        self.model.selected_eid = 'Monster 1'
        
        # Draw again
        result2 = self.panel_view.draw(self.surface, self.model, self.panel_rect, self.items)
        
        # Results should be consistent (structure-wise)
        assert 'tab_rects' in result1
        assert 'tab_rects' in result2
        assert len(result1['tab_rects']) == len(result2['tab_rects'])
    
    @patch('pygame.mouse.get_pos')
    def test_view_mouse_interaction_areas(self, mock_get_pos):
        """Test that views provide proper interaction areas."""
        mock_get_pos.return_value = (50, 50)
        
        result = self.panel_view.draw(self.surface, self.model, self.panel_rect, self.items)
        
        # Should have clickable areas
        assert 'tab_rects' in result
        tab_rects = result['tab_rects']
        
        # Each tab rect should be a valid clickable area
        for rect in tab_rects:
            assert isinstance(rect, pygame.Rect)
            assert rect.width > 0
            assert rect.height > 0


if __name__ == "__main__":
    pytest.main([__file__])
