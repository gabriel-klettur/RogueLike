class ParticlePresetComponent:
    """Marker component for persisted particle instances using a preset id.

    Also stores the persisted JSON entry id to keep a stable link for
    selection, dragging, and deletion.

    Includes an optional scale multiplier to adjust visual size per instance.
    """
    def __init__(self, preset_id: str, entry_id: int | None = None, scale_multiplier: float = 1.0):
        self.preset_id = str(preset_id)
        self.entry_id = int(entry_id) if entry_id is not None else None
        try:
            self.scale_multiplier = float(scale_multiplier)
        except Exception:
            self.scale_multiplier = 1.0
