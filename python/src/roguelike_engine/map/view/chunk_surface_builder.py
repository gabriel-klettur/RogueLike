"""Utilities to build chunk surfaces for different cache update paths."""
from __future__ import annotations

from typing import Iterable, Tuple

import pygame

from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.model.layer import Layer
from roguelike_engine.map.view.constants import MAX_SURFACE_DIM, OPAQUE_BLACK
from roguelike_engine.map.view.sprite_cache import SpriteScaler
from roguelike_engine.map.view.sprite_resolver import SpriteResolver

ChunkCoord = Tuple[int, int]


def build_chunk_surface(
    map_matrix: list[str],
    layers_by_type: dict[Layer, list[list[str]]],
    chunk: ChunkCoord,
    chunk_size: int,
    zoom: float,
    ordered_layers: Iterable[Layer],
    resolver: SpriteResolver,
    scaler: SpriteScaler,
) -> pygame.Surface:
    """Create a surface representing a single chunk.

    Parameters mirror the data available in all original callers so the
    implementation can be shared by the initial build and the incremental
    updates. All arithmetic is clamped to ``MAX_SURFACE_DIM`` to keep the
    allocated surfaces reasonable.
    """
    width_tiles = len(map_matrix[0]) if map_matrix else 0
    height_tiles = len(map_matrix)
    chunk_x, chunk_y = chunk
    start_x = chunk_x * chunk_size
    start_y = chunk_y * chunk_size

    tile_w = min(chunk_size, max(width_tiles - start_x, 0))
    tile_h = min(chunk_size, max(height_tiles - start_y, 0))

    pixel_w = _clamp_dimension(tile_w * TILE_SIZE, zoom)
    pixel_h = _clamp_dimension(tile_h * TILE_SIZE, zoom)

    surface = pygame.Surface((pixel_w, pixel_h), pygame.SRCALPHA)
    surface.fill(OPAQUE_BLACK)

    for ty in range(start_y, start_y + tile_h):
        row_chars = map_matrix[ty]
        for tx in range(start_x, start_x + tile_w):
            char = row_chars[tx]
            _draw_tile(
                surface=surface,
                map_matrix=map_matrix,
                layers_by_type=layers_by_type,
                ordered_layers=ordered_layers,
                resolver=resolver,
                scaler=scaler,
                chunk_origin=(start_x, start_y),
                zoom=zoom,
                world_x=tx,
                world_y=ty,
            )

    return surface


def _draw_tile(
    surface: pygame.Surface,
    map_matrix: list[str],
    layers_by_type: dict[Layer, list[list[str]]],
    ordered_layers: Iterable[Layer],
    resolver: SpriteResolver,
    scaler: SpriteScaler,
    chunk_origin: Tuple[int, int],
    zoom: float,
    world_x: int,
    world_y: int,
) -> None:
    chunk_x, chunk_y = chunk_origin
    char = map_matrix[world_y][world_x]
    local_x = world_x - chunk_x
    local_y = world_y - chunk_y

    for layer in ordered_layers:
        code = layers_by_type[layer][world_y][world_x]

        if not resolver.should_draw(layer, code):
            continue

        sprite = resolver.resolve(char, code, layer)
        if not sprite:
            continue

        scaled = scaler.scaled(sprite, zoom)
        px = int(round(local_x * TILE_SIZE * zoom))
        py = int(round(local_y * TILE_SIZE * zoom))
        surface.blit(scaled, (px, py))


def _clamp_dimension(size: int, zoom: float) -> int:
    scaled = int(round(size * zoom))
    if scaled < 1:
        return 1
    if scaled > MAX_SURFACE_DIM:
        return MAX_SURFACE_DIM
    return scaled
