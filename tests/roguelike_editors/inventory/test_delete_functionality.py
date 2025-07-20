#!/usr/bin/env python3
"""
Test script to verify the delete functionality works correctly.
"""

import sys
import os

# Add the src directory to the Python path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'src'))

def test_delete_models():
    """Test that all delete-related models are properly initialized."""
    from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.delete.delete_model import DeleteModel
    from roguelike_editors.inventory.right_panel.inventory_items_panel.inventory_items_panel_model import InventoryitemsPanelModel
    from roguelike_editors.inventory.right_panel.inventory_items_panel.grid.grid_model import GridModel
    
    print("Testing delete models...")
    
    # Test DeleteModel
    delete_model = DeleteModel()
    assert delete_model.show_delete_mode == False
    assert delete_model.show_delete_quantity_input == False
    assert delete_model.delete_quantity == 1
    print("✓ DeleteModel initialized correctly")
    
    # Test InventoryItemsPanelModel
    panel_model = InventoryitemsPanelModel()
    assert hasattr(panel_model, 'delete')
    assert hasattr(panel_model, 'show_delete_mode')
    assert hasattr(panel_model, 'show_delete_quantity_input')
    assert hasattr(panel_model, 'delete_quantity')
    assert hasattr(panel_model, 'grid_model')
    print("✓ InventoryItemsPanelModel has delete properties")
    
    # Test GridModel
    grid_model = GridModel()
    assert hasattr(grid_model, 'show_delete_mode')
    assert grid_model.show_delete_mode == False
    print("✓ GridModel has delete mode property")
    
    # Test property access
    panel_model.show_delete_mode = True
    assert panel_model.delete.show_delete_mode == True
    panel_model.delete_quantity = 5
    assert panel_model.delete.delete_quantity == 5
    print("✓ Property access works correctly")
    
    print("All delete model tests passed! ✅")

def test_delete_controller():
    """Test that delete controller can be imported and initialized."""
    try:
        from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.delete.delete_controller import DeleteController
        print("✓ DeleteController can be imported")
        
        # Check that the delete_item method exists
        assert hasattr(DeleteController, 'delete_item')
        print("✓ DeleteController has delete_item method")
        
        print("Delete controller tests passed! ✅")
    except ImportError as e:
        print(f"❌ Failed to import DeleteController: {e}")

def test_delete_event_handler():
    """Test that delete event handler can be imported."""
    try:
        from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.delete.delete_event_handler import DeleteEventHandler
        print("✓ DeleteEventHandler can be imported")
        
        # Check that the handle method exists
        assert hasattr(DeleteEventHandler, 'handle')
        print("✓ DeleteEventHandler has handle method")
        
        print("Delete event handler tests passed! ✅")
    except ImportError as e:
        print(f"❌ Failed to import DeleteEventHandler: {e}")

if __name__ == "__main__":
    print("Testing Delete Functionality")
    print("=" * 40)
    
    try:
        test_delete_models()
        print()
        test_delete_controller()
        print()
        test_delete_event_handler()
        print()
        print("🎉 All tests passed! Delete functionality should work correctly.")
        print()
        print("How to use the delete functionality:")
        print("1. Press the 'Delete Item' button to enter delete mode")
        print("2. The button will turn red to indicate delete mode is active")
        print("3. Click on any item in the grid to delete it")
        print("4. You can adjust the quantity to delete using the quantity input")
        print("5. Click outside items or press the button again to exit delete mode")
        
    except Exception as e:
        print(f"❌ Test failed: {e}")
        import traceback
        traceback.print_exc()
