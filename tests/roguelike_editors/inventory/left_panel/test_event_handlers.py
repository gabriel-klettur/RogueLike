#!/usr/bin/env python3
"""
Tests for the left panel event handlers.
"""

import pytest
import pygame
import sys
import os
from unittest.mock import Mock, MagicMock, patch

# Add the src directory to the Python path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', '..', '..', 'src'))

from roguelike_editors.inventory.left_panel.panel_event_handler import PanelEventHandler
from roguelike_editors.inventory.left_panel.panel_view import PanelView
from roguelike_editors.inventory.left_panel.list.list_event_handler import ListEventHandler
from roguelike_editors.inventory.left_panel.tabs.tabs_event_handler import TabsEventHandler
from roguelike_editors.inventory.left_panel.panel_model import InventoryPanelModel


class TestPanelEventHandler:
    """Test suite for PanelEventHandler."""
    
    def setup_method(self):
        """Set up test fixtures."""
        # Initialize pygame for event testing
        pygame.init()
        
        # Mock editor controller
        self.mock_editor_controller = Mock()
        
        # Create model, controller, and view
        self.model = InventoryPanelModel()
        self.mock_panel_controller = Mock()
        self.mock_panel_controller.model = self.model
        
        # Create view
        font = pygame.font.Font(None, 24)
        self.view = PanelView(font)
        
        # Create event handler
        self.event_handler = PanelEventHandler(
            self.mock_editor_controller,
            self.mock_panel_controller,
            self.view,
            self.model
        )
    
    def teardown_method(self):
        """Clean up after tests."""
        pygame.quit()
    
    def test_initialization(self):
        """Test that PanelEventHandler initializes correctly."""
        assert self.event_handler.controller == self.mock_panel_controller
        assert hasattr(self.event_handler, 'tabs_handler')
        assert hasattr(self.event_handler, 'list_handler')
        assert isinstance(self.event_handler.tabs_handler, TabsEventHandler)
        assert isinstance(self.event_handler.list_handler, ListEventHandler)
    
    def test_handle_event_delegation_to_tabs(self):
        """Test that tabs handler exists and can be called."""
        # Mock the tabs handler to return True (event consumed)
        self.event_handler.tabs_handler.handle = Mock(return_value=True)
        
        # Mock editor controller attributes to avoid complex interactions
        self.mock_editor_controller.model.camera_focus_target = None
        self.view.tab_rects = []
        self.view.panel_rect = pygame.Rect(0, 0, 100, 100)
        
        # Create a simple mock event
        mock_event = Mock()
        mock_event.type = pygame.KEYDOWN  # Use a simple event type
        
        result = self.event_handler.handle(mock_event)
        
        # Should return True (event consumed)
        assert result == True
        self.event_handler.tabs_handler.handle.assert_called_once_with(mock_event)
    
    def test_handle_event_delegation_to_list(self):
        """Test that events are properly delegated to list event handler."""
        # Mock tabs handler to return False, list handler to return True
        self.event_handler.tabs_handler.handle = Mock(return_value=False)
        self.event_handler.list_handler.handle = Mock(return_value=True)
        
        # Mock editor controller attributes to avoid complex interactions
        self.mock_editor_controller.model.camera_focus_target = None
        self.view.tab_rects = []
        self.view.panel_rect = pygame.Rect(0, 0, 100, 100)
        
        mock_event = Mock()
        mock_event.type = pygame.KEYDOWN  # Use simple event type
        
        result = self.event_handler.handle(mock_event)
        
        # Should return True (event consumed by list handler)
        assert result == True
        self.event_handler.tabs_handler.handle.assert_called_once_with(mock_event)
        self.event_handler.list_handler.handle.assert_called_once_with(mock_event)
    
    def test_handle_event_not_consumed(self):
        """Test behavior when no handler consumes the event."""
        # Mock both handlers to return False
        self.event_handler.tabs_handler.handle = Mock(return_value=False)
        self.event_handler.list_handler.handle = Mock(return_value=False)
        
        # Mock editor controller attributes to avoid complex interactions
        self.mock_editor_controller.model.camera_focus_target = None
        self.view.tab_rects = []
        self.view.panel_rect = pygame.Rect(0, 0, 100, 100)
        
        mock_event = Mock()
        mock_event.type = pygame.KEYDOWN  # Use simple event type
        
        result = self.event_handler.handle(mock_event)
        
        # Should return False (event not consumed)
        assert result == False
        self.event_handler.tabs_handler.handle.assert_called_once_with(mock_event)
        self.event_handler.list_handler.handle.assert_called_once_with(mock_event)
    
    def test_handle_event_exception_handling(self):
        """Test that exceptions in event handlers are handled gracefully."""
        # Mock tabs handler to raise an exception
        self.event_handler.tabs_event_handler.handle = Mock(side_effect=Exception("Test exception"))
        self.event_handler.list_event_handler.handle = Mock(return_value=True)
        
        mock_event = Mock()
        mock_event.type = pygame.MOUSEBUTTONUP
        
        # Should not raise exception and should continue to list handler
        result = self.event_handler.handle(mock_event)
        
        # List handler should still be called and return True
        assert result == True
        self.event_handler.list_event_handler.handle.assert_called_once_with(mock_event)


