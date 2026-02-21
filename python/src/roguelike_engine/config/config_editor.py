"""
Editor configuration constants for map editing and rendering.
"""

# Tiles painting coalescing thresholds
TILE_PAINT_BATCH = 32   # flush chunk rebuild when dirty cells reach this size
TILE_PAINT_TICK = 16    # periodic flush every N processed tiles

# Chunk rendering configuration
MAP_CHUNK_SIZE = 32     # tiles per chunk side (MAP_CHUNK_SIZE x MAP_CHUNK_SIZE)
