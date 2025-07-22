def flood_fill(matrix, start_row, start_col, target, replacement):
    """
    Flood-fill algorithm for a 2D grid (matrix).
    Modifies matrix in place, replacing all connected cells matching target with replacement.
    """
    stack = [(start_row, start_col)]
    visited = set()

    while stack:
        r, c = stack.pop()
        if (r, c) in visited:
            continue
        visited.add((r, c))

        # Bounds check
        if r < 0 or c < 0 or r >= len(matrix) or c >= len(matrix[0]):
            continue

        if matrix[r][c] != target:
            continue

        # Replace cell
        matrix[r][c] = replacement

        # Push neighbors
        stack.extend([
            (r + 1, c),
            (r - 1, c),
            (r, c + 1),
            (r, c - 1),
        ])
