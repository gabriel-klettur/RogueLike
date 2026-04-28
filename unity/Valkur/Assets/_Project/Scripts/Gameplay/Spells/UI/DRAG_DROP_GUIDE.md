# Spell Drag-and-Drop Feature Guide

## Overview
This feature allows you to drag spells from the **Spells Editor** (F4) and drop them directly onto the **Spell Bar HUD** to assign them to hotbar slots.

## How It Works

### Components Created

1. **DraggableSpellItem** (`Scripts/Gameplay/Spells/UI/DraggableSpellItem.cs`)
   - Attached to each spell item in the picker grid
   - Makes spells draggable
   - Tracks which spell is being dragged via `DraggableSpellItem.DraggedSpell`
   - Provides visual feedback (60% opacity) while dragging

2. **DropZoneSpellSlot** (`Scripts/Gameplay/Spells/UI/DropZoneSpellSlot.cs`)
   - Attached to each slot in the spell bar HUD
   - Detects when a spell is dropped
   - Assigns the dropped spell to that slot in the player's `SpellCaster`
   - Highlights with blue tint when hovering over with a spell

### Usage Steps

1. **Open the Spells Editor** (Press F4 in-game)
2. **Open the Spells Panel** (Click "Spells v" button)
3. **Find the spell** you want to assign (use search if needed)
4. **Drag the spell item** from the grid
5. **Drag to a slot** in the Spell Bar HUD (bottom of screen)
6. **Drop** the spell onto the desired hotbar slot
7. The spell is now assigned and ready to use!

## Architecture

### Drag Flow
```
Spell Picker Item (DraggableSpellItem)
  ↓ OnBeginDrag()
  → Sets DraggableSpellItem.DraggedSpell
  → Reduces opacity to 0.6f
  ↓ OnDrag()
  → Follows cursor position
  ↓ OnEndDrag()
  → Clears DraggedSpell reference
  → Restores opacity
```

### Drop Flow
```
Spell Bar HUD Slot (DropZoneSpellSlot)
  ↓ OnPointerEnter()
  → Highlights slot with blue color if spell is being dragged
  ↓ OnDrop()
  → Checks if DraggableSpellItem.DraggedSpell exists
  → Uses reflection to access SpellCaster.spellSlots (private field)
  → Assigns spell to the slot array
  ↓ OnPointerExit()
  → Restores slot to normal color
```

## Technical Notes

### Reflection Usage
The `DropZoneSpellSlot` uses C# reflection to access the private `spellSlots` array on `SpellCaster`:
```csharp
var field = typeof(SpellCaster).GetField("spellSlots", 
    BindingFlags.NonPublic | BindingFlags.Instance);
```

This allows the HUD to update spell assignments without requiring API changes to `SpellCaster`.

### EventSystem Integration
Both components use Unity's **EventSystem** (`IBeginDragHandler`, `IDragHandler`, `IEndDragHandler`, `IDropHandler`, `IPointerEnterHandler`, `IPointerExitHandler`):
- Requires GraphicRaycaster on Canvas
- Requires Image components for raycasting
- Works with EventSystem.current

## Future Enhancements

1. **Drag Preview** — Show a spell icon following the cursor during drag
2. **Drag Cancellation** — Press ESC to cancel drag operation
3. **Spell Removal** — Drag from hotbar to trash zone to unequip
4. **Persistence** — Save hotbar assignments to player data
5. **Drag from HUD** — Reorganize spells between slots via drag-and-drop
6. **Undo/Redo** — Track spell assignments in editor history

## Troubleshooting

### Spells won't drag
- Ensure **EventSystem** is in the scene
- Check that spell picker buttons have **Image** components
- Verify **GraphicRaycaster** is on the Canvas

### Spells won't drop on slots
- Ensure spell bar HUD slots have **Image** components
- Check that **DropZoneSpellSlot** is attached to slot GameObjects
- Verify **raycastTarget** is enabled on slot Images

### Wrong spell assigned
- Check `SpellCaster` slot array size matches HUD slot count
- Verify spell definition is valid (not null)
- Check console for reflection errors

## Files Modified

- `SpellsRuntimeEditor.Picker.cs` — Added DraggableSpellItem to each spell picker item
- `SpellBarHUD.cs` — Added DropZoneSpellSlot to each hotbar slot
- `DraggableSpellItem.cs` — NEW: Handles drag behavior
- `DropZoneSpellSlot.cs` — NEW: Handles drop behavior
