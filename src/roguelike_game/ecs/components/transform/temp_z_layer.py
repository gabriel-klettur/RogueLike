class TempZLayer:
    """
    Temporary Z-layer override with TTL.
    - temp_layer: layer to apply while active
    - base_layer: original layer to restore after expiration
    - expires_at_ms: pygame time (ms) when temp should end
    """
    def __init__(self, temp_layer: int, base_layer: int, expires_at_ms: int):
        self.temp_layer = temp_layer
        self.base_layer = base_layer
        self.expires_at_ms = expires_at_ms
