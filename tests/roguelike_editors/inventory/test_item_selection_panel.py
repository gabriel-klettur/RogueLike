#!/usr/bin/env python3
"""
Test script to verify the item selection panel functionality works correctly.
"""

import sys
import os

# Add the src directory to the Python path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'src'))

def test_item_selection_panel():
    """Test that item selection panel handles ground items correctly."""
    from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel
    from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_controller import ItemSelectionPanelController
    
    print("Testing Item Selection Panel...")
    
    # Create model and controller
    model = ItemSelectionPanelModel()
    controller = ItemSelectionPanelController(model)
    
    # Set up test data
    ground_items = ["wood x5", "stone x3", "iron_ingot x2"]
    default_items = ["sword", "shield", "potion"]
    
    controller.open(default_items, ground_items)
    
    print("✓ Panel opened successfully")
    print(f"Ground items: {model.ground_items}")
    
    # Switch to ground tab
    controller.change_tab('ground')
    assert model.current_tab == 'ground'
    print("✓ Switched to ground tab")
    
    # Select first item (wood x5)
    controller.select_item("wood x5")
    model.selected_index = 0  # Simulate list selection
    assert model.selected_item == "wood x5"
    assert model.selected_index == 0
    print("✓ Selected wood x5")
    
    # Set quantity to 2
    controller.set_quantity("2")
    assert model.quantity == 2
    print("✓ Set quantity to 2")
    
    # Confirm selection (should take 2 wood, leaving 3)
    item, qty = controller.confirm()
    assert item == "wood"
    assert qty == 2
    assert model.ground_items[0] == "wood x3"  # Should be updated
    assert model.selected_item == "wood x3"    # Should remain selected
    print("✓ Confirmed selection - wood x3 remaining")
    
    # Test taking all remaining wood
    controller.set_quantity("3")
    item, qty = controller.confirm()
    assert item == "wood"
    assert qty == 3
    assert "wood" not in str(model.ground_items)  # Should be completely removed
    assert model.selected_item is None           # Should clear selection
    assert model.selected_index is None         # Should clear index
    print("✓ Took all remaining wood - item removed from list")
    
    print(f"Final ground items: {model.ground_items}")
    
    # Test that panel doesn't auto-close
    assert model.show_panel == True  # Should still be open
    print("✓ Panel remains open after confirming items")
    
    print("🎉 All Item Selection Panel tests passed!")

if __name__ == "__main__":
    print("Testing Item Selection Panel Functionality")
    print("=" * 50)
    
    try:
        test_item_selection_panel()
        print()
        print("✅ All tests passed! The item selection panel should now:")
        print("1. Not auto-close when adding items to inventory")
        print("2. Properly update ground item quantities")
        print("3. Remove items from list when quantity reaches 0")
        print("4. Maintain proper selection state")
        
    except Exception as e:
        print(f"❌ Test failed: {e}")
        import traceback
        traceback.print_exc()
