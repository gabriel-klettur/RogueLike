import pygame
from roguelike_engine.map.model.layer import Layer
from roguelike_editors.map.services.overlay_service import set_overlay_cell
from roguelike_engine.tile.utils.loader import get_sprite_for_tile


class PaintTilesCommand:
    """
    Command object to support undo/redo for a batch of tile overlay edits.
    Each edit is a tuple (row, col, before_code, after_code).
    """
    def __init__(self, zone: str, overlay_code: str):
        self.zone = zone
        self.overlay_code = overlay_code
        self.edits: list[tuple[int, int, str | None, str | None]] = []

    def add_edit(self, row: int, col: int, before: str | None, after: str | None) -> None:
        self.edits.append((row, col, before, after))

    def undo(self, map_manager) -> list[tuple[int, int]]:
        """
        Revert all edits in reverse order. Returns list of affected cell coords (row, col).
        """
        affected: list[tuple[int, int]] = []
        world = map_manager.layers.get(Layer.Ground)
        if not world:
            return affected
        for row, col, before, _after in reversed(self.edits):
            # Bounds check and write previous code
            if 0 <= row < len(world) and 0 <= col < len(world[0]):
                set_overlay_cell(map_manager, col, row, before or "")
                # Keep Tile object in sync for renderer caches
                tiles_grid = map_manager.tiles_by_layer.get(Layer.Ground)
                if tiles_grid and 0 <= row < len(tiles_grid) and 0 <= col < len(tiles_grid[0]):
                    t = tiles_grid[row][col]
                    base = t.tile_type
                    t.overlay_code = before or ""
                    t.sprite = get_sprite_for_tile(base, t.overlay_code)
                    t.scaled_cache.clear()
                affected.append((row, col))
        # Invalidate cached scaled sprites is handled by chunk update call site
        return affected

    def redo(self, map_manager) -> list[tuple[int, int]]:
        """
        Re-apply all edits. Returns list of affected cell coords (row, col).
        """
        affected: list[tuple[int, int]] = []
        world = map_manager.layers.get(Layer.Ground)
        if not world:
            return affected
        for row, col, _before, after in self.edits:
            if 0 <= row < len(world) and 0 <= col < len(world[0]):
                set_overlay_cell(map_manager, col, row, after or "")
                tiles_grid = map_manager.tiles_by_layer.get(Layer.Ground)
                if tiles_grid and 0 <= row < len(tiles_grid) and 0 <= col < len(tiles_grid[0]):
                    t = tiles_grid[row][col]
                    base = t.tile_type
                    t.overlay_code = after or ""
                    t.sprite = get_sprite_for_tile(base, t.overlay_code)
                    t.scaled_cache.clear()
                affected.append((row, col))
        return affected
