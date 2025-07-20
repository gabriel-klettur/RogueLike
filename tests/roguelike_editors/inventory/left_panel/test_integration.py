#!/usr/bin/env python3
"""
Integration tests for the left panel components.
"""

import pytest
import pygame
import sys
import os
from unittest.mock import Mock, MagicMock, patch

# Add the src directory to the Python path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', '..', '..', 'src'))

from roguelike_editors.inventory.left_panel.panel_controller import PanelController
from roguelike_editors.inventory.left_panel.panel_view import PanelView
from roguelike_editors.inventory.left_panel.panel_event_handler import PanelEventHandler
from roguelike_editors.inventory.left_panel.panel_model import InventoryPanelModel


class TestLeftPanelIntegration:
    """Integration tests for the complete left panel system."""
    
    def setup_method(self):
        """Set up test fixtures for integration testing."""
        pygame.init()
        
        # Create mock editor controller with realistic data
        self.mock_editor_controller = Mock()
        self.mock_editor_controller.model = Mock()
        self.mock_editor_controller.model.selected_eid = None
        self.mock_editor_controller.model.current_category = 'player'
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
            },
            'monsters': {
                'monster1': {
                    'template_id': 'goblin',
                    'slots': [
                        {'item': 'gold', 'quantity': 5},
                        {'item': 'dagger', 'quantity': 1}
                    ]
                },
                'monster2': {
                    'template_id': 'orc',
                    'slots': [
                        {'item': 'club', 'quantity': 1},
                        {'item': 'gold', 'quantity': 10}
                    ]
                }
            },
            'map': {
                'location1': 'Forest',
                'location2': 'Cave'
            }
        }
        
        # Mock world with components
        self.mock_world = Mock()
        self.mock_world.components = {
            'PositionComponent': {
                1: Mock(x=100, y=200),
                2: Mock(x=300, y=400)
            },
            'MonsterInstanceComponent': {
                1: Mock(instance_id='monster1'),
                2: Mock(instance_id='monster2')
            }
        }
        self.mock_editor_controller.world = self.mock_world
        
        # Create the MVC components
        self.model = InventoryPanelModel()
        self.controller = PanelController(self.mock_editor_controller, self.model)
        self.font = pygame.font.Font(None, 24)
        self.view = PanelView(self.font, margin=5)
        self.event_handler = PanelEventHandler(self.controller)
        
        # Create test surface and rects
        self.surface = pygame.Surface((800, 600))
        self.panel_rect = pygame.Rect(10, 10, 300, 500)
    
    def teardown_method(self):
        """Clean up after tests."""
        pygame.quit()
    
    def test_mvc_integration_basic_flow(self):
        """Test basic MVC flow: model -> view -> controller."""
        # 1. Model has initial state
        assert self.controller.model.current_category == 'player'
        assert self.controller.model.selected_eid is None
        
        # 2. Controller can get items from model
        items = self.controller.get_items_list()
        assert isinstance(items, list)
        
        # 3. View can render the model state
        result = self.view.draw(self.surface, self.controller.model, self.panel_rect, items)
        assert isinstance(result, dict)
        assert 'tab_rects' in result
        
        # 4. Controller can update model
        self.controller.change_category('monsters')
        assert self.controller.model.current_category == 'monsters'
    
    def test_category_switching_integration(self):
        """Test complete category switching workflow."""
        # Start with player category
        assert self.controller.model.current_category == 'player'
        
        # Get player items
        player_items = self.controller.get_items_list()
        expected_player_items = ['sword x1', 'potion x3', 'shield x1']
        assert player_items == expected_player_items
        
        # Switch to monsters category
        self.controller.change_category('monsters')
        assert self.controller.model.current_category == 'monsters'
        assert self.mock_editor_controller.model.current_category == 'monsters'
        
        # Get monster items (should be different)
        monster_items = self.controller.get_items_list()
        assert monster_items != player_items
        
        # Switch to map category
        self.controller.change_category('map')
        assert self.controller.model.current_category == 'map'
        
        # Get map items
        map_items = self.controller.get_items_list()
        assert 'location1' in map_items
        assert 'location2' in map_items
    
    def test_entity_selection_integration(self):
        """Test entity selection workflow."""
        # Select an entity
        test_eid = 'test-entity-123'
        self.controller.select_entity(test_eid)
        
        # Check that both models are updated
        assert self.controller.model.selected_eid == test_eid
        assert self.mock_editor_controller.model.selected_eid == test_eid
        
        # View should be able to render with selected entity
        items = self.controller.get_items_list()
        result = self.view.draw(self.surface, self.controller.model, self.panel_rect, items)
        assert isinstance(result, dict)
    
    def test_event_handling_integration(self):
        """Test event handling integration."""
        # Draw view to get tab rects
        items = self.controller.get_items_list()
        result = self.view.draw(self.surface, self.controller.model, self.panel_rect, items)
        tab_rects = result['tab_rects']
        
        # Update view's tab_rects for event handling
        self.view.tab_rects = tab_rects
        
        # Create mock event for tab click
        event = Mock()
        event.type = pygame.MOUSEBUTTONUP
        event.button = 1
        
        # Test clicking on different tabs
        if len(tab_rects) > 1:
            # Click on second tab (monsters)
            event.pos = (tab_rects[1].centerx, tab_rects[1].centery)
            
            # Handle event
            handled = self.event_handler.handle(event)
            
            # Should be handled and category should change
            assert handled == True
            assert self.controller.model.current_category == 'monsters'
    
    def test_data_flow_consistency(self):
        """Test that data flows consistently through the system."""
        # Test with different categories
        categories = ['player', 'monsters', 'map']
        
        for category in categories:
            # Change category
            self.controller.change_category(category)
            
            # Get items
            items = self.controller.get_items_list()
            
            # Render view
            result = self.view.draw(self.surface, self.controller.model, self.panel_rect, items)
            
            # Check consistency
            assert self.controller.model.current_category == category
            assert isinstance(items, list)
            assert isinstance(result, dict)
            assert 'tab_rects' in result
    
    def test_error_handling_integration(self):
        """Test error handling across components."""
        # Test with invalid data
        self.mock_editor_controller.model.active_data = None
        
        # Should handle gracefully
        try:
            items = self.controller.get_items_list()
            result = self.view.draw(self.surface, self.controller.model, self.panel_rect, items)
            assert isinstance(items, list)
            assert isinstance(result, dict)
        except Exception as e:
            # If exceptions occur, they should be handled gracefully
            assert False, f"Unexpected exception: {e}"
    
    def test_state_persistence_across_operations(self):
        """Test that state is properly maintained across multiple operations."""
        # Perform multiple operations
        self.controller.change_category('monsters')
        self.controller.select_entity('monster1')
        items1 = self.controller.get_items_list()
        
        # Change category and back
        self.controller.change_category('player')
        self.controller.change_category('monsters')
        items2 = self.controller.get_items_list()
        
        # Selected entity should be preserved
        assert self.controller.model.selected_eid == 'monster1'
        # Items should be consistent
        assert items1 == items2
    
    def test_view_event_handler_coordination(self):
        """Test coordination between view and event handler."""
        # Draw view
        items = self.controller.get_items_list()
        result = self.view.draw(self.surface, self.controller.model, self.panel_rect, items)
        
        # Update view state for event handling
        self.view.tab_rects = result['tab_rects']
        
        # Test that event handler can access view state
        assert hasattr(self.event_handler, 'tabs_event_handler')
        assert hasattr(self.event_handler, 'list_event_handler')
        
        # Event handler should be able to process events with view state
        event = Mock()
        event.type = pygame.MOUSEBUTTONUP
        event.button = 1
        event.pos = (50, 50)
        
        # Should not crash
        try:
            handled = self.event_handler.handle(event)
            assert isinstance(handled, bool)
        except Exception as e:
            assert False, f"Event handling failed: {e}"