class TestListEventHandler:
    """Test suite for ListEventHandler."""
    
    def setup_method(self):
        """Set up test fixtures."""
        pygame.init()
        
        # Mock panel controller
        self.mock_panel_controller = Mock()
        self.mock_panel_controller.model = InventoryPanelModel()
        self.mock_panel_controller.select_entity = Mock()
        
        # Mock view with scroll panel
        self.mock_view = Mock()
        self.mock_scroll_panel = Mock()
        self.mock_scroll_panel.scroll_rect = pygame.Rect(10, 10, 200, 300)
        self.mock_scroll_panel.items = ['item1', 'item2', 'item3']
        self.mock_scroll_panel.scroll_offset = 0
        self.mock_view.inventory_panel_view = Mock()
        self.mock_view.inventory_panel_view.list_view = Mock()
        self.mock_view.inventory_panel_view.list_view.scroll_panel = self.mock_scroll_panel
        
        # Create event handler
        self.event_handler = ListEventHandler(self.mock_panel_controller, self.mock_view)
    
    def teardown_method(self):
        """Clean up after tests."""
        pygame.quit()
    
    def test_initialization(self):
        """Test that ListEventHandler initializes correctly."""
        assert self.event_handler.panel_controller == self.mock_panel_controller
        assert self.event_handler.view == self.mock_view
    
    @patch('pygame.mouse.get_pos')
    def test_handle_mouse_click_in_scroll_area(self, mock_get_pos):
        """Test handling mouse click within scroll area."""
        # Set up mouse position within scroll area
        mock_get_pos.return_value = (50, 50)  # Within scroll_rect
        
        # Create mouse click event
        event = Mock()
        event.type = pygame.MOUSEBUTTONUP
        event.button = 1
        event.pos = (50, 50)
        
        result = self.event_handler.handle(event)
        
        # Should handle the event
        assert result == True
        # Should call select_entity
        self.mock_panel_controller.select_entity.assert_called_once()
    
    @patch('pygame.mouse.get_pos')
    def test_handle_mouse_click_outside_scroll_area(self, mock_get_pos):
        """Test handling mouse click outside scroll area."""
        # Set up mouse position outside scroll area
        mock_get_pos.return_value = (300, 300)  # Outside scroll_rect
        
        # Create mouse click event
        event = Mock()
        event.type = pygame.MOUSEBUTTONUP
        event.button = 1
        event.pos = (300, 300)
        
        result = self.event_handler.handle(event)
        
        # Should not handle the event
        assert result == False
        # Should not call select_entity
        self.mock_panel_controller.select_entity.assert_not_called()
    
    def test_handle_non_mouse_event(self):
        """Test handling non-mouse events."""
        # Create keyboard event
        event = Mock()
        event.type = pygame.KEYDOWN
        
        result = self.event_handler.handle(event)
        
        # Should not handle the event
        assert result == False
    
    def test_handle_wrong_mouse_button(self):
        """Test handling wrong mouse button clicks."""
        # Create right mouse click event
        event = Mock()
        event.type = pygame.MOUSEBUTTONUP
        event.button = 3  # Right click
        event.pos = (50, 50)
        
        result = self.event_handler.handle(event)
        
        # Should not handle the event
        assert result == False


