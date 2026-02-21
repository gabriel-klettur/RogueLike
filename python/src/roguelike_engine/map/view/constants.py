"""Common constants used by the chunked map rendering pipeline."""
from __future__ import annotations

DEBUG_CHUNKED: bool = False
"""Enable verbose logging for chunk generation when set to ``True``."""

MAX_ZOOM: float = 10.0
"""Hard cap applied to camera zoom to avoid enormous surfaces."""

MAX_SURFACE_DIM: int = 4096
"""Maximum width/height for any cached surface to keep memory bounded."""

OPAQUE_BLACK: tuple[int, int, int, int] = (0, 0, 0, 255)
"""Default background color for chunk surfaces."""
