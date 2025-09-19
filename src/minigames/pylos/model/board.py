from __future__ import annotations

from dataclasses import dataclass
from typing import List, Optional, Tuple, Dict

# Simple two-player identifier. Could be replaced by an Enum if needed.
PlayerId = int  # 1 or 2


@dataclass(frozen=True)
class Cell:
    """Represents a position in the Pylos pyramid.

    Attributes:
        layer: 0 is bottom (4x4), then 1 (3x3), 2 (2x2), 3 (1x1) top.
        x: Column index within the layer.
        y: Row index within the layer.
    """

    layer: int
    x: int
    y: int


class Board:
    """Game board for Pylos with a fixed 4-layer pyramid (4x4 -> 1x1).

    The board stores ownership of each cell (None, 1, or 2). Placement rules:
    - A marble can be placed on layer 0 anywhere a cell is empty.
    - For layers > 0, the cell must be empty AND supported by 4 marbles beneath:
      (layer-1, x, y), (layer-1, x+1, y), (layer-1, x, y+1), (layer-1, x+1, y+1).
    """

    # Layer sizes from bottom to top
    LAYER_SIZES: Tuple[int, int, int, int] = (4, 3, 2, 1)

    def __init__(self) -> None:
        # 3D structure: layers -> rows -> cols
        self._cells: List[List[List[Optional[PlayerId]]]] = [
            [[None for _ in range(size)] for _ in range(size)]
            for size in self.LAYER_SIZES
        ]
        self.last_player_to_move: Optional[PlayerId] = None

    def clone(self) -> "Board":
        """Create a deep copy of the board.

        Used by the AI to explore future states without affecting the real game.
        """
        new_b = Board()
        # Deep copy cells
        new_b._cells = [
            [row.copy() for row in layer]
            for layer in self._cells
        ]
        new_b.last_player_to_move = self.last_player_to_move
        return new_b

    # ---- Queries ----
    def size_of_layer(self, layer: int) -> int:
        return self.LAYER_SIZES[layer]

    def get(self, cell: Cell) -> Optional[PlayerId]:
        return self._cells[cell.layer][cell.y][cell.x]

    def is_inside(self, cell: Cell) -> bool:
        size = self.size_of_layer(cell.layer)
        return 0 <= cell.x < size and 0 <= cell.y < size and 0 <= cell.layer < 4

    def is_empty(self, cell: Cell) -> bool:
        return self.get(cell) is None

    def is_supported(self, cell: Cell) -> bool:
        """Checks whether a cell on layer>0 is supported by 4 marbles below.

        Layer 0 requires no support (always True if empty).
        """
        if cell.layer == 0:
            return True
        # for cell (L, x, y) we need (L-1,x,y), (L-1,x+1,y), (L-1,x,y+1), (L-1,x+1,y+1)
        below_layer = cell.layer - 1
        needed = [
            Cell(below_layer, cell.x, cell.y),
            Cell(below_layer, cell.x + 1, cell.y),
            Cell(below_layer, cell.x, cell.y + 1),
            Cell(below_layer, cell.x + 1, cell.y + 1),
        ]
        # All must be inside and not None
        for c in needed:
            if not self.is_inside(c):
                return False
            if self.get(c) is None:
                return False
        return True

    def valid_placement(self, cell: Cell) -> bool:
        if not self.is_inside(cell):
            return False
        if not self.is_empty(cell):
            return False
        return self.is_supported(cell)

    def is_free(self, cell: Cell) -> bool:
        """A cell is free if it has no marble above relying on it for support."""
        if self.get(cell) is None:
            return False
        if cell.layer == 3:
            return True
        # Above cells that might sit on top of this cell
        above_layer = cell.layer + 1
        for dy in (0, -1):
            for dx in (0, -1):
                ax = cell.x + dx
                ay = cell.y + dy
                if 0 <= ax < self.size_of_layer(above_layer) and 0 <= ay < self.size_of_layer(above_layer):
                    above = Cell(above_layer, ax, ay)
                    if self.get(above) is not None:
                        # If an above marble exists, it relies on this cell among four supports
                        return False
        return True

    def all_cells(self) -> List[Cell]:
        result: List[Cell] = []
        for layer, size in enumerate(self.LAYER_SIZES):
            for y in range(size):
                for x in range(size):
                    result.append(Cell(layer, x, y))
        return result

    def empty_cells(self) -> List[Cell]:
        return [c for c in self.all_cells() if self.get(c) is None]

    def valid_moves(self) -> List[Cell]:
        return [c for c in self.empty_cells() if self.is_supported(c)]

    # ---- Mutations ----
    def place(self, player: PlayerId, cell: Cell) -> bool:
        """Place a marble for player at cell if valid. Returns True if placed."""
        if self.valid_placement(cell):
            self._cells[cell.layer][cell.y][cell.x] = player
            self.last_player_to_move = player
            return True
        return False

    def move(self, player: PlayerId, src: Cell, dst: Cell) -> bool:
        """Move one of player's free marbles upwards to a valid destination."""
        if self.get(src) != player:
            return False
        if not self.is_free(src):
            return False
        if not self.valid_placement(dst):
            return False
        if dst.layer <= src.layer:
            return False  # climb must go upwards
        # Perform move
        self._cells[src.layer][src.y][src.x] = None
        self._cells[dst.layer][dst.y][dst.x] = player
        self.last_player_to_move = player
        return True

    def remove_at(self, cell: Cell) -> Optional[PlayerId]:
        """Remove marble at cell if present and free. Returns player or None."""
        owner = self.get(cell)
        if owner is None:
            return None
        if not self.is_free(cell):
            return None
        self._cells[cell.layer][cell.y][cell.x] = None
        return owner

    def squares_created_by(self, player: PlayerId, cell: Cell) -> int:
        """Count 2x2 squares for player that include the given cell on its layer."""
        layer = cell.layer
        if layer >= 3:
            return 0
        size = self.size_of_layer(layer)
        count = 0
        # A 2x2 including (x,y) can start at (x-1,y-1),(x-1,y),(x,y-1),(x,y) when inside
        for oy in (cell.y - 1, cell.y):
            for ox in (cell.x - 1, cell.x):
                if 0 <= ox < size - 1 and 0 <= oy < size - 1:
                    block = [
                        Cell(layer, ox, oy),
                        Cell(layer, ox + 1, oy),
                        Cell(layer, ox, oy + 1),
                        Cell(layer, ox + 1, oy + 1),
                    ]
                    if all(self.get(b) == player for b in block):
                        count += 1
        return count

    def lines_created_by(self, player: PlayerId, cell: Cell) -> int:
        """Count straight alignment lines formed by placing/moving to `cell`.

        Variant rule (Expert+Líneas):
        - On layer 0 (4x4): a line means 4 in a row horizontally or vertically.
        - On layer 1 (3x3): a line means 3 in a row horizontally or vertically.
        - Other layers do not count for the alignment variant.
        The function returns how many distinct lines including `cell` are fully owned by `player`.
        """
        layer = cell.layer
        size = self.size_of_layer(layer)
        target_len = 4 if layer == 0 else (3 if layer == 1 else 0)
        if target_len == 0:
            return 0
        count = 0
        # Horizontal lines that include cell.y
        owners_row = [self.get(Cell(layer, x, cell.y)) for x in range(size)]
        if all(o == player for o in owners_row):
            # Entire row is player's, but only counts if its length matches target_len
            if size == target_len:
                count += 1
        else:
            # For safety if future sizes differ, check sliding window of target_len
            for start in range(0, size - target_len + 1):
                window = owners_row[start:start + target_len]
                if all(o == player for o in window) and start <= cell.x < start + target_len:
                    count += 1
                    break
        # Vertical lines that include cell.x
        owners_col = [self.get(Cell(layer, cell.x, y)) for y in range(size)]
        if all(o == player for o in owners_col):
            if size == target_len:
                count += 1
        else:
            for start in range(0, size - target_len + 1):
                window = owners_col[start:start + target_len]
                if all(o == player for o in window) and start <= cell.y < start + target_len:
                    count += 1
                    break
        return count

    def free_marbles(self, player: PlayerId) -> List[Cell]:
        return [c for c in self.all_cells() if self.get(c) == player and self.is_free(c)]

    # ---- Terminal checks ----
    def is_apex_filled(self) -> bool:
        top = Cell(3, 0, 0)
        return self.get(top) is not None

    def is_full(self) -> bool:
        return all(self.get(c) is not None for c in self.all_cells())

    def count_player_marbles(self) -> Dict[PlayerId, int]:
        counts: Dict[PlayerId, int] = {1: 0, 2: 0}
        for c in self.all_cells():
            p = self.get(c)
            if p in (1, 2):
                counts[p] += 1
        return counts