class TestTabsEventHandler:
    """Test suite for TabsEventHandler."""
    
    def setup_method(self):
        """Set up test fixtures."""
        pygame.init()
        
        # Mock panel controller
        self.mock_panel_controller = Mock()
        self.mock_panel_controller.model = InventoryPanelModel()
        self.mock_panel_controller.change_category = Mock()
        
        # Mock view with tab rects
        self.mock_view = Mock()
        self.mock_view.tab_rects = [
            pygame.Rect(10, 10, 80, 30),   # player tab
            pygame.Rect(90, 10, 80, 30),   # monsters tab
            pygame.Rect(170, 10, 80, 30)   # map tab
        ]
        
        # Create event handler
        self.event_handler = TabsEventHandler(self.mock_panel_controller, self.mock_view)
    
    def teardown_method(self):
        """Clean up after tests."""
        pygame.quit()
    
    def test_initialization(self):
        """Test that TabsEventHandler initializes correctly."""
        assert self.event_handler.panel_controller == self.mock_panel_controller
        assert self.event_handler.view == self.mock_view
    
    def test_handle_tab_click_player(self):
        """Test clicking on player tab."""
        # Create mouse click event on first tab
        event = Mock()
        event.type = pygame.MOUSEBUTTONUP
        event.button = 1
        event.pos = (50, 25)  # Within first tab rect
        
        result = self.event_handler.handle(event)
        
        # Should handle the event
        assert result == True
        # Should call change_category with 'player'
        self.mock_panel_controller.change_category.assert_called_once_with('player')
    
    def test_handle_tab_click_monsters(self):
        """Test clicking on monsters tab."""
        # Create mouse click event on second tab
        event = Mock()
        event.type = pygame.MOUSEBUTTONUP
        event.button = 1
        event.pos = (130, 25)  # Within second tab rect
        
        result = self.event_handler.handle(event)
        
        # Should handle the event
        assert result == True
        # Should call change_category with 'monsters'
        self.mock_panel_controller.change_category.assert_called_once_with('monsters')
    
    def test_handle_tab_click_map(self):
        """Test clicking on map tab."""
        # Create mouse click event on third tab
        event = Mock()
        event.type = pygame.MOUSEBUTTONUP
        event.button = 1
        event.pos = (210, 25)  # Within third tab rect
        
        result = self.event_handler.handle(event)
        
        # Should handle the event
        assert result == True
        # Should call change_category with 'map'
        self.mock_panel_controller.change_category.assert_called_once_with('map')
    
    def test_handle_click_outside_tabs(self):
        """Test clicking outside all tab areas."""
        # Create mouse click event outside all tabs
        event = Mock()
        event.type = pygame.MOUSEBUTTONUP
        event.button = 1
        event.pos = (300, 25)  # Outside all tab rects
        
        result = self.event_handler.handle(event)
        
        # Should not handle the event
        assert result == False
        # Should not call change_category
        self.mock_panel_controller.change_category.assert_not_called()
    
    def test_handle_non_mouse_event(self):
        """Test handling non-mouse events."""
        # Create keyboard event
        event = Mock()
        event.type = pygame.KEYDOWN
        
        result = self.event_handler.handle(event)
        
        # Should not handle the event
        assert result == False
    
    def test_handle_no_tab_rects(self):
        """Test handling when no tab rects are available."""
        # Remove tab rects
        self.mock_view.tab_rects = None
        
        # Create mouse click event
        event = Mock()
        event.type = pygame.MOUSEBUTTONUP
        event.button = 1
        event.pos = (50, 25)
        
        result = self.event_handler.handle(event)
        
        # Should not handle the event
        assert result == False
        # Should not call change_category
        self.mock_panel_controller.change_category.assert_not_called()
    
    def test_handle_empty_tab_rects(self):
        """Test handling when tab rects list is empty."""
        # Empty tab rects
        self.mock_view.tab_rects = []
        
        # Create mouse click event
        event = Mock()
        event.type = pygame.MOUSEBUTTONUP
        event.button = 1
        event.pos = (50, 25)
        
        result = self.event_handler.handle(event)
        
        # Should not handle the event
        assert result == False
        # Should not call change_category
        self.mock_panel_controller.change_category.assert_not_called()


if __name__ == "__main__":
    pytest.main([__file__])