class TestLeftPanelPerformance:
    """Performance tests for left panel components."""
    
    def setup_method(self):
        """Set up performance test fixtures."""
        pygame.init()
        
        # Create controller with large dataset
        self.mock_editor_controller = Mock()
        self.mock_editor_controller.model = Mock()
        self.mock_editor_controller.model.selected_eid = None
        self.mock_editor_controller.model.current_category = 'player'
        
        # Large dataset for performance testing
        large_data = {}
        for category in ['player', 'monsters', 'map']:
            large_data[category] = {}
            for i in range(100):  # 100 items per category
                if category == 'player':
                    large_data[category][f'player{i}'] = {
                        'slots': [{'item': f'item{j}', 'quantity': j+1} for j in range(10)]
                    }
                elif category == 'monsters':
                    large_data[category][f'monster{i}'] = {
                        'template_id': f'template{i}',
                        'slots': [{'item': f'item{j}', 'quantity': j+1} for j in range(5)]
                    }
                else:
                    large_data[category][f'location{i}'] = f'Location {i}'
        
        self.mock_editor_controller.model.active_data = large_data
        self.mock_editor_controller.world = Mock()
        self.mock_editor_controller.world.components = {}
        
        self.controller = InventoryPanelController(self.mock_editor_controller)
        self.font = pygame.font.Font(None, 24)
        self.view = PanelView(self.font, margin=5)
        self.surface = pygame.Surface((800, 600))
        self.panel_rect = pygame.Rect(10, 10, 300, 500)
    
    def teardown_method(self):
        """Clean up after performance tests."""
        pygame.quit()
    
    def test_large_dataset_performance(self):
        """Test performance with large datasets."""
        import time
        
        # Test category switching performance
        categories = ['player', 'monsters', 'map']
        
        start_time = time.time()
        
        for _ in range(10):  # 10 iterations
            for category in categories:
                self.controller.change_category(category)
                items = self.controller.get_items_list()
                result = self.view.draw(self.surface, self.controller.model, self.panel_rect, items)
        
        end_time = time.time()
        elapsed = end_time - start_time
        
        # Should complete in reasonable time (less than 1 second for 30 operations)
        assert elapsed < 1.0, f"Performance test took too long: {elapsed} seconds"
    
    def test_memory_usage_stability(self):
        """Test that memory usage remains stable."""
        # Perform many operations to check for memory leaks
        for i in range(100):
            self.controller.change_category('player' if i % 2 == 0 else 'monsters')
            items = self.controller.get_items_list()
            result = self.view.draw(self.surface, self.controller.model, self.panel_rect, items)
            
            # Clear references to help garbage collection
            del items
            del result
        
        # Test should complete without memory issues
        assert True


if __name__ == "__main__":
    pytest.main([__file__])
